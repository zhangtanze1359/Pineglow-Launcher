#Requires -Version 5.1
<#
.SYNOPSIS
    **不用 GUI 进世界**（R06 的"进入世界/加载存档"，第 56 轮）：1.20.1 支持 `--quickPlaySingleplayer <世界名>`，
    配合产品自己的 `CustomGameArgs` 就能直接进存档。
.DESCRIPTION
    流程：① 启动器里创建 1.20.1 实例并安装版本 → ② 停启动器，改 config.json（JavaPath 指到已备好的 JRE 17、
    CustomGameArgs 加 --quickPlaySingleplayer）并把已生成的世界放进 saves/ → ③ 重启启动器、启动实例、
    从游戏日志确认"集成服务器起来了 + 出生区已准备 + 玩家已加入"。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JavaExe,
    [Parameter(Mandatory)][string] $WorldDir,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $Version = '1.20.1',
    [string] $WorldName = 'QuickWorld',
    [string] $UserName = 'WorldTester',
    [string] $WorkRoot,
    [int] $InstallTimeoutSec = 1800,
    [int] $ObserveSeconds = 150,
    [int] $StartupTimeoutSec = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
if (-not (Test-Path $outDir)) { [void](New-Item -ItemType Directory -Path $outDir -Force) }
$logPath = Join-Path $outDir 'step.log'
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

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$javaExe = (Resolve-Path -LiteralPath $JavaExe).Path
$worldSrc = (Resolve-Path -LiteralPath $WorldDir).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('worldentry-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
Log ("runDir=$runDir")

# ---------------- 阶段 1：装 1.20.1
$port = Get-FreePort
$l1 = Start-Launcher $cold $port
if (-not $l1.token) { Log '启动器未就绪'; exit 2 }
Log ("阶段1 启动器 pid=$($l1.proc.Id) port=$port")
$create = Api $port $l1.token 'POST' '/api/instances' ('{"name":"world-entry","version":"' + $Version + '"}')
Log ("创建实例: " + $create.text)
$state = Api $port $l1.token 'GET' '/api/state' $null
$inst = @($state.body.instances) | Where-Object { $_.name -eq 'world-entry' } | Select-Object -First 1
if (-not $inst) { Log '找不到新实例'; exit 2 }
$instId = [string]$inst.id
$sw = [Diagnostics.Stopwatch]::StartNew()
$dl = Api $port $l1.token 'POST' '/api/versions/download' ('{"versionId":"' + $Version + '"}') 120
$taskId = ''
if ($dl.ok -and $dl.body -and ($dl.body.PSObject.Properties.Name -contains 'id')) { $taskId = [string]$dl.body.id }
Log ("安装任务已排队 id=" + $taskId + "（**必须轮询到终态**，否则停启动器会把任务掐掉）")
$task = $null
while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 3
    $list = Api $port $l1.token 'GET' '/api/downloads' $null
    $task = @($list.body) | Where-Object { $_.id -eq $taskId } | Select-Object -First 1
    if ($task -and ($task.status -eq 'completed' -or $task.status -eq 'failed')) { break }
    if ([int]$sw.Elapsed.TotalSeconds % 30 -lt 4) { Log ("    …{0}s status={1}" -f [int]$sw.Elapsed.TotalSeconds, $(if ($task) { $task.status } else { '?' })) }
}
$sw.Stop()
$installSec = [int]$sw.Elapsed.TotalSeconds
$dl = [pscustomobject]@{ ok = ($task -and $task.status -eq 'completed'); text = (($task | ConvertTo-Json -Compress -Depth 5)) }
Log ("安装 {0}: ok={1} 用时={2}s {3}" -f $Version, $dl.ok, $installSec, $dl.text.Substring(0, [Math]::Min(160, $dl.text.Length)))

