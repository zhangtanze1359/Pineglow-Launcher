param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-67/71: the 500-without-a-log half of defect #15 (its other half was withdrawn in record 70).
#
# Measured in record 64: with an instance whose own directory is missing, prelaunch-check reported
# ok=True / 0 errors / 0 warnings, the launch then returned HTTP 500, and the log stayed at 2 lines.
#
# Two independent causes, both fixed here:
#   ① the dispatcher's catch writes only to Debug.WriteLine, which never reaches launcher-*.log —
#      so EVERY 500 is invisible in the log the user is told to send;
#   ② prelaunch-check only tested the SHARED game dir, never the instance's own directory.
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

# ① every 500 now reaches the log file
$old1 = @'
		catch (Exception ex)
		{
			Debug.WriteLine($"[LocalApi] api error: {ex}");
			await WriteJsonAsync(response, 500, new { error = ex.Message });
		}
'@
$new1 = @'
		catch (Exception ex)
		{
			// FIX-APILOG: 此前这里只写 Debug.WriteLine —— 它**不进 launcher-*.log**，
			// 于是任何 500 在用户能提供的日志里都查不到（第 52 轮实测：实例目录缺失时
			// launch 返回 500，而日志只有 2 行）。补一条落盘的错误日志。
			Debug.WriteLine($"[LocalApi] api error: {ex}");
			LauncherLog.Error($"[LocalApi] {method} {path} 失败: {ex.Message}", ex);
			await WriteJsonAsync(response, 500, new { error = ex.Message });
		}
'@
Patch $las $old1 $new1 'P-APILOG every 500 reaches the log file'

# ② 已删除：它针对的"prelaunch 假阴性"在记录 70 里被撤回——产品在自检之前**已经把实例目录创建出来了**，
# 报 ok=true 是正确的。留着这段就是无用的死代码，所以从补丁里去掉（不再改动 prelaunch-check）。

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'apilog-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
