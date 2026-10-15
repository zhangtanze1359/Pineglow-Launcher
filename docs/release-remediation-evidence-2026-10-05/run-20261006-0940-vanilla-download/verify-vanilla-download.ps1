#Requires -Version 5.1
<#
.SYNOPSIS
    原版版本的**真实下载与完整性核对**（R06「原版下载/修复」的服务腿）。

.DESCRIPTION
    R06 一直把"原版下载"记为需要真机。但下载这一段**不需要游戏、不需要 Java**：
    用候选产物自己的 `POST /api/versions/download` 触发真实安装，再逐项核对落盘结果
    与 Mojang 官方元数据（client.jar 的 size+sha1、libraries 数量、assets objects 数量）。
    只读外部服务、只写临时目录；不启动游戏。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $VersionId,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 90,
    [int] $PollTimeoutSec = 1200
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
function Sha1([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA1).Hash.ToLower() }

# ---- 官方元数据（作为核对基准）
Write-Host ''
Write-Host '== 原版下载真实验收 ==' -ForegroundColor Cyan
$manifest = Invoke-RestMethod 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json' -TimeoutSec 60
$entry = $manifest.versions | Where-Object { $_.id -eq $VersionId } | Select-Object -First 1
if (-not $entry) { Write-Host "官方清单里没有版本 $VersionId" -ForegroundColor Red; exit 2 }
$vdoc = Invoke-RestMethod $entry.url -TimeoutSec 60
$expClientSize = [int64]$vdoc.downloads.client.size
$expClientSha1 = ([string]$vdoc.downloads.client.sha1).ToLower()
$expLibs = @($vdoc.libraries).Count
$expAssetsId = [string]$vdoc.assetIndex.id
$expAssetsTotal = [int64]$vdoc.assetIndex.totalSize
Write-Host "  版本 $VersionId : client=$expClientSize B sha1=$($expClientSha1.Substring(0,12))… libs=$expLibs assets=$expAssetsId ($([math]::Round($expAssetsTotal/1MB,1)) MB)"

# ---- 启动候选产物
$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('松光 原版下载 ' + [guid]::NewGuid().ToString('N'))
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
$hdr = @{ 'X-Pineglow-Token' = $token }

# ---- 触发下载
Write-Host ''
Write-Host '  -- 触发 POST /api/versions/download --' -ForegroundColor Yellow
$body = '{"versionId":"' + $VersionId + '"}'
$start = Invoke-RestMethod "http://127.0.0.1:$port/api/versions/download" -Method Post -Headers $hdr -ContentType 'application/json' -Body $body -TimeoutSec 60
Write-Host ("     响应: " + ($start | ConvertTo-Json -Compress))

# ---- 轮询任务
$sw = [Diagnostics.Stopwatch]::StartNew()
$task = $null
$deadline = (Get-Date).AddSeconds($PollTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $list = Invoke-RestMethod "http://127.0.0.1:$port/api/downloads" -Headers $hdr -TimeoutSec 30
    $task = @($list.tasks) | Where-Object { $_.id -eq $start.id } | Select-Object -First 1
    if ($task -and ($task.status -eq 'completed' -or $task.status -eq 'failed')) { break }
    if ($sw.Elapsed.TotalSeconds % 30 -lt 3) { Write-Host ("     … $([int]$sw.Elapsed.TotalSeconds)s status=$($task.status) progress=$($task.progress) msg=$($task.message)") -ForegroundColor DarkGray }
}
$sw.Stop()
Write-Host ("     终态: status=$($task.status) progress=$($task.progress) 用时=$([int]$sw.Elapsed.TotalSeconds)s msg=$($task.message)")

# ---- 核对落盘
$gameDir = Join-Path $cold 'data\.minecraft'
$checks = New-Object System.Collections.Generic.List[object]
function Add-C($name, $ok, $detail) {
    $checks.Add([pscustomobject]@{ name = $name; ok = [bool]$ok; detail = $detail }) | Out-Null
    $c = if ($ok) { 'Green' } else { 'Red' }; $mark = if ($ok) { '[OK]  ' } else { '[FAIL]' }
    Write-Host ("  {0} {1} :: {2}" -f $mark, $name, $detail) -ForegroundColor $c
}

