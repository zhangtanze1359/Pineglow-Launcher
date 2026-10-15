#Requires -Version 5.1
<#
.SYNOPSIS
    MR00 步骤 3：固定 ExecutionRoot，生成 execution-root.json 与全量 input-manifest.json。

.DESCRIPTION
    只读 SourceRoot；只写 EvidenceRoot。
    ExecutionRoot 由 scripts/verify-source-snapshot.ps1 的「组装 + 完整性校验」阶段产出
    （该阶段不经过临时 .cmd 执行器，其结果经本轮实测可信：214 文件、泄漏 0、bin/obj 0、
      白名单逐项 SHA 与源一致）。步骤执行阶段因两个已证实缺陷作废，改由 Invoke-Step.ps1 复跑。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ExecutionRoot,
    [Parameter(Mandatory = $true)][string] $EvidenceRoot,
    [Parameter(Mandatory = $true)][string] $RunId,
    [string] $SourceRoot = 'D:\松光启动器',
    [string] $DeliveryRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器',
    [string] $BuilderRunId = '20261002-072004Z'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not (Test-Path -LiteralPath $ExecutionRoot)) { throw "执行根不存在: $ExecutionRoot" }

function Get-Sha256 {
    param([string] $Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $fs = $null
    try {
        $fs = [System.IO.File]::OpenRead($Path)
        $b = $sha.ComputeHash($fs)
        $sb = New-Object System.Text.StringBuilder
        foreach ($x in $b) { [void]$sb.Append($x.ToString('x2')) }
        return $sb.ToString()
    } finally { if ($fs) { $fs.Dispose() }; $sha.Dispose() }
}

# ---- 执行根实际清点 ----
$all = @(Get-ChildItem $ExecutionRoot -Recurse -File | Where-Object { $_.Name -ne '.snapshot-owner' } | Sort-Object FullName)
$binObj = @(Get-ChildItem $ExecutionRoot -Recurse -Directory | Where-Object { $_.Name -in @('bin','obj') })
$marker = Join-Path $ExecutionRoot '.snapshot-owner'
$markerValue = if (Test-Path $marker) { (Get-Content $marker -Raw).Trim() } else { '' }

# ---- 与源仓库比对：tracked 来源 ----
Push-Location $SourceRoot
try {
    $srcHead = (& git rev-parse HEAD).Trim()
    $srcBranch = (& git branch --show-current).Trim()
    $srcTracked = @(& git ls-files)
} finally { Pop-Location }
$trackedSet = @{}
foreach ($t in $srcTracked) { $trackedSet[($t -replace '\\','/')] = $true }

# ---- 白名单 ----
$inputsTxt = Join-Path $SourceRoot 'scripts\source-snapshot-inputs.txt'
$cats = [ordered]@{}; $cur = '_'; $cats[$cur] = New-Object System.Collections.Generic.List[string]
foreach ($raw in [System.IO.File]::ReadAllLines($inputsTxt)) {
    $l = $raw.Trim()
    if ($l -eq '') { continue }
    if ($l -match '^#\s*-+\s*类别\s*([A-Z])') { $cur = $Matches[1]; if (-not $cats.Contains($cur)) { $cats[$cur] = New-Object System.Collections.Generic.List[string] }; continue }
    if ($l.StartsWith('#')) { continue }
    $cats[$cur].Add(($l -replace '\\','/').Trim())
}
$wl = @{}
foreach ($k in $cats.Keys) { foreach ($p in $cats[$k]) { $wl[$p] = $k } }

# ---- 逐文件 SHA，并分类 ----
$entries = New-Object System.Collections.Generic.List[object]
$mismatch = New-Object System.Collections.Generic.List[string]
$missingInSource = New-Object System.Collections.Generic.List[string]
$leak = New-Object System.Collections.Generic.List[string]

foreach ($f in $all) {
    $rel = $f.FullName.Substring($ExecutionRoot.Length).TrimStart('\','/') -replace '\\','/'
    $sha = Get-Sha256 $f.FullName
    $origin = if ($trackedSet.ContainsKey($rel)) { 'tracked-workspace' } elseif ($wl.ContainsKey($rel)) { "whitelist-$($wl[$rel])" } else { 'UNEXPECTED' }
    if ($origin -eq 'UNEXPECTED') { $leak.Add($rel) }
    $srcSha = $null
    if (Test-Path -LiteralPath (Join-Path $SourceRoot $rel)) {
        $srcSha = Get-Sha256 (Join-Path $SourceRoot $rel)
        if ($srcSha -ne $sha) { $mismatch.Add($rel) }
    } else {
        $missingInSource.Add($rel)
    }
    $entries.Add([ordered]@{
        path = $rel
        origin = $origin
        bytes = $f.Length
        sha256 = $sha
        sha256Source = $srcSha
        matchesSource = $(if ($null -eq $srcSha) { $null } else { ($srcSha -eq $sha) })
    })
}

$manifestPath = Join-Path $EvidenceRoot "MR00\$RunId\baseline\input-manifest.json"
($entries | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $manifestPath -Encoding UTF8

# 纯文本 SHA 清单（sha256  相对路径）
$txtManifest = Join-Path $EvidenceRoot "MR00\$RunId\baseline\input-manifest.sha256.txt"
($entries | ForEach-Object { "$($_.sha256)  $($_.path)" }) | Set-Content -LiteralPath $txtManifest -Encoding UTF8

# ---- execution-root.json（固定入口 + run-id 副本，内容相同）----
$root = [ordered]@{
    generatedUtc        = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    runId               = $RunId
    scope               = 'MR00+MR01 第一批'
    sourceRoot          = $SourceRoot
    sourceRootHead      = $srcHead
    sourceRootBranch    = $srcBranch
    deliveryRoot        = $DeliveryRoot
    executionRoot       = $ExecutionRoot
    executionRootKind   = 'verify-source-snapshot.ps1 生成的 TEMP 隔离快照'
    snapshotMarkerToken = $markerValue
    fileCount           = $all.Count
    binObjDirCount      = $binObj.Count
    unexpectedFiles     = @($leak)
    shaMismatchWithSource = @($mismatch)
    missingInSource     = @($missingInSource)
    inputManifestPath   = $manifestPath
    inputManifestSha256Txt = $txtManifest
    whitelistTotal      = $wl.Count
    builtByRunId        = $BuilderRunId
    builderNote         = '组装与完整性校验阶段结果有效；步骤执行阶段因 2 个已证实缺陷作废，见 mr00-verification-defects.md'
    environment = [ordered]@{
        dotnetSdk   = '10.0.301'
        globalJson  = 'version 10.0.301 / rollForward latestPatch / allowPrerelease false'
        node        = 'v24.19.0'
        npm         = '11.17.0'
        powerShell  = $PSVersionTable.PSVersion.ToString()
        platform    = 'Windows'
    }
    knownBoundaries = @(
        'RID 发布为已知未锁定边界：packages.lock.json 只覆盖 net10.0 且不含 ILLink 任务包，--locked-mode 必然 NU1004',
        '本批次不启动真实 GUI、不启动游戏、不调用真实付费模型，均记为未验证'
    )
}
$rootJson = $root | ConvertTo-Json -Depth 6
$rootFixed = Join-Path $EvidenceRoot 'MR00\execution-root.json'
$rootCopy  = Join-Path $EvidenceRoot "MR00\$RunId\baseline\execution-root.json"
Set-Content -LiteralPath $rootFixed -Value $rootJson -Encoding UTF8
Set-Content -LiteralPath $rootCopy  -Value $rootJson -Encoding UTF8

Write-Host ''
Write-Host "ExecutionRoot : $ExecutionRoot"
Write-Host "文件数        : $($all.Count)"
Write-Host "bin/obj 目录  : $($binObj.Count)"
Write-Host "泄漏文件      : $($leak.Count)"
Write-Host "SHA 与源不一致: $($mismatch.Count)"
Write-Host "源中不存在    : $($missingInSource.Count)"
Write-Host "白名单总数    : $($wl.Count)"
Write-Host ''
Write-Host "固定入口  : $rootFixed"
Write-Host "run 副本  : $rootCopy"
Write-Host "全量清单  : $manifestPath  ($($entries.Count) 条)"
Write-Host "SHA 清单  : $txtManifest"
if ($leak.Count -gt 0)    { Write-Host "泄漏: $($leak -join ', ')" -ForegroundColor Red }
if ($mismatch.Count -gt 0) { Write-Host "不一致: $($mismatch -join ', ')" -ForegroundColor Red }
if ($binObj.Count -gt 0)   { Write-Host "存在 bin/obj: $($binObj.FullName -join ', ')" -ForegroundColor Red }
