param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-93: defect #13 part 2 -- wire Java auto-install to the LAUNCH path.
#
# README lists "Java：自动检测与自动安装" as a headline feature. Measured in records 63/70: the
# auto-install exists but is reachable ONLY from modpack import (JavaRuntimeService.InstallAsync has
# exactly one caller, InstancePackService.cs:757). On the ordinary path -- install a vanilla version,
# click 开始游戏 -- a machine with no Java gets:
#   "未找到可用的 Java 运行时（该版本需要 Java 21），请先在设置中指定 Java 路径"
# ...while the settings page offers no way to install one (and, before P-JAVALABEL, even claimed to be
# using a "bundled JRE" that does not exist).
#
# This patch makes the launch path do what the import path already does: install the required Java.
# It deliberately keeps the ORIGINAL message as the fallback when the download fails, so a user with
# no network still gets the actionable "请在设置中指定 Java 路径" guidance instead of a raw download error.
#
# ORDER: this patch uses RunSync, so apply-runsync.ps1 must run first (see APPLY-ORDER.md).
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$report = New-Object System.Collections.Generic.List[object]
$las = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs'
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

# ---- 1. the helper, inserted right before AppendJvmArgument
$old1 = @(
'	private static string AppendJvmArgument(string existing, string argument)'
) -join $L
$new1 = @(
'	/// <summary>',
'	/// FIX-JAVAAUTO（缺陷 13 ②）：启动路径上的 Java 自动安装。',
'	/// 此前自动安装只挂在整合包导入上（JavaRuntimeService.InstallAsync 全仓只有一个调用点），',
'	/// 于是"装个原版 → 点开始游戏"这条最常见的路径上，没 Java 的用户只会拿到一句"请先在设置中指定路径"，',
'	/// 而设置页没有安装入口 —— 而 README 把「Java：自动检测与自动安装」列为一级功能。',
'	/// 这里改成与导入路径一致：先自动装；**装不上时仍抛出原来那句可操作的提示**。',
'	/// </summary>',
'	private static string InstallRequiredJavaOrThrow(int requiredMajor, string fallbackMessage)',
'	{',
'		try',
'		{',
'			LauncherLog.Info($"[Java] 未找到 Java {requiredMajor}，正在自动下载…");',
'			JavaInstallation installed = RunSync(() => new JavaRuntimeService(',
'				ApiHttp,',
'				msg => LauncherLog.Info("[Java] " + msg))',
'				.InstallAsync(requiredMajor, new Progress<JavaRuntimeProgress>(p => LauncherLog.Info("[Java] " + p.Message))));',
'			LauncherLog.Info("[Java] 自动安装完成: " + installed.JavaPath);',
'			return installed.JavaPath;',
'		}',
'		catch (Exception ex)',
'		{',
'			// 自动安装失败（多半是网络）时，保留原来那句可操作的提示，并把失败原因附上。',
'			LauncherLog.Warn("[Java] 自动安装失败: " + ex.Message);',
'			throw new InvalidOperationException(fallbackMessage + "（自动下载 Java 也失败了：" + ex.Message + "）", ex);',
'		}',
'	}',
'',
'	private static string AppendJvmArgument(string existing, string argument)'
) -join $L
Patch $las $old1 $new1 'P-JAVAAUTO 1/4 the helper'

# ---- 2. "found Java, but wrong version" -> install instead of refusing
$old2 = @(
'			throw new InvalidOperationException(',
'				$"该版本需要 Java {requiredMajor}，但本机只找到 Java {(any.VersionMajor > 0 ? any.VersionMajor.ToString() : "未知版本")}（{any.Path}）。"',
'				+ $"请在设置中指定 Java {requiredMajor} 的路径（整合包导入会自动安装所需 Java）。");'
) -join $L
$new2 = @(
'			return InstallRequiredJavaOrThrow(requiredMajor,',
'				$"该版本需要 Java {requiredMajor}，但本机只找到 Java {(any.VersionMajor > 0 ? any.VersionMajor.ToString() : "未知版本")}（{any.Path}）。"',
'				+ $"请在设置中指定 Java {requiredMajor} 的路径。");'
) -join $L
Patch $las $old2 $new2 'P-JAVAAUTO 2/4 wrong version -> auto-install'

# ---- 3. "no Java at all" -> install instead of refusing
$old3 = @(
'		throw new InvalidOperationException($"未找到可用的 Java 运行时（该版本需要 Java {requiredMajor}），请先在设置中指定 Java 路径");'
) -join $L
$new3 = @(
'		return InstallRequiredJavaOrThrow(requiredMajor,',
'			$"未找到可用的 Java 运行时（该版本需要 Java {requiredMajor}），请先在设置中指定 Java 路径");'
) -join $L
Patch $las $old3 $new3 'P-JAVAAUTO 3/4 no Java -> auto-install'

# ---- 4. configured paths must EXIST, otherwise they never reach the auto-install
# Found in record 94: the first two branches return the configured path without checking it, so a
# JavaPath that was valid once and is gone now (machine moved, cache cleared, JDK uninstalled) returns
# immediately and never reaches the scan + auto-install below -- the launch ends in a 500.
$old4 = @(
'		if (!string.IsNullOrWhiteSpace(instance.JavaPath))',
'		{',
'			return instance.JavaPath;',
'		}',
'',
'		if (!string.IsNullOrWhiteSpace(config.JavaPath))',
'		{',
'			return config.JavaPath;',
'		}'
) -join $L
$new4 = @(
'		// FIX-JAVAAUTO2（缺陷 13 ② 的覆盖缺口，记录 94）：配置里的路径必须存在才算数。',
'		// 否则"配置写着某个路径、该路径后来失效"（换机器 / 清了缓存 / 卸载了 JDK）会在第一行就返回，',
'		// 永远走不到下面的扫描与自动安装，启动以 500 结束。',
'		if (!string.IsNullOrWhiteSpace(instance.JavaPath) && File.Exists(instance.JavaPath))',
'		{',
'			return instance.JavaPath;',
'		}',
'',
'		if (!string.IsNullOrWhiteSpace(config.JavaPath) && File.Exists(config.JavaPath))',
'		{',
'			return config.JavaPath;',
'		}'
) -join $L
Patch $las $old4 $new4 'P-JAVAAUTO 4/4 configured paths must exist'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'javaauto-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
