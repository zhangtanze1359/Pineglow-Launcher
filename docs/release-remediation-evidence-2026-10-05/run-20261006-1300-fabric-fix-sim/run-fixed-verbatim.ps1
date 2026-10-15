#Requires -Version 5.1
<#
.SYNOPSIS
    修复组重跑（第 48 轮续 2）：用 **System.Diagnostics.ProcessStartInfo.Arguments 原样传递**命令行
    （PowerShell 的 Start-Process -ArgumentList 会重新加引号/整体当一个参数；.cmd 文件又受 ANSI 编码影响中文路径）。
.DESCRIPTION
    只做一件事：拿"产品生成的那条命令行"，在 `-Dfile.encoding=UTF-8` 后**只插入** `-Djava.library.path=<natives>`，
    原样跑起来，然后看游戏日志是否进入渲染管线（对比基线的崩溃）。
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
$logPath = Join-Path $outDir 'step3.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }

$runDir = (Resolve-Path -LiteralPath $RunDir).Path
$instDir = (Get-ChildItem (Join-Path $runDir 'cold\data\.minecraft\instances') -Directory | Select-Object -First 1).FullName
$raw = [IO.File]::ReadAllText($ArgsFile, [Text.Encoding]::UTF8).Trim()
$javaExe = ''
if ($raw -match '^\s*"([^"]+)"') { $javaExe = $Matches[1]; $raw = $raw.Substring($Matches[0].Length).TrimStart() }
elseif ($raw -match '^\s*(\S+\.exe)') { $javaExe = $Matches[1]; $raw = $raw.Substring($Matches[1].Length).TrimStart() }
Log ("实例目录=" + $instDir)
Log ("java.exe=" + $javaExe)
Log ("参数长度=" + $raw.Length)
$hasLib = ($raw -match '-Djava\.library\.path')
Log ("参数含 -Djava.library.path=" + $hasLib)

$gameLog = Join-Path $instDir 'logs\latest.log'
if (Test-Path $gameLog) { Remove-Item $gameLog -Force }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $javaExe
$psi.Arguments = $raw                      # 原样传递，不重新加引号
$psi.WorkingDirectory = $instDir
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$p = [System.Diagnostics.Process]::Start($psi)
Log ("已启动 java pid=" + $p.Id)

Start-Sleep -Seconds 6
$chk = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" -ErrorAction SilentlyContinue
if ($chk) { Log ("实际命令行含 -Djava.library.path=" + ([string]$chk.CommandLine -match '-Djava\.library\.path')) } else { Log '进程已消失（6 秒内）' }

$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $WaitSeconds) {
    Start-Sleep -Seconds 10
    $alive = -not $p.HasExited
    $tail = ''
    if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
    Log ("  {0,4}s  java存活={1}  日志: {2}" -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(110, $tail.Length)))
    if (-not $alive) { break }
}
$lines = @(); if (Test-Path $gameLog) { $lines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
@($lines) | Set-Content (Join-Path $outDir 'fixed3-game.log') -Encoding utf8
$m = [ordered]@{
    fabricLoader = @($lines | Where-Object { $_ -match 'with Fabric Loader' }).Count
    mods = @($lines | Where-Object { $_ -match 'Loading \d+ mods' }).Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError' }).Count
    logLines = $lines.Count
}
Log ("标记: fabric={0} mods={1} lwjgl={2} sound={3} atlas={4} 崩溃={5} 行数={6}" -f $m.fabricLoader, $m.mods, $m.lwjgl, $m.sound, $m.atlas, $m.crash, $m.logLines)
$verdict = if ($m.lwjgl -gt 0 -and $m.atlas -gt 0 -and $m.crash -eq 0) { 'PASS：补上 -Djava.library.path 后进入渲染管线（不再崩溃）' } elseif ($m.crash -gt 0) { 'FAIL：仍崩溃' } else { 'UNKNOWN' }
Log ('判定: ' + $verdict)
$res = [ordered]@{ generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'); runDir = $runDir; instanceDir = $instDir; javaExe = $javaExe; argsHasLibraryPath = $hasLib; markers = $m; verdict = $verdict }
$res | ConvertTo-Json -Depth 5 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
