#Requires -Version 5.1
<#
.SYNOPSIS
    **更新链**实测（第 59 轮）：清单走 HTTP 会不会被接受？产物地址是否强制 HTTPS？
    下载→SHA256 校验→替换自己→重启 这条链在本地能验到哪一步？
.DESCRIPTION
    做法：本地用 HTTP 提供 `manifest.json`（内容我控制，日志记录每次请求），把 `config.json` 的 `updateUrl` 指过去，
    依次调 `GET /api/update/check`、`POST /api/update/download`、`POST /api/update/apply`，
    并观察：清单是否被真的取走、产物是否被下载、**EXE 的 SHA256 是否被替换成清单里的值**、进程是否退出并尝试重启。
    产物地址必须是**公开 HTTPS**（清单校验强制），所以这里用 Modrinth CDN 上一个不可变的 `.mrpack`（内容我已知、sha256 我算过）。
    注意：本脚本**不改本机信任库**，因此不尝试自签 HTTPS。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ServerScript,
    [Parameter(Mandatory)][string] $AssetUrl,
    [Parameter(Mandatory)][string] $AssetSha256,
    [Parameter(Mandatory)][long] $AssetSize,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $NewVersion = '1.0.1',
    [string] $WorkRoot,
    [string] $PrePlaceNativeLibs = '',
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

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$serverJs = (Resolve-Path -LiteralPath $ServerScript).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('updchain-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
$shaBefore = (Get-FileHash -LiteralPath $targetExe -Algorithm SHA256).Hash.ToLower()
Log ("测试副本 EXE sha256(前)=$shaBefore  大小=" + (Get-Item $targetExe).Length)

# ---- 本地 HTTP 清单
$srvPort = Get-FreePort
$manifestPath = Join-Path $outDir 'manifest.json'
$manifest = [ordered]@{ version = $NewVersion; url = $AssetUrl; size = $AssetSize; sha256 = $AssetSha256; notes = '本地更新链探针（第 59 轮）'; mandatory = $false }
($manifest | ConvertTo-Json -Depth 4) | Set-Content -Path $manifestPath -Encoding utf8
$reqLog = Join-Path $outDir 'manifest-requests.log'
if (Test-Path $reqLog) { Remove-Item $reqLog -Force }
$srv = Start-Process -FilePath 'node' -ArgumentList ('"' + $serverJs + '" ' + $srvPort + ' "' + $manifestPath + '" "' + $reqLog + '"') -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
Log ("本地 HTTP 清单 http://127.0.0.1:$srvPort/manifest.json  产物=" + $AssetUrl)
Log ("  产物 sha256=" + $AssetSha256 + " size=" + $AssetSize)

# ---- config.json 预置 updateUrl（HTTP！）
[IO.File]::WriteAllText((Join-Path $dataDir 'config.json'), ('{"updateUrl":"http://127.0.0.1:' + $srvPort + '/manifest.json"}'), (New-Object Text.UTF8Encoding($false)))

# 可选：预置原生库（第 60 轮）——用于让"能启动的产物"跑起来，从而验证"重启后新进程正常服务"。
# 注意：真实更新里新版本产物自带原生库（候选已修）；这里预置只是因为测试用的 v1.0.0 发布件缺原生库。
if ($PrePlaceNativeLibs -and (Test-Path $PrePlaceNativeLibs)) {
    $src = (Resolve-Path -LiteralPath $PrePlaceNativeLibs).Path
    foreach ($f in 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll', 'av_libglesv2.dll') {
        if (Test-Path (Join-Path $src $f)) { Copy-Item (Join-Path $src $f) $cold -Force; Log ("  预置原生库: " + $f) }
    }
}
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
Log ("启动器 pid=$($proc.Id) apiPort=$port")

$status = Api $port $token 'GET' '/api/update' $null
Log ("GET /api/update -> " + $status.text.Substring(0, [Math]::Min(200, $status.text.Length)))
$check = Api $port $token 'GET' '/api/update/check' $null 180
Log ("GET /api/update/check -> " + $check.text.Substring(0, [Math]::Min(400, $check.text.Length)))
Start-Sleep -Seconds 2
$reqs = @(); if (Test-Path $reqLog) { $reqs = @(Get-Content $reqLog | ForEach-Object { [string]$_ }) }
$manifestFetched = @($reqs | Where-Object { $_ -match 'manifest\.json' }).Count
Log ("  本地清单被请求次数=$manifestFetched（若 >0 ⇒ **HTTP 清单确实被取用**）")

$download = $null
if ($check.ok) {
    $download = Api $port $token 'POST' '/api/update/download' '{}' 600
    Log ("POST /api/update/download -> " + $download.text.Substring(0, [Math]::Min(400, $download.text.Length)))
    # 下载是**后台任务**（第 59 轮踩过：apply 紧跟其后会报"更新包未就绪"）⇒ 必须轮询 GET /api/update 到终态
    $dlSw = [Diagnostics.Stopwatch]::StartNew()
    $lastState = ''
    while ($dlSw.Elapsed.TotalSeconds -lt 300) {
        Start-Sleep -Seconds 2
        $st = Api $port $token 'GET' '/api/update' $null
        $stateNow = ''; $pathNow = ''; $errNow = ''
        if ($st.ok -and $st.body) {
            if ($st.body.PSObject.Properties.Name -contains 'state') { $stateNow = [string]$st.body.state }
            if ($st.body.PSObject.Properties.Name -contains 'path') { $pathNow = [string]$st.body.path }
            if ($st.body.PSObject.Properties.Name -contains 'error') { $errNow = [string]$st.body.error }
        }
        if ($stateNow -ne $lastState) { Log ("    更新状态: {0} path={1} error={2}（{3}s）" -f $stateNow, $pathNow, $errNow, [int]$dlSw.Elapsed.TotalSeconds); $lastState = $stateNow }
        if ($stateNow -eq 'ready' -or $stateNow -eq 'downloaded' -or $stateNow -eq 'idle') { break }
        if ($stateNow -eq 'failed' -or $errNow) { break }
    }
    $dlSw.Stop()
    Log ("  下载终态: state={0} 用时={1}s" -f $lastState, [int]$dlSw.Elapsed.TotalSeconds)
}

$apply = $null
if ($download -and $download.ok) {
    Log 'POST /api/update/apply（预期：进程退出、EXE 被替换、尝试重启）'
    $apply = Api $port $token 'POST' '/api/update/apply' '{}' 120
    Log ("  apply -> " + $apply.text.Substring(0, [Math]::Min(300, $apply.text.Length)))
}

# ---- 观察替换与退出
$replaced = $false; $shaAfter = ''
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    if (Test-Path $targetExe) {
        $shaAfter = (Get-FileHash -LiteralPath $targetExe -Algorithm SHA256).Hash.ToLower()
        if ($shaAfter -ne $shaBefore) { $replaced = $true; break }
    }
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { Log ("  {0}s 启动器进程已退出" -f [int]((Get-Date) - $proc.StartTime).TotalSeconds) }
}
Log ("EXE sha256(后)=$shaAfter  被替换=" + $replaced + "  等于清单值=" + ($shaAfter -eq $AssetSha256.ToLower()))
$scripts = @(Get-ChildItem $cold -File -Filter '*.cmd' -EA SilentlyContinue) + @(Get-ChildItem $cold -File -Filter '*.bat' -EA SilentlyContinue) + @(Get-ChildItem $dataDir -File -Filter '*.cmd' -EA SilentlyContinue) + @(Get-ChildItem $dataDir -File -Filter '*.bat' -EA SilentlyContinue)
foreach ($s in $scripts) { Log ("  替换脚本 " + $s.FullName); [IO.File]::WriteAllText((Join-Path $outDir ('replace-script-' + $s.Name + '.txt')), [IO.File]::ReadAllText($s.FullName, [Text.Encoding]::UTF8), (New-Object Text.UTF8Encoding($false))) }
$procAlive = [bool](Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)
$restarted = @(Get-Process -Name 'Pineglow.Launcher' -ErrorAction SilentlyContinue) | Where-Object { $_.Id -ne $proc.Id }
Log ("  原进程存活=" + $procAlive + "  其它同名进程数=" + @($restarted).Count)
# ---- 重启检测：替换脚本会重启 EXE；若产物是真 EXE，应当能在同一个 API 端口上重新健康起来
$restartHealthy = $false; $restartVersion = ''; $restartPid = 0
$swR = [Diagnostics.Stopwatch]::StartNew()
while ($swR.Elapsed.TotalSeconds -lt 90) {
    Start-Sleep -Seconds 3
    try {
        $rr = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($rr.StatusCode -eq 200) {
            $restartHealthy = $true
            $hh = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content
            if ($hh -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') {
                $tk2 = $Matches[1]
                $uu = Api $port $tk2 'GET' '/api/update' $null
                if ($uu.ok -and $uu.body -and ($uu.body.PSObject.Properties.Name -contains 'currentVersion')) { $restartVersion = [string]$uu.body.currentVersion }
            }
            $np = @(Get-Process -Name 'Pineglow.Launcher' -ErrorAction SilentlyContinue)
            if (@($np).Count -gt 0) { $restartPid = [int]$np[0].Id }
            break
        }
    } catch { }
}
Log ("  重启检测: 健康={0} 新进程 pid={1} 新版本={2}（用时 {3}s）" -f $restartHealthy, $restartPid, $restartVersion, [int]$swR.Elapsed.TotalSeconds)
if ($procAlive) { Stop-Process -Id $proc.Id -Force -EA SilentlyContinue }
foreach ($p in @($restarted)) { Stop-Process -Id $p.Id -Force -EA SilentlyContinue }
try { if (-not $srv.HasExited) { Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue } } catch { }

[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    manifestUrl = "http://127.0.0.1:$srvPort/manifest.json"
    manifest = $manifest
    exeSha256Before = $shaBefore
    exeSha256After = $shaAfter
    manifestFetchRequests = $manifestFetched
    updateStatus = $status.text
    updateCheck = $check.text
    updateDownload = $(if ($download) { $download.text } else { $null })
    updateApply = $(if ($apply) { $apply.text } else { $null })
    exeReplaced = $replaced
    exeEqualsManifestSha = ($shaAfter -eq $AssetSha256.ToLower())
    replaceScripts = @($scripts | ForEach-Object { $_.Name })
    originalProcessAlive = $procAlive
    restartedHealthy = $restartHealthy
    restartedPid = $restartPid
    restartedVersion = $restartVersion
    otherLauncherProcesses = @($restarted).Count
    notes = '本地清单走 HTTP；产物地址为公开 HTTPS（清单校验强制）；未改本机信任库'
} | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
exit 0
