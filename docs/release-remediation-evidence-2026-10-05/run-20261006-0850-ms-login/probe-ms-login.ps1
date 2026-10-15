#Requires -Version 5.1
<#
.SYNOPSIS
    微软设备码登录：把"不需要真人"的那几段用真实端点验掉。

.DESCRIPTION
    R06 的账号项一直记为 NOT_RUN（需要真实微软账号）。但其中有三段**不需要真人**，却正是
    最容易悄悄坏掉的地方：
      1) 内置 Client ID + scope 对**个人微软账号**是否仍然可用（被撤销/停用 ⇒ 所有人登录都失败）；
      2) 端点与表单字段是否符合 Microsoft 的 device code 契约；
      3) 客户端拿到设备码后的**轮询**路径：未完成时应如实返回 pending，而不是报成功或报失败。
    本脚本：① 直连 Microsoft 用内置 client_id 申请设备码；② 启动候选产物走它自己的
    /api/accounts/microsoft/device 与 /poll。
    不输入任何账号密码、不完成授权；产生的设备码会自然过期。user_code 只记录前 4 位。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $ClientId = '60934999-9c8b-4731-8b78-a98d010edcde',
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 90
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$out = [ordered]@{ generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'); clientId = $ClientId }

function Mask-Code([string] $c) {
    if ([string]::IsNullOrWhiteSpace($c)) { return '' }
    if ($c.Length -le 4) { return '****' }
    return $c.Substring(0, 4) + ('*' * 4)
}

# ---------------------------------------------------------------- 1) 直连 Microsoft
Write-Host ''
Write-Host '== 微软设备码登录：真实端点可验部分 ==' -ForegroundColor Cyan
Write-Host "  内置 client_id: $ClientId"
Write-Host ''
Write-Host '  -- 1) 直连 Microsoft 申请设备码 --' -ForegroundColor Yellow
$direct = [ordered]@{ status = 0; error = ''; errorDescription = ''; verificationUri = ''; userCodeMasked = ''; expiresIn = 0; interval = 0; rawPreview = '' }
try {
    $resp = Invoke-WebRequest -Uri 'https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode' -Method Post `
        -Body @{ client_id = $ClientId; scope = 'XboxLive.signin offline_access' } `
        -UseBasicParsing -TimeoutSec 60
    $direct.status = [int]$resp.StatusCode
    $j = [string]$resp.Content | ConvertFrom-Json
    $direct.verificationUri = [string]$j.verification_uri
    $direct.userCodeMasked = Mask-Code([string]$j.user_code)
    $direct.expiresIn = [int]$j.expires_in
    $direct.interval = [int]$j.interval
    $direct.rawPreview = (([string]$resp.Content) -replace '"device_code":"[^"]+"', '"device_code":"<masked>"').Substring(0, [Math]::Min(400, (([string]$resp.Content) -replace '"device_code":"[^"]+"', '"device_code":"<masked>"').Length))
    Write-Host "     [OK] 200  verification_uri=$($direct.verificationUri)  user_code=$($direct.userCodeMasked)  expires_in=$($direct.expiresIn)s  interval=$($direct.interval)s" -ForegroundColor Green
} catch {
    $r = $_.Exception.Response
    if ($r) {
        $direct.status = [int]$r.StatusCode
        try {
            $reader = New-Object System.IO.StreamReader($r.GetResponseStream())
            $txt = $reader.ReadToEnd(); $reader.Dispose()
            $direct.rawPreview = $txt.Substring(0, [Math]::Min(400, $txt.Length))
            try { $je = $txt | ConvertFrom-Json; $direct.error = [string]$je.error; $direct.errorDescription = [string]$je.error_description } catch { }
        } catch { }
    } else { $direct.rawPreview = $_.Exception.Message }
    Write-Host "     [FAIL] status=$($direct.status) error=$($direct.error) desc=$($direct.errorDescription.Substring(0,[Math]::Min(120,$direct.errorDescription.Length)))" -ForegroundColor Red
}
$out['directDeviceCode'] = $direct

# ---------------------------------------------------------------- 2) 候选产物自己的两段
function Get-FreePort {
    for ($i = 0; $i -lt 60; $i++) {
        $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop()
        if ($p -gt 1024) { return $p }
    }
    throw '找不到空闲端口'
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('松光 登录探测 ' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'
[void](New-Item -ItemType Directory -Path $cold -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $cold 'data') -Force)
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
if (-not $token) { Write-Host '启动器未就绪' -ForegroundColor Red; exit 2 }

function Post-Api {
    param([string] $Label, [string] $Path, [int] $TimeoutSec = 90)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $rec = [ordered]@{ label = $Label; path = $Path; status = 0; elapsedMs = 0; body = '' }
    try {
        $resp = Invoke-WebRequest "http://127.0.0.1:$port$Path" -Method Post -UseBasicParsing -TimeoutSec $TimeoutSec `
            -Headers @{ 'X-Pineglow-Token' = $token } -ContentType 'application/json' -Body '{}'
        $sw.Stop()
        $rec.status = [int]$resp.StatusCode
        $rec.body = [string]$resp.Content
    } catch {
        $sw.Stop()
        $r = $_.Exception.Response
        if ($r) {
            $rec.status = [int]$r.StatusCode
            try { $rd = New-Object System.IO.StreamReader($r.GetResponseStream()); $rec.body = $rd.ReadToEnd(); $rd.Dispose() } catch { }
        } else { $rec.body = $_.Exception.Message }
    }
    $rec.elapsedMs = [int]$sw.Elapsed.TotalMilliseconds
    $shown = $rec.body -replace '"userCode":"[^"]*"', '"userCode":"<masked>"'
    if ($shown.Length -gt 260) { $shown = $shown.Substring(0, 260) + '…' }
    $color = if ($rec.status -eq 200) { 'Green' } else { 'Red' }
    Write-Host ("     {0,-26} {1,3} {2,6}ms  {3}" -f $Label, $rec.status, $rec.elapsedMs, $shown) -ForegroundColor $color
    return [pscustomobject]$rec
}

Write-Host ''
Write-Host '  -- 2) 候选产物：创建登录码 --' -ForegroundColor Yellow
$dev = Post-Api 'accounts/microsoft/device' '/api/accounts/microsoft/device' 90
$out['deviceStart'] = $dev
try { $jd = $dev.body | ConvertFrom-Json; $out['deviceStartParsed'] = [ordered]@{ ok = $jd.ok; verificationUri = $jd.verificationUri; expiresIn = $jd.expiresIn; interval = $jd.interval; userCodeMasked = (Mask-Code([string]$jd.userCode)); message = $jd.message; error = $jd.error } } catch { }

Write-Host ''
Write-Host '  -- 3) 候选产物：轮询一次（无人完成 ⇒ 应如实 pending）--' -ForegroundColor Yellow
$poll = Post-Api 'accounts/microsoft/poll' '/api/accounts/microsoft/poll' 90
$out['poll'] = $poll
try { $jp = $poll.body | ConvertFrom-Json; $out['pollParsed'] = [ordered]@{ ok = $jp.ok; status = $jp.status; error = $jp.error; message = $jp.message } } catch { }

# 判定
$verdict = 'UNKNOWN'
$reasons = New-Object System.Collections.Generic.List[string]
if ($direct.status -ne 200) { $reasons.Add("直连设备码申请失败 status=$($direct.status) error=$($direct.error)") | Out-Null }
if ($null -ne $out['deviceStartParsed'] -and $out['deviceStartParsed'].ok -ne $true) { $reasons.Add("候选产物创建登录码未成功: $($out['deviceStartParsed'].message)") | Out-Null }
if ($null -ne $out['pollParsed'] -and $out['pollParsed'].status -ne 'pending') { $reasons.Add("轮询未返回 pending 而是 '$($out['pollParsed'].status)'") | Out-Null }
$verdict = if ($reasons.Count -eq 0) { 'PASS（真人步骤仍未验）' } else { 'FAIL' }
$out['verdict'] = $verdict
$out['reasons'] = $reasons

if ($JsonPath) { ($out | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Remove-Item -LiteralPath $runDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "微软登录（无需真人部分）判定: $verdict" -ForegroundColor Cyan
foreach ($r in $reasons) { Write-Host "  - $r" -ForegroundColor Red }
exit $(if ($verdict -eq 'FAIL') { 1 } else { 0 })
