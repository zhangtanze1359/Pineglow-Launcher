#Requires -Version 5.1
<#
.SYNOPSIS
    受控复跑 Quilt 1.21+ 导入，**抓取 500 响应体**（第 95 轮）：判断是"包/加载器不支持"还是"未处理异常无诊断"。
.DESCRIPTION
    用真实产品 API 导入同一个真实 Quilt 1.21.1 包；失败时读取 HTTP 响应体与产品日志，并检查是否留下半成品实例目录。
    不改产品源码；只读与观测。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][string] $PackPath,
    [Parameter(Mandatory)][string] $JsonPath,
    [int] $ImportTimeoutSec = 900
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'controlled-import-step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$cold = (Resolve-Path -LiteralPath $ColdDir).Path
$pack = (Resolve-Path -LiteralPath $PackPath).Path
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'ci-stdout.log') -RedirectStandardError (Join-Path $cold 'ci-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 启动器未就绪'; exit 2 }
Log ('启动器 pid=' + $proc.Id + ' apiPort=' + $port + '  包=' + (Split-Path $pack -Leaf) + ' ' + (Get-Item $pack).Length + ' B')
$instRoot = Join-Path $cold 'data\.minecraft\instances'
$before = @(Get-ChildItem $instRoot -Directory -EA SilentlyContinue | ForEach-Object { $_.Name })
$logFile = Join-Path $cold ('data\logs\launcher-' + (Get-Date).ToString('yyyyMMdd') + '.log')
$logBefore = if (Test-Path $logFile) { @(Get-Content $logFile).Count } else { 0 }

$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
$body = @{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64 } | ConvertTo-Json -Compress
$req = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:$port/api/instances/import")
$req.Method = 'POST'; $req.ContentType = 'application/json; charset=utf-8'
$req.Timeout = $ImportTimeoutSec * 1000; $req.ReadWriteTimeout = $ImportTimeoutSec * 1000
$req.Headers.Add('X-Pineglow-Token', $token)
$bytes = [Text.Encoding]::UTF8.GetBytes($body); $req.ContentLength = $bytes.Length
$s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
$sw = [Diagnostics.Stopwatch]::StartNew()
$status = 0; $respBody = ''
try {
    $resp = $req.GetResponse()
    $status = [int]([System.Net.HttpWebResponse]$resp).StatusCode
    $sr = New-Object IO.StreamReader($resp.GetResponseStream()); $respBody = $sr.ReadToEnd(); $sr.Close(); $resp.Close()
} catch [System.Net.WebException] {
    $we = $_.Exception
    if ($we.Response) {
        $status = [int]([System.Net.HttpWebResponse]$we.Response).StatusCode
        try { $sr = New-Object IO.StreamReader($we.Response.GetResponseStream()); $respBody = $sr.ReadToEnd(); $sr.Close() } catch { $respBody = '(读取响应体失败: ' + $_.Exception.Message + ')' }
    } else { $respBody = '(无响应对象) ' + $we.Message }
}
$sw.Stop()
Log ('导入返回: HTTP ' + $status + ' 用时=' + [int]$sw.Elapsed.TotalSeconds + 's')
Log ('响应体: ' + $respBody.Substring(0, [Math]::Min(600, $respBody.Length)))
Start-Sleep -Seconds 5
$after = @(Get-ChildItem $instRoot -Directory -EA SilentlyContinue | ForEach-Object { $_.Name })
$new = @($after | Where-Object { $before -notcontains $_ })
Log ('新增实例目录: ' + $(if ($new.Count -eq 0) { '(无)' } else { $new -join ', ' }))
$logAfter = if (Test-Path $logFile) { @(Get-Content $logFile).Count } else { 0 }
$newLines = @()
if ($logAfter -gt $logBefore) { $newLines = @(Get-Content $logFile | Select-Object -Skip $logBefore) }
Log ('产品日志新增行数=' + $newLines.Count)
$newLines | Select-Object -First 20 | ForEach-Object { Log ('  LOG ' + $_) }
$stderrFile = Join-Path $cold 'ci-stderr.log'
$stderrText = if (Test-Path $stderrFile) { [IO.File]::ReadAllText($stderrFile, [Text.Encoding]::UTF8) } else { '' }
Log ('产品 stderr 长度=' + $stderrText.Length + $(if ($stderrText.Length -gt 0) { ' 前 400 字符: ' + $stderrText.Substring(0, [Math]::Min(400, $stderrText.Length)) } else { '' }))
@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    pack = (Split-Path $pack -Leaf); packBytes = (Get-Item $pack).Length
    httpStatus = $status; seconds = [int]$sw.Elapsed.TotalSeconds; responseBody = $respBody
    newInstanceDirs = $new; productLogNewLines = $newLines; productStderr = $stderrText
} | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($status -eq 200) { exit 0 } else { exit 1 }
