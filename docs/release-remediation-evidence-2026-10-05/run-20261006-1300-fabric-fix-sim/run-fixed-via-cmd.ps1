#Requires -Version 5.1
<#
.SYNOPSIS
    修复组重跑（第 48 轮续）：用 **cmd.exe 原样传递命令行**，避免 PowerShell 5.1 的 ArgumentList 重新加引号/整体当一个参数。
.DESCRIPTION
    上一轮 Start-Process -ArgumentList <单个字符串> 把整串当成一个参数 → java 报 ClassNotFoundException（不是游戏问题）。
    本脚本把"同一条命令行 + 只插入 -Djava.library.path"写进 .cmd 文件，用 cmd /c 执行，并观察游戏日志。
#>
param(
    [Parameter(Mandatory)][string] $RunDir,
    [Parameter(Mandatory)][string] $ArgsFile,
    [Parameter(Mandatory)][string] $JsonPath,
    [int] $WaitSeconds = 150
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'step2.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }

$runDir = (Resolve-Path -LiteralPath $RunDir).Path
$instDir = (Get-ChildItem (Join-Path $runDir 'cold\data\.minecraft\instances') -Directory | Select-Object -First 1).FullName
$argsRaw = [IO.File]::ReadAllText($ArgsFile, [Text.Encoding]::UTF8).Trim()
Log ("实例目录=" + $instDir)
Log ("参数长度=" + $argsRaw.Length)
$javaExe = ''
if ($argsRaw -match '^\s*"([^"]+)"') { $javaExe = $Matches[1]; $argsRaw = $argsRaw.Substring($Matches[0].Length).TrimStart() }
elseif ($argsRaw -match '^\s*(\S+\.exe)') { $javaExe = $Matches[1]; $argsRaw = $argsRaw.Substring($Matches[1].Length).TrimStart() }
Log ("java.exe=" + $javaExe + "  剩余参数长度=" + $argsRaw.Length)

# 清掉旧的 latest.log（基线已改名），保证读到的是本次
$gameLog = Join-Path $instDir 'logs\latest.log'
if (Test-Path $gameLog) { Remove-Item $gameLog -Force }

$cmdFile = Join-Path $outDir 'run-fixed.cmd'
$stdout = Join-Path $outDir 'fixed2-stdout.log'
$stderr = Join-Path $outDir 'fixed2-stderr.log'
$content = '@echo off' + "`r`n" + '"' + $javaExe + '" ' + $argsRaw + ' > "' + $stdout + '" 2> "' + $stderr + '"' + "`r`n"
[IO.File]::WriteAllText($cmdFile, $content, (New-Object Text.ASCIIEncoding()))
Log ("已写 " + $cmdFile)

$p = Start-Process -FilePath 'cmd.exe' -ArgumentList ('/c "' + $cmdFile + '"') -WorkingDirectory $instDir -PassThru -WindowStyle Hidden
Log ("cmd pid=" + $p.Id + "（内层 java 由它拉起）")
Start-Sleep -Seconds 5
$javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
$mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
Log ("java 进程数=" + $mine.Count + "  " + (($mine | ForEach-Object { "pid=" + $_.ProcessId + " hasLib=" + ([string]$_.CommandLine -match '-Djava\.library\.path') }) -join '; '))

$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $WaitSeconds) {
    Start-Sleep -Seconds 10
    $alive = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
    $tail = ''
    if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
    Log ("  {0,4}s  java={1}  日志: {2}" -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(110, $tail.Length)))
    if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
}
$lines = @(); if (Test-Path $gameLog) { $lines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
@($lines) | Set-Content (Join-Path $outDir 'fixed2-game.log') -Encoding utf8
$m = [ordered]@{
    fabricLoader = @($lines | Where-Object { $_ -match 'with Fabric Loader' }).Count
    mods = @($lines | Where-Object { $_ -match 'Loading \d+ mods' }).Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError' }).Count
    logLines = $lines.Count
}
Log ("标记: fabric={0} mods={1} lwjgl={2} sound={3} atlas={4} 崩溃={5} 日志行数={6}" -f $m.fabricLoader, $m.mods, $m.lwjgl, $m.sound, $m.atlas, $m.crash, $m.logLines)
$verdict = if ($m.lwjgl -gt 0 -and $m.atlas -gt 0 -and $m.crash -eq 0) { 'PASS：补上 -Djava.library.path 后进入渲染管线（不再崩溃）' } elseif ($m.crash -gt 0) { 'FAIL：仍崩溃' } else { 'UNKNOWN' }
Log ('判定: ' + $verdict)
$res = [ordered]@{ generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'); runDir = $runDir; instanceDir = $instDir; javaExe = $javaExe; markers = $m; verdict = $verdict; stderrTail = $(if (Test-Path $stderr) { @(Get-Content $stderr -Tail 10 | ForEach-Object { [string]$_ }) } else { @() }) }
$res | ConvertTo-Json -Depth 5 | Set-Content -Path $JsonPath -Encoding utf8
@(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
exit 0
