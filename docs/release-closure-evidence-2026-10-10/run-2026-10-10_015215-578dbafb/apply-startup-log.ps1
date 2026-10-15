param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-57 / restructured round-141: give the launcher a readable failure path (defect #10).
#
# Verified in record 56: the product has NO global exception handler anywhere, and App.cs:44
# `new WebShellWindow()` is not protected — while the immediately preceding LocalApiService.Start()
# at :31 IS wrapped with a log. So a failure while constructing the shell (the most likely cause
# being a missing/broken WebView2 runtime) crashes with no log line at all.
#
# This patch does NOT try to keep running. It makes the failure *diagnosable*:
#   - a global AppDomain/TaskScheduler handler so every future unhandled exception reaches the log;
#   - the shell construction wrapped so its failure is logged with an explanation before rethrowing.
#
# WHY THIS IS TWO STEPS (round 140): the original single-step version anchored on a THREE-LINE block
# (`if (…)` + `{` + `MainWindow = …`). P-SINGLEINSTANCE inserts its guard into exactly that block, so
# whichever of the two ran first destroyed the other's anchor — both orders were tried and both failed.
# The fix is not "a smaller anchor" but TWO anchors: the two things this patch inserts go to DIFFERENT
# places (the global handlers go BEFORE the `if`; the try/catch wraps the `MainWindow` line). Each step
# now anchors on ONE unique line, so insertions between them cannot break either one.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$report = New-Object System.Collections.Generic.List[object]
$app = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher\App.cs'
$L = [char]10

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

# ---- 1/2: the two global handlers, inserted BEFORE the lifetime check (single-line anchor)
$old1 = @(
'		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)'
) -join $L
$new1 = @(
'		// FIX-STARTUPLOG: 全树此前没有任何全局异常处理，任何未捕获异常都不会留下日志。',
'		// 这两条让"以后任何一次崩溃"至少能在 launcher-*.log 里被看到。',
'		AppDomain.CurrentDomain.UnhandledException += (_, e) =>',
'		{',
'			LauncherLog.Error("[Fatal] 未处理的异常: " + (e.ExceptionObject?.ToString() ?? "(null)"));',
'		};',
'		System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>',
'		{',
'			LauncherLog.Error("[Fatal] 未观察的任务异常: " + e.Exception);',
'			e.SetObserved();',
'		};',
'',
'		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)'
) -join $L
Patch $app $old1 $new1 'P-STARTUPLOG 1/2 global handlers (single-line anchor)'

# ---- 2/2: wrap the shell construction (single-line anchor, independent of what sits above it)
$old2 = @(
'			classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();'
) -join $L
$new2 = @(
'			// FIX-STARTUPLOG: 同方法里 LocalApiService.Start()（第 31 行）有 try/catch 与日志，',
'			// 紧邻的这里却没有 —— 而窗口壳构造失败最常见的原因正是 WebView2 运行时缺失/损坏，',
'			// 那种情况下用户只会看到进程消失。这里补上日志后再抛，保持"启动失败就退出"的语义不变。',
'			try',
'			{',
'				classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();',
'			}',
'			catch (Exception ex)',
'			{',
'				LauncherLog.Error(',
'					"[Fatal] 主窗口创建失败（最常见原因是缺少或损坏的 WebView2 运行时；"',
'					+ "请安装 Microsoft Edge WebView2 Runtime 后重试）: " + ex.Message, ex);',
'				throw;',
'			}'
) -join $L
Patch $app $old2 $new2 'P-STARTUPLOG 2/2 protected shell construction (single-line anchor)'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'startuplog-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
