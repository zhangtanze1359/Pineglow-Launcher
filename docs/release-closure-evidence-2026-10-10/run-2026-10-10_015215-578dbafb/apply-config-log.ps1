param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-65: stop the config-load failure from being silent (defect #14, part of the
# "失败了但不说" family alongside #10 and #15).
#
# ConfigStore.LoadAsync ends in a bare `catch` that returns a default AppConfig with no log line and
# no trace of what went wrong. Measured in record 63: a config truncated to 60% still starts the
# launcher, silently swaps in defaults (1 instance instead of 7), leaves the corrupt file on disk
# untouched, and writes NOTHING to the log.
#
# Falling back to defaults is right. Doing it silently is not. This patch keeps the fallback and adds:
#   - a LauncherLog.Error naming the file and the exception;
#   - the corrupt file preserved under a timestamped name, so it can be inspected/recovered.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$report = New-Object System.Collections.Generic.List[object]
$cfg = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services\ConfigStore.cs'

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

$old = @'
		catch
		{
			AppConfig obj6 = new AppConfig
			{
				GameDirectory = PlatformPaths.GetDefaultMinecraftDirectory(),
				UseAutoMemory = true,
				UpdateUrl = DefaultUpdateUrl
			};
			InstanceService.EnsureDefaultInstance(obj6);
			AccountService.EnsureAccounts(obj6);
			return obj6;
		}
'@
$new = @'
		catch (Exception ex)
		{
			// FIX-CONFIGLOG: 回退到默认值是对的，**沉默是错的**。此前这里是一个裸 catch：
			// 配置损坏时账号/实例/设置被悄悄换成默认值，日志里却一个字都没有（第 51 轮实测）。
			// 现在：① 写一条带路径与异常的日志；② 把损坏的文件改名保留，便于查看与恢复。
			LauncherLog.Error("[Config] 读取配置失败，已回退到默认配置: " + configPath, ex);
			try
			{
				// FIX-CONFIGLOG：损坏配置**只保留一份**——若已存在任何 .corrupt-* 副本就跳过。
				// 此前写成"每次加载失败都保留"，而 LoadAsync 一次运行会被调用多次，
				// 副本会随调用次数累积（第 53 轮实测一次运行出现两份）。
				string cfgDir = Path.GetDirectoryName(configPath) ?? ".";
				bool alreadyKept = Directory.EnumerateFiles(cfgDir, Path.GetFileName(configPath) + ".corrupt-*").Any();
				if (File.Exists(configPath) && !alreadyKept)
				{
					string keep = configPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
					File.Copy(configPath, keep, overwrite: false);
					LauncherLog.Error("[Config] 原文件已保留为: " + keep);
				}
			}
			catch (Exception keepEx)
			{
				LauncherLog.Warn("[Config] 保留损坏配置失败: " + keepEx.Message);
			}

			AppConfig obj6 = new AppConfig
			{
				GameDirectory = PlatformPaths.GetDefaultMinecraftDirectory(),
				UseAutoMemory = true,
				UpdateUrl = DefaultUpdateUrl
			};
			InstanceService.EnsureDefaultInstance(obj6);
			AccountService.EnsureAccounts(obj6);
			return obj6;
		}
'@
Patch $cfg $old $new 'P-CONFIGLOG log + preserve the corrupt config'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'configlog-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
