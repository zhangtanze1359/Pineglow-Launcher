#Requires -Version 5.1
<#
.SYNOPSIS
    **资产级回退**的真实验收（R03 设计链上最后一条没被证明的链接）。

.DESCRIPTION
    已有的真实网络验收（scripts\verify-update-live-download.ps1）证明了：
      · 清单**源**级降级（Gitee 固定 update.json 404 → GitHub 备用清单）——真实端点；
      · 真实 HTTPS 下载 + SHA256 校验 + 篡改拒绝——真实发布资产。
    但它把夹具清单里的 `fallbackUrl` 写成空串，所以**"清单里 url 坏了、靠 fallbackUrl 救回来"**
    这条路径从未被任何测试跑过。而候选发布的清单恰恰是
    `url = Gitee 附件`、`fallbackUrl = GitHub 直链`——**主源挂掉时用户能不能下载，全靠它**。

    本脚本用真实公网地址做三个对照（每个都换一份全新冷启动配置，互不影响）：

      A 主源可用   ：url = 真实 Gitee 附件（匿名可下）      → 必须完成且哈希一致
      B 资产级回退 ：url = 真实 404 坏链，fallbackUrl = 真实 GitHub 资产 → 必须完成且哈希一致
      C 反证       ：url = 坏链，fallbackUrl = 同一个坏链    → 必须失败
                     （C 的存在是为了证明 B 的成功**不是**"客户端根本没读 url"）

    前置断言：坏链确实返回 404、两个真实资产都可取且 SHA256 与期望一致。
    退出码：0 = 通过；1 = 失败；2 = 用法/环境错误。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $WorkingAssetUrl,
    [Parameter(Mandatory)][string] $PrimaryAssetUrl,
    [Parameter(Mandatory)][string] $BrokenAssetUrl,
    [Parameter(Mandatory)][string] $ExpectedSha256,
    [int64] $ExpectedSize = 0,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 90,
    [int] $DownloadTimeoutSec = 300,
    [switch] $KeepWorkDir
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Checks = New-Object System.Collections.Generic.List[object]
$script:processes = New-Object System.Collections.Generic.List[object]
$noBomUtf8 = New-Object System.Text.UTF8Encoding($false)

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

function Get-HttpStatus {
    param([string] $Url, [int] $TimeoutSec = 60)
    try {
        $r = Invoke-WebRequest -Uri $Url -Method Head -UseBasicParsing -TimeoutSec $TimeoutSec
        return [int]$r.StatusCode
    } catch {
        $resp = $_.Exception.Response
        if ($resp -and $resp.StatusCode) { return [int]$resp.StatusCode }
        return -1
    }
}

# ---------------------------------------------------------------- 现场

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { Write-Host "找不到发布产物: $ExePath" -ForegroundColor Red; exit 2 }
foreach ($u in @($WorkingAssetUrl, $PrimaryAssetUrl, $BrokenAssetUrl)) {
    if ($u -notmatch '^https://') { Write-Host "资产地址必须是真实 HTTPS: $u" -ForegroundColor Red; exit 2 }
}
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
if (-not (Test-Path -LiteralPath $WorkRoot)) { [void](New-Item -ItemType Directory -Path $WorkRoot -Force) }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path

$runDir = Join-Path $WorkRoot ('松光 资产回退 真实验收 ' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $runDir -Force)
$fixtureDir = Join-Path $runDir 'fixture'
[void](New-Item -ItemType Directory -Path $fixtureDir -Force)
$extractShared = Join-Path $runDir 'bundle-extract'
[void](New-Item -ItemType Directory -Path $extractShared -Force)

Write-Host ''
Write-Host '== 资产级回退真实验收（R03） ==' -ForegroundColor Cyan
Write-Host "  发布产物 : $exe  (sha256=$((Get-Sha256File $exe).Substring(0,16)))"
Write-Host "  主源资产 : $PrimaryAssetUrl"
Write-Host "  备用资产 : $WorkingAssetUrl"
Write-Host "  坏链样本 : $BrokenAssetUrl"
Write-Host "  运行目录 : $runDir"

$results = [ordered]@{
    exePath = $exe
    exeSha256 = (Get-Sha256File $exe)
    primaryAssetUrl = $PrimaryAssetUrl
    workingAssetUrl = $WorkingAssetUrl
    brokenAssetUrl = $BrokenAssetUrl
}

# ================================================================ 0. 前置断言
Write-Host ''
Write-Host '  -- 前置：坏链真的坏、两个真实资产真的可取 --' -ForegroundColor Yellow

$brokenStatus = Get-HttpStatus -Url $BrokenAssetUrl
Add-Check '前置：坏链返回 404（否则 B/C 无意义）' ($brokenStatus -eq 404) "status=$brokenStatus"

