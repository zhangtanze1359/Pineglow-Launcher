param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-132: defect #12's zombie -- a single-instance guard.
#
# Record 131 established the root cause by a discriminating experiment: give two instances their OWN
# working ports and the second STILL renders nothing (0 msedgewebview2 children), while the fixture
# contains exactly ONE WebView2 user-data folder:
#     <exeDir>\Pineglow.Launcher.exe.WebView2\EBWebView
# So the second instance's window shell is created (App.cs:44 runs unconditionally -- the catch above it
# has no return) but its WebView never initializes. The process then stays alive doing nothing.
#
# That is why the guard has to sit BEFORE the window is created, and why "exit when the port is busy"
# would NOT work: with a free port the second instance never reaches that code path.
#
# Behaviour chosen (minimum, and reversible): the second instance logs a line and shuts down.
# A nicer variant -- raise the first instance's window -- needs IPC and is deliberately NOT done here.
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

# ---- 1. the mutex field
$old1 = @(
'public sealed class App : Application',
'{',
'	public override void Initialize()'
) -join $L
$new1 = @(
'public sealed class App : Application',
'{',
'	// FIX-SINGLEINSTANCE（缺陷 12）：持有"本应用只跑一个实例"的具名互斥体。',
'	// 为什么需要它：第二个实例与第一个**争用同一个 WebView2 用户数据目录**',
'	// （<exeDir>\Pineglow.Launcher.exe.WebView2），结果是窗口壳建出来了、里面什么都不渲染，',
'	// 而进程不会自己退出（第 121 轮的判别实验：给它一个**可用的独立端口**，它照样渲染不出来）。',
'	private static Mutex? _singleInstanceMutex;',
'',
'	public override void Initialize()'
) -join $L
Patch $app $old1 $new1 'P-SINGLEINSTANCE 1/3 the mutex field'

# ---- 2. using System.Threading
$old2 = @(
'using System;',
'using Avalonia;'
) -join $L
$new2 = @(
'using System;',
'using System.Threading;',
'using Avalonia;'
) -join $L
Patch $app $old2 $new2 'P-SINGLEINSTANCE 2/3 using System.Threading'

# ---- 3. the guard, before the window is created
$old3 = @(
'		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)',
'		{',
'			classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();'
) -join $L
$new3 = @(
'		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime)',
'		{',
'			// FIX-SINGLEINSTANCE：**必须在创建窗口之前**拦住第二个实例 ——',
'			// 一旦走到 new WebShellWindow()，它的 WebView2 就会去抢那个被占用的用户数据目录，',
'			// 于是留下一个"空窗口壳 + 不退出"的进程。',
'			_singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Local\PineglowLauncher.SingleInstance", createdNew: out bool createdNew);',
'			if (!createdNew)',
'			{',
'				LauncherLog.Info("已有实例在运行，本进程退出（单实例守卫）");',
'				Console.Error.WriteLine("Pineglow Launcher 已在运行。");',
'				// 绕开 Avalonia 的关闭流程（第 126 轮实测：先 base() 再 Shutdown() 无效，退出码仍是 0xE0434352）。',
'				// 注意：Environment.Exit 不 flush StreamWriter —— 所以必须同时看那一行日志还在不在。',
'				Environment.Exit(0);',
'				classicDesktopStyleApplicationLifetime.Shutdown();',
'				return;',
'			}',
'',
'			classicDesktopStyleApplicationLifetime.MainWindow = new WebShellWindow();'
) -join $L
Patch $app $old3 $new3 'P-SINGLEINSTANCE 3/3 the guard'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'singleinstance-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
