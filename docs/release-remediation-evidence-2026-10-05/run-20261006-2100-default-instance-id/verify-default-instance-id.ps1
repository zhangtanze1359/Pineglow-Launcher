#Requires -Version 5.1
<#
.SYNOPSIS
    **全新安装下"默认游戏"实例的 id 每次都在变 ⇒ 任何针对它的操作都报"实例不存在"**（第 65 轮）
.DESCRIPTION
    线索：`ConfigStore.LoadAsync()` 在 `config.json` **不存在**时 `new AppConfig` + `InstanceService.EnsureDefaultInstance(obj)`
    然后**直接 return（不落盘）**；而 `GameInstance.Id` 是新生成的 GUID ⇒ 每次 `Load()` 造出来的默认实例 id 都不同。
    本脚本做最小复现：全新便携目录（无 config.json）→ 连续两次 `/api/state` 取 id → 用第一次的 id 调 `/launch`
    → 再做一次"会落盘"的操作（建离线账号）→ 看 id 是否从此稳定、操作是否恢复可用。
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
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 5) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail }
    }
}
function FirstId($state) {
    $arr = @()
    if ($state.ok -and $state.body -and ($state.body.PSObject.Properties.Name -contains 'instances')) { $arr = @($state.body.instances) }
    if ($arr.Count -eq 0) { return '' }
    if ($arr[0].PSObject.Properties.Name -contains 'id') { return [string]$arr[0].id }
    return ''
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('defaultid-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$cfgPath = Join-Path $dataDir 'config.json'
Log ("全新便携目录；启动前 config.json 存在=" + (Test-Path $cfgPath))

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

$s1 = Api $port $token 'GET' '/api/state' $null 60
$id1 = FirstId $s1
$s2 = Api $port $token 'GET' '/api/state' $null 60
$id2 = FirstId $s2
$s3 = Api $port $token 'GET' '/api/state' $null 60
$id3 = FirstId $s3
Log ("连续三次 /api/state 的默认实例 id：" + $id1 + " / " + $id2 + " / " + $id3)
$unstable = ($id1 -ne $id2) -or ($id2 -ne $id3)
Log ("  id 是否每次都变=" + $unstable + " ；此刻 config.json 存在=" + (Test-Path $cfgPath))

$launch = Api $port $token 'POST' ("/api/instances/" + $id1 + "/launch") '{}' 60
Log ("用第 1 次的 id 调 /launch -> status=" + $launch.status + " " + $launch.text.Substring(0, [Math]::Min(160, $launch.text.Length)))
$pre = Api $port $token 'GET' ("/api/instances/" + $id1 + "/prelaunch-check") $null 60
Log ("用第 1 次的 id 调 /prelaunch-check -> status=" + $pre.status + " " + $pre.text.Substring(0, [Math]::Min(160, $pre.text.Length)))

# 触发一次"会落盘"的操作（建离线账号 ⇒ SaveConfig）
$acc = Api $port $token 'POST' '/api/accounts/offline' '{"userName":"DefaultIdProbe"}' 60
Log ("建离线账号（应触发 SaveConfig）-> " + $acc.text.Substring(0, [Math]::Min(120, $acc.text.Length)) + " ；config.json 存在=" + (Test-Path $cfgPath))
$s4 = Api $port $token 'GET' '/api/state' $null 60
$id4 = FirstId $s4
$s5 = Api $port $token 'GET' '/api/state' $null 60
$id5 = FirstId $s5
Log ("落盘后再取两次 id：" + $id4 + " / " + $id5 + " ；是否稳定=" + ($id4 -eq $id5))
$pre2 = Api $port $token 'GET' ("/api/instances/" + $id4 + "/prelaunch-check") $null 60
Log ("落盘后用该 id 调 /prelaunch-check -> status=" + $pre2.status + " " + $pre2.text.Substring(0, [Math]::Min(200, $pre2.text.Length)))

$checks = [ordered]@{
    id_unstable_before_save = $unstable
    launch_with_state_id_failed = ((-not $launch.ok) -and ($launch.status -eq 400))
    prelaunch_with_state_id_failed = ((-not $pre.ok) -and ($pre.status -eq 400))
    config_saved_after_account = (Test-Path $cfgPath)
    id_stable_after_save = ($id4 -eq $id5)
    action_works_after_save = ($pre2.ok -or $pre2.status -ne 400)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'REPRODUCED：全新安装下默认实例 id 每次都在变，针对它的操作全部 400；落盘一次后 id 稳定、操作恢复'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = '未完全复现：' + $fails.Count + ' 项不符（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    ids = [ordered]@{ first = $id1; second = $id2; third = $id3; afterSave = $id4; afterSaveSecond = $id5 }
    launchWithFirstId = [ordered]@{ status = $launch.status; response = $launch.text }
    prelaunchWithFirstId = [ordered]@{ status = $pre.status; response = $pre.text }
    afterSavePrelaunch = [ordered]@{ status = $pre2.status; response = $pre2.text }
    configJsonExists = [ordered]@{ beforeStart = $false; afterStateCalls = (Test-Path $cfgPath); afterAccount = (Test-Path $cfgPath) }
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
