param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-35: close the whole "sync-over-async" class (defect #11's siblings).
#
# Ten call sites in LocalApiService block on a Task with GetAwaiter().GetResult(). That is safe only
# while every caller happens to sit on a thread-pool thread. The moment one of them is reached from
# a thread that owns a SynchronizationContext (the Avalonia UI thread), the async continuation is
# posted back to the thread that is blocked waiting — a deadlock, exactly what round 28 hit.
#
# Rather than chase ConfigureAwait through five different async chains, route every one of those
# blocking waits through one helper that runs the work on the thread pool. Task.Run guarantees the
# delegate starts with NO SynchronizationContext, so nothing inside can capture one.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$S = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services'
$T = Join-Path $Root 'dev\PineglowLauncher.Tests'
$report = New-Object System.Collections.Generic.List[object]

function Write-PreservingEncoding([string]$file, [string]$text) {
  $bytes = [IO.File]::ReadAllBytes($file)
  $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($hasBom)))
}

function PatchN([string]$file, [string]$old, [string]$new, [int]$expected, [string]$label) {
  $text = [IO.File]::ReadAllText($file)
  $usesCrlf = $text.Contains("`r`n")
  if ($usesCrlf) { $old = $old -replace "`r?`n", "`r`n"; $new = $new -replace "`r?`n", "`r`n" }
  else { $old = $old -replace "`r?`n", "`n"; $new = $new -replace "`r?`n", "`n" }
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne $expected) { throw "[$label] expected $expected matches in $file but found $count" }
  Write-PreservingEncoding $file $text.Replace($old, $new)
  $report.Add([pscustomobject]@{ label = $label; count = $count; ok = $true })
  Write-Output "[OK] $label ($count)"
}

$las = Join-Path $S 'LocalApiService.cs'

# ---------------------------------------------------------------- 1. the helper
$anchor = @'
	private static readonly object DownloadGate = new object();
'@
$helper = @'
	/// <summary>
	/// 同步等待一个 async 操作完成，且**不捕获调用者的 SynchronizationContext**。
	///
	/// 为什么需要它：直接 <c>task.GetAwaiter().GetResult()</c> 在**拥有 SynchronizationContext 的线程**
	/// （Avalonia UI 线程就是）上阻塞时，只要 async 链捕获了上下文，continuation 就会被投回那个
	/// 正在阻塞等待的线程 —— 死锁（第 28 轮实测把启动器挂死的就是这个）。
	/// <c>Task.Run</c> 保证委托在**没有** SynchronizationContext 的线程池线程上开始执行，
	/// 因此链上任何 await 都不会捕获上下文，从任何线程调用都安全。
	/// </summary>
	internal static T RunSync<T>(Func<Task<T>> operation) => Task.Run(operation).GetAwaiter().GetResult();

	/// <inheritdoc cref="RunSync{T}(Func{Task{T}})"/>
	internal static void RunSync(Func<Task> operation) => Task.Run(operation).GetAwaiter().GetResult();

	private static readonly object DownloadGate = new object();
'@
PatchN $las $anchor $helper 1 'P-RUNSYNC 1/8 add the RunSync helpers'

# ---------------------------------------------------------------- 2. the ten call sites
PatchN $las 'ConfigStore.LoadAsync().GetAwaiter().GetResult()' 'RunSync(() => ConfigStore.LoadAsync())' 1 'P-RUNSYNC 2/8 ConfigStore.Load'
PatchN $las 'ConfigStore.SaveAsync(config).GetAwaiter().GetResult()' 'RunSync(() => ConfigStore.SaveAsync(config))' 1 'P-RUNSYNC 3/8 ConfigStore.Save'
PatchN $las 'BuildInstanceSummary(config, x).GetAwaiter().GetResult()' 'RunSync(() => BuildInstanceSummary(config, x))' 2 'P-RUNSYNC 4/8 BuildInstanceSummary'
PatchN $las 'JavaLocator.ScanAsync(null).GetAwaiter().GetResult()' 'RunSync(() => JavaLocator.ScanAsync(null))' 2 'P-RUNSYNC 5/8 JavaLocator.ScanAsync'
PatchN $las 'JavaLocator.FindAsync().GetAwaiter().GetResult()' 'RunSync(() => JavaLocator.FindAsync())' 1 'P-RUNSYNC 6/8 JavaLocator.FindAsync'
PatchN $las 'WriteJsonAsync(response, 400, new { ok = false, message = "日志目录不存在" }).GetAwaiter().GetResult();' 'RunSync(() => WriteJsonAsync(response, 400, new { ok = false, message = "日志目录不存在" }));' 1 'P-RUNSYNC 7/8 WriteJsonAsync 400'
PatchN $las 'WriteJsonAsync(response, 200, new { ok = true, path = dir }).GetAwaiter().GetResult();' 'RunSync(() => WriteJsonAsync(response, 200, new { ok = true, path = dir }));' 1 'P-RUNSYNC 7b/8 WriteJsonAsync 200'
PatchN $las 'WriteJsonAsync(response, 500, new { ok = false, message = ex.Message }).GetAwaiter().GetResult();' 'RunSync(() => WriteJsonAsync(response, 500, new { ok = false, message = ex.Message }));' 1 'P-RUNSYNC 8/8 WriteJsonAsync 500'

# ---------------------------------------------------------------- 3. regression test
$testFile = Join-Path $T 'RunSyncTests.cs'
$testSrc = @'
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 回归测试：<see cref="LocalApiService.RunSync{T}"/> 必须能从**拥有 SynchronizationContext 的线程**调用。
///
/// 复刻第 30 轮那个"阻塞且不处理自己消息队列"的上下文：直接 <c>GetAwaiter().GetResult()</c> 会死锁，
/// 而 RunSync 走线程池，链上不捕获上下文，因此能正常返回。
/// </summary>
[Collection("LocalApiService")]
public sealed class RunSyncTests
{
	private sealed class BlockedUiContext : SynchronizationContext
	{
		private int _posted;

		public int PostedCount => Volatile.Read(ref _posted);

		public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posted);

		public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();
	}

	private static async Task<int> YieldsOnceAsync()
	{
		// 一次真正会让出线程的 await：这是死锁的必要条件
		await Task.Yield();
		return 42;
	}

	[Fact]
	public void RunSync_CompletesEvenWhenTheCallingThreadOwnsASynchronizationContext()
	{
		BlockedUiContext context = new BlockedUiContext();
		int value = -1;
		Exception? failure = null;

		Thread worker = new Thread(new ThreadStart(delegate
		{
			SynchronizationContext.SetSynchronizationContext(context);
			try
			{
				value = LocalApiService.RunSync(() => YieldsOnceAsync());
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		}))
		{
			IsBackground = true
		};
		worker.Start();

		DateTime deadline = DateTime.UtcNow.AddSeconds(30);
		while (worker.IsAlive && DateTime.UtcNow < deadline)
		{
			Thread.Sleep(20);
		}

		Assert.False(worker.IsAlive, "RunSync 在拥有 SynchronizationContext 的线程上死锁了");
		Assert.Null(failure);
		Assert.Equal(42, value);
		Assert.True(context.PostedCount == 0,
			$"RunSync 不该把 continuation 投回调用者的上下文（Post 次数 = {context.PostedCount}）");
	}
}
'@
[IO.File]::WriteAllText($testFile, $testSrc, (New-Object Text.UTF8Encoding($false)))
$report.Add([pscustomobject]@{ label = 'P-TEST RunSync'; count = 1; ok = $true })
Write-Output '[OK] P-TEST RunSync regression test'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'runsync-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
