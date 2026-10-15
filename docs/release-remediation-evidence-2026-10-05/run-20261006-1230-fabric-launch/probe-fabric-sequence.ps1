#Requires -Version 5.1
<#
.SYNOPSIS
    判定"Fabric 实例启动崩溃"到底怪谁：抓**每一个** java 进程的启动序列与主类，
    同时读版本 JSON 的 mainClass。
.DESCRIPTION
    第 47 轮观察到的崩溃是 `UnsatisfiedLinkError: Failed to locate library: lwjgl.dll`。
    但我抓到的那条命令行主类是 `dev.kostromdan.mods.crash_assistant...Boot`（整合包自带的 CrashAssistant 模组），
    且命令行里**没有** `-Djava.library.path`。所以必须分清：
      · 启动器自己拉起的那个 JVM 有没有 `-Djava.library.path`？
      · CrashAssistant 是否会**重新拉起**一个 JVM（--args-file），并在重写参数时丢掉它？
    本脚本每 200ms 采样一次 java 进程，记录 (时间, pid, 主类, 是否有 java.library.path, 父进程)，持续 25 秒。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ColdDir,
    [string] $JsonPath,
    [int] $Seconds = 25
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = if ($JsonPath) { Split-Path -Parent $JsonPath } else { $env:TEMP }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; Write-Host $l; [IO.File]::AppendAllText((Join-Path $outDir 'sequence.log'), $l + "`r`n", (New-Object Text.UTF8Encoding($false))) }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$cold = (Resolve-Path -LiteralPath $ColdDir).Path
if (-not (Test-Path (Join-Path $cold 'Pineglow.Launcher.exe'))) { Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force }
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden
$token = $null
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $token }
$state = Invoke-RestMethod "http://127.0.0.1:$port/api/state" -Headers $hdr -TimeoutSec 30
$inst = @($state.instances) | Where-Object { $_.loaderType -match 'Fabric' } | Select-Object -First 1
$instDir = [string]$inst.gameDirectory
Log ("实例 {0} loader={1} version={2}" -f $inst.name, $inst.loaderType, $inst.minecraftVersion)

# 版本 JSON 的 mainClass（启动器要用它）
$verId = [string]$inst.minecraftVersion
$vj = Join-Path $instDir ("versions\" + $verId + "\" + $verId + ".json")
if (Test-Path $vj) { $vd = [IO.File]::ReadAllText($vj, [Text.Encoding]::UTF8) | ConvertFrom-Json; Log ("版本 JSON mainClass=" + $vd.mainClass + "  inheritsFrom=" + $vd.inheritsFrom) }

$launch = Invoke-RestMethod "http://127.0.0.1:$port/api/instances/$($inst.id)/launch" -Method Post -Headers $hdr -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes('{}')) -TimeoutSec 120
Log ("launch -> " + ($launch | ConvertTo-Json -Compress) + "   (启动器报告的 pid=" + $launch.pid + ")")

$seen = @{}
$rows = New-Object System.Collections.Generic.List[object]
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
    Start-Sleep -Milliseconds 200
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    foreach ($j in $javas) {
        $key = [string]$j.ProcessId
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        $cl = [string]$j.CommandLine
        $main = ''
        if ($cl -match '([\w\.\$]+)\s+(--args-file|--username|--version|--gameDir|$)') { $main = $Matches[1] }
        $hasLib = ($cl -match '-Djava\.library\.path')
        $libVal = ''; if ($hasLib -and $cl -match '-Djava\.library\.path=([^ ]+)') { $libVal = $Matches[1] }
        $row = [pscustomobject]@{ sec = [math]::Round($sw.Elapsed.TotalSeconds, 1); pid = [int]$j.ProcessId; parent = [int]$j.ParentProcessId; hasLibPath = $hasLib; libPath = $libVal; cmdLen = $cl.Length; cmd = $cl }
        $rows.Add($row) | Out-Null
        Log ("  [{0,5}s] 新 java pid={1} parent={2} java.library.path={3} cmdLen={4}" -f $row.sec, $row.pid, $row.parent, $hasLib, $cl.Length)
        [IO.File]::WriteAllText((Join-Path $outDir ("cmdline-" + $j.ProcessId + ".txt")), $cl, (New-Object Text.UTF8Encoding($false)))
    }
}
Log ("共观察到 " + $rows.Count + " 个 java 进程")
foreach ($r in $rows) {
    $tail = $r.cmd.Substring([Math]::Max(0, $r.cmd.Length - 200))
    Log ("  pid={0} parent={1} hasLibPath={2} 结尾=...{3}" -f $r.pid, $r.parent, $r.hasLibPath, $tail)
}
$res = [ordered]@{ generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'); instance = $inst.name; loaderType = [string]$inst.loaderType; versionJsonMainClass = $(if (Test-Path $vj) { ([IO.File]::ReadAllText($vj, [Text.Encoding]::UTF8) | ConvertFrom-Json).mainClass } else { '' }); launcherReportedPid = $launch.pid; processes = $rows }
if ($JsonPath) { $res | ConvertTo-Json -Depth 5 | Set-Content -Path $JsonPath -Encoding utf8 }
foreach ($r in $rows) { Stop-Process -Id $r.pid -Force -ErrorAction SilentlyContinue }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
