#Requires -Version 5.1
<#
.SYNOPSIS
    R06「外部服务」真实端点探测（第二版）：用候选产物自己的 API 打真实上游，完整响应落盘。

.DESCRIPTION
    第 33 轮已实测 piston-meta / launchercontent / fabric / quilt / adoptium / modrinth /
    microsoftonline / curseforge 在本机都可达 ⇒「外部服务不可用」不能再用作 R06 的挡箭牌。
    本脚本只做**只读**调用（不登录、不下载游戏、不改配置）：
      · 本地状态类：health / state / stats / instances / accounts
      · 真实上游类：versions/remote（piston-meta）、news（launchercontent）、java、sources、
        content/search（Modrinth + CurseForge）、content/icon（真实图标代理）、news-image（真实图片代理）
      · 皮肤路由：/api/accounts/{id}/skin-image —— **需要账号**，此处记录它在无账号时的真实响应，
        而不是把它当成"网络失败"。
    完整响应体写到 <JsonPath 同目录>\bodies\，便于逐条核对；JSON 里只留摘要与预览。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 90,
    [switch] $KeepWorkDir
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$runs = New-Object System.Collections.Generic.List[object]
$bodyDir = if ($JsonPath) { Join-Path (Split-Path -Parent $JsonPath) 'bodies' } else { Join-Path $env:TEMP ('r06-bodies-' + [guid]::NewGuid().ToString('N')) }
[void](New-Item -ItemType Directory -Path $bodyDir -Force)

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
$runDir = Join-Path $WorkRoot ('松光 R06 服务探测 ' + [guid]::NewGuid().ToString('N'))
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

Write-Host ''
Write-Host '== R06 外部服务真实端点探测（v2）==' -ForegroundColor Cyan
Write-Host "  产物: $exe"
Write-Host "  端口: $port   目录: $cold"

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
if (-not $token) { Write-Host '启动器未就绪或取不到令牌' -ForegroundColor Red; exit 2 }
Write-Host "  会话令牌: $($token.Substring(0,8))…"

$bodies = [ordered]@{}

function Probe {
    param([string] $Label, [string] $Path, [int] $TimeoutSec = 120, [switch] $Binary)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $rec = [ordered]@{ label = $Label; path = $Path; elapsedMs = 0; status = 0; ok = $false; bytes = 0; summary = ''; preview = '' }
    $safe = ($Label -replace '[^\w\-]', '_')
    try {
        $resp = Invoke-WebRequest "http://127.0.0.1:$port$Path" -UseBasicParsing -TimeoutSec $TimeoutSec -Headers @{ 'X-Pineglow-Token' = $token }
        $sw.Stop()
        $rec.status = [int]$resp.StatusCode
        $rec.ok = ($rec.status -eq 200)
        $contentType = [string]$resp.Headers['Content-Type']
        if ($Binary -or $contentType -notmatch 'json|text') {
            $bytes = [byte[]]$resp.Content
            $rec.bytes = $bytes.Length
            $rec.summary = "binary content-type=$contentType bytes=$($rec.bytes)"
            [IO.File]::WriteAllBytes((Join-Path $bodyDir "$safe.bin"), $bytes)
        } else {
            $body = [string]$resp.Content
            $rec.bytes = $body.Length
            [IO.File]::WriteAllText((Join-Path $bodyDir "$safe.json"), $body, (New-Object Text.UTF8Encoding($false)))
            $bodies[$Label] = $body
            $rec.preview = if ($body.Length -gt 700) { $body.Substring(0, 700) } else { $body }
            try {
                $j = $body | ConvertFrom-Json
                if ($j -is [array]) { $rec.summary = "array count=$(@($j).Count)" }
                else {
                    $names = @($j.PSObject.Properties.Name)
                    $rec.summary = 'keys=' + (($names | Select-Object -First 10) -join ',')
                    foreach ($cand in 'items', 'versions', 'sources', 'results', 'accounts', 'installations') {
                        if ($names -contains $cand) { $rec.summary += "; $cand=$(@($j.$cand).Count)" }
                    }
                }
            } catch { $rec.summary = '非 JSON 或解析失败' }
        }
    } catch {
        $sw.Stop()
        $code = 0
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) { $code = [int]$_.Exception.Response.StatusCode }
        $rec.status = $code
        $rec.summary = $_.Exception.Message
    }
    $rec.elapsedMs = [int]$sw.Elapsed.TotalMilliseconds
    $runs.Add([pscustomobject]$rec) | Out-Null
    $mark = if ($rec.ok) { '[OK]  ' } else { '[--]  ' }
    $color = if ($rec.ok) { 'Green' } else { 'DarkYellow' }
    Write-Host ("  {0} {1,-30} {2,4} {3,7}ms  {4}" -f $mark, $Label, $rec.status, $rec.elapsedMs, $rec.summary) -ForegroundColor $color
    return $rec
}

