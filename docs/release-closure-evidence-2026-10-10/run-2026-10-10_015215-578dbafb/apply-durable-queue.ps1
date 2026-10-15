param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir,
  [switch]$RestoreOnUiThread
)
# Round-28: implement the README's "可恢复任务队列" (recoverable download queue) inside an ISOLATED copy.
# The model (AppConfig.DownloadQueue + VersionDownloadQueueItem/State) already existed but had NO
# producer and NO consumer; the live queue was a process-lifetime List, so a restart lost every task.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$M = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Models'
$S = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services'
$report = New-Object System.Collections.Generic.List[object]

function Write-PreservingEncoding([string]$file, [string]$text) {
  $bytes = [IO.File]::ReadAllBytes($file)
  $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($hasBom)))
}

function Patch([string]$file, [string]$old, [string]$new, [string]$label) {
  $text = [IO.File]::ReadAllText($file)
  $usesCrlf = $text.Contains("`r`n")
  if ($usesCrlf) { $old = $old -replace "`r?`n", "`r`n"; $new = $new -replace "`r?`n", "`r`n" }
  else { $old = $old -replace "`r?`n", "`n"; $new = $new -replace "`r?`n", "`n" }
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne 1) { throw "[$label] expected exactly 1 match in $file but found $count" }
  Write-PreservingEncoding $file $text.Replace($old, $new)
  $report.Add([pscustomobject]@{ label = $label; file = $file.Substring($Root.Length + 1); ok = $true })
  Write-Output "[OK] $label"
}

# ---------------------------------------------------------------- 1. model: carry the fields the live queue needs
$model = Join-Path $M 'VersionDownloadQueueItem.cs'
Patch $model "namespace Pineglow.Launcher.Models;" "using System;`n`nnamespace Pineglow.Launcher.Models;" 'P-DURABLEQUEUE 0/6 using System (DateTimeOffset)'
$modelOld = @'
	public string VersionId { get; init; } = string.Empty;
'@
$modelNew = @'
	public string VersionId { get; init; } = string.Empty;

	// FIX-DURABLEQUEUE: these three fields were missing, which is part of why the durable model
	// could never be wired to the live queue (the live entry also tracks an id, progress and a start time).
	public string Id { get; set; } = string.Empty;

	public double Progress { get; set; }

	public DateTimeOffset StartedAt { get; set; }
'@
Patch $model $modelOld $modelNew 'P-DURABLEQUEUE 1/6 model fields'

# ---------------------------------------------------------------- 2. persistence helpers next to the live queue
$las = Join-Path $S 'LocalApiService.cs'
$declOld = @'
	private static readonly object DownloadGate = new object();
	private static readonly List<DownloadTaskEntry> DownloadTasks = new List<DownloadTaskEntry>();
'@
$declNew = @'
	private static readonly object DownloadGate = new object();
	private static readonly List<DownloadTaskEntry> DownloadTasks = new List<DownloadTaskEntry>();

	// FIX-DURABLEQUEUE: AppConfig.DownloadQueue + VersionDownloadQueueItem/State existed as a model
	// with NO producer and NO consumer, so the live queue above was process-lifetime only and a
	// restart silently lost every task. The queue is now persisted on state TRANSITIONS (never on
	// progress ticks) and rebuilt at startup.
	private const int MaxPersistedDownloadTasks = 50;

	private static VersionDownloadQueueState DownloadStateFromWire(string status) => status switch
	{
		"queued" => VersionDownloadQueueState.Waiting,
		"downloading" => VersionDownloadQueueState.Downloading,
		"completed" => VersionDownloadQueueState.Completed,
		_ => VersionDownloadQueueState.Failed,
	};

	private static string DownloadStateToWire(VersionDownloadQueueState state) => state switch
	{
		VersionDownloadQueueState.Waiting => "queued",
		VersionDownloadQueueState.Downloading => "downloading",
		VersionDownloadQueueState.Completed => "completed",
		_ => "failed",
	};

	/// <summary>把当前队列写回 config.json。只在状态迁移时调用——进度刷新每秒很多次，不能每次都落盘。</summary>
	private static void PersistDownloadQueue()
	{
		List<VersionDownloadQueueItem> snapshot;
		lock (DownloadGate)
		{
			snapshot = DownloadTasks.Skip(Math.Max(0, DownloadTasks.Count - MaxPersistedDownloadTasks))
				.Select(t => new VersionDownloadQueueItem
				{
					Id = t.Id,
					VersionId = t.VersionId,
					State = DownloadStateFromWire(t.Status),
					Message = t.Message,
					Progress = t.Progress,
					StartedAt = t.StartedAt
				}).ToList();
		}

		try
		{
			AppConfig config = LoadConfig();
			config.DownloadQueue = snapshot;
			SaveConfig(config);
		}
		catch (Exception ex)
		{
			// 队列落盘失败不能影响正在进行的下载
			LauncherLog.Warn("[Downloads] 持久化下载队列失败：" + ex.Message);
		}
	}

	/// <summary>
	/// 启动时把上次运行留下的队列读回来。仍在 queued/downloading 的条目在进程死亡时已中断，
	/// **不自动重下**——如实标成 failed 并写明原因，是否重新发起由用户决定。
	/// </summary>
	private static void RestoreDownloadQueue()
	{
		List<VersionDownloadQueueItem> persisted;
		try
		{
			persisted = LoadConfig().DownloadQueue;
		}
		catch (Exception ex)
		{
			LauncherLog.Warn("[Downloads] 读取持久化下载队列失败：" + ex.Message);
			return;
		}

		if (persisted == null || persisted.Count == 0)
		{
			return;
		}

		int restored;
		lock (DownloadGate)
		{
			DownloadTasks.Clear();
			foreach (VersionDownloadQueueItem item in persisted)
			{
				bool interrupted = item.State is VersionDownloadQueueState.Waiting or VersionDownloadQueueState.Downloading;
				DownloadTasks.Add(new DownloadTaskEntry
				{
					Id = string.IsNullOrWhiteSpace(item.Id)
						? "dlv" + Math.Abs(item.VersionId.GetHashCode(StringComparison.Ordinal))
						: item.Id,
					VersionId = item.VersionId,
					Status = interrupted ? "failed" : DownloadStateToWire(item.State),
					Progress = interrupted ? 0.0 : item.Progress,
					Message = interrupted ? "上次运行被中断，可重新下载" : item.Message,
					StartedAt = item.StartedAt == default ? DateTimeOffset.Now : item.StartedAt
				});
			}

			restored = DownloadTasks.Count;
		}

		LauncherLog.Info($"[Downloads] 已恢复 {restored} 条下载队列记录");
	}
