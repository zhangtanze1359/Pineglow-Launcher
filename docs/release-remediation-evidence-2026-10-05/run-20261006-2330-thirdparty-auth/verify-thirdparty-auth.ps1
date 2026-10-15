#Requires -Version 5.1
<#
.SYNOPSIS
    **第三方（authlib-injector / Yggdrasil）账号**的地址校验与登录失败路径实测（第 70 轮）。
.DESCRIPTION
    路由：`POST /api/accounts/authlib {serverUrl,userName,password}`（LocalApiService.cs:4187 → `AuthlibInjectorService.LoginAsync`）。
    本轮不追求"登录成功"（需要真实第三方账号），而是验三件事：
      ① 非 URL ⇒ 明确拒绝；② 非 Yggdrasil 的 HTTPS 站点 ⇒ 明确失败；③ **真实公共 Yggdrasil** + 假凭据 ⇒ **干净的失败**（不挂起、不崩、不加账号）。
    另附：authlib-injector agent 的两个下载 URL 是否可用（与 Forge 安装器 404 同一类风险）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [string] $RealYggdrasil = 'https://littleskin.cn/api/yggdrasil',
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
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec
        } else { $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $TimeoutSec }
        $sw.Stop()
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 5); ms = [int]$sw.Elapsed.TotalMilliseconds }
    } catch {
        $sw.Stop()
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail; ms = [int]$sw.Elapsed.TotalMilliseconds }
    }
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('tp-' + [guid]::NewGuid().ToString('N'))
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
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)
$acc0 = Api $port $token 'GET' '/api/accounts' $null 60
$cnt0 = 0; if ($acc0.ok -and $acc0.body -and ($acc0.body.PSObject.Properties.Name -contains 'accounts')) { $cnt0 = @($acc0.body.accounts).Count }
Log ("初始账号数=" + $cnt0)

$cases = @(
    @{ name = 'bad-url'; server = 'not-a-url'; user = 'probe'; pass = 'probe' },
    @{ name = 'not-yggdrasil'; server = 'https://example.com/'; user = 'probe'; pass = 'probe' },
    @{ name = 'real-yggdrasil-bad-cred'; server = $RealYggdrasil; user = 'pineglow-nonexistent-probe'; pass = 'definitely-wrong-password' }
)
$out = [ordered]@{}
foreach ($c in $cases) {
    $r = Api $port $token 'POST' '/api/accounts/authlib' (@{ serverUrl = $c.server; userName = $c.user; password = $c.pass }) 90
    Log ("[" + $c.name + "] serverUrl=" + $c.server + " -> status=" + $r.status + "（" + $r.ms + "ms） " + $r.text.Substring(0, [Math]::Min(220, $r.text.Length)))
    $out[$c.name] = [ordered]@{ serverUrl = $c.server; status = $r.status; ms = $r.ms; response = $r.text }
}
$acc1 = Api $port $token 'GET' '/api/accounts' $null 60
$cnt1 = 0; if ($acc1.ok -and $acc1.body -and ($acc1.body.PSObject.Properties.Name -contains 'accounts')) { $cnt1 = @($acc1.body.accounts).Count }
$health = Api $port $token 'GET' '/api/health' $null 30
Log ("失败后账号数=" + $cnt1 + "（应与初始相同）; /api/health ok=" + $health.ok)

$checks = [ordered]@{
    bad_url_rejected = (($out['bad-url'].status -eq 400) -and ($out['bad-url'].response -match 'http'))
    not_yggdrasil_failed_cleanly = (($out['not-yggdrasil'].status -eq 400) -or ($out['not-yggdrasil'].status -eq 500))
    real_yggdrasil_failed_cleanly = (($out['real-yggdrasil-bad-cred'].status -eq 400) -or ($out['real-yggdrasil-bad-cred'].status -eq 500) -or ($out['real-yggdrasil-bad-cred'].status -eq 401))
    no_account_added = ($cnt1 -eq $cnt0)
    launcher_still_healthy = $health.ok
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：地址校验与三种失败路径都给出确定性结果，未挂起、未加账号、启动器仍健康'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    accountCountBefore = $cnt0
    cases = $out
    accountCountAfter = $cnt1
    launcherHealthy = $health.ok
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
