#Requires -Version 5.1
<#
.SYNOPSIS
    验证"代理设置在**启动时**是否生效"（第 52 轮副产物）：config.json 预置代理 → 直接发网络请求看代理日志；
    再 POST /api/config（触发 SaveConfig → RefreshApiHttpProxy）后重复同一请求，对比。
.DESCRIPTION
    代码事实：`ApiHttp` 是静态字段，初始 `proxyEnabled: false`（LocalApiService.cs:41）；
    `LoadConfig()` 只设 `MirrorHelper.SelectedKey`、**不刷新代理**（1051-1060）；
    只有 `SaveConfig()` 才调 `RefreshApiHttpProxy`（1062-1071）。本脚本用行为验证这一点。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ProxyScript,
    [string] $WorkRoot,
    [string] $JsonPath
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'proxy-apply-step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$proxyJs = (Resolve-Path -LiteralPath $ProxyScript).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('proxyapply-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)

$proxyPort = Get-FreePort
$proxyLog = Join-Path $outDir 'proxy-apply-proxy.log'
if (Test-Path $proxyLog) { Remove-Item $proxyLog -Force }
$proxyProc = Start-Process -FilePath 'node' -ArgumentList ('"' + $proxyJs + '" "' + $proxyLog + '" ' + $proxyPort) -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
Log ("代理 port={0}" -f $proxyPort)

[IO.File]::WriteAllText((Join-Path $dataDir 'config.json'), ('{"proxyEnabled":true,"proxyHost":"127.0.0.1","proxyPort":' + $proxyPort + '}'), (New-Object Text.UTF8Encoding($false)))
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden
$token = $null
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $token }
Log ("启动器 apiPort={0}" -f $port)

function CountProxyLines { if (Test-Path $proxyLog) { return @(Get-Content $proxyLog).Count } else { return 0 } }
function CallNet([string]$label) {
    $before = CountProxyLines
    try { $null = Invoke-RestMethod "http://127.0.0.1:$port/api/versions/remote" -Headers $hdr -TimeoutSec 90 } catch { Log ("  $label 请求异常: " + $_.Exception.Message.Substring(0, [Math]::Min(80, $_.Exception.Message.Length))) }
    Start-Sleep -Seconds 3
    $after = CountProxyLines
    Log ("  {0}: 代理日志 {1} → {2}（新增 {3} 行）" -f $label, $before, $after, ($after - $before))
    return ($after - $before)
}

Log '-- A 启动后直接请求（未经过任何 config 保存）--'
$deltaA = CallNet 'A /api/versions/remote'

Log '-- B 先 POST /api/config（触发 SaveConfig → RefreshApiHttpProxy），再请求 --'
$cfgBody = @{ proxyEnabled = $true; proxyHost = '127.0.0.1'; proxyPort = $proxyPort } | ConvertTo-Json -Compress
try {
    $null = Invoke-RestMethod "http://127.0.0.1:$port/api/config" -Method Post -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($cfgBody)) -TimeoutSec 60
    Log '  POST /api/config 成功'
} catch { Log ('  POST /api/config 失败: ' + $_.Exception.Message.Substring(0, [Math]::Min(100, $_.Exception.Message.Length))) }
$deltaB = CallNet 'B /api/versions/remote'

$verdict = if ($deltaA -eq 0 -and $deltaB -gt 0) { '确认：代理设置**启动时不生效**，只有运行时保存配置后才生效' }
elseif ($deltaA -gt 0) { '启动即生效（与代码阅读不一致，需复核）' }
else { '两次都没有代理流量（可能该端点不走 ApiHttp）' }
Log ('判定: ' + $verdict)
[ordered]@{ generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'); proxyPort = $proxyPort; deltaBeforeConfigSave = $deltaA; deltaAfterConfigSave = $deltaB; verdict = $verdict } |
    ConvertTo-Json -Depth 4 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
try { if (-not $proxyProc.HasExited) { Stop-Process -Id $proxyProc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
