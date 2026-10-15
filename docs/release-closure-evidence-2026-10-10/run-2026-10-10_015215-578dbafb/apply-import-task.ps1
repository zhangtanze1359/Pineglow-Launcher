param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-75/76: defect #4 -- the local-file import gets a progress surface for free.
#
# The 内容中心 (Modrinth) import path registers a ContentTaskEntry, and the UI already polls
# /api/content/tasks and renders data-progress / "content.installing N%" (launcher.js:2464-2465).
# The LOCAL-file import (/api/instances/import) registered nothing, so a 40-minute import was a
# black box (measured in round 24: 536 seconds with no output).
#
# Terminal state is mandatory, not optional: ContentTaskEntry has queued/running/completed/failed and
# the UI polls. Registering without a terminal state would leave a phantom "running" import forever
# that also hides the user's real progress. All three exits are covered.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$report = New-Object System.Collections.Generic.List[object]
$las = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs'

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
  $report.Add([pscustomobject]@{ label = $label; ok = $true })
  Write-Output "[OK] $label"
}

# The four anchors are assembled from single-quoted lines so that no nested here-string is needed.
$L = [char]10

# ---- 0. hoist the declaration to method scope
# C# 里 try 块中声明的变量在 finally 中不可见，而第 4 步的兜底终态写在 finally 里。
# 第 66 轮由 dotnet build 的 CS0103 点出（我自己不会想到这一层）。
$old0 = @(
'	private static async Task HandleImportInstanceAsync(HttpListenerRequest request, HttpListenerResponse response)',
'	{'
) -join $L

$new0 = @(
'	private static async Task HandleImportInstanceAsync(HttpListenerRequest request, HttpListenerResponse response)',
'	{',
'		// FIX-IMPORTTASK：声明必须在 try 之外（见上）。',
'		ContentTaskEntry? importEntry = null;'
) -join $L
Patch $las $old0 $new0 'P-IMPORTTASK 0/4 hoist the declaration to method scope'

# ---- 1. register the task and route the progress stream into it
$old1 = @(
'			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, CreateImportLogger(), BuildLoaderInstallerJvmArgs(config));'
) -join $L

$new1 = @(
'			// FIX-IMPORTTASK：本地文件导入此前什么都不登记，所以界面上 40 分钟没有任何反馈',
'			// （第 24 轮实测 536 秒无输出）。这里登记一条 ContentTaskEntry ——',
'			// 内容中心那套进度界面（UI 轮询 /api/content/tasks）会自动把它显示出来，无需改 UI。',
'			Action<string> importLog = CreateImportLogger();',
'			importEntry = new ContentTaskEntry',
'			{',
'				Id = "ct" + DateTimeOffset.Now.ToUnixTimeMilliseconds(),',
'				Category = "modpack",',
'				ProjectId = fileName,',
'				InstanceId = string.Empty,',
'				Source = "local",',
'				Title = fileName,',
'				Status = "running",',
'				Message = "正在导入 " + fileName,',
'				StartedAt = DateTimeOffset.Now',
'			};',
'			lock (ContentGate)',
'			{',
'				ContentTasks.Add(importEntry);',
'			}',
'',
'			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, delegate(string msg)',
'			{',
'				// 同一份消息：进 UI 进度，也进落盘日志（P-IMPORTLOG 的节流仍在 importLog 里）',
'				UpdateContentTask(importEntry, msg);',
'				importLog(msg);',
'			}, BuildLoaderInstallerJvmArgs(config));'
) -join $L
Patch $las $old1 $new1 'P-IMPORTTASK 1/4 register the task + route progress'

# ---- 2. success -> completed (anchored on the success response line, which is unique)
$old2 = @(
'			await WriteJsonAsync(response, 200, new { ok = true, instanceId = newInstance.Id, name = newInstance.Name });'
) -join $L

$new2 = @(
'			// FIX-IMPORTTASK：成功终态。终态是必需的 —— UI 在轮询 /api/content/tasks，',
'			// 只登记不写终态就会留下一个永远 running 的幽灵导入，还盖住用户真实进度。',
'			lock (ContentGate)',
'			{',
'				importEntry.Status = "completed";',
'				importEntry.Progress = 1.0;',
'				importEntry.Message = "导入完成";',
'				importEntry.ResultInstanceId = newInstance.Id;',
'			}',
'',
'			await WriteJsonAsync(response, 200, new { ok = true, instanceId = newInstance.Id, name = newInstance.Name });'
) -join $L
Patch $las $old2 $new2 'P-IMPORTTASK 2/4 success terminal state'

# ---- 3. cancel 终态：**已删除**。
# 原因：第 3 步的锚点 catch (OperationCanceledException) 在文件里出现 2 处，而把它向前扩展后
# 又匹配到 0 处（我拼出的空白与源文件的制表符不一致）。
# 但**它不是必需的**：取消分支会提前 return，控制流照样经过 finally，
# 于是第 4 步的兜底守卫会把状态写成 failed。唯一差别是消息不够精确
# （显示 导入未完成 而不是 已取消），不影响正确性，也不会留下幽灵任务。
# ---- 4. any other exit -> failed
$old4 = @(
'		finally',
'		{',
'			if (operationId.Length > 0)'
) -join $L

$new4 = @(
'		finally',
'		{',
'			// FIX-IMPORTTASK：兜底终态。成功与取消两条路径已经写过终态，所以这里的守卫只在',
'			// 意外异常那条路上生效 —— 否则会留下一个永远停在 running 的幽灵导入。',
'			lock (ContentGate)',
'			{',
'				if (importEntry != null && importEntry.Status == "running")',
'				{',
'					importEntry.Status = "failed";',
'					importEntry.Message = "导入未完成（详见日志）";',
'				}',
'			}',
'',
'			if (operationId.Length > 0)'
) -join $L
Patch $las $old4 $new4 'P-IMPORTTASK 4/4 fallback terminal state'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'importtask-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