'@
Patch $las $declOld $declNew 'P-DURABLEQUEUE 2/6 persistence helpers'

# ---------------------------------------------------------------- 3. restore at startup
# Start() is called from App.OnFrameworkInitializationCompleted, i.e. on the Avalonia UI thread.
# ProductionConfigStore.Load() is sync-over-async (ConfigStore.LoadAsync().GetAwaiter().GetResult()),
# so calling it there deadlocks UNLESS the P-DEADLOCK fix has been applied. -RestoreOnUiThread is
# therefore the switch that proves the deadlock fix works: with it, the restore runs on the UI thread.
$startOld = @'
		_uiRoot = ResolveUiRoot();
'@
if ($RestoreOnUiThread) {
  $restoreCall = "`t`tRestoreDownloadQueue();"
  $restoreWhy = '		// FIX-DURABLEQUEUE: restore on the UI thread — only safe with P-DEADLOCK applied.'
} else {
  $restoreCall = "`t`t_ = Task.Run(RestoreDownloadQueue);"
  $restoreWhy = '		// FIX-DURABLEQUEUE: restore off the UI thread — LoadConfig() is sync-over-async internally.'
}
$startNew = $restoreWhy + "`n" + $restoreCall + "`n`t`t_uiRoot = ResolveUiRoot();"
$startNew = $startNew -replace "`r?`n", "`n"
if ($RestoreOnUiThread) {
  Patch $las $startOld ("`t`t" + $startNew) 'P-DURABLEQUEUE 3/6 restore on Start() (UI thread)'
} else {
  Patch $las $startOld ("`t`t" + $startNew) 'P-DURABLEQUEUE 3/6 restore on Start() (background thread)'
}

# ---------------------------------------------------------------- 4. persist on each transition
$queuedOld = @'
			DownloadTasks.Add(entry);
		}

		_ = Task.Run(() => RunDownloadTaskAsync(entry, gameDir));
'@
$queuedNew = @'
			DownloadTasks.Add(entry);
		}

		PersistDownloadQueue();
		_ = Task.Run(() => RunDownloadTaskAsync(entry, gameDir));
'@
Patch $las $queuedOld $queuedNew 'P-DURABLEQUEUE 4/6 persist when queued'

$downloadingOld = @'
				entry.Status = "downloading";
				entry.Message = "正在获取版本元数据";
			}
'@
$downloadingNew = @'
				entry.Status = "downloading";
				entry.Message = "正在获取版本元数据";
			}
			PersistDownloadQueue();
'@
Patch $las $downloadingOld $downloadingNew 'P-DURABLEQUEUE 5/6 persist when it starts running'

$terminalOld = @'
			await downloader.InstallVersionAsync(gameDir, entry.VersionId, CancellationToken.None);
			lock (DownloadGate)
			{
				entry.Status = "completed";
				entry.Progress = 1.0;
				entry.Message = "安装完成";
			}
		}
		catch (Exception ex)
		{
			lock (DownloadGate)
			{
				entry.Status = "failed";
				entry.Message = ex.Message;
			}
		}
	}
'@
$terminalNew = @'
			await downloader.InstallVersionAsync(gameDir, entry.VersionId, CancellationToken.None);
			lock (DownloadGate)
			{
				entry.Status = "completed";
				entry.Progress = 1.0;
				entry.Message = "安装完成";
			}

			PersistDownloadQueue();
		}
		catch (Exception ex)
		{
			lock (DownloadGate)
			{
				entry.Status = "failed";
				entry.Message = ex.Message;
			}

			PersistDownloadQueue();
		}
	}
'@
Patch $las $terminalOld $terminalNew 'P-DURABLEQUEUE 6/6 persist on terminal states'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'durablequeue-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
