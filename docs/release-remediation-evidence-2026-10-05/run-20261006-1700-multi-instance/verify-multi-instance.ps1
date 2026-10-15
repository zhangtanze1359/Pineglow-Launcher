#Requires -Version 5.1
<#
.SYNOPSIS
    **多实例并发**验收（第 57 轮）：两个不同实例同时跑；同一实例重复启动必须被挡；退出后守卫要解除。
.DESCRIPTION
    代码事实：`RunningInstanceRegistry` 是按 **instanceId** 键的 `ConcurrentDictionary`
    ⇒ 不同实例应当可以**同时**运行；同一实例由 `TryBeginLaunch` 原子挡住。
    本脚本：装一个 1.18.2（共享 gameDir）→ 建两个实例 → 同时启动 → 抓两个 JVM → 重复启动同一实例看是否 400
    → 杀掉后确认守卫解除。
    注意（第 56 轮踩过）：`POST /api/versions/download` 是**异步排队**，必须轮询 `/api/downloads` 到终态；
    该端点返回 **`{"tasks":[…]}`（对象）**。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JavaExe,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $Version = '1.18.2',
    [string] $UserName = 'ConcurrencyTester',
    [string] $WorkRoot,
    [int] $InstallTimeoutSec = 2400,
    [int] $ObserveSeconds = 60
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
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail }
    }
}
function Wait-Jvm([string]$gameDir, [int]$timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
        $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($gameDir)) })
        if ($mine.Count -gt 0) { return @($mine | ForEach-Object { [int]$_.ProcessId }) }
    }
    return @()
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$javaExe = (Resolve-Path -LiteralPath $JavaExe).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('conc-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
Log ("runDir=$runDir")

# ---- 1) 建两个实例 + 装版本
$port = Get-FreePort
$l1 = Start-Launcher $cold $port
if (-not $l1.token) { Log '启动器未就绪'; exit 2 }
$ids = @{}
foreach ($nm in 'conc-a', 'conc-b') {
    $c = Api $port $l1.token 'POST' '/api/instances' ('{"name":"' + $nm + '","version":"' + $Version + '"}')
    if (-not $c.ok) { Log ("创建 $nm 失败: " + $c.text); exit 2 }
    $ids[$nm] = [string]$c.body.instance.id
    Log ("实例 $nm id=$($ids[$nm])")
}
$sw = [Diagnostics.Stopwatch]::StartNew()
$q = Api $port $l1.token 'POST' '/api/versions/download' ('{"versionId":"' + $Version + '"}') 120
$taskId = ''; if ($q.ok -and $q.body -and ($q.body.PSObject.Properties.Name -contains 'id')) { $taskId = [string]$q.body.id }
Log ("安装任务 id=$taskId")
$task = $null
while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 3
    $list = Api $port $l1.token 'GET' '/api/downloads' $null
    $tasks = @(); if ($list.ok -and $list.body -and ($list.body.PSObject.Properties.Name -contains 'tasks')) { $tasks = @($list.body.tasks) }
    $task = $tasks | Where-Object { $_.id -eq $taskId } | Select-Object -First 1
    if ($task -and ($task.status -eq 'completed' -or $task.status -eq 'failed')) { break }
    if ([int]$sw.Elapsed.TotalSeconds % 30 -lt 4) { Log ("    …{0}s status={1} progress={2}" -f [int]$sw.Elapsed.TotalSeconds, $(if ($task) { $task.status } else { '?' }), $(if ($task) { $task.progress } else { '' })) }
}
$sw.Stop()
$installOk = ($task -and $task.status -eq 'completed')
Log ("安装 {0}: status={1} 用时={2}s" -f $Version, $(if ($task) { $task.status } else { 'null' }), [int]$sw.Elapsed.TotalSeconds)
if (-not $installOk) { Log '安装未完成，退出'; exit 1 }