Write-Host ''
Write-Host '  -- 本地状态类（不触外网）--' -ForegroundColor Yellow
Probe 'health'    '/api/health' 20 | Out-Null
Probe 'state'     '/api/state' 30 | Out-Null
Probe 'stats'     '/api/stats' 30 | Out-Null
Probe 'instances' '/api/instances' 30 | Out-Null
Probe 'accounts'  '/api/accounts' 30 | Out-Null

Write-Host ''
Write-Host '  -- 真实上游：版本 / 新闻 / Java / 内容源 --' -ForegroundColor Yellow
Probe 'versions_remote' '/api/versions/remote' 120 | Out-Null
Probe 'news' '/api/news' 120 | Out-Null
Probe 'java'    '/api/java' 120 | Out-Null
Probe 'sources' '/api/sources' 60 | Out-Null

Write-Host ''
Write-Host '  -- 真实上游：内容搜索（Modrinth / CurseForge）--' -ForegroundColor Yellow
$mr = Probe 'content_search_modrinth' '/api/content/search?q=sodium&source=modrinth' 120
Probe 'content_search_curseforge' '/api/content/search?q=jei&source=curseforge' 120 | Out-Null

$projectId = $null
try {
    $j = $bodies['content_search_modrinth'] | ConvertFrom-Json
    $first = @($j.results) | Select-Object -First 1
    if ($first) { $projectId = [string]$first.id }
} catch { }
if ($projectId) {
    Write-Host ''
    Write-Host "  -- 真实图标代理（Modrinth projectId=$projectId）--" -ForegroundColor Yellow
    Probe 'content_icon_modrinth' "/api/content/icon?source=modrinth&category=mod&projectId=$projectId&token=$token" 120 -Binary | Out-Null
} else {
    Write-Host '  （未取到 projectId，跳过图标代理）' -ForegroundColor DarkGray
}

$imgId = $null
try {
    $nj = $bodies['news'] | ConvertFrom-Json
    foreach ($it in @($nj.items)) { if ($it.imageId) { $imgId = [string]$it.imageId; break } }
} catch { }
if ($imgId) {
    Write-Host ''
    Write-Host "  -- 真实新闻图片代理（id=$($imgId.Substring(0,12))…）--" -ForegroundColor Yellow
    Probe 'news_image' "/api/news-image?id=$imgId" 120 -Binary | Out-Null
} else {
    Write-Host '  （新闻未给出 imageId，跳过图片代理）' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '  -- 皮肤路由（需要账号；记录无账号时的真实响应）--' -ForegroundColor Yellow
Probe 'skin_image_no_account' '/api/accounts/not-a-real-account/skin-image?token=' 60 | Out-Null

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exePath = $exe
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    runDir = $runDir
    bodyDir = $bodyDir
    probes = $runs
    okCount = @($runs | Where-Object { $_.ok }).Count
    respondedCount = @($runs | Where-Object { $_.status -gt 0 }).Count
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Milliseconds 500
if (-not $KeepWorkDir) { Remove-Item -LiteralPath $runDir -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host ''
Write-Host ("R06 服务探测: 200-OK={0} 有响应={1} 共 {2} 项；完整响应在 {3}" -f $result.okCount, $result.respondedCount, $runs.Count, $bodyDir) -ForegroundColor Cyan
exit 0
