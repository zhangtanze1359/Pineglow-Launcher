param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-33: fix the log-file sharing that made a second launcher instance produce NO log at all
# (defect #12, part 3). Isolated copy only.
#
# LauncherLog.RolloverLocked() opened the day's log with `new StreamWriter(file, append: true)`,
# whose default sharing is FileShare.Read — it does NOT let another process open the same file.
# When a second instance started, its open threw IOException, RolloverLocked's catch set the writer
# to null, and every later Write() returned early. That instance therefore logged NOTHING — including
# the "[LocalApi] 启动失败" that would have explained why it had no API.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

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

$log = Join-Path $S 'LauncherLog.cs'

$old1 = @'
			_writer = new StreamWriter(file, append: true) { AutoFlush = true };
'@
$new1 = @'
			// FIX-LOGSHARE: StreamWriter 的默认共享模式是 FileShare.Read，**不允许**另一个进程
			// 打开同一个文件。第二个启动器实例因此打不开当天的日志，RolloverLocked 的 catch 把
			// _writer 置空，之后每次 Write() 都在开头直接 return —— 那个实例**一行日志都没有**，
			// 包括本该解释"为什么它没有 API"的那条 "[LocalApi] 启动失败"。
			// 改成 FileShare.ReadWrite，让并发实例可以一起追加写同一个文件。
			FileStream stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
			_writer = new StreamWriter(stream) { AutoFlush = true };
'@
Patch $log $old1 $new1 'P-LOGSHARE day log opened with FileShare.ReadWrite'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'logshare-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
