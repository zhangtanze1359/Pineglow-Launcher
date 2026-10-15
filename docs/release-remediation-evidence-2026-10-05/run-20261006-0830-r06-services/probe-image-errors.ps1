#Requires -Version 5.1
<#
.SYNOPSIS
    抓取两个图片代理端点在**真实上游/本机网络**下的精确错误正文（用于判定"环境限制"还是"产品缺陷"）。

.DESCRIPTION
    第 33 轮探测里两个图片端点非 200：
      · /api/news-image     → 400（9ms）
      · /api/content/icon   → 504（约 5s = ContentIconService.ConnectTimeoutSeconds）
    代码里 400 对应 policy_rejected/dns_private/dns_mixed/dns_failed/mime_rejected/...，
    而 unknown_image 是 **404** —— 所以必须拿到 `{"error":"<kind>"}` 才能下结论。
    本脚本只读，且会把 400/502/504 的正文原样打出来。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 90
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

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
$runDir = Join-Path $WorkRoot ('松光 图片代理诊断 ' + [guid]::NewGuid().ToString('N'))
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

Write-Host ''
Write-Host '== 图片代理错误正文诊断 ==' -ForegroundColor Cyan
$records = New-Object System.Collections.Generic.List[object]

function Call-Raw {
    param([string] $Label, [string] $Path, [int] $TimeoutSec = 60)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $rec = [ordered]@{ label = $Label; path = $Path; status = 0; elapsedMs = 0; contentType = ''; bytes = 0; body = '' }
    try {
        $resp = Invoke-WebRequest "http://127.0.0.1:$port$Path" -UseBasicParsing -TimeoutSec $TimeoutSec -Headers @{ 'X-Pineglow-Token' = $token }
        $sw.Stop()
        $rec.status = [int]$resp.StatusCode
        $rec.contentType = [string]$resp.Headers['Content-Type']
        $rec.bytes = ([byte[]]$resp.Content).Length
        $rec.body = "<binary $($rec.bytes) bytes>"
    } catch {
        $sw.Stop()
        $resp = $_.Exception.Response
        if ($resp) {
            $rec.status = [int]$resp.StatusCode
            try {
                $stream = $resp.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $rec.body = $reader.ReadToEnd()
                $reader.Dispose()
            } catch { $rec.body = '<读取失败>' }
        } else { $rec.body = $_.Exception.Message }
    }
    $rec.elapsedMs = [int]$sw.Elapsed.TotalMilliseconds
    $records.Add([pscustomobject]$rec) | Out-Null
    $color = if ($rec.status -eq 200) { 'Green' } else { 'DarkYellow' }
    Write-Host ("  {0,-30} {1,4} {2,7}ms  {3}" -f $rec.label, $rec.status, $rec.elapsedMs, $rec.body) -ForegroundColor $color
    return $rec
}

# 先触发一次新闻拉取（会注册图片 id）
$news = Invoke-RestMethod "http://127.0.0.1:$port/api/news" -TimeoutSec 120 -Headers @{ 'X-Pineglow-Token' = $token }
$imgId = $null
foreach ($it in @($news.items)) { if ($it.imageId) { $imgId = [string]$it.imageId; break } }
Write-Host "  新闻条目=$(@($news.items).Count)  首个 imageId=$(if ($imgId) { $imgId.Substring(0,16) + '…' } else { '<null>' })"
Write-Host "  meta: state=$($news.meta.state) source=$($news.meta.source) isStale=$($news.meta.isStale) error=$($news.meta.error)"

Call-Raw 'news-image(真实 id)' "/api/news-image?id=$imgId" 60 | Out-Null
Call-Raw 'news-image(不存在的 id)' '/api/news-image?id=' + ('0' * 64) 60 | Out-Null
Call-Raw 'news-image(空 id)' '/api/news-image?id=' 60 | Out-Null

$mr = Invoke-RestMethod "http://127.0.0.1:$port/api/content/search?q=sodium&source=modrinth" -TimeoutSec 120 -Headers @{ 'X-Pineglow-Token' = $token }
$pid1 = [string](@($mr.results) | Select-Object -First 1).id
Write-Host "  Modrinth 首个 projectId=$pid1"
Call-Raw 'content/icon(modrinth 真实 id)' "/api/content/icon?source=modrinth&category=mod&projectId=$pid1&token=$token" 60 | Out-Null

$cf = Invoke-RestMethod "http://127.0.0.1:$port/api/content/search?q=jei&source=curseforge" -TimeoutSec 120 -Headers @{ 'X-Pineglow-Token' = $token }
$pid2 = [string](@($cf.results) | Select-Object -First 1).id
Write-Host "  CurseForge 首个 projectId=$pid2"
Call-Raw 'content/icon(curseforge 真实 id)' "/api/content/icon?source=curseforge&category=mod&projectId=$pid2&token=$token" 60 | Out-Null
Call-Raw 'content/icon(非法主机源)' "/api/content/icon?source=evil&category=mod&projectId=$pid1&token=$token" 60 | Out-Null
Call-Raw 'content/icon(无 token)' "/api/content/icon?source=modrinth&category=mod&projectId=$pid1" 60 | Out-Null

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    newsMeta = $news.meta
    records = $records
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Remove-Item -LiteralPath $runDir -Recurse -Force -ErrorAction SilentlyContinue
exit 0
