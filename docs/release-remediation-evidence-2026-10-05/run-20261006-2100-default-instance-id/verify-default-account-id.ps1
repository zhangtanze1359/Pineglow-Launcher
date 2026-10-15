#Requires -Version 5.1
<#
.SYNOPSIS
    同一条根因的**第二个面**：默认**账号**是否也"每次一个新 id"（第 66 轮）。
.DESCRIPTION
    `ConfigStore.LoadAsync()` 在无 config.json 时还会调 `AccountService.EnsureAccounts(obj)` 且不落盘；
    `Account.Id` 同样是对象级新 GUID ⇒ 推测默认账号也不可寻址。本脚本做最小复现：
    连续两次 `/api/state` 取默认账号 id → 用第一次的 id 调一个"按账号寻址"的接口（`DELETE /api/accounts/{id}`）看是否 400。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [int] $StartupTimeoutSec = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'account-step.log'
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
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 5) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail }
    }
}
function FirstAccountId($state) {
    $arr = @()
    if ($state.ok -and $state.body -and ($state.body.PSObject.Properties.Name -contains 'accounts')) { $arr = @($state.body.accounts) }
    if ($arr.Count -eq 0) { return '' }
    if ($arr[0].PSObject.Properties.Name -contains 'id') { return [string]$arr[0].id }
    return ''
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('acctid-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
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
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port + " ；config.json 存在=" + (Test-Path (Join-Path $dataDir 'config.json')))

$a1 = FirstAccountId (Api $port $token 'GET' '/api/state' $null 60)
$a2 = FirstAccountId (Api $port $token 'GET' '/api/state' $null 60)
Log ("连续两次 /api/state 的默认账号 id：" + $a1 + " / " + $a2 + " ；是否相同=" + ($a1 -eq $a2))

$del = Api $port $token 'DELETE' ("/api/accounts/" + $a1) $null 60
Log ("用第 1 次的 id 调 DELETE /api/accounts/{id} -> status=" + $del.status + " " + $del.text.Substring(0, [Math]::Min(160, $del.text.Length)))
$list = Api $port $token 'GET' '/api/accounts' $null 60
Log ("GET /api/accounts -> " + $list.text.Substring(0, [Math]::Min(200, $list.text.Length)))

$checks = [ordered]@{
    default_account_id_unstable = ($a1 -ne $a2)
    account_action_failed = ((-not $del.ok) -and ($del.status -eq 400))
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'REPRODUCED：默认账号 id 每次也不同、按账号寻址的接口报不存在（与默认实例同一条根因）'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = '未复现：' + $fails.Count + ' 项不符（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    accountIds = [ordered]@{ first = $a1; second = $a2; same = ($a1 -eq $a2) }
    deleteResponse = [ordered]@{ status = $del.status; response = $del.text }
    accountsAfter = $list.text
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
