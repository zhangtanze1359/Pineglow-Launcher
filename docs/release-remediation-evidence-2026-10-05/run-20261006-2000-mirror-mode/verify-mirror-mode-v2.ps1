#Requires -Version 5.1
<#
.SYNOPSIS
    **镜像源模式**实测（第 63 轮）：下载源切到 BMCLAPI 后，装一个版本到底能不能成。
.DESCRIPTION
    便携启动器 → `POST /api/config {downloadSource:"bmclapi", proxyEnabled:true, proxyHost/Port:本地记录型代理}`
    → `GET /api/sources` 确认选中 → 装 1.5.2 → 轮询 `/api/downloads` 到终态 → 核对 client.jar 的 sha1 与官方值、
    assets 落盘、代理日志里出现的**主机名**（证明确实走了镜像）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ProxyScript,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $Version = '1.5.2',
    [string] $ExpectedJarSha1 = '465378c9dc2f779ae1d6e8046ebc46fb53a57968',
    [string] $WorkRoot,
    [int] $InstallTimeoutSec = 1200,
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
function Api([int]$port, [string]$token, [string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 120) {
    $hdr = @{ 'X-Pineglow-Token' = $token }
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec
        } else { $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $TimeoutSec }
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 5) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail }
    }
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$proxyJs = (Resolve-Path -LiteralPath $ProxyScript).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('mirror-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)

$proxyPort = Get-FreePort
$proxyLog = Join-Path $outDir 'proxy.log'
if (Test-Path $proxyLog) { Remove-Item $proxyLog -Force }
$proxyProc = Start-Process -FilePath 'node' -ArgumentList ('"' + $proxyJs + '" "' + $proxyLog + '" ' + $proxyPort) -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2

$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port + " proxy=" + $proxyPort)

$cfgBody = '{"downloadSource":"bmclapi","proxyEnabled":true,"proxyHost":"127.0.0.1","proxyPort":' + $proxyPort + '}'
$set = Api $port $token 'POST' '/api/config' $cfgBody 60
Log ("POST /api/config -> " + $set.text.Substring(0, [Math]::Min(160, $set.text.Length)))
$src = Api $port $token 'GET' '/api/sources' $null 60
$selected = ''
if ($src.ok -and $src.body -and ($src.body.PSObject.Properties.Name -contains 'selected')) { $selected = [string]$src.body.selected }
Log ("GET /api/sources -> selected=" + $selected)

$create = Api $port $token 'POST' '/api/instances' ('{"name":"mirror-test","version":"' + $Version + '"}')
$instId = ''
if ($create.ok -and $create.body -and ($create.body.instance.PSObject.Properties.Name -contains 'id')) { $instId = [string]$create.body.instance.id }
Log ("创建实例 id=" + $instId)

