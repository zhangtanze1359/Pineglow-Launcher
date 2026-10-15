#Requires -Version 5.1
<#
.SYNOPSIS
    **真的启动一次原版 Minecraft**（R06「原版…启动/退出」）——用候选产物自己的启动接口，
    装好版本、就绪 Java、加一个离线账号，然后**观察**：JVM 是否起来、游戏日志说什么、进程是活着还是退出、退出码是什么。

.DESCRIPTION
    这台机器有真实 GPU（NVIDIA RTX 4060 Laptop / AMD Radeon 780M）、15.2 GB 内存与交互式桌面，
    所以"能不能真的把游戏拉起来"值得一试。无论成功或失败都记录原始证据：
      · 成功：证明启动链路（命令行/classpath/natives/账号注入/进程派发）真的可用；
      · 失败：从 `logs/latest.log` 与退出码给出**具体原因**（例如图形/OpenAL 不可用），并如实标注是环境还是产品。
    产品 API **没有停止接口**（只有 launch），所以收尾由本脚本 kill 自己拉起的进程树。
    每步立即写 <JsonPath 同目录>\step.log；每个请求有超时，绝不无限等待。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $Version = '1.18.2',
    [string] $UserName = 'AcceptanceTester',
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 1800,
    [int] $ObserveSeconds = 180,
    [int] $CallTimeoutSec = 45
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression.FileSystem

