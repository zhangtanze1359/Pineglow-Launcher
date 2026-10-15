#Requires -Version 5.1
<#
.SYNOPSIS
    **不用 GUI 进世界**（阶段 2+3，第 56 轮续）：复用已装好 1.20.1 的冷目录 —— 改配置、放世界、启动、看日志。
.DESCRIPTION
    上一版脚本的两个**我自己的** bug：① `POST /api/versions/download` 是**异步排队**（`{"status":"queued"}`），
    我没轮询到终态就停启动器，把任务掐了；② `GET /api/downloads` 返回 `{"tasks":[...]}`（对象不是数组），
    我按数组取 `id` 永远匹配不到 ⇒ 空转到超时。本版只做阶段 2+3，且直接复用已安装好的目录。
#>
param(
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][string] $InstanceId,
    [Parameter(Mandatory)][string] $JavaExe,
    [Parameter(Mandatory)][string] $WorldDir,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorldName = 'QuickWorld',
    [string] $UserName = 'WorldTester',
    [int] $ObserveSeconds = 150
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'step2.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }
function Start-Launcher([string]$cold, [int]$port) {
    $env:PINEGLOW_API_PORT = "$port"
    $pr = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
    $tk = $null; $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $tk = $Matches[1] }; break } } catch { }
    }
    return [pscustomobject]@{ proc = $pr; token = $tk }
}
function Api([int]$port, [string]$token, [string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 60) {
    $hdr = @{ 'X-Pineglow-Token' = $token }
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec
        } else { $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $TimeoutSec }
        return [pscustomobject]@{ ok = $true; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; body = $null; text = ("status=$code " + $detail) }
    }
}

$cold = (Resolve-Path -LiteralPath $ColdDir).Path
$javaExe = (Resolve-Path -LiteralPath $JavaExe).Path
$worldSrc = (Resolve-Path -LiteralPath $WorldDir).Path
$dataDir = Join-Path $cold 'data'
$cfgPath = Join-Path $dataDir 'config.json'
$cfg = [IO.File]::ReadAllText($cfgPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$cfgInst = @($cfg.Instances) | Where-Object { $_.Id -eq $InstanceId } | Select-Object -First 1
if (-not $cfgInst) { Log '配置文件里找不到实例'; exit 2 }
$gameDir = [string]$cfgInst.GameDirectory
Log ("实例 $($cfgInst.Name) ver=$($cfgInst.MinecraftVersion) gameDir=$gameDir")
$cfgInst.JavaPath = $javaExe
$cfgInst.CustomGameArgs = '--quickPlaySingleplayer ' + $WorldName
$cfg | ConvertTo-Json -Depth 8 | Set-Content -Path $cfgPath -Encoding utf8
Log ("已改 config.json：JavaPath=$javaExe  CustomGameArgs=$($cfgInst.CustomGameArgs)")
$savesDir = Join-Path $gameDir 'saves'; [void](New-Item -ItemType Directory -Path $savesDir -Force)
$dest = Join-Path $savesDir $WorldName
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item -LiteralPath $worldSrc -Destination $dest -Recurse -Force
$worldFiles = @(Get-ChildItem $dest -Recurse -File -EA SilentlyContinue).Count
Log ("世界已放入 saves：文件数=$worldFiles level.dat=" + (Test-Path (Join-Path $dest 'level.dat')))

$port = Get-FreePort
$l = Start-Launcher $cold $port
if (-not $l.token) { Log '启动器未就绪'; exit 2 }
Log ("启动器 pid=$($l.proc.Id) port=$port")
$null = Api $port $l.token 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
$pre = Api $port $l.token 'GET' ("/api/instances/" + $InstanceId + "/prelaunch-check") $null
Log ("prelaunch: " + $pre.text.Substring(0, [Math]::Min(200, $pre.text.Length)))
$launch = Api $port $l.token 'POST' ("/api/instances/" + $InstanceId + "/launch") '{}' 120
Log ("launch -> " + $launch.text)

$jvmPid = 0; $deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($gameDir)) })
    if ($mine.Count -gt 0) { $jvmPid = [int]$mine[0].ProcessId; break }
}
Log ("游戏 JVM pid=$jvmPid")
$cmdLine = ''
if ($jvmPid -gt 0) { $cp = Get-CimInstance Win32_Process -Filter "ProcessId=$jvmPid" -EA SilentlyContinue; if ($cp) { $cmdLine = [string]$cp.CommandLine } }
$hasQuickPlay = ($cmdLine -match 'quickPlaySingleplayer')
Log ("命令行含 quickPlaySingleplayer=" + $hasQuickPlay)
[IO.File]::WriteAllText((Join-Path $outDir 'world-entry-cmdline.txt'), $cmdLine, (New-Object Text.UTF8Encoding($false)))

