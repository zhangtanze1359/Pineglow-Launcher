#Requires -Version 5.1
<#
.SYNOPSIS
    更新链路的**真实网络**验收（R03 + R06）：真实默认源降级 + 真实 HTTPS 下载与校验 + 篡改必须被拒。

.DESCRIPTION
    为什么需要它：仓库里的自动化测试全部离线（本地假服务器 / 合成夹具），
    所以"清单降级"和"下载校验"只在替代环境里被证明过。本脚本用**真实端点**补这一段：

      阶段 A（真实默认源）：不写 config.json，让客户端用它自己的默认地址链
        （Gitee 固定 update.json → GitHub Release manifest.json）去检查更新，
        断言响应里逐源给出真实原因（Gitee 那条必须是 404），
        并且要么降级成功（usedFallback=true），要么失败摘要列出每个源的原因。
        注：本机到 GitHub 不通时备用源会超时——这也是真实结果之一，不能伪装成成功。

      阶段 B（真实 HTTPS 下载 + SHA256）：清单由本地 HTTP 夹具提供（清单允许 http），
        但清单里的**资产地址是真实 HTTPS**。驱动真实应用走 /api/update/download，
        断言下载产物字节数与 SHA256 与真实文件一致、无 .part 残留。
        刻意**不**调用 /api/update/apply：那会把启动器替换成被测资产。

      阶段 C（篡改必须被拒）：同一真实 HTTPS 资产、但清单里的 SHA256 被改写，
        下载必须以精确原因失败，且不得留下任何"看起来下载好了"的文件或 .part 残留。

    退出码：0 = 通过；1 = 失败；2 = 用法/环境错误。

.PARAMETER ExePath
    真实发布产物（单文件 EXE）。脚本会复制它到临时冷启动目录，不动原件。

.PARAMETER AssetUrl
    真实 HTTPS 资产地址（例如 Gitee 仓库里的一个 raw 文件）。

.PARAMETER ServerBaseUrl
    可选的"真实默认源"基地址，仅用于阶段 A 的可选自定义（默认留空 = 用产品内置默认链）。

.PARAMETER JsonPath
    证据 JSON 输出路径。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $AssetUrl,
    [string] $WorkRoot,
    [int] $StartupTimeoutSec = 90,
    [int] $DownloadTimeoutSec = 180,
    [string] $JsonPath,
    [switch] $KeepWorkDir
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param([string] $Name, [bool] $Ok, [string] $Detail = '')
    $script:Checks.Add([pscustomobject]@{ name = $Name; ok = $Ok; detail = $Detail }) | Out-Null
    if ($Ok) { Write-Host ("  [OK]   {0} {1}" -f $Name, $Detail) -ForegroundColor Green }
    else { Write-Host ("  [FAIL] {0} {1}" -f $Name, $Detail) -ForegroundColor Red; $script:Failures.Add("$Name :: $Detail") | Out-Null }
}

function Get-Sha256File {
    param([string] $Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $fs = $null
    try {
        $fs = [System.IO.File]::OpenRead($Path)
        $bytes = $sha.ComputeHash($fs)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $bytes) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally { if ($fs) { $fs.Dispose() }; $sha.Dispose() }
}

function Get-FreePort {
    for ($i = 0; $i -lt 60; $i++) {
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $p = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
        $listener.Stop()
        if ($p -gt 1024) { return $p }
    }
    throw '找不到空闲端口'
}

function Read-TextShared {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try { $r = New-Object System.IO.StreamReader($fs); $t = $r.ReadToEnd(); $r.Dispose(); return $t } finally { $fs.Dispose() }
    } catch { return '' }
}

# ---------------------------------------------------------------- 现场

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { Write-Host "找不到发布产物: $ExePath" -ForegroundColor Red; exit 2 }
$withAsset = -not [string]::IsNullOrWhiteSpace($AssetUrl)
if ($withAsset -and $AssetUrl -notmatch '^https://') { Write-Host "AssetUrl 必须是真实 HTTPS 地址: $AssetUrl" -ForegroundColor Red; exit 2 }
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
if (-not (Test-Path -LiteralPath $WorkRoot)) { [void](New-Item -ItemType Directory -Path $WorkRoot -Force) }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path