$primaryLocal = Join-Path $fixtureDir 'primary-asset.bin'
$workingLocal = Join-Path $fixtureDir 'working-asset.bin'
foreach ($pair in @(@{ u = $PrimaryAssetUrl; p = $primaryLocal; n = '主源(Gitee)' },
                     @{ u = $WorkingAssetUrl; p = $workingLocal; n = '备用(GitHub)' })) {
    try {
        Invoke-WebRequest -Uri $pair.u -OutFile $pair.p -UseBasicParsing -TimeoutSec 300
        $h = Get-Sha256File $pair.p
        $len = (Get-Item -LiteralPath $pair.p).Length
        Add-Check ("前置：$($pair.n) 真实资产可取且哈希与期望一致") ($h -eq $ExpectedSha256) ("bytes=$len sha256=" + $h.Substring(0, 16))
        if ($ExpectedSize -gt 0) { Add-Check ("前置：$($pair.n) 字节数与期望一致") ($len -eq $ExpectedSize) ("got=$len want=$ExpectedSize") }
    } catch {
        Add-Check ("前置：$($pair.n) 真实资产可取") $false $_.Exception.Message
    }
}

# ================================================================ 夹具服务器
$manifests = @{
    'manifest-primary.json' = [ordered]@{ version = '1.0.1'; url = $PrimaryAssetUrl; fallbackUrl = ''; size = $ExpectedSize; sha256 = $ExpectedSha256; notes = 'asset-fallback-acceptance'; mandatory = $false }
    'manifest-fallback.json' = [ordered]@{ version = '1.0.1'; url = $BrokenAssetUrl; fallbackUrl = $WorkingAssetUrl; size = $ExpectedSize; sha256 = $ExpectedSha256; notes = 'asset-fallback-acceptance'; mandatory = $false }
    'manifest-both-broken.json' = [ordered]@{ version = '1.0.1'; url = $BrokenAssetUrl; fallbackUrl = $BrokenAssetUrl; size = $ExpectedSize; sha256 = $ExpectedSha256; notes = 'asset-fallback-negative'; mandatory = $false }
}
foreach ($k in $manifests.Keys) {
    [System.IO.File]::WriteAllText((Join-Path $fixtureDir $k), ($manifests[$k] | ConvertTo-Json), $noBomUtf8)
}
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
$serverProc = Start-Process -FilePath 'node' -ArgumentList ('"' + $serverJs + '" "' + $fixtureDir + '" ' + $fixturePort) -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2
Add-Check '夹具服务器已启动' (-not $serverProc.HasExited) "port=$fixturePort"

# ================================================================ 启动器驱动
function Start-Launcher {
    param([string] $ColdDir, [string] $ConfigJson)
    [void](New-Item -ItemType Directory -Path $ColdDir -Force)
    Copy-Item -LiteralPath $exe -Destination (Join-Path $ColdDir 'Pineglow.Launcher.exe') -Force
    [void](New-Item -ItemType File -Path (Join-Path $ColdDir 'portable.flag') -Force)
    $dataDir = Join-Path $ColdDir 'data'
    [void](New-Item -ItemType Directory -Path $dataDir -Force)
    [System.IO.File]::WriteAllText((Join-Path $dataDir 'config.json'), $ConfigJson, $noBomUtf8)
    $port = Get-FreePort
    $oldPort = $env:PINEGLOW_API_PORT
    $oldBase = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
    $env:PINEGLOW_API_PORT = "$port"
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extractShared
    try {
        $proc = Start-Process -FilePath (Join-Path $ColdDir 'Pineglow.Launcher.exe') -WorkingDirectory $ColdDir -PassThru `
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
            if ($r.StatusCode -eq 200) {
                $html = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content
                if ($html -notmatch 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { throw '取不到会话令牌' }
                return [pscustomobject]@{ Proc = $proc; Dir = $ColdDir; Port = $port; Token = $Matches[1] }
            }
        } catch { }
    }
    throw "启动器未在 $StartupTimeoutSec 秒内就绪（dir=$ColdDir port=$port）"
}

function Invoke-Api {
    param([int] $Port, [string] $Token, [string] $Path, [string] $Method = 'GET', [int] $TimeoutSec = 90)
    $headers = @{ 'X-Pineglow-Token' = $Token }
    if ($Method -eq 'POST') {
        return Invoke-RestMethod -Uri "http://127.0.0.1:$Port$Path" -Method Post -Headers $headers -TimeoutSec $TimeoutSec -ContentType 'application/json' -Body '{}'
    }
    return Invoke-RestMethod -Uri "http://127.0.0.1:$Port$Path" -Method Get -Headers $headers -TimeoutSec $TimeoutSec
}

function Run-DownloadCase {
    param([string] $Name, [string] $ManifestFile, [string] $Tag)
    Write-Host ''
    Write-Host "  -- $Name --" -ForegroundColor Yellow
    $cold = Join-Path $runDir "$Tag 冷启动"
    $cfg = "{`"updateUrl`":`"http://127.0.0.1:$fixturePort/$ManifestFile`"}"
    $app = Start-Launcher -ColdDir $cold -ConfigJson $cfg
    $check = Invoke-Api -Port $app.Port -Token $app.Token -Path '/api/update/check' -TimeoutSec 90
    Write-Host ('     check: ' + ($check | ConvertTo-Json -Depth 6 -Compress))
    $null = Invoke-Api -Port $app.Port -Token $app.Token -Path '/api/update/download' -Method 'POST' -TimeoutSec 90
    $deadline = (Get-Date).AddSeconds($DownloadTimeoutSec)
    $status = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 800
        $status = Invoke-Api -Port $app.Port -Token $app.Token -Path '/api/update' -TimeoutSec 30
        if ($status.state -eq 'completed' -or $status.state -eq 'failed') { break }
    }
    Write-Host ('     status: ' + ($status | ConvertTo-Json -Depth 6 -Compress))
    return [pscustomobject]@{ check = $check; status = $status; app = $app }
}