$logPath = if ($JsonPath) { Join-Path (Split-Path -Parent $JsonPath) 'step.log' } else { Join-Path $env:TEMP 'launch-step.log' }
$logDir = Split-Path -Parent $logPath
if (-not (Test-Path $logDir)) { [void](New-Item -ItemType Directory -Path $logDir -Force) }
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) {
    $line = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m
    [IO.File]::AppendAllText($logPath, $line + "`r`n", (New-Object Text.UTF8Encoding($false)))
    Write-Host $line
}
function Get-FreePort {
    for ($i = 0; $i -lt 60; $i++) {
        $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop()
        if ($p -gt 1024) { return $p }
    }
    throw '找不到空闲端口'
}
function DirBytes([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $items = @(Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue)
    if ($items.Count -eq 0) { return 0 }
    return [int64](($items | Measure-Object -Property Length -Sum).Sum)
}

$results = [ordered]@{ generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz') }

# ---------------------------------------------------------------- 1) 启动候选产物
$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('launch-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'
$dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$port = Get-FreePort
$extract = Join-Path $runDir 'bundle-extract'
[void](New-Item -ItemType Directory -Path $extract -Force)
$oldPort = $env:PINEGLOW_API_PORT; $oldBase = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
$env:PINEGLOW_API_PORT = "$port"; $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extract
try {
    $proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru `
        -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
} finally {
    $env:PINEGLOW_API_PORT = $oldPort; $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $oldBase
}
$token = $null
$deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    if ((Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) -eq $null) { break }
    try {
        $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) {
            $html = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content
            if ($html -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }
            break
        }
    } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
Log "启动器 pid=$($proc.Id) port=$port runDir=$runDir"
$hdr = @{ 'X-Pineglow-Token' = $token }
function Api([string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 0) {
    if ($TimeoutSec -le 0) { $TimeoutSec = $CallTimeoutSec }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Log ("  -> {0} {1}" -f $Method, $Path)
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec
        } else {
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $TimeoutSec
        }
        $sw.Stop(); Log ("  <- 200 {0} {1}ms" -f $Path, [int]$sw.Elapsed.TotalMilliseconds)
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6) }
    } catch {
        $sw.Stop(); $resp = $_.Exception.Response; $code = 0; $txt = $_.Exception.Message
        if ($resp) { $code = [int]$resp.StatusCode; try { $rd = New-Object System.IO.StreamReader($resp.GetResponseStream()); $txt = $rd.ReadToEnd(); $rd.Dispose() } catch { } }
        Log ("  <- status={0} {1}ms {2}" -f $code, [int]$sw.Elapsed.TotalMilliseconds, $txt.Substring(0, [Math]::Min(200, $txt.Length)))
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $txt }
    }
}

# ---------------------------------------------------------------- 2) 建实例 + 装版本
Log '== 原版启动实测 开始 =='
$create = Api 'POST' '/api/instances' ('{"name":"launch-acceptance","version":"' + $Version + '"}')
if (-not $create.ok) { Log '创建实例失败，退出'; exit 2 }
$instId = [string]$create.body.instance.id
$state = Api 'GET' '/api/state' $null
$inst = @($state.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$instDir = [string]$inst.gameDirectory
Log "实例 id=$instId gameDir=$instDir"
$results.instanceId = $instId; $results.instanceGameDir = $instDir

$dl = Api 'POST' '/api/versions/download' ('{"versionId":"' + $Version + '"}')
if (-not $dl.ok) { Log '触发版本下载失败，退出'; exit 2 }
$taskId = [string]$dl.body.id
$sw = [Diagnostics.Stopwatch]::StartNew()
$task = $null
while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 5
    $list = Api 'GET' '/api/downloads' $null
    $task = @($list.body.tasks) | Where-Object { $_.id -eq $taskId } | Select-Object -First 1
    if ($task -and ($task.status -eq 'completed' -or $task.status -eq 'failed')) { break }
    if ([int]$sw.Elapsed.TotalSeconds % 30 -lt 6) { Log ("     安装中 {0}s status={1} progress={2} msg={3}" -f [int]$sw.Elapsed.TotalSeconds, $task.status, $task.progress, $task.message) }
}
Log ("  版本安装终态: status={0} 用时={1}s msg={2}" -f $task.status, [int]$sw.Elapsed.TotalSeconds, $task.message)
$results.versionInstall = [ordered]@{ status = $task.status; elapsedSec = [int]$sw.Elapsed.TotalSeconds; message = $task.message }
if ($task.status -ne 'completed') { Log '版本安装未完成，退出'; exit 1 }

# ---------------------------------------------------------------- 3) 就绪真实 Java 17（与产品同一来源）
Log '-- 就绪 Java 17（Adoptium）--'
$api = 'https://api.adoptium.net/v3/assets/latest/17/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse'
$meta = Invoke-RestMethod $api -TimeoutSec 60
$pkg = $meta[0].binary.package
$jreDir = Join-Path $dataDir 'runtimes\java-17-windows-x64'
[void](New-Item -ItemType Directory -Path $jreDir -Force)
$zipPath = Join-Path $runDir ([string]$pkg.name)
$swj = [Diagnostics.Stopwatch]::StartNew()
Invoke-WebRequest -Uri ([string]$pkg.link) -OutFile $zipPath -UseBasicParsing -TimeoutSec 900
$swj.Stop()
$got = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLower()
$want = ([string]$pkg.checksum).ToLower()
Log ("  JRE 下载 {0} B / {1}s；SHA256 一致={2}" -f (Get-Item $zipPath).Length, [int]$swj.Elapsed.TotalSeconds, ($got -eq $want))
Expand-Archive -LiteralPath $zipPath -DestinationPath $jreDir -Force
$javaExe = Get-ChildItem $jreDir -Recurse -File -Filter java.exe | Select-Object -First 1
Log ("  java.exe = {0}" -f [string]$javaExe.FullName)
$results.java = [ordered]@{ bytes = (Get-Item $zipPath).Length; sha256Match = ($got -eq $want); exe = [string]$javaExe.FullName }

# ---------------------------------------------------------------- 4) 离线账号 + 预检
Log '-- 离线账号 + prelaunch-check --'
$acc = Api 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
Log ("  离线账号 ok={0}" -f $acc.ok)
$pre = Api 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
Log ("  预检 ok={0} errors={1}" -f $pre.body.ok, (@($pre.body.errors) -join ' | '))
$results.prelaunch = [ordered]@{ ok = [bool]$pre.body.ok; errors = @($pre.body.errors); warnings = @($pre.body.warnings) }

# ---------------------------------------------------------------- 5) 启动
Log '-- POST launch --'
$swL = [Diagnostics.Stopwatch]::StartNew()
$launch = Api 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
$swL.Stop()
$results.launchResponse = [ordered]@{ ok = $launch.ok; status = $launch.status; elapsedSec = [int]$swL.Elapsed.TotalSeconds; text = $launch.text }
Log ("  launch 响应: ok={0} status={1} 用时={2}s text={3}" -f $launch.ok, $launch.status, [int]$swL.Elapsed.TotalSeconds, $launch.text.Substring(0, [Math]::Min(220, $launch.text.Length)))

# ---------------------------------------------------------------- 6) 观察 JVM 与游戏日志
Log '-- 观察 JVM / 游戏日志 --'
$gameLog = Join-Path $instDir 'logs\latest.log'
$observed = New-Object System.Collections.Generic.List[object]
$javaSeen = $false; $javaCmd = ''
$deadline = (Get-Date).AddSeconds($ObserveSeconds)
$lastLog = -20
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $elapsed = [int]($ObserveSeconds - ($deadline - (Get-Date)).TotalSeconds)
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    # 注意（第 45 轮踩过）：管道结果必须 @() 包起来；StrictMode 下单个对象没有 .Count 会直接中断脚本
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instId) -or $_.CommandLine -match [regex]::Escape($instDir)) })
    if ($mine.Count -gt 0 -and -not $javaSeen) {
        $javaSeen = $true; $javaCmd = ($mine[0].CommandLine)
        Log ("  JVM 已出现 pid={0}" -f $mine[0].ProcessId)
    }
    if ($elapsed - $lastLog -ge 15) {
        $lastLog = $elapsed
        $tail = ''
        if (Test-Path $gameLog) { $tail = (Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue) -join ' ' }
        Log ("     {0,4}s  jvm={1}  游戏日志={2}" -f $elapsed, $mine.Count, ($tail.Substring(0, [Math]::Min(110, $tail.Length))))
        $observed.Add([pscustomobject]@{ sec = $elapsed; jvm = $mine.Count; logTail = $tail }) | Out-Null
    }
    if ($javaSeen -and $mine.Count -eq 0) { Log '  JVM 已退出'; break }
}
$stillRunning = $false
$javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
$mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instId) -or $_.CommandLine -match [regex]::Escape($instDir)) })
$stillRunning = ($mine.Count -gt 0)
Log ("  观察结束: JVM 曾出现={0}  当前仍在运行={1}" -f $javaSeen, $stillRunning)

$gameLogText = @()
if (Test-Path $gameLog) { $gameLogText = @(Get-Content $gameLog -Tail 120 -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
$markers = @{
    lwjgl = @($gameLogText | Where-Object { $_ -match 'LWJGL|Backend library|OpenGL' }).Count
    user = @($gameLogText | Where-Object { $_ -match 'Setting user|UUID of player' }).Count
    sound = @($gameLogText | Where-Object { $_ -match 'OpenAL|Sound engine|Sound engine started' }).Count
    started = @($gameLogText | Where-Object { $_ -match 'Sound engine started|Created: .*atlas|Preparing spawn area|Starting integrated minecraft server' }).Count
    fatal = @($gameLogText | Where-Object { $_ -match 'FATAL|Exception|Error|failed|Could not' }).Count
}
Log ("  日志标记: LWJGL/GL={0} 用户={1} 声音={2} 启动完成迹象={3} 致命/异常行={4}" -f $markers.lwjgl, $markers.user, $markers.sound, $markers.started, $markers.fatal)
$results.observation = [ordered]@{ javaSeen = $javaSeen; stillRunning = $stillRunning; javaCommandHead = $javaCmd.Substring(0, [Math]::Min(400, $javaCmd.Length)); markers = $markers; timeline = $observed; gameLogTail = ($gameLogText | Select-Object -Last 40) }

# ---------------------------------------------------------------- 7) 收尾：产品没有 stop 接口，自己 kill
Log '-- 收尾 --'
$killed = 0
foreach ($p in $mine) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue; $killed++ } catch { } }
Log ("  已 kill JVM 进程数={0}（产品没有 stop 接口，只能外部终止）" -f $killed)
Start-Sleep -Seconds 3
$stateAfter = Api 'GET' '/api/state' $null
$instAfter = @($stateAfter.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$results.afterKill = [ordered]@{ killed = $killed; instanceRunningFlag = $(if ($instAfter.PSObject.Properties.Name -contains 'running') { [bool]$instAfter.running } else { $null }) }
Log ("  kill 后实例 running 标记={0}" -f $results.afterKill.instanceRunningFlag)

$launcherLog = Get-ChildItem (Join-Path $dataDir 'logs') -File -Filter 'launcher-*.log' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($launcherLog) { $results.launcherLogTail = @(Get-Content $launcherLog.FullName -Tail 60 | ForEach-Object { [string]$_ }) }

$results.exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
$results.runDir = $runDir
if ($JsonPath) { ($results | ConvertTo-Json -Depth 7) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Log '== 结束 =='
exit 0