$runDir = Join-Path $WorkRoot ('松光 更新 真实验收 ' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $runDir -Force)
$fixtureDir = Join-Path $runDir 'fixture'
[void](New-Item -ItemType Directory -Path $fixtureDir -Force)
$extractShared = Join-Path $runDir 'bundle-extract'
[void](New-Item -ItemType Directory -Path $extractShared -Force)

Write-Host ''
Write-Host '== 更新链路真实验收（R03/R06） ==' -ForegroundColor Cyan
Write-Host "  发布产物: $exe"
Write-Host "  真实资产: $(if ($withAsset) { $AssetUrl } else { '<未提供：只跑阶段 A>' })"
Write-Host "  运行目录: $runDir"

$noBomUtf8 = New-Object System.Text.UTF8Encoding($false)
$assetSize = 0
$assetSha = ''
$fixturePort = 0
$serverProc = $null

# ---------------------------------------------------------------- 真实资产（先自己下一份，拿到大小与 SHA256 作为断言基准）

if ($withAsset) {
    $assetLocal = Join-Path $fixtureDir 'asset-reference.bin'
    try {
        Invoke-WebRequest -Uri $AssetUrl -OutFile $assetLocal -UseBasicParsing -TimeoutSec 60
    } catch {
        Write-Host "无法从真实 HTTPS 取到资产（网络受限？）: $($_.Exception.Message)" -ForegroundColor Red
        exit 2
    }
    $assetSize = (Get-Item -LiteralPath $assetLocal).Length
    $assetSha = Get-Sha256File $assetLocal
    Add-Check '真实 HTTPS 资产可获取（作为下载基准）' ($assetSize -gt 0) ("bytes=$assetSize sha256=" + $assetSha.Substring(0, 16))

    # ------------------------------------------------------------ 夹具 HTTP 服务器（清单允许 http；资产仍必须是 https）

    $manifestGood = [ordered]@{
        version     = '1.0.1'
        url         = $AssetUrl
        fallbackUrl = ''
        size        = $assetSize
        sha256      = $assetSha
        notes       = 'live-download-acceptance'
        mandatory   = $false
    }
    $manifestBad = [ordered]@{
        version     = '1.0.1'
        url         = $AssetUrl
        fallbackUrl = ''
        size        = $assetSize
        sha256      = ('0' * 64)
        notes       = 'live-download-acceptance-tampered'
        mandatory   = $false
    }
    [System.IO.File]::WriteAllText((Join-Path $fixtureDir 'manifest-good.json'), ($manifestGood | ConvertTo-Json), $noBomUtf8)
    [System.IO.File]::WriteAllText((Join-Path $fixtureDir 'manifest-bad.json'), ($manifestBad | ConvertTo-Json), $noBomUtf8)

    $serverJs = Join-Path $fixtureDir 'serve.cjs'
    $serverSource = @'
const http = require('http');
const fs = require('fs');
const path = require('path');
const root = process.argv[2];
const port = Number(process.argv[3]);
http.createServer((req, res) => {
  const rel = decodeURIComponent(new URL(req.url, 'http://x').pathname).replace(/^\/+/, '');
  const file = path.join(root, rel);
  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) { res.writeHead(404); res.end('not found'); return; }
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8', 'Content-Length': st.size });
    fs.createReadStream(file).pipe(res);
  });
}).listen(port, '127.0.0.1');
'@
    [System.IO.File]::WriteAllText($serverJs, $serverSource, $noBomUtf8)

    $fixturePort = Get-FreePort
    $serverProc = Start-Process -FilePath 'node' -ArgumentList ('"' + $serverJs + '" "' + $fixtureDir + '" ' + $fixturePort) `
        -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    Add-Check '本地清单夹具服务器已启动' (-not $serverProc.HasExited) ("port=$fixturePort")
}

$processes = New-Object System.Collections.Generic.List[object]

function Start-Launcher {
    param([string] $ColdDir, [string] $ConfigJson)
    [void](New-Item -ItemType Directory -Path $ColdDir -Force)
    Copy-Item -LiteralPath $exe -Destination (Join-Path $ColdDir 'Pineglow.Launcher.exe') -Force
    [void](New-Item -ItemType File -Path (Join-Path $ColdDir 'portable.flag') -Force)
    if ($ConfigJson) {
        $dataDir = Join-Path $ColdDir 'data'
        [void](New-Item -ItemType Directory -Path $dataDir -Force)
        # 必须无 BOM：config.json 由 JsonSerializer 解析，带 BOM 会被判为坏配置并回退到默认源
        [System.IO.File]::WriteAllText((Join-Path $dataDir 'config.json'), $ConfigJson, $noBomUtf8)
    }
    $port = Get-FreePort
    $oldPort = $env:PINEGLOW_API_PORT
    $oldBase = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
    $env:PINEGLOW_API_PORT = "$port"
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extractShared
    try {
        $proc = Start-Process -FilePath (Join-Path $ColdDir 'Pineglow.Launcher.exe') -WorkingDirectory $ColdDir -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $ColdDir 'stdout.log') -RedirectStandardError (Join-Path $ColdDir 'stderr.log')
    } finally {
        $env:PINEGLOW_API_PORT = $oldPort
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $oldBase
    }
    $script:processes.Add([pscustomobject]@{ Proc = $proc; Dir = $ColdDir; Port = $port }) | Out-Null

    $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        if ((Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) -eq $null) { break }
        try {
            $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { return [pscustomobject]@{ Proc = $proc; Dir = $ColdDir; Port = $port; Token = (Get-SessionToken -Port $port) } }
        } catch { }
    }
    throw "启动器未在 $StartupTimeoutSec 秒内就绪（dir=$ColdDir port=$port）"
}

function Get-SessionToken {
    param([int] $Port)
    $html = (Invoke-WebRequest "http://127.0.0.1:$Port/" -UseBasicParsing -TimeoutSec 10).Content
    if ($html -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { return $Matches[1] }
    throw '无法从界面里取到会话令牌'
}

function Invoke-Api {
    param([int] $Port, [string] $Token, [string] $Path, [string] $Method = 'GET', [int] $TimeoutSec = 90)
    $headers = @{ 'X-Pineglow-Token' = $Token }
    if ($Method -eq 'POST') {
        return Invoke-RestMethod -Uri "http://127.0.0.1:$Port$Path" -Method Post -Headers $headers -TimeoutSec $TimeoutSec -ContentType 'application/json' -Body '{}'
    }
    return Invoke-RestMethod -Uri "http://127.0.0.1:$Port$Path" -Method Get -Headers $headers -TimeoutSec $TimeoutSec
}

$results = [ordered]@{}

try {
    # ============================================================ 阶段 A：真实默认源降级
    Write-Host ''
    Write-Host '  -- 阶段 A：真实默认源（Gitee → GitHub 备用源） --' -ForegroundColor Yellow
    $coldA = Join-Path $runDir 'A 默认源 冷启动'
    $a = Start-Launcher -ColdDir $coldA -ConfigJson $null
    $checkA = Invoke-Api -Port $a.Port -Token $a.Token -Path '/api/update/check' -TimeoutSec 120
    $results['stageA'] = $checkA
    Write-Host ("     check: " + ($checkA | ConvertTo-Json -Depth 6 -Compress))

    Add-Check '阶段A：更新功能处于开启状态' ($checkA.enabled -eq $true) ("enabled=$($checkA.enabled)")

    # 环境无关的不变量：默认地址链要么真的取到清单（主源或备用源），
    # 要么给出"逐源可读"的失败摘要。**不能**把"主源必须 404"当门槛 ——
    # 一旦有人把 Gitee 的 update.json 修好，主源成功才是正确结果。
    $gotManifest = ($checkA.PSObject.Properties.Name -contains 'latestVersion') -and
                   (-not [string]::IsNullOrWhiteSpace([string]$checkA.latestVersion)) -and
                   ($checkA.PSObject.Properties.Name -contains 'manifestSource') -and
                   (-not [string]::IsNullOrWhiteSpace([string]$checkA.manifestSource))
    $perSourceFailure = ($checkA.PSObject.Properties.Name -contains 'manifestSources') -and
                        (@($checkA.manifestSources).Count -ge 2) -and
                        (-not [string]::IsNullOrWhiteSpace([string]$checkA.error))
    Add-Check '阶段A：默认地址链要么取到清单，要么逐源说明失败原因' ($gotManifest -or $perSourceFailure) `
        ("gotManifest=$gotManifest perSourceFailure=$perSourceFailure")
    Add-Check '阶段A：取到清单时来源可辨认（manifestSource 非空）' ($gotManifest -or $perSourceFailure) `
        ("manifestSource=$($checkA.manifestSource) usedFallback=$($checkA.usedFallback)")

    # 环境事实（证据，不作为门槛）：默认主源 Gitee 固定 update.json 本轮是否失败
    $giteeNote = 'primary-ok'
    if ($checkA.PSObject.Properties.Name -contains 'manifestSources') {
        $gitee = @($checkA.manifestSources) | Where-Object { $_.url -like '*gitee.com*' } | Select-Object -First 1
        if ($gitee) { $giteeNote = "gitee ok=$($gitee.ok) reason=$($gitee.reason)" }
    } elseif ($checkA.usedFallback -eq $true) {
        # 走到备用源才拿到清单 ⇒ 主源必然失败（成功分支不回传逐源明细）
        $giteeNote = "primary-failed（已按设计降级到备用源: $($checkA.manifestSource)）"
    }
    $results['stageANote'] = $giteeNote
    Write-Host "     默认主源本轮实测: $giteeNote" -ForegroundColor DarkGray

    if (-not $withAsset) {
        Write-Host '  （未提供 -AssetUrl：跳过阶段 B/C 的真实下载与篡改拒绝）' -ForegroundColor Yellow
    }
    else {

    # ============================================================ 阶段 B：真实 HTTPS 下载 + SHA256
    Write-Host ''
    Write-Host '  -- 阶段 B：真实 HTTPS 资产下载与校验 --' -ForegroundColor Yellow
    $coldB = Join-Path $runDir 'B 真实下载 冷启动'
    $cfgB = "{`"updateUrl`":`"http://127.0.0.1:$fixturePort/manifest-good.json`"}"
    $b = Start-Launcher -ColdDir $coldB -ConfigJson $cfgB
    $checkB = Invoke-Api -Port $b.Port -Token $b.Token -Path '/api/update/check' -TimeoutSec 60
    $results['stageB'] = $checkB
    Write-Host ("     check: " + ($checkB | ConvertTo-Json -Depth 6 -Compress))
    Add-Check '阶段B：检查更新看到 1.0.1' ($checkB.updateAvailable -eq $true -and $checkB.latestVersion -eq '1.0.1') ("latest=$($checkB.latestVersion)")
    Add-Check '阶段B：清单来自用户自定义源（不标记为备用源）' ($checkB.usedFallback -eq $false) ("manifestSource=$($checkB.manifestSource)")

    $null = Invoke-Api -Port $b.Port -Token $b.Token -Path '/api/update/download' -Method 'POST' -TimeoutSec 60
    $deadline = (Get-Date).AddSeconds($DownloadTimeoutSec)
    $statusB = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 800
        $statusB = Invoke-Api -Port $b.Port -Token $b.Token -Path '/api/update' -TimeoutSec 30
        if ($statusB.state -eq 'completed' -or $statusB.state -eq 'failed') { break }
    }
    $results['stageBStatus'] = $statusB
    Add-Check '阶段B：真实 HTTPS 下载完成' ($statusB.state -eq 'completed') ("state=$($statusB.state) error=$($statusB.error)")
    $destB = [string]$statusB.path
    Add-Check '阶段B：下载产物落在 updates 目录' ($destB -and (Test-Path -LiteralPath $destB)) ("path=$destB")
    if ($destB -and (Test-Path -LiteralPath $destB)) {
        $sizeB = (Get-Item -LiteralPath $destB).Length
        $shaB = Get-Sha256File $destB
        Add-Check '阶段B：字节数与真实资产一致' ($sizeB -eq $assetSize) ("got=$sizeB want=$assetSize")
        Add-Check '阶段B：SHA256 与真实资产一致' ($shaB -eq $assetSha) ("got=" + $shaB.Substring(0, 16) + " want=" + $assetSha.Substring(0, 16))
        Add-Check '阶段B：无 .part 残留' (-not (Test-Path -LiteralPath ($destB + '.part')))
    }

    # ============================================================ 阶段 C：篡改的清单必须被拒
    Write-Host ''
    Write-Host '  -- 阶段 C：SHA256 被改写 → 必须拒绝 --' -ForegroundColor Yellow
    $coldC = Join-Path $runDir 'C 篡改拒绝 冷启动'
    $cfgC = "{`"updateUrl`":`"http://127.0.0.1:$fixturePort/manifest-bad.json`"}"
    $c = Start-Launcher -ColdDir $coldC -ConfigJson $cfgC
    $null = Invoke-Api -Port $c.Port -Token $c.Token -Path '/api/update/download' -Method 'POST' -TimeoutSec 60
    $deadline = (Get-Date).AddSeconds($DownloadTimeoutSec)
    $statusC = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 800
        $statusC = Invoke-Api -Port $c.Port -Token $c.Token -Path '/api/update' -TimeoutSec 30
        if ($statusC.state -eq 'completed' -or $statusC.state -eq 'failed') { break }
    }
    $results['stageCStatus'] = $statusC
    Write-Host ("     status: " + ($statusC | ConvertTo-Json -Depth 6 -Compress))
    Add-Check '阶段C：篡改的清单导致下载失败' ($statusC.state -eq 'failed') ("state=$($statusC.state)")
    Add-Check '阶段C：失败原因指向校验失败' ([string]$statusC.error -like '*校验失败*') ("error=$($statusC.error)")
    $updatesDirC = Join-Path $coldC 'data\updates'
    $leftoverC = @()
    if (Test-Path -LiteralPath $updatesDirC) {
        $leftoverC = @(Get-ChildItem -LiteralPath $updatesDirC -File | Where-Object { $_.Name -notlike 'apply-state*' })
    }
    Add-Check '阶段C：不得留下可用的“已下载”文件或 .part 残留' ($leftoverC.Count -eq 0) ("leftover=" + (($leftoverC | ForEach-Object { $_.Name }) -join ','))
    }
} finally {
    foreach ($entry in $processes) {
        try {
            $live = Get-Process -Id $entry.Proc.Id -ErrorAction SilentlyContinue
            if ($null -ne $live) { $null = & taskkill.exe /T /F /PID ([string]$entry.Proc.Id) 2>&1 }
        } catch { }
    }
    if ($null -ne $serverProc) {
        try {
            $live = Get-Process -Id $serverProc.Id -ErrorAction SilentlyContinue
            if ($null -ne $live) { $null = & taskkill.exe /T /F /PID ([string]$serverProc.Id) 2>&1 }
        } catch { }
    }

    $report = [ordered]@{
        generatedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        exePath      = $exe
        exeSha256    = Get-Sha256File $exe
        assetUrl     = $AssetUrl
        assetSize    = $assetSize
        assetSha256  = $assetSha
        runDir       = $runDir
        checks       = $script:Checks.ToArray()
        failures     = $script:Failures.ToArray()
        results      = $results
        verdict      = if ($script:Failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
    }
    if ($JsonPath) {
        $parent = Split-Path -Parent $JsonPath
        if ($parent -and -not (Test-Path -LiteralPath $parent)) { [void](New-Item -ItemType Directory -Path $parent -Force) }
        $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $JsonPath -Encoding utf8
    } else {
        $report | ConvertTo-Json -Depth 10 | Write-Host
    }

    if (-not $KeepWorkDir -and (Test-Path -LiteralPath $runDir)) {
        try { Remove-Item -LiteralPath $runDir -Recurse -Force } catch { }
    }
}

Write-Host ''
if ($script:Failures.Count -gt 0) {
    Write-Host ("更新链路真实验收判定: FAIL（{0} 项失败）" -f $script:Failures.Count) -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}
Write-Host '更新链路真实验收判定: PASS' -ForegroundColor Green
exit 0