# ---------------- 阶段 2：停启动器，改配置 + 放世界
try { if (-not $l1.proc.HasExited) { Stop-Process -Id $l1.proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 3
$cfgPath = Join-Path $dataDir 'config.json'
$cfg = [IO.File]::ReadAllText($cfgPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$cfgInst = @($cfg.Instances) | Where-Object { $_.Id -eq $instId } | Select-Object -First 1
if (-not $cfgInst) { Log '配置文件里找不到实例'; exit 2 }
$gameDir = [string]$cfgInst.GameDirectory
Log ("实例 gameDir=" + $gameDir)
# JavaPath 指向已备好的 JRE 17；CustomGameArgs 加快速进世界
$cfgInst.JavaPath = $javaExe
$cfgInst.CustomGameArgs = '--quickPlaySingleplayer ' + $WorldName
$cfg | ConvertTo-Json -Depth 8 | Set-Content -Path $cfgPath -Encoding utf8
Log ("已改 config.json：JavaPath=" + $javaExe + "  CustomGameArgs=" + $cfgInst.CustomGameArgs)
# 放世界
$savesDir = Join-Path $gameDir 'saves'
[void](New-Item -ItemType Directory -Path $savesDir -Force)
$dest = Join-Path $savesDir $WorldName
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item -LiteralPath $worldSrc -Destination $dest -Recurse -Force
$worldFiles = @(Get-ChildItem $dest -Recurse -File -EA SilentlyContinue).Count
Log ("世界已放入 saves：文件数=" + $worldFiles + " level.dat=" + (Test-Path (Join-Path $dest 'level.dat')))

# ---------------- 阶段 3：重启启动器并启动
$port2 = Get-FreePort
$l2 = Start-Launcher $cold $port2
if (-not $l2.token) { Log '启动器二次启动失败'; exit 2 }
Log ("阶段3 启动器 pid=$($l2.proc.Id) port=$port2")
$null = Api $port2 $l2.token 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
$pre = Api $port2 $l2.token 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
Log ("prelaunch: " + $pre.text.Substring(0, [Math]::Min(160, $pre.text.Length)))
$launch = Api $port2 $l2.token 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
Log ("launch -> " + $launch.text)

$jvmPid = 0; $deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($gameDir)) })
    if ($mine.Count -gt 0) { $jvmPid = [int]$mine[0].ProcessId; break }
}
Log ("游戏 JVM pid=$jvmPid")

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
Start-Sleep -Seconds 5
$stateAfter = Api $port2 $l2.token 'GET' '/api/state' $null
$instAfter = @($stateAfter.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$playSec = if ($instAfter.PSObject.Properties.Name -contains 'totalPlaySeconds') { [int]$instAfter.totalPlaySeconds } else { -1 }
$verdict = if ($m.playerJoined -gt 0 -and $m.preparingSpawn -gt 0 -and $m.crash -eq 0) { 'PASS：真的进了世界（集成服务器已启动、出生区已准备、玩家已加入）' }
elseif ($m.integratedServer -gt 0 -and $m.crash -eq 0) { '部分：集成服务器起来了，但没看到"玩家已加入"' }
elseif ($m.crash -gt 0) { 'FAIL：崩溃' }
else { 'UNKNOWN：没有世界加载迹象' }
Log ('判定: ' + $verdict + "  退出后 totalPlaySeconds=" + $playSec)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    instance = [ordered]@{ id = $instId; version = $Version; gameDirectory = $gameDir; javaPath = $javaExe; customGameArgs = $cfgInst.CustomGameArgs }
    world = [ordered]@{ name = $WorldName; source = $worldSrc; files = $worldFiles; levelDat = (Test-Path (Join-Path $dest 'level.dat')) }
    install = [ordered]@{ ok = $dl.ok; seconds = $installSec; response = $dl.text }
    launch = $launch.text
    markers = $m
    playSecondsAfterExit = $playSec
    verdict = $verdict
} | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $l2.proc.HasExited) { Stop-Process -Id $l2.proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