$sw = [Diagnostics.Stopwatch]::StartNew()
$q = Api $port $token 'POST' '/api/versions/download' ('{"versionId":"' + $Version + '"}') 120
$taskId = ''
if ($q.ok -and $q.body -and ($q.body.PSObject.Properties.Name -contains 'id')) { $taskId = [string]$q.body.id }
Log ("安装任务 id=" + $taskId)
$task = $null
while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 3
    $list = Api $port $token 'GET' '/api/downloads' $null 60
    $tasks = @()
    if ($list.ok -and $list.body -and ($list.body.PSObject.Properties.Name -contains 'tasks')) { $tasks = @($list.body.tasks) }
    $task = $tasks | Where-Object { $_.id -eq $taskId } | Select-Object -First 1
    if ($task -and ($task.status -eq 'completed' -or $task.status -eq 'failed')) { break }
    if ([int]$sw.Elapsed.TotalSeconds % 30 -lt 4) {
        $st = '?'; $pg = ''
        if ($task) { $st = [string]$task.status; if ($task.PSObject.Properties.Name -contains 'progress') { $pg = [string]$task.progress } }
        Log ("    ..." + [int]$sw.Elapsed.TotalSeconds + "s status=" + $st + " progress=" + $pg)
    }
}
$sw.Stop()
$status = ''; $msg = ''
if ($task) {
    $status = [string]$task.status
    if ($task.PSObject.Properties.Name -contains 'error') { $msg = [string]$task.error }
    if (-not $msg -and ($task.PSObject.Properties.Name -contains 'message')) { $msg = [string]$task.message }
}
Log ("安装终态: status=" + $status + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s msg=" + $msg)

$cfgPath = Join-Path $dataDir 'config.json'
$gameDir = Join-Path $dataDir '.minecraft'
if (Test-Path $cfgPath) {
    $cfg = [IO.File]::ReadAllText($cfgPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($cfg.PSObject.Properties.Name -contains 'GameDirectory') { $gameDir = [string]$cfg.GameDirectory }
}
$jar = Join-Path $gameDir ("versions\" + $Version + "\" + $Version + ".jar")
$jarOk = Test-Path $jar
$jarSha1 = ''; $jarBytes = 0
if ($jarOk) { $jarSha1 = (Get-FileHash -LiteralPath $jar -Algorithm SHA1).Hash.ToLower(); $jarBytes = (Get-Item $jar).Length }
$assetsDir = Join-Path $gameDir 'assets\objects'
$assetCount = 0
if (Test-Path $assetsDir) { $assetCount = @(Get-ChildItem $assetsDir -Recurse -File -EA SilentlyContinue).Count }
Log ("client.jar 存在=" + $jarOk + " 大小=" + $jarBytes + " sha1=" + $jarSha1 + " 与官方一致=" + ($jarSha1 -eq $ExpectedJarSha1.ToLower()))
Log ("assets 对象数=" + $assetCount)

$hosts = @{}
if (Test-Path $proxyLog) {
    foreach ($line in @(Get-Content $proxyLog | ForEach-Object { [string]$_ })) {
        $h = ''
        if ($line -match 'CONNECT\s+([^:\s]+):') { $h = $Matches[1] }
        elseif ($line -match 'HTTP\s+\w+\s+https?://([^/\s]+)') { $h = $Matches[1] }
        if (-not $h) { continue }
        if ($hosts.ContainsKey($h)) { $hosts[$h] = [int]$hosts[$h] + 1 } else { $hosts[$h] = 1 }
    }
}
Log ("代理日志主机数=" + $hosts.Count)
foreach ($k in ($hosts.Keys | Sort-Object)) { Log ("  " + $k.PadRight(42) + " " + $hosts[$k] + " 次") }
$mirrorUsed = (@($hosts.Keys | Where-Object { $_ -match 'bangbang93|bmclapi' }).Count -gt 0)
$mojangCount = @($hosts.Keys | Where-Object { $_ -match 'mojang|minecraft\.net' }).Count

$checks = [ordered]@{
    source_selected_bmclapi = ($selected -eq 'bmclapi')
    install_completed = ($status -eq 'completed')
    client_jar_sha1_official = ($jarSha1 -eq $ExpectedJarSha1.ToLower())
    assets_present = ($assetCount -gt 0)
    mirror_host_used = $mirrorUsed
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：镜像源模式下安装成功、jar 与官方 sha1 一致、代理日志确认走了镜像主机'
if ($fails.Count -gt 0) {
    $failKeys = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $failKeys + '）'
}
Log ('判定: ' + $verdictText)

$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    version = $Version
    selectedSource = $selected
    install = [ordered]@{ taskId = $taskId; status = $status; seconds = [int]$sw.Elapsed.TotalSeconds; message = $msg }
    artifacts = [ordered]@{ clientJar = $jarOk; bytes = $jarBytes; sha1 = $jarSha1; expectedSha1 = $ExpectedJarSha1; assetObjects = $assetCount }
    hostsSeen = $hosts
    mirrorHostUsed = $mirrorUsed
    mojangHostCount = $mojangCount
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
try { if (-not $proxyProc.HasExited) { Stop-Process -Id $proxyProc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