# ================================================================ A/B/C
$caseA = Run-DownloadCase -Name 'A 主源可用（真实 Gitee 附件，匿名）' -ManifestFile 'manifest-primary.json' -Tag 'A 主源'
Add-Check 'A：主源(Gitee)清单可读' ($caseA.check.updateAvailable -eq $true) "latest=$($caseA.check.latestVersion)"
Add-Check 'A：下载完成' ($caseA.status.state -eq 'completed') "state=$($caseA.status.state) error=$($caseA.status.error)"
if ($caseA.status.state -eq 'completed') {
    $p = [string]$caseA.status.path
    $lenA = (Get-Item -LiteralPath $p).Length
    $shaA = Get-Sha256File $p
    Add-Check 'A：字节数与真实资产一致' ($lenA -eq $ExpectedSize) "got=$lenA want=$ExpectedSize"
    Add-Check 'A：SHA256 与真实资产一致' ($shaA -eq $ExpectedSha256) ("got=" + $shaA.Substring(0, 16))
    Add-Check 'A：无 .part 残留' (-not (Test-Path -LiteralPath ($p + '.part')))
}

$caseB = Run-DownloadCase -Name 'B 资产级回退（url=坏链 → fallbackUrl=真实 GitHub 资产）' -ManifestFile 'manifest-fallback.json' -Tag 'B 回退'
Add-Check 'B：清单可读' ($caseB.check.updateAvailable -eq $true) "latest=$($caseB.check.latestVersion)"
Add-Check 'B：下载完成（⇒ 走了 fallbackUrl）' ($caseB.status.state -eq 'completed') "state=$($caseB.status.state) error=$($caseB.status.error)"
if ($caseB.status.state -eq 'completed') {
    $p = [string]$caseB.status.path
    $lenB = (Get-Item -LiteralPath $p).Length
    $shaB = Get-Sha256File $p
    Add-Check 'B：字节数与真实资产一致' ($lenB -eq $ExpectedSize) "got=$lenB want=$ExpectedSize"
    Add-Check 'B：SHA256 与真实资产一致' ($shaB -eq $ExpectedSha256) ("got=" + $shaB.Substring(0, 16))
    Add-Check 'B：无 .part 残留' (-not (Test-Path -LiteralPath ($p + '.part')))
}

$caseC = Run-DownloadCase -Name 'C 反证（url 与 fallbackUrl 都是坏链）' -ManifestFile 'manifest-both-broken.json' -Tag 'C 反证'
Add-Check 'C：必须失败（否则 B 的成功不成立）' ($caseC.status.state -eq 'failed') "state=$($caseC.status.state) error=$($caseC.status.error)"
Add-Check 'C：失败原因可读' (-not [string]::IsNullOrWhiteSpace([string]$caseC.status.error)) "error=$($caseC.status.error)"
Add-Check 'C：不得留下可用的下载产物' ([string]::IsNullOrWhiteSpace([string]$caseC.status.path)) "path=$($caseC.status.path)"

$results['caseA'] = @{ check = $caseA.check; status = $caseA.status }
$results['caseB'] = @{ check = $caseB.check; status = $caseB.status }
$results['caseC'] = @{ check = $caseC.check; status = $caseC.status }
$results['checks'] = $script:Checks
$results['failures'] = $script:Failures
$results['runDir'] = $runDir
$results['verdict'] = if ($script:Failures.Count -eq 0) { 'PASS' } else { 'FAIL' }

# ================================================================ 收尾
foreach ($p in $script:processes) {
    try { if (-not $p.Proc.HasExited) { Stop-Process -Id $p.Proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
}
try { if ($serverProc -and -not $serverProc.HasExited) { Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue } } catch { }

if ($JsonPath) {
    ($results | ConvertTo-Json -Depth 8) | Set-Content -Path $JsonPath -Encoding utf8
}
if (-not $KeepWorkDir) {
    Remove-Item -LiteralPath $runDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
if ($script:Failures.Count -eq 0) {
    Write-Host '资产级回退真实验收判定: PASS' -ForegroundColor Green
    exit 0
} else {
    Write-Host ("资产级回退真实验收判定: FAIL（{0} 项失败）" -f $script:Failures.Count) -ForegroundColor Red
    exit 1
}