$jar = Join-Path $gameDir ("versions\$VersionId\$VersionId.jar")
Add-C '任务 completed' ($task.status -eq 'completed') "status=$($task.status)"
Add-C 'client.jar 存在' (Test-Path -LiteralPath $jar) $jar
if (Test-Path -LiteralPath $jar) {
    $size = (Get-Item -LiteralPath $jar).Length
    $sha = Sha1 $jar
    Add-C 'client.jar 大小与官方一致' ($size -eq $expClientSize) "got=$size want=$expClientSize"
    Add-C 'client.jar SHA1 与官方一致' ($sha -eq $expClientSha1) "got=$($sha.Substring(0,12))… want=$($expClientSha1.Substring(0,12))…"
}
$verJson = Join-Path $gameDir ("versions\$VersionId\$VersionId.json")
Add-C '版本 JSON 落盘' (Test-Path -LiteralPath $verJson) $verJson
$libDir = Join-Path $gameDir 'libraries'
$libJars = @(Get-ChildItem -LiteralPath $libDir -Recurse -File -Filter *.jar -ErrorAction SilentlyContinue)
Add-C 'libraries 有 jar 落盘' ($libJars.Count -gt 0) "jars=$($libJars.Count)（官方声明 $expLibs 个库，含 native/规则过滤，数量不必相等）"
$idx = Join-Path $gameDir ("assets\indexes\$expAssetsId.json")
Add-C 'assets 索引落盘' (Test-Path -LiteralPath $idx) $idx
$objDir = Join-Path $gameDir 'assets\objects'
$objs = @(Get-ChildItem -LiteralPath $objDir -Recurse -File -ErrorAction SilentlyContinue)
$objBytes = ($objs | Measure-Object Length -Sum).Sum
# 重要（第 40 轮自我更正）：官方 assetIndex.totalSize 会把**重复条目**重复计入
# （pre-1.6 索引 749 条 → 去重后只有 468 个对象；totalSize 47.21MB 而唯一对象合计 44.01MB）。
# 所以"与 totalSize 比体积"是**错误的门槛**；正确做法是按索引里的 hash 逐对象核对存在性与字节数。
$uniq = @{}
if (Test-Path -LiteralPath $idx) {
    $idxDoc = [IO.File]::ReadAllText($idx, [Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($prop in $idxDoc.objects.PSObject.Properties) {
        $h = [string]$prop.Value.hash
        if ($h -and -not $uniq.ContainsKey($h)) { $uniq[$h] = [int64]$prop.Value.size }
    }
}
$uniqBytes = 0; foreach ($v in $uniq.Values) { $uniqBytes += [int64]$v }
$missing = 0; $short = 0
foreach ($h in $uniq.Keys) {
    $fp = Join-Path (Join-Path $objDir $h.Substring(0, 2)) $h
    if (-not (Test-Path -LiteralPath $fp)) { $missing++ }
    elseif ((Get-Item -LiteralPath $fp).Length -ne [int64]$uniq[$h]) { $short++ }
}
Write-Host ("     assets: 索引条目 $($uniq.Count + 0) 个唯一对象 / 唯一字节 $([math]::Round($uniqBytes/1MB,2)) MB；磁盘 $($objs.Count) 个文件 / $([math]::Round($objBytes/1MB,2)) MB") -ForegroundColor DarkGray
Write-Host ("     （官方 totalSize=$([math]::Round($expAssetsTotal/1MB,2)) MB 含重复条目，不作为门槛）") -ForegroundColor DarkGray
if ($uniq.Count -gt 0) {
    Add-C 'assets 索引里的每个对象都在磁盘上' ($missing -eq 0) "missing=$missing / $($uniq.Count)"
    Add-C 'assets 每个对象字节数精确一致（无截断）' ($short -eq 0) "truncated=$short"
    Add-C 'assets 唯一对象总体积与索引一致（±1%）' ([math]::Abs($objBytes - $uniqBytes) / [double]$uniqBytes -lt 0.01) ("got=$([math]::Round($objBytes/1MB,2))MB want=$([math]::Round($uniqBytes/1MB,2))MB")
}
$totalBytes = (Get-ChildItem -LiteralPath $gameDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
Write-Host ("     游戏目录总计: $([math]::Round($totalBytes/1MB,1)) MB") -ForegroundColor DarkGray

# ---- 日志里的失败/告警
$log = Get-ChildItem (Join-Path $cold 'data\logs') -File -Filter 'launcher-*.log' -ErrorAction SilentlyContinue | Select-Object -First 1
$logTail = @()
# 注意：必须显式转成 [string]，否则 Get-Content 返回的 PS 扩展类型元数据会被 ConvertTo-Json 展开（第 40 轮踩过：JSON 膨胀到 7.9MB）
if ($log) { $logTail = @(Get-Content $log.FullName -Tail 60 | ForEach-Object { [string]$_ }) }

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    versionId = $VersionId
    expected = [ordered]@{ clientSize = $expClientSize; clientSha1 = $expClientSha1; libraries = $expLibs; assetIndex = $expAssetsId; assetsTotalSize = $expAssetsTotal }
    task = $task
    elapsedSec = [int]$sw.Elapsed.TotalSeconds
    gameDirBytes = $totalBytes
    libraryJarCount = $libJars.Count
    assetObjectCount = $objs.Count
    assetObjectBytes = $objBytes
    checks = $checks
    failures = @($checks | Where-Object { -not $_.ok })
    logTail = $logTail
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 1
Write-Host ''
$fails = @($checks | Where-Object { -not $_.ok }).Count
if ($fails -eq 0) { Write-Host "原版下载真实验收判定: PASS（$($checks.Count) 项 0 失败，用时 $([int]$sw.Elapsed.TotalSeconds)s）" -ForegroundColor Green; exit 0 }
Write-Host "原版下载真实验收判定: FAIL（$fails 项失败）" -ForegroundColor Red
exit 1