# ---- 2) 停启动器 → 指定 Java → 重启
try { if (-not $l1.proc.HasExited) { Stop-Process -Id $l1.proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 3
$cfgPath = Join-Path $dataDir 'config.json'
$cfg = [IO.File]::ReadAllText($cfgPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$gameDir = ''
foreach ($inst in $cfg.Instances) { if ($ids.Values -contains [string]$inst.Id) { $inst.JavaPath = $javaExe; $gameDir = [string]$inst.GameDirectory } }
$cfg | ConvertTo-Json -Depth 8 | Set-Content -Path $cfgPath -Encoding utf8
Log ("已把两个实例的 JavaPath 指到 $javaExe；共享 gameDir=$gameDir")

$port2 = Get-FreePort
$l2 = Start-Launcher $cold $port2
if (-not $l2.token) { Log '启动器二次启动失败'; exit 2 }
$null = Api $port2 $l2.token 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')

# ---- 3) 同时启动两个不同实例
$rA = Api $port2 $l2.token 'POST' ("/api/instances/" + $ids['conc-a'] + "/launch") '{}' 120
Log ("启动 conc-a -> " + $rA.text)
$rB = Api $port2 $l2.token 'POST' ("/api/instances/" + $ids['conc-b'] + "/launch") '{}' 120
Log ("启动 conc-b -> " + $rB.text)
Start-Sleep -Seconds 10
$pids = Wait-Jvm $gameDir 20
Log ("检测到与 gameDir 匹配的 java 进程数=" + $pids.Count + "  pids=" + ($pids -join ','))
$bothRunning = ($pids.Count -ge 2) -and $rA.ok -and $rB.ok

# ---- 4) 同一实例重复启动必须被挡
$dup = Api $port2 $l2.token 'POST' ("/api/instances/" + $ids['conc-a'] + "/launch") '{}' 60
Log ("重复启动 conc-a -> status=" + $dup.status + "  " + $dup.text.Substring(0, [Math]::Min(160, $dup.text.Length)))
$dupBlocked = (-not $dup.ok) -and ($dup.status -eq 400)

# ---- 5) 观察并存活
$sw2 = [Diagnostics.Stopwatch]::StartNew()
while ($sw2.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 10
    $alive = @($pids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    Log ("    {0,3}s 存活实例进程={1}/{2}" -f [int]$sw2.Elapsed.TotalSeconds, $alive.Count, $pids.Count)
}

# ---- 6) 全部杀掉 → 守卫应解除（再启动一次应当 ok）
foreach ($p in $pids) { Stop-Process -Id $p -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 8
$again = Api $port2 $l2.token 'POST' ("/api/instances/" + $ids['conc-a'] + "/launch") '{}' 120
Log ("杀掉后再次启动 conc-a -> " + $again.text)
$cleared = $again.ok
$pids2 = Wait-Jvm $gameDir 20
foreach ($p in $pids2) { Stop-Process -Id $p -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 5

$verdict = if ($bothRunning -and $dupBlocked -and $cleared) { 'PASS：两个不同实例可并发运行；同一实例重复启动被 400 挡住；退出后守卫解除' }
elseif (-not $bothRunning) { 'FAIL：两个不同实例没有同时跑起来' }
elseif (-not $dupBlocked) { 'FAIL：同一实例重复启动没有被挡' }
else { '部分：并发与拦截成立，但退出后守卫未及时解除' }
Log ('判定: ' + $verdict)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    version = $Version
    install = [ordered]@{ status = $(if ($task) { $task.status } else { 'null' }); seconds = [int]$sw.Elapsed.TotalSeconds }
    instances = $ids
    sharedGameDir = $gameDir
    launchA = $rA.text
    launchB = $rB.text
    concurrentJvmPids = $pids
    duplicateLaunch = [ordered]@{ status = $dup.status; response = $dup.text }
    relaunchAfterKill = $again.text
    verdict = $verdict
} | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $l2.proc.HasExited) { Stop-Process -Id $l2.proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit $(if ($bothRunning -and $dupBlocked -and $cleared) { 0 } else { 1 })