$gameLog = Join-Path $gameDir 'logs\latest.log'
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 10
    $alive = ($jvmPid -gt 0) -and [bool](Get-Process -Id $jvmPid -ErrorAction SilentlyContinue)
    $tail = ''
    if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
    Log ("  {0,4}s alive={1}  {2}" -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(120, $tail.Length)))
    if (-not $alive) { break }
}
$lines = @(); if (Test-Path $gameLog) { $lines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
@($lines) | Set-Content (Join-Path $outDir 'world-entry-game.log') -Encoding utf8
$m = [ordered]@{
    integratedServer = @($lines | Where-Object { $_ -match 'Starting integrated minecraft server' }).Count
    preparingSpawn = @($lines | Where-Object { $_ -match 'Preparing spawn area' }).Count
    timeElapsed = @($lines | Where-Object { $_ -match 'Time elapsed: ' }).Count
    playerJoined = @($lines | Where-Object { $_ -match 'joined the game' }).Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError' }).Count
    logLines = $lines.Count
}
Log ("标记: integratedServer={0} preparingSpawn={1} timeElapsed={2} playerJoined={3} lwjgl={4} atlas={5} 崩溃={6} 行数={7}" -f `
    $m.integratedServer, $m.preparingSpawn, $m.timeElapsed, $m.playerJoined, $m.lwjgl, $m.atlas, $m.crash, $m.logLines)
$alive = ($jvmPid -gt 0) -and [bool](Get-Process -Id $jvmPid -ErrorAction SilentlyContinue)
if ($alive) { Stop-Process -Id $jvmPid -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 6
$stateAfter = Api $port $l.token 'GET' '/api/state' $null
$instAfter = @($stateAfter.body.instances) | Where-Object { $_.id -eq $InstanceId } | Select-Object -First 1
$playSec = if ($instAfter -and ($instAfter.PSObject.Properties.Name -contains 'totalPlaySeconds')) { [int]$instAfter.totalPlaySeconds } else { -1 }
$verdict = if ($m.playerJoined -gt 0 -and $m.preparingSpawn -gt 0 -and $m.crash -eq 0) { 'PASS：真的进了世界（集成服务器已启动、出生区已准备、玩家已加入）' }
elseif ($m.integratedServer -gt 0 -and $m.crash -eq 0) { '部分：集成服务器起来了，但没看到"玩家已加入"' }
elseif ($m.crash -gt 0) { 'FAIL：崩溃' } else { 'UNKNOWN：没有世界加载迹象' }
Log ('判定: ' + $verdict + "  退出后 totalPlaySeconds=" + $playSec)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    instance = [ordered]@{ id = $InstanceId; name = [string]$cfgInst.Name; version = [string]$cfgInst.MinecraftVersion; gameDirectory = $gameDir; javaPath = $javaExe; customGameArgs = [string]$cfgInst.CustomGameArgs }
    world = [ordered]@{ name = $WorldName; source = $worldSrc; files = $worldFiles; levelDat = (Test-Path (Join-Path $dest 'level.dat')) }
    commandLineHasQuickPlay = $hasQuickPlay
    launch = $launch.text
    markers = $m
    playSecondsAfterExit = $playSec
    verdict = $verdict
} | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $l.proc.HasExited) { Stop-Process -Id $l.proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
