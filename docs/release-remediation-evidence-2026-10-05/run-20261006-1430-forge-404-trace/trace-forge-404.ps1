#Requires -Version 5.1
<#
.SYNOPSIS
    用**记录型代理**追查"Forge 安装器 404"：让启动器走我们自己的代理，看它真正请求的是哪个地址（第 52 轮）。
.DESCRIPTION
    产品自带代理设置（config.json 的 proxyEnabled/proxyHost/proxyPort，ApiHttp 会用它），
    所以无需改任何代码就能把请求导到本机代理。HTTPS 走 CONNECT ⇒ 日志能看到**主机名**（足以区分官方 maven 与镜像），
    路径看不到（不做 MITM，不改本机信任库）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $MrpackPath,
    [Parameter(Mandatory)][string] $ProxyScript,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120,
    [int] $ImportTimeoutSec = 1800
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = if ($JsonPath) { Split-Path -Parent $JsonPath } else { $env:TEMP }
if (-not (Test-Path $outDir)) { [void](New-Item -ItemType Directory -Path $outDir -Force) }
$logPath = Join-Path $outDir 'step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path
$proxyJs = (Resolve-Path -LiteralPath $ProxyScript).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('trace404-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)

# ---- 启动记录型代理
$proxyPort = Get-FreePort
$proxyLog = Join-Path $outDir 'proxy.log'
if (Test-Path $proxyLog) { Remove-Item $proxyLog -Force }
$proxyProc = Start-Process -FilePath 'node' -ArgumentList ('"' + $proxyJs + '" "' + $proxyLog + '" ' + $proxyPort) -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
Log ("代理 pid={0} port={1} 日志={2}" -f $proxyProc.Id, $proxyPort, $proxyLog)

# ---- 让启动器用这个代理（产品自带设置）
$cfg = '{"proxyEnabled":true,"proxyHost":"127.0.0.1","proxyPort":' + $proxyPort + '}'
[IO.File]::WriteAllText((Join-Path $dataDir 'config.json'), $cfg, (New-Object Text.UTF8Encoding($false)))
Log ("已写 config.json: " + $cfg)

$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru `
    -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
$token = $null
$deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $token }
Log ("启动器 pid={0} apiPort={1}" -f $proc.Id, $port)

# ---- 关键：先 POST /api/config 触发 RefreshApiHttpProxy（否则启动时的静态 ApiHttp 不带代理，见 proxy-apply-test.ps1）
$cfgBody = @{ proxyEnabled = $true; proxyHost = '127.0.0.1'; proxyPort = $proxyPort } | ConvertTo-Json -Compress
try {
    $null = Invoke-RestMethod "http://127.0.0.1:$port/api/config" -Method Post -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($cfgBody)) -TimeoutSec 60
    Log '  已 POST /api/config 触发代理刷新'
} catch { Log ('  POST /api/config 失败: ' + $_.Exception.Message) }
Start-Sleep -Seconds 2

# ---- 导入（同步接口，可能数分钟）
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
$body = @{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64 } | ConvertTo-Json -Compress
$sw = [Diagnostics.Stopwatch]::StartNew()
$result = ''
try {
    $r = Invoke-RestMethod "http://127.0.0.1:$port/api/instances/import" -Method Post -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec $ImportTimeoutSec
    $result = ($r | ConvertTo-Json -Compress)
} catch {
    $code = 0; $detail = ''
    if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
    try { $detail = [string]$_.ErrorDetails.Message } catch { }
    if (-not $detail) { $detail = $_.Exception.Message }
    $result = "ERROR status=$code $detail"
}
$sw.Stop()
Log ("导入用时={0}s 结果={1}" -f [int]$sw.Elapsed.TotalSeconds, $result.Substring(0, [Math]::Min(220, $result.Length)))

# ---- 读代理日志，找与 forge/maven/镜像相关的行
$plog = @(); if (Test-Path $proxyLog) { $plog = @(Get-Content $proxyLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
$hosts = @{}
foreach ($line in $plog) {
    # 注意：PowerShell 5.1 **没有** ?? 运算符（第 52 轮踩过，语法检查会直接拦下）
    if ($line -match 'CONNECT\s+([^:\s]+):') { $h = $Matches[1] } elseif ($line -match 'HTTP\s+\w+\s+https?://([^/\s]+)') { $h = $Matches[1] } else { continue }
    if ($hosts.ContainsKey($h)) { $hosts[$h] = [int]$hosts[$h] + 1 } else { $hosts[$h] = 1 }
}
Log ("代理日志行数={0}  不同主机数={1}" -f $plog.Count, $hosts.Count)
foreach ($k in ($hosts.Keys | Sort-Object)) { Log ("  {0,-40} {1} 次" -f $k, $hosts[$k]) }
$forgeLines = @($plog | Where-Object { $_ -match 'forge|maven|bangbang|neoforged' })
Log ("与 forge/maven/镜像 相关的代理行数=" + $forgeLines.Count)
foreach ($l in ($forgeLines | Select-Object -First 20)) { Log ("  " + $l) }

$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    proxyPort = $proxyPort
    importSeconds = [int]$sw.Elapsed.TotalSeconds
    importResult = $result
    proxyLogLines = $plog.Count
    hostsSeen = $hosts
    forgeRelatedLines = $forgeLines
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
@($plog) | Set-Content (Join-Path $outDir 'proxy-lines.txt') -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
try { if (-not $proxyProc.HasExited) { Stop-Process -Id $proxyProc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
