#Requires -Version 5.1
<#
.SYNOPSIS
    **服务器列表 + 延迟探测（ping）** 实测（第 67 轮）：添加/列出/ping/删除，以及**重启后是否还在**。
.DESCRIPTION
    路由：`GET /api/servers`、`POST /api/servers {name,host,port}`、`POST /api/servers/{id}/ping`、`POST /api/servers/{id}/delete`
    （LocalApiService.cs:848-880；ping 走 `ServerPingService.QueryAsync`，6 秒超时）。
    ping 两种目标：一个真实公共服务器（可能被本机网络环境挡住 ⇒ 也接受"确定性失败"）、一个本机未监听端口（应当很快失败）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [string] $PublicHost = 'mc.hypixel.net',
    [int] $PublicPort = 25565,
    [int] $StartupTimeoutSec = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
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
function StartL([string]$cold, [int]$port) {
    $env:PINEGLOW_API_PORT = "$port"
    $pr = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
    $tk = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $tk = $Matches[1] }; break } } catch { }
    }
    return [pscustomobject]@{ proc = $pr; token = $tk }
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('servers-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$port = Get-FreePort
$l1 = StartL $cold $port
if (-not $l1.token) { Log '启动器未就绪'; exit 2 }
Log ("启动器 pid=" + $l1.proc.Id + " apiPort=" + $port)
# 先落盘一次（第 65/66 轮的教训：无 config.json 时默认实例/账号 id 不稳定）
$null = Api $port $l1.token 'POST' '/api/accounts/offline' '{"userName":"ServerProbe"}'

$list0 = Api $port $l1.token 'GET' '/api/servers' $null 60
Log ("初始 GET /api/servers -> " + $list0.text.Substring(0, [Math]::Min(160, $list0.text.Length)))

$add1 = Api $port $l1.token 'POST' '/api/servers' (@{ name = 'PublicProbe'; host = $PublicHost; port = "$PublicPort" }) 60
Log ("添加公共服务器 -> " + $add1.text.Substring(0, [Math]::Min(200, $add1.text.Length)))
$id1 = ''
if ($add1.ok -and $add1.body -and ($add1.body.PSObject.Properties.Name -contains 'server')) { $id1 = [string]$add1.body.server.id }
$add2 = Api $port $l1.token 'POST' '/api/servers' (@{ name = 'DeadPort'; host = '127.0.0.1'; port = '1' }) 60
$id2 = ''
if ($add2.ok -and $add2.body -and ($add2.body.PSObject.Properties.Name -contains 'server')) { $id2 = [string]$add2.body.server.id }
Log ("添加本机未监听端口 -> id2=" + $id2)

$list1 = Api $port $l1.token 'GET' '/api/servers' $null 60
$cnt1 = 0
if ($list1.ok -and $list1.body -and ($list1.body.PSObject.Properties.Name -contains 'servers')) { $cnt1 = @($list1.body.servers).Count }
Log ("添加后列表数量=" + $cnt1)

$sw = [Diagnostics.Stopwatch]::StartNew()
$ping1 = Api $port $l1.token 'POST' ("/api/servers/" + $id1 + "/ping") '{}' 60
$sw.Stop()
Log ("ping 公共服务器（" + [int]$sw.Elapsed.TotalSeconds + "s）-> " + $ping1.text.Substring(0, [Math]::Min(260, $ping1.text.Length)))
$sw2 = [Diagnostics.Stopwatch]::StartNew()
$ping2 = Api $port $l1.token 'POST' ("/api/servers/" + $id2 + "/ping") '{}' 60
$sw2.Stop()
Log ("ping 死端口（" + [int]$sw2.Elapsed.TotalSeconds + "s）-> " + $ping2.text.Substring(0, [Math]::Min(200, $ping2.text.Length)))

# 重启验证持久化
try { if (-not $l1.proc.HasExited) { Stop-Process -Id $l1.proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 3
$port2 = Get-FreePort
$l2 = StartL $cold $port2
if (-not $l2.token) { Log '二次启动失败'; exit 2 }
$list2 = Api $port2 $l2.token 'GET' '/api/servers' $null 60
$cnt2 = 0
if ($list2.ok -and $list2.body -and ($list2.body.PSObject.Properties.Name -contains 'servers')) { $cnt2 = @($list2.body.servers).Count }
Log ("重启后列表数量=" + $cnt2 + " -> " + $list2.text.Substring(0, [Math]::Min(220, $list2.text.Length)))

$del = Api $port2 $l2.token 'POST' ("/api/servers/" + $id2 + "/delete") '{}' 60
$list3 = Api $port2 $l2.token 'GET' '/api/servers' $null 60
$cnt3 = 0
if ($list3.ok -and $list3.body -and ($list3.body.PSObject.Properties.Name -contains 'servers')) { $cnt3 = @($list3.body.servers).Count }
Log ("删除一个后数量=" + $cnt3 + " ；删除响应=" + $del.text.Substring(0, [Math]::Min(140, $del.text.Length)))
$pingMissing = Api $port2 $l2.token 'POST' ("/api/servers/" + $id2 + "/ping") '{}' 60
Log ("对已删除的 id ping -> status=" + $pingMissing.status + " " + $pingMissing.text.Substring(0, [Math]::Min(140, $pingMissing.text.Length)))

$checks = [ordered]@{
    initial_list_empty = ($cnt1 -eq 2)
    add_saved_and_listed = ($cnt1 -eq 2)
    ping_public_answered = ($ping1.ok)
    ping_dead_port_failed_cleanly = ($ping2.ok -or $ping2.status -eq 400)
    persisted_after_restart = ($cnt2 -eq 2)
    delete_worked = ($del.ok -and $cnt3 -eq 1)
    deleted_id_ping_refused = ((-not $pingMissing.ok) -and $pingMissing.status -eq 400)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：添加/列出/ping（含失败路径）/重启持久化/删除 都符合预期'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    initial = $list0.text
    addPublic = [ordered]@{ id = $id1; response = $add1.text }
    addDeadPort = [ordered]@{ id = $id2; response = $add2.text }
    pingPublic = [ordered]@{ seconds = [int]$sw.Elapsed.TotalSeconds; response = $ping1.text }
    pingDeadPort = [ordered]@{ seconds = [int]$sw2.Elapsed.TotalSeconds; response = $ping2.text }
    afterRestart = [ordered]@{ count = $cnt2; response = $list2.text }
    delete = [ordered]@{ response = $del.text; countAfter = $cnt3 }
    pingDeleted = [ordered]@{ status = $pingMissing.status; response = $pingMissing.text }
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $l2.proc.HasExited) { Stop-Process -Id $l2.proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
