#Requires -Version 5.1
<#
.SYNOPSIS
    在系统临时目录构建「候选源码快照」并做可复现性验证。

.DESCRIPTION
    本轮修复（2026-10-13，CV-01）：快照文件数量改为按「tracked ∪ 白名单」的**唯一规范相对路径
    集合**计数——旧实现把 trackedCopied + whitelistCopied 直接相加，两者重叠时必然误判 FAIL。
    缺失、泄漏、哈希、锁定、测试与退出码门槛全部保留，并新增「预期集合逐项存在于快照内」的
    显式断言，因此去重不会让缺文件假通过。

    解决的问题：当前工作区可构建，但「正式源码输入」不明确。

    前提修正（重要，不要再误述）：clean HEAD 自身可能是自洽的。
    真正的风险是——把 32 个已修改文件纳入交付，却漏掉它们相依赖的未跟踪文件。
    本脚本用「同一工作区、有无白名单」两次隔离构建来量化这个风险。

    脚本行为：
      1. 在系统临时位置创建随机命名的隔离目录（脚本自身生成 GUID）。
      2. 复制「全部 tracked 文件的工作区版本」——即当前磁盘内容，含 32 个 M 的修改后内容。
      3. 额外复制白名单 scripts/source-snapshot-inputs.txt 逐项列出的未跟踪文件（当前类别 A–L）。
         -SkipCategories 可排除某些类别以制造 RED。
      4. 校验：文件数量（按「tracked ∪ 白名单」的唯一规范相对路径集合计，重叠只算一次）、
         白名单逐项存在且 SHA256 与源一致、预期集合逐项存在于快照内、
         「tracked 与白名单之外的文件数为 0」的通用泄漏检测、构建前无 bin/obj。
      5. 执行：Node 正式测试 -> 非 RID 锁定还原 -> Release build -> C# Release 全套
         -> VerifyHost build -> win-x64 单文件 publish。
      6. 逐步记录命令、cwd、UTC 起止、退出码、关键输出、产物路径与 SHA256。

    锁定边界（务必准确表述，不要夸大成「全链锁定」）：
      * 非 RID 的 restore/build 路径（launcher 与 tests）使用 --locked-mode，
        锁定还原失败即整体失败，不使用普通还原兜底。
      * 带 RID 的 publish 无法使用锁定还原：已实测 NU1004——现有 packages.lock.json
        只覆盖 net10.0 且不含 RID 专属包（RID 发布会额外引入 Microsoft.NET.ILLink.Tasks）。
        该步骤记为 KNOWN-UNLOCKED（未锁定发布边界），不是通过，也不阻断判定。
      * 普通 publish（不加锁定属性）是本任务实际采用的发布构建验证项。

    产物路径（务必区分，勿再混淆）：
      * <rid>/Pineglow.Launcher.exe          = build 输出目录的 apphost，**不是**单文件发布产物
      * <rid>/publish/Pineglow.Launcher.exe = 真正的 PublishSingleFile 单文件自包含 exe
      脚本校验的是后者，并把前者仅作对照记录。

    RED 模式：记录预期失败但不中断证据收集，且整体判定必须为非零。

    安全边界：
      - 不修改原仓库任何文件；不执行 git reset/restore/checkout/clean/stash/add/commit。
      - 只复制 tracked 工作区版本 + 白名单逐项列出的未跟踪文件；不复制其余未跟踪文件、用户配置或密钥。
      - 不依赖原仓库 bin/obj 缓存（快照从零开始并显式断言）。
      - -CleanupSnapshot 为可选开关，默认保留快照供独立复验；
        递归删除前必须校验解析后的绝对路径与本次运行标记，且证据目录不得位于待删目录内。

.PARAMETER RepoRoot
    源仓库根目录。默认取脚本所在目录的上一级。

.PARAMETER SkipCategories
    逗号分隔的类别字母（如 A）。被排除类别的白名单文件不复制，用于 RED 复现。

.PARAMETER CleanupSnapshot
    结束后删除隔离快照目录（默认保留）。删除前执行路径安全校验。

.PARAMETER EvidenceDir
    证据输出目录。原始日志与 JSON 汇总写入此处。默认位于系统临时目录且在快照目录之外。

.EXAMPLE
    ./scripts/verify-source-snapshot.ps1
    GREEN：tracked ∪ 全部白名单项（重叠按唯一路径只算一次），锁定还原为硬失败。

.EXAMPLE
    ./scripts/verify-source-snapshot.ps1 -SkipCategories A
    RED-2：测试源/夹具/lock/Node 测试齐备，但缺生产源码与 UI vendor。
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $SkipCategories = '',
    [switch] $CleanupSnapshot,
    [string] $EvidenceDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# ---------------------------------------------------------------- 步骤运行器（R05 修复件）
# R05 根因：旧实现把含中文 EvidenceDir 的整条命令写进 `Set-Content -Encoding ascii` 的 .cmd，
# 中文被写成 '?'，cmd 报语法错误、日志根本没产生，却仍有步骤被记成 exit 0 / PASS。
# 现在统一走 scripts/lib/StepRunner.psm1：命令文本经 CreateProcessW 以 Unicode 传入，
# 退出码/超时/日志缺失/零输出都是可断言的事实，且判定与证据分离。
$stepRunnerModule = Join-Path $PSScriptRoot 'lib/StepRunner.psm1'
if (-not (Test-Path -LiteralPath $stepRunnerModule)) { throw "找不到步骤运行器模块: $stepRunnerModule" }
Import-Module $stepRunnerModule -Force -DisableNameChecking

# ---------------------------------------------------------------- 基础

$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Notes    = New-Object System.Collections.Generic.List[string]
$script:Steps    = New-Object System.Collections.Generic.List[object]

function Write-Section {
    param([string] $Text)
    Write-Host ''
    Write-Host ('=' * 78) -ForegroundColor DarkCyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host ('=' * 78) -ForegroundColor DarkCyan
}
function Write-Ok   { param([string]$m) Write-Host "  [OK]   $m" -ForegroundColor Green }
function Write-Bad  { param([string]$m) Write-Host "  [FAIL] $m" -ForegroundColor Red }
function Write-Note { param([string]$m) Write-Host "  [note] $m" -ForegroundColor Yellow }

# SHA256 用 .NET 实现，不依赖 Get-FileHash cmdlet（保证 5.1 / 7.x 与受限模块路径下一致）
function Get-Sha256 {
    param([string] $Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $fs = $null
    try {
        $fs = [System.IO.File]::OpenRead($Path)
        $bytes = $sha.ComputeHash($fs)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $bytes) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally {
        if ($fs) { $fs.Dispose() }
        $sha.Dispose()
    }
}

$onWindows = $true
if (Get-Variable -Name 'IsWindows' -ErrorAction SilentlyContinue) { $onWindows = [bool]$IsWindows }
elseif ($PSVersionTable.PSEdition -eq 'Core') { $onWindows = $false }

# ---------------------------------------------------------------- 路径

if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$inputsTxt = Join-Path $RepoRoot 'scripts/source-snapshot-inputs.txt'
if (-not (Test-Path -LiteralPath $inputsTxt)) { throw "找不到白名单文件: $inputsTxt" }

$runToken = [guid]::NewGuid().ToString('N')
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())

function New-IsolatedSnapshotDir {
    for ($i = 0; $i -lt 8; $i++) {
        $p = Join-Path $tempRoot ('pineglow-snapshot-{0}' -f ([guid]::NewGuid().ToString('N')))
        if (-not (Test-Path -LiteralPath $p)) {
            [void](New-Item -ItemType Directory -Path $p)
            return (Resolve-Path -LiteralPath $p).Path
        }
    }
    throw '无法在临时目录创建隔离快照目录（重试 8 次均冲突）。'
}

$SnapshotDir = New-IsolatedSnapshotDir
$markerPath = Join-Path $SnapshotDir '.snapshot-owner'
Set-Content -LiteralPath $markerPath -Value $runToken -Encoding ascii -NoNewline

if (-not $EvidenceDir) { $EvidenceDir = Join-Path $tempRoot "pineglow-evidence-$runToken" }
[void](New-Item -ItemType Directory -Path $EvidenceDir -Force)
$EvidenceDir = (Resolve-Path -LiteralPath $EvidenceDir).Path

$skipSet = @($SkipCategories -split ',' | ForEach-Object { $_.Trim().ToUpper() } | Where-Object { $_ -ne '' })
$isRedMode = ($skipSet.Count -gt 0)

$Summary = [ordered]@{
    generatedUtc        = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    generatedLocal      = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    powerShellVersion   = $PSVersionTable.PSVersion.ToString()
    platform            = $(if ($onWindows) { 'Windows' } else { 'NonWindows' })
    mode                = $(if ($isRedMode) { 'RED' } else { 'GREEN' })
    repoRoot            = $RepoRoot
    repoHead            = ''
    snapshotDir         = $SnapshotDir
    evidenceDir         = $EvidenceDir
    skipCategories      = $SkipCategories
    trackedCopied       = 0
    whitelistTotal      = 0
    whitelistCopied     = 0
    uniqueExpectedFiles = 0
    overlapWithTracked  = 0
    whitelistSkipped    = @()
    whitelistMissing    = @()
    hashMismatches      = @()
    expectedMissing     = @()
    unexpectedFiles     = @()
    closureRootsChecked = @()
    closureMissing      = @()
    snapshotFileCount   = 0
    binObjFound         = 0
    steps               = @()
    buildOutputApphost  = $null
    publishedExe        = $null
    knownUnlockedBoundaries = @()
    notes               = @()
    finalVerdict        = 'UNKNOWN'
    snapshotCleaned     = $false
}

Write-Section '候选源码快照 · 可复现性验证'
Write-Note "PowerShell  : $($Summary.powerShellVersion) / $($Summary.platform)"
Write-Note "模式        : $($Summary.mode)"
Write-Note "RepoRoot    : $RepoRoot"
Write-Note "SnapshotDir : $SnapshotDir"
Write-Note "EvidenceDir : $EvidenceDir"
Write-Note "Started UTC : $($Summary.generatedUtc)"
Write-Note "SkipCats    : $(if ($SkipCategories) { $SkipCategories } else { '<none>' })"

# ---------------------------------------------------------------- 白名单

function Read-InputManifest {
    param([string] $Path)
    $cats = [ordered]@{}
    $current = '_'
    $cats[$current] = New-Object System.Collections.Generic.List[string]
    foreach ($raw in [System.IO.File]::ReadAllLines($Path)) {
        $line = $raw.Trim()
        if ($line -eq '') { continue }
        if ($line -match '^#\s*-+\s*类别\s*([A-Z])') {
            $current = $Matches[1]
            if (-not $cats.Contains($current)) {
                $cats[$current] = New-Object System.Collections.Generic.List[string]
            }
            continue
        }
        if ($line.StartsWith('#')) { continue }
        $norm = ($line -replace '\\', '/').Trim()
        if ($norm -match '(^|/)\.\.(/|$)') { throw "白名单不允许 '..' 路径: $line" }
        if ($norm -match '\*')            { throw "白名单不允许通配符路径: $line" }
        $cats[$current].Add($norm)
    }
    return $cats
}

$cats = Read-InputManifest -Path $inputsTxt
$whitelistAll = @()
foreach ($k in $cats.Keys) { $whitelistAll += $cats[$k] }
$Summary.whitelistTotal = $whitelistAll.Count

Write-Host ''
Write-Host '  白名单分类：' -ForegroundColor Cyan
foreach ($k in $cats.Keys) {
    $st = if ($skipSet -contains $k) { 'SKIP(RED)' } else { 'include' }
    $col = if ($skipSet -contains $k) { 'Yellow' } else { 'Gray' }
    Write-Host ("    {0,-3} {1,3} 项  {2}" -f $k, $cats[$k].Count, $st) -ForegroundColor $col
}

$forbidden = @('^docs/', '^dev/PineglowLauncher\.Tests/TestResults/', '^\.testagent/', '^\.trae/',
               '\.bak', '\.tmp\d*$', '\.orig$', 'probe', '^scripts/_', '^scripts/run-dify')
foreach ($p in $whitelistAll) {
    foreach ($fp in $forbidden) { if ($p -match $fp) { throw "白名单包含被排除路径 '$p'（匹配 $fp）。" } }
}
Write-Ok "白名单自检通过（共 $($whitelistAll.Count) 项，无通配符、无 docs/TestResults/.testagent/.trae/临时脚本）"

# ---------------------------------------------------------------- 组装

function Copy-RelativeFile {
    param([string] $Rel, [string] $Dest)
    $dst = Join-Path $Dest $Rel
    $dd = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $dd)) { [void](New-Item -ItemType Directory -Path $dd -Force) }
    Copy-Item -LiteralPath (Join-Path $RepoRoot $Rel) -Destination $dst -Force
}

Write-Section '步骤 1/7：组装隔离快照'

$tracked = @(& git -C $RepoRoot ls-files)
if ($LASTEXITCODE -ne 0) { throw 'git ls-files 失败' }
$Summary.repoHead = (& git -C $RepoRoot rev-parse HEAD).Trim()
$tracked = @($tracked | Where-Object { $_ -ne '' })

# CV-01（本轮修复）：tracked 与白名单是**重叠**的两个来源，不是互斥的两批文件。
# 旧实现把 trackedCopied + whitelistCopied 直接相加（本轮 302 + 163 = 465），
# 而磁盘上真实存在的唯一文件只有 302 个，GREEN 因此在步骤 2 被误判为 FAIL。
# 现在统一按「实际纳入的唯一规范相对路径集合」计数：$expectedSet 既作数量门槛，
# 也作逐项缺失断言（缺失、泄漏、哈希、锁定门槛一条都没有取消，白名单也没有删）。
$expectedSet = @{}
$trackedCopiedCount = 0
foreach ($f in $tracked) {
    $rel = $f -replace '\\', '/'
    if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $rel))) {
        # 结构化失败，而不是让 Copy-Item 抛异常把整条验证打断（那样连汇总都写不出来）
        Write-Bad "tracked 文件在工作区不存在: $rel"
        $script:Failures.Add("tracked 文件在工作区不存在，无法复制工作区版本: $rel")
        continue
    }
    $expectedSet[$rel] = $true
    Copy-RelativeFile -Rel $rel -Dest $SnapshotDir
    $trackedCopiedCount++
}
$Summary.trackedCopied = $trackedCopiedCount
Write-Ok "tracked 工作区版本已复制: $trackedCopiedCount 个文件"

$copied = 0
$overlap = 0
foreach ($k in $cats.Keys) {
    if ($skipSet -contains $k) {
        foreach ($p in $cats[$k]) { $Summary.whitelistSkipped = @($Summary.whitelistSkipped) + $p }
        continue
    }
    foreach ($p in $cats[$k]) {
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $p))) {
            $Summary.whitelistMissing = @($Summary.whitelistMissing) + $p
            Write-Bad "白名单项缺失: $p"
            continue
        }
        # 已在 tracked 集合里的白名单项只记重叠数，不再把期望文件数抬高一次
        if ($expectedSet.ContainsKey($p)) { $overlap++ } else { $expectedSet[$p] = $true }
        Copy-RelativeFile -Rel $p -Dest $SnapshotDir
        $copied++
    }
}
$Summary.whitelistCopied = $copied
$Summary.uniqueExpectedFiles = $expectedSet.Count
$Summary.overlapWithTracked = $overlap
if (@($Summary.whitelistSkipped).Count -gt 0) {
    Write-Note "-SkipCategories 排除 $(@($Summary.whitelistSkipped).Count) 项（RED 模式）"
}
if (@($Summary.whitelistMissing).Count -gt 0) {
    $script:Failures.Add("白名单项在源仓库不存在: $(@($Summary.whitelistMissing) -join ', ')")
} else { Write-Ok "白名单文件已复制: $copied 个（缺失 0）" }

# ---------------------------------------------------------------- 校验

Write-Section '步骤 2/7：快照完整性校验'

$files = @(Get-ChildItem -LiteralPath $SnapshotDir -Recurse -File | Where-Object { $_.Name -ne '.snapshot-owner' })
$Summary.snapshotFileCount = $files.Count
# CV-01：期望数是「唯一规范相对路径集合」的大小（tracked ∪ 白名单，重叠只算一次），
# 不是 trackedCopied + whitelistCopied。旧口径在两者重叠时必然误报数量不符。
$expectedCount = $expectedSet.Count
Write-Host ("  文件数量: 期望 {0} / 实际 {1}" -f $expectedCount, $Summary.snapshotFileCount) -ForegroundColor Cyan
Write-Host ("            口径: tracked {0} + 白名单 {1}，重叠 {2} -> 唯一路径 {3}" -f $Summary.trackedCopied, $Summary.whitelistCopied, $Summary.overlapWithTracked, $expectedCount) -ForegroundColor DarkGray
if ($files.Count -ne $expectedCount) { $script:Failures.Add("快照文件数量不符: 期望 $expectedCount（唯一路径集合）实际 $($files.Count)") }
else { Write-Ok '文件数量一致（按唯一规范相对路径集合计，重叠只算一次）' }

# 去重计数不得掩盖缺失：预期集合里逐项断言真实存在，缺哪一项就报哪一项。
# （数量相等 + 泄漏为 0 已能推出「无缺失」，这里把它变成可独立复验的显式断言与可读证据。）
$expectedMissing = @()
foreach ($rel in @($expectedSet.Keys | Sort-Object)) {
    if (-not (Test-Path -LiteralPath (Join-Path $SnapshotDir $rel))) { $expectedMissing += $rel }
}
$Summary.expectedMissing = $expectedMissing
if ($expectedMissing.Count -gt 0) {
    Write-Bad "快照内缺失预期文件 $($expectedMissing.Count) 个:"
    foreach ($m in ($expectedMissing | Select-Object -First 20)) { Write-Host "       $m" -ForegroundColor Red }
    $script:Failures.Add("快照内缺失预期文件 $($expectedMissing.Count) 个: $($expectedMissing -join ', ')")
} else {
    Write-Ok ("预期集合逐项存在于快照内（{0} 项，缺失 0）" -f $expectedSet.Count)
}

# 通用泄漏检测：快照内每个文件必须是 tracked 或白名单之一（额外文件必须为 0）
$trackedSet = @{}
foreach ($t in $tracked) { $trackedSet[($t -replace '\\','/')] = $true }
$wlSet = @{}
foreach ($k in $cats.Keys) {
    if ($skipSet -contains $k) { continue }
    foreach ($p in $cats[$k]) { $wlSet[$p] = $true }
}
$unexpected = @()
foreach ($f in $files) {
    $rel = $f.FullName.Substring($SnapshotDir.Length).TrimStart('\','/') -replace '\\','/'
    if (-not $trackedSet.ContainsKey($rel) -and -not $wlSet.ContainsKey($rel)) { $unexpected += $rel }
}
$Summary.unexpectedFiles = $unexpected
if ($unexpected.Count -gt 0) {
    Write-Bad "快照内存在非白名单文件 $($unexpected.Count) 个（泄漏）:"
    foreach ($u in ($unexpected | Select-Object -First 20)) { Write-Host "       $u" -ForegroundColor Red }
    $script:Failures.Add("快照泄漏 $($unexpected.Count) 个非白名单文件")
} else { Write-Ok '通用泄漏检测通过：额外文件 0（无 tracked/白名单之外的文件）' }

# 候选项闭包断言（R07，2026-10-10 恢复现场暴露）：
# 原实现只验「数量自洽 + 无泄漏」。恢复后 tracked 从 151 降到 139、白名单不变，这两条**依然全过**
# —— 期望数与实际数一起变小，泄漏检测也无从发现「本该收进来却没收到」的文件。
# 结果：22 项输入被静默漏掉（其中 11 项是产品源码/测试/VerifyHost），直到后面硬编码抽查
# 去读 AiDiagnoseService.cs 才抛异常中止，连汇总都写不出来。
# 这里按目录枚举产品输入树，逐项要求出现在快照里，缺项结构化失败并列出路径。
# 口径：只覆盖产品输入树（dev/ui/scripts/.github）。docs 证据按既有决定不进候选快照，故不列入。
$closureRoots = @('dev', 'ui', 'scripts', '.github')
$closureExcludedSegments = @('bin', 'obj', 'TestResults', '.vs', 'node_modules')
$closureMissing = @()
$closureChecked = 0
foreach ($rootName in $closureRoots) {
    $rootPath = Join-Path $RepoRoot $rootName
    if (-not (Test-Path -LiteralPath $rootPath)) { continue }
    foreach ($f in @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Force -ErrorAction SilentlyContinue)) {
        $rel = ($f.FullName.Substring($RepoRoot.Length).TrimStart('\', '/') -replace '\\', '/')
        $excluded = $false
        foreach ($seg in ($rel -split '/')) { if ($closureExcludedSegments -contains $seg) { $excluded = $true; break } }
        if ($excluded) { continue }
        if ($f.Name -like '*.user' -or $f.Name -like '*.suo') { continue }
        $closureChecked++
        if (-not (Test-Path -LiteralPath (Join-Path $SnapshotDir $rel))) { $closureMissing += $rel }
    }
}
$Summary.closureRootsChecked = $closureRoots
$Summary.closureMissing = $closureMissing
if ($closureMissing.Count -gt 0) {
    Write-Bad ("候选项闭包不完整：{0} 个产品输入未进入快照（已检查 {1} 个）" -f $closureMissing.Count, $closureChecked)
    foreach ($m in ($closureMissing | Select-Object -First 30)) { Write-Host "       $m" -ForegroundColor Red }
    $script:Failures.Add("候选项闭包不完整，缺 $($closureMissing.Count) 项: $($closureMissing -join ', ')")
} else {
    Write-Ok ("候选项闭包断言通过：dev/ui/scripts/.github 下 {0} 个输入均在快照内" -f $closureChecked)
}

# 白名单逐项存在 + SHA256 与源一致
$hashLines = New-Object System.Collections.Generic.List[string]
$mismatch = 0
foreach ($k in $cats.Keys) {
    if ($skipSet -contains $k) { continue }
    foreach ($p in $cats[$k]) {
        $dst = Join-Path $SnapshotDir $p
        if (-not (Test-Path -LiteralPath $dst)) { $script:Failures.Add("快照内缺失白名单项: $p"); $mismatch++; continue }
        $hSrc = Get-Sha256 (Join-Path $RepoRoot $p)
        $hDst = Get-Sha256 $dst
        if ($hSrc -ne $hDst) {
            $Summary.hashMismatches = @($Summary.hashMismatches) + $p
            Write-Bad "SHA256 不一致: $p"; $mismatch++; continue
        }
        $hashLines.Add(('{0}  {1}' -f $hDst, $p))
    }
}
if ($mismatch -eq 0) { Write-Ok '白名单逐项存在性与 SHA256 全部一致' }
else { $script:Failures.Add("白名单校验失败项: $mismatch") }

# tracked 抽查：确认快照用的是工作区版本而非 HEAD 版本。
# R07：原实现硬编码 7 条路径，并假定它们「必然是 tracked、必然在快照内」——这正是
# 「以前 tracked ⇒ 现在必然收集到」的错误推导。恢复后 Git 基底变早，其中两项
# （AiDiagnoseService.cs、VerifyHost.csproj）已不在 tracked 集合里，Get-Sha256 直接抛
# FileNotFoundException，整条验证在步骤 2 中途崩掉。
# 现在抽查对象由「相对 HEAD 有差异的 tracked 文件」推导（这些正是 HEAD 版本 ≠ 工作区版本的文件），
# 且任何缺项都转成结构化失败而不是异常。
$spotList = @()
$diffNames = @()
$prevEap = $ErrorActionPreference
try {
    # 5.1 陷阱：原生命令写到 stderr 的 warning（例如 core.autocrlf 对 .github/workflows/*.yml
    # 提示「LF will be replaced by CRLF」）在 $ErrorActionPreference='Stop' 下会变成
    # NativeCommandError，把整条验证打断。这里只给这一条只读命令降噪，不改全局策略。
    $ErrorActionPreference = 'Continue'
    $diffNames = @(& git -C $RepoRoot diff --name-only HEAD -- 2>$null)
}
finally {
    $ErrorActionPreference = $prevEap
}
if ($LASTEXITCODE -eq 0) {
    $spotList = @($diffNames |
        Where-Object { $_ -ne '' } |
        ForEach-Object { $_ -replace '\\', '/' } |
        Where-Object { $trackedSet.ContainsKey($_) -and (Test-Path -LiteralPath (Join-Path $RepoRoot $_)) })
}
if ($spotList.Count -eq 0) {
    $spotList = @($tracked | Where-Object { Test-Path -LiteralPath (Join-Path $RepoRoot $_) })
}
$spotList = @($spotList | Select-Object -First 8)
$spotBefore = $script:Failures.Count
foreach ($p in $spotList) {
    $srcPath = Join-Path $RepoRoot $p
    $snapPath = Join-Path $SnapshotDir $p
    if (-not (Test-Path -LiteralPath $snapPath)) {
        Write-Bad "抽查项不在快照内（收集闭包缺口）: $p"
        $script:Failures.Add("tracked 抽查项不在快照内: $p")
        continue
    }
    if (-not (Test-Path -LiteralPath $srcPath)) {
        Write-Bad "抽查项在源仓库不存在（无法比对工作区版本）: $p"
        $script:Failures.Add("tracked 抽查项在源仓库不存在: $p")
        continue
    }
    if ((Get-Sha256 $srcPath) -ne (Get-Sha256 $snapPath)) {
        Write-Bad "tracked 抽查不一致（快照未使用工作区版本）: $p"
        $script:Failures.Add("tracked 抽查不一致: $p")
    }
}
if ($script:Failures.Count -eq $spotBefore) { Write-Ok ("tracked 抽查：{0} 项确为工作区版本（对象由 diff 推导）" -f $spotList.Count) }

$bins = @(Get-ChildItem -LiteralPath $SnapshotDir -Recurse -Directory -ErrorAction SilentlyContinue |
          Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' })
$Summary.binObjFound = $bins.Count
if ($bins.Count -gt 0) {
    Write-Bad "快照内存在 bin/obj: $($bins.Count) 处"
    $script:Failures.Add('快照内存在 bin/obj，冷构建前提被污染')
} else { Write-Ok '快照无 bin/obj 缓存（满足冷构建前提）' }

# 脚本编码守卫（R05 同类问题）：本机宿主是 Windows PowerShell 5.1，它按系统 ANSI 代码页
# 读取无 BOM 的 .ps1/.psm1。含非 ASCII（中文注释/提示）却没有 BOM 的脚本会被整体解析错乱，
# 从而让「验证器」在真空中失败或给出失真结论。这里把这种输入直接判为快照缺陷。
$bomRisk = @()
foreach ($f in $files) {
    if ($f.Extension -ne '.ps1' -and $f.Extension -ne '.psm1') { continue }
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { continue }
    $hasNonAscii = $false
    foreach ($b in $bytes) { if ($b -gt 0x7F) { $hasNonAscii = $true; break } }
    if ($hasNonAscii) { $bomRisk += $f.FullName.Substring($SnapshotDir.Length).TrimStart('\','/') }
}
if ($bomRisk.Count -gt 0) {
    Write-Bad "存在含非 ASCII 但无 UTF-8 BOM 的 PowerShell 脚本 $($bomRisk.Count) 个（PS 5.1 会解析错乱）:"
    foreach ($r in $bomRisk) { Write-Host "       $r" -ForegroundColor Red }
    $script:Failures.Add("含非 ASCII 但无 BOM 的 PowerShell 脚本: $($bomRisk -join ', ')")
} else { Write-Ok 'PowerShell 脚本编码守卫通过（无「含非 ASCII 却缺 BOM」的脚本）' }

$manifestPath = Join-Path $EvidenceDir 'snapshot-manifest.sha256.txt'
$hashLines | Sort-Object | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Ok "候选 manifest 已写出: $($hashLines.Count) 行 -> $manifestPath"

# 锁定文件形态守卫（MR19，本轮实测又踩到一次）：本机 `dotnet publish -r <rid>` 会**就地改写**
# dev/PineglowLauncher/packages.lock.json，把它变成含 `"net10.0/<rid>"` 段 + Microsoft.NET.ILLink.Tasks
# 的"RID 形态"。而候选源码里的 lock 必须是「非 RID 形态」（8663 字节那种），
# 否则 CI 上的 `--locked-mode` 还原必然 NU1004，整条流水线红。
# 这两个 lock 是白名单里的**生成物**（未被 git 跟踪），所以本地跑一次 RID 发布就会污染候选，
# 必须在组装阶段硬失败，而不是等 CI 才发现。
$lockProblems = @()
foreach ($lockRel in @('dev/PineglowLauncher/packages.lock.json', 'dev/PineglowLauncher.Tests/packages.lock.json')) {
    $lockPath = Join-Path $SnapshotDir $lockRel
    if (-not (Test-Path -LiteralPath $lockPath)) { $lockProblems += "$lockRel 缺失"; continue }
    $lockText = [System.IO.File]::ReadAllText($lockPath)
    if ($lockText -match '"net10\.0/') { $lockProblems += "$lockRel 含 RID 段（net10.0/<rid>）" }
    if ($lockText -match 'Microsoft\.NET\.ILLink\.Tasks') { $lockProblems += "$lockRel 引用了 Microsoft.NET.ILLink.Tasks（只由 RID 发布注入）" }
}
if ($lockProblems.Count -gt 0) {
    Write-Bad "锁定文件形态不合法 $($lockProblems.Count) 项（会让 CI 的 --locked-mode 还原 NU1004）:"
    foreach ($x in $lockProblems) { Write-Host "       $x" -ForegroundColor Red }
    Write-Note "  修法：在源仓库跑 `dotnet restore <项目> --force-evaluate` 重新生成非 RID 形态的 lock 文件"
    $script:Failures.Add("锁定文件形态不合法: $($lockProblems -join '; ')")
} else { Write-Ok '锁定文件形态守卫通过（两份 lock 均为非 RID 形态）' }

# ---------------------------------------------------------------- 步骤执行

function Invoke-VerificationStep {
    param(
        [string]   $Name,
        [string]   $CommandLine,
        [string]   $LogName,
        [string[]] $ParsePatterns = @(),
        [switch]   $AllowFailure,
        [string]   $ExpectedUnsupportedReason = '',
        [switch]   $RequireOutput,
        [string[]] $RequireMatch = @(),
        [string[]] $RequirePositiveCount = @(),
        [int]      $TimeoutSec = 1800
    )
    Write-Host ''
    Write-Host "  -> $Name" -ForegroundColor Yellow
    Write-Host "     cwd: $SnapshotDir" -ForegroundColor DarkGray
    Write-Host "     cmd: $CommandLine" -ForegroundColor DarkGray

    $logPath = Join-Path $EvidenceDir $LogName

    $result = Invoke-StepProcess -Name $Name -CommandLine $CommandLine -WorkingDirectory $SnapshotDir `
        -LogPath $logPath -TimeoutSec $TimeoutSec
    $gate = Test-StepGate -Result $result -RequireOutput:$RequireOutput `
        -RequireMatch $RequireMatch -RequirePositiveCount $RequirePositiveCount

    $output = [string]$result.outputText
    $hits = [ordered]@{}
    foreach ($pat in $ParsePatterns) {
        $vals = @()
        foreach ($m in [regex]::Matches($output, $pat, 'IgnoreCase,Multiline')) { $vals += $m.Value.Trim() }
        $hits[$pat] = ($vals -join ' | ')
    }

    $code = $result.exitCode
    $status = 'PASS'

    if ($result.timedOut) {
        # 超时永远不通过，且不受 AllowFailure 影响：它连「失败」都还没跑完
        $status = 'FAIL'
        $script:Failures.Add("$Name 超时（超过 ${TimeoutSec}s 未结束，已按进程树终止）")
    } elseif ($null -eq $code) {
        $status = 'FAIL'
        $script:Failures.Add("$Name 未取得退出码（进程未正常启动）: $($result.error)")
    } elseif ($code -ne 0) {
        if ($ExpectedUnsupportedReason) {
            # 已实测的平台边界：既不算通过，也不掩盖为普通失败，而是显式记为未锁定/未通过边界
            $status = 'KNOWN-UNLOCKED'
            $msg = "[已知边界·未通过] $Name : $ExpectedUnsupportedReason"
            $script:Notes.Add($msg)
            $Summary.knownUnlockedBoundaries = @($Summary.knownUnlockedBoundaries) + [ordered]@{
                step = $Name; command = $CommandLine; exitCode = $code; reason = $ExpectedUnsupportedReason
            }
            Write-Note $msg
        } elseif ($AllowFailure) {
            $status = 'FAIL-EXPECTED'
            $script:Notes.Add("[RED 预期失败] $Name 退出码 $code")
        } else {
            $status = 'FAIL'
            $script:Failures.Add("$Name 退出码 $code")
        }
    } elseif (-not $gate.ok) {
        # R05 核心修复点：退出码 0 但证据不可信（日志缺失/零输出/零测试/缺必需证据）
        # 绝不能再记 PASS。这一支是旧实现把「命令根本没跑」记成 PASS 的地方。
        $status = 'FAIL'
        foreach ($reason in $gate.reasons) {
            $script:Failures.Add("$Name 退出 0 但证据不可信: $reason")
        }
    }

    $col = switch ($status) {
        'PASS'             { 'Green' }
        'FAIL-EXPECTED'    { 'Yellow' }
        'KNOWN-UNLOCKED'   { 'Yellow' }
        default            { 'Red' }
    }
    Write-Host ("     exit={0}  status={1}  {2:N1}s  log={3}  logBytes={4}" -f $code, $status, $result.durationSec, $LogName, $result.logBytes) -ForegroundColor $col
    foreach ($k in @($hits.Keys)) { if ($hits[$k]) { Write-Host "     match [$k] => $($hits[$k])" -ForegroundColor Gray } }
    foreach ($reason in $gate.reasons) { Write-Host "     gate: $reason" -ForegroundColor DarkYellow }

    if ($code -ne 0 -or $status -eq 'FAIL') {
        Write-Host '     ---- 原始输出尾部 ----' -ForegroundColor Yellow
        foreach ($l in @($output -split "`r?`n" | Select-Object -Last 20)) { Write-Host "     $l" -ForegroundColor DarkYellow }
        Write-Host '     ------------------------' -ForegroundColor Yellow
    }

    $script:Steps.Add([pscustomobject]@{
        name        = $Name
        command     = $CommandLine
        cwd         = $SnapshotDir
        exitCode    = $code
        status      = $status
        timedOut    = $result.timedOut
        durationSec = $result.durationSec
        startUtc    = $result.startUtc
        endUtc      = $result.endUtc
        logFile     = $logPath
        logExists   = $result.logExists
        logBytes    = $result.logBytes
        gateOk      = $gate.ok
        gateReasons = $gate.reasons
        keyOutput   = $hits
    })
}

$isRed = $isRedMode

Write-Section '步骤 3/7：验证器自证与 UI 资源闭包'

# R05：先证明「验证器本身可信」，再让后续结论依赖它。
# 本机执行策略默认不允许未签名脚本，所以显式 Bypass（非 Windows 用 pwsh，该参数被忽略）。
$psHostExe = 'powershell'
$psHostArgs = '-NoProfile -ExecutionPolicy Bypass -File'
if (-not $onWindows) { $psHostExe = 'pwsh'; $psHostArgs = '-NoProfile -File' }
Invoke-VerificationStep -Name '步骤运行器自证（R05，17 项）' `
    -CommandLine "$psHostExe $psHostArgs scripts/tests/step-runner.test.ps1" `
    -LogName 'step0-step-runner-selftest.log' -AllowFailure:$isRed -TimeoutSec 900 `
    -RequireOutput -RequireMatch 'FAILED=0' -RequirePositiveCount 'TOTAL=(\d+)'

# R02：ui/index.html -> css/pineglow.css -> ../assets/pine-forest.svg 的资源闭包必须完整。
# 报告刻意写相对路径：命令文本不掺入可能含中文/空格的绝对证据路径，
# 落盘后再复制进证据目录（含 missingCount 断言）。
Invoke-VerificationStep -Name 'UI 本地资源闭包检查（R02）' `
    -CommandLine 'node scripts/check-ui-resource-closure.cjs --root ui --json ui-resource-closure.json' `
    -LogName 'step1a-ui-resource-closure.log' -AllowFailure:$isRed -TimeoutSec 600 `
    -RequireOutput -RequireMatch '闭包完整'

Invoke-VerificationStep -Name 'R02 门槛自证（node --test 闭包用例 7 项）' `
    -CommandLine 'node --test --test-reporter=spec scripts/tests/ui-resource-closure.test.cjs' `
    -LogName 'step1b-ui-closure-selftest.log' -AllowFailure:$isRed -TimeoutSec 900 `
    -RequireOutput -RequireMatch 'fail 0' -RequirePositiveCount 'pass\s+(\d+)'

$closureReport = Join-Path $SnapshotDir 'ui-resource-closure.json'
if (Test-Path -LiteralPath $closureReport) {
    try {
        $closure = [System.IO.File]::ReadAllText($closureReport) | ConvertFrom-Json
        if ([int]$closure.missingCount -ne 0) {
            Write-Bad "资源闭包报告 missingCount=$($closure.missingCount)（非 0 即漏资源）"
            $script:Failures.Add("资源闭包报告 missingCount=$($closure.missingCount)")
        } else {
            Write-Ok "资源闭包报告: 引用 $($closure.totalLocalReferences) 条 / 缺失 0"
        }
        Copy-Item -LiteralPath $closureReport -Destination (Join-Path $EvidenceDir 'ui-resource-closure.json') -Force
    } catch {
        Write-Bad "资源闭包报告无法解析: $($_.Exception.Message)"
        $script:Failures.Add('资源闭包报告无法解析')
    }
} else {
    Write-Bad '资源闭包检查未生成报告，无法证明资源完整'
    $script:Failures.Add('资源闭包检查未生成报告')
}

Write-Section '步骤 4/7：正式测试清单与 Node 前端测试（R04）'

# R04：先校验「清单 scripts/test-inventory.txt == 快照里实际存在的 *.test.cjs」双向一致，
# 再按清单运行。旧实现是硬编码 11 个文件（实际有 20+），少跑的测试在 CI 里毫无痕迹。
Invoke-VerificationStep -Name '测试清单校验（R04，双向一致）' `
    -CommandLine 'node scripts/run-inventory-tests.cjs --check --json test-inventory-report.json' `
    -LogName 'step1c-test-inventory.log' -AllowFailure:$isRed -TimeoutSec 600 `
    -RequireOutput -RequireMatch '清单校验通过'

Invoke-VerificationStep -Name 'Node 正式测试（按 scripts/test-inventory.txt 运行）' `
    -CommandLine 'node scripts/run-inventory-tests.cjs' `
    -LogName 'step1-node-tests.log' -AllowFailure:$isRed -TimeoutSec 1800 `
    -RequireOutput -RequireMatch 'fail\s+0' -RequirePositiveCount 'pass\s+(\d+)' `
    -ParsePatterns @('tests \d+','suites \d+','pass \d+','fail \d+','cancelled \d+','skipped \d+','duration_ms [\d.]+','清单校验通过')

$inventoryReport = Join-Path $SnapshotDir 'test-inventory-report.json'
if (Test-Path -LiteralPath $inventoryReport) {
    try {
        $inv = [System.IO.File]::ReadAllText($inventoryReport) | ConvertFrom-Json
        if ($inv.verdict -ne 'PASS' -or @($inv.problems).Count -gt 0) {
            Write-Bad "测试清单报告为 $($inv.verdict)"
            foreach ($p in @($inv.problems)) { Write-Bad "  - $p" }
            $script:Failures.Add("测试清单报告 $($inv.verdict)")
        } else {
            Write-Ok "测试清单报告 PASS：node 测试 $($inv.nodeCount) 个，磁盘发现 $($inv.discoveredNodeTests) 个"
        }
        Copy-Item -LiteralPath $inventoryReport -Destination (Join-Path $EvidenceDir 'test-inventory-report.json') -Force
    } catch {
        Write-Bad "测试清单报告无法解析: $($_.Exception.Message)"
        $script:Failures.Add('测试清单报告无法解析')
    }
} else {
    Write-Bad '测试清单校验未生成报告'
    $script:Failures.Add('测试清单校验未生成报告')
}

Write-Section '步骤 5/7：.NET 还原 / 构建 / 测试'

$launcherProj = 'dev/PineglowLauncher/Pineglow.Launcher.csproj'
$testsProj    = 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj'
$verifyProj   = 'dev/PineglowLauncher/VerifyHost/VerifyHost.csproj'

# 非 RID 路径使用锁定还原。GREEN 模式下任何非零都是硬失败，没有普通还原兜底。
Invoke-VerificationStep -Name 'restore launcher (--locked-mode)' `
    -CommandLine "dotnet restore $launcherProj --locked-mode" -LogName 'step2-restore-launcher-locked.log' `
    -AllowFailure:$isRed -RequireOutput -ParsePatterns @('NU1004','NU1604','error [A-Z]+\d+')
Invoke-VerificationStep -Name 'restore tests (--locked-mode)' `
    -CommandLine "dotnet restore $testsProj --locked-mode" -LogName 'step3-restore-tests-locked.log' `
    -AllowFailure:$isRed -RequireOutput -ParsePatterns @('NU1004','NU1604','error [A-Z]+\d+')

# 门槛证据刻意用**与语言环境无关**的标记：
# 本机 .NET CLI 输出中文（「已成功生成。」「已通过! - 失败: 0，通过: 870」），
# 用 'Build succeeded' / 'Passed!' 这类英文串做门槛会在中文环境里误判为「缺证据」。
# 见本轮实测：第一版门槛正是在这里把 exit=0 的正确构建判成了 FAIL。
Invoke-VerificationStep -Name 'build launcher (-c Release)' `
    -CommandLine "dotnet build $launcherProj -c Release --no-restore" -LogName 'step6-build-launcher-release.log' `
    -AllowFailure:$isRed -RequireOutput -RequireMatch 'Pineglow\.Launcher' `
    -ParsePatterns @('Build succeeded','已成功生成','\d+ Error\(s\)','\d+ 个错误','\d+ Warning\(s\)','\d+ 个警告','Missing Three\.js[^"]*','Missing skin3d[^"]*','error [A-Z]+\d+')

Invoke-VerificationStep -Name 'C# 测试全套 (-c Release)' `
    -CommandLine "dotnet test $testsProj -c Release --no-restore --logger `"console;verbosity=minimal`"" `
    -LogName 'step7-dotnet-test-release.log' -AllowFailure:$isRed `
    -RequireOutput -RequireMatch '(?:Failed|失败):\s*0' -RequirePositiveCount '(?:Passed|通过):\s*(\d+)' `
    -ParsePatterns @('Passed!.*','已通过!.*','Failed!.*','(?:Failed|失败): *\d+','(?:Passed|通过): *\d+','(?:Skipped|已跳过): *\d+','(?:Total|总计): *\d+','error [A-Z]+\d+')

Invoke-VerificationStep -Name 'build VerifyHost (-c Release)' `
    -CommandLine "dotnet build $verifyProj -c Release" -LogName 'step8-build-verifyhost.log' -AllowFailure:$isRed `
    -RequireOutput -RequireMatch 'VerifyHost' `
    -ParsePatterns @('Build succeeded','已成功生成','\d+ Error\(s\)','\d+ 个错误','\d+ Warning\(s\)','\d+ 个警告','error [A-Z]+\d+')

# VerifyHost --bisect-smoke：二分定位的端到端冒烟（含故障注入）。
# 独立验收曾指出"这个冒烟从未被真正运行过，且它只做 portable 沙盒、
# 不能证明 Windows 命名凭据没被动过"。现在它在脚本内部把 SecretStore 换成纯内存后端，
# 并自行断言"生产凭据后端调用次数 = 0"，因此在任何环境（含 CI runner）都能安全真跑。
# 注意：这里必须用反斜杠 —— 它是**可执行文件路径**而不是参数，cmd 不认正斜杠路径
# （实测失败输出：'dev' 不是内部或外部命令）。
$verifyHostExe = 'dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe'
Invoke-VerificationStep -Name 'VerifyHost --bisect-smoke（含凭据后端隔离断言）' `
    -CommandLine "$verifyHostExe --bisect-smoke --port 48411" `
    -LogName 'step8b-verifyhost-bisect-smoke.log' -AllowFailure:$isRed -TimeoutSec 600 `
    -RequireOutput -RequireMatch '\[BisectSmoke\] PASS','credential-isolation: production credential backend calls must be 0' `
    -ParsePatterns @('\[PASS\] \d+','\[FAIL\]','\[BisectSmoke\] (PASS|FAIL)','隔离凭据后端操作 \d+ 次')

Write-Section '步骤 6/7：win-x64 单文件 publish'

# 带 RID 的 publish 无法使用锁定还原（已实测 NU1004：lock 文件只覆盖 net10.0，
# 且 RID 发布会引入 Microsoft.NET.ILLink.Tasks）。此处显式记为未锁定发布边界。
$publishCore = "dotnet publish $launcherProj -c Release -r win-x64 --self-contained /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false"
Invoke-VerificationStep -Name 'publish win-x64 (锁定还原探测)' `
    -CommandLine "$publishCore -p:RestoreLockedMode=true" -LogName 'step9-publish-locked-probe.log' `
    -ExpectedUnsupportedReason 'packages.lock.json 未覆盖 win-x64 RID 且缺 ILLink 任务包；已实测 NU1004。RID 发布为已知未锁定边界。' `
    -ParsePatterns @('NU1004','NU1604','error [A-Z]+\d+')

# 本任务实际采用的发布构建验证：普通 publish，不加锁定属性。
Invoke-VerificationStep -Name 'publish win-x64 单文件 (正式发布构建验证)' `
    -CommandLine $publishCore -LogName 'step10-publish.log' -AllowFailure:$isRed `
    -RequireOutput -RequireMatch 'Pineglow\.Launcher' `
    -ParsePatterns @('NU1004','error [A-Z]+\d+')

# 产物路径必须区分两类文件，二者绝不可混淆：
#   1) build 输出目录 .../net10.0/win-x64/Pineglow.Launcher.exe
#      —— 普通 apphost（仅启动器壳，依赖同目录下的 dll 集合），不是单文件发布产物。
#   2) publish 输出目录 .../net10.0/win-x64/publish/Pineglow.Launcher.exe
#      —— 真正的 PublishSingleFile 单文件自包含 exe。
# 本任务验证的是第 2 个，第 1 个仅作对照记录。
$ridOutDir    = Join-Path $SnapshotDir 'dev/PineglowLauncher/bin/Release/net10.0/win-x64'
$apphostPath  = Join-Path $ridOutDir 'Pineglow.Launcher.exe'
$publishDir   = Join-Path $ridOutDir 'publish'
$singleExePath= Join-Path $publishDir 'Pineglow.Launcher.exe'

$apphostInfo = $null
if (Test-Path -LiteralPath $apphostPath) {
    $a = Get-Item -LiteralPath $apphostPath
    $apphostInfo = [ordered]@{ path = $apphostPath; sizeBytes = $a.Length; sha256 = (Get-Sha256 $apphostPath) }
    Write-Note ("对照·build 输出 apphost（非单文件发布产物）: {0} bytes  sha256={1}" -f $a.Length, $apphostInfo.sha256)
} else {
    Write-Note "对照·build 输出 apphost 不存在（正常：未单独执行 build publish 流程时可能不存在）"
}
$Summary.buildOutputApphost = $apphostInfo

if (Test-Path -LiteralPath $singleExePath -PathType Leaf) {
    $exe = Get-Item -LiteralPath $singleExePath
    $h = Get-Sha256 $singleExePath
    Write-Ok ("单文件发布产物: {0}  {1} bytes  sha256={2}" -f $singleExePath, $exe.Length, $h)
    $Summary.publishedExe = [ordered]@{
        path       = $singleExePath
        kind       = 'PublishSingleFile (self-contained, win-x64)'
        sizeBytes  = $exe.Length
        sha256     = $h
        publishDir = $publishDir
    }
    # 防止再次把 apphost 当成单文件产物：单文件产物必须显著大于 apphost
    if ($apphostInfo -and $exe.Length -le $apphostInfo.sizeBytes) {
        Write-Bad "单文件产物大小未超过 apphost，疑似取错文件"
        $script:Failures.Add('单文件产物大小未超过 apphost，疑似取错文件')
    }
} else {
    Write-Bad "单文件发布产物缺失: $singleExePath"
    Write-Note "  提示：publish 默认输出到 <rid>/publish/ 子目录；<rid>/ 下的 exe 只是 apphost"
    $script:Failures.Add("单文件发布产物缺失: $singleExePath")
}

Write-Section '步骤 7/7：发布产物独立冷启动（R01 复现那道 RED 的门槛）'

# R01 的 RED 就是「只把工作流实际上传的 EXE 放进全新目录启动 → 缺 Skia 原生库直接退出」。
# 这里用同一个动作做门槛：只复制 EXE，中文+空格新目录，真实窗口 + live API + 资源 + 同版校验。
# 路径刻意用相对路径：命令行不掺中文/空格绝对路径，报告落盘后再复制进证据目录。
if (Test-Path -LiteralPath $singleExePath -PathType Leaf) {
    Invoke-VerificationStep -Name '独立冷启动（单文件 EXE，中文+空格新目录）' `
        -CommandLine "$psHostExe $psHostArgs scripts/verify-single-file-standalone.ps1 -ExePath dev/PineglowLauncher/bin/Release/net10.0/win-x64/publish/Pineglow.Launcher.exe -ExpectedUiRoot ui -JsonPath standalone-cold-start.json" `
        -LogName 'step11-standalone-cold-start.log' -AllowFailure:$isRed -TimeoutSec 900 `
        -RequireOutput -RequireMatch '独立冷启动判定: PASS'

    $coldReport = Join-Path $SnapshotDir 'standalone-cold-start.json'
    if (Test-Path -LiteralPath $coldReport) {
        try {
            $cold = [System.IO.File]::ReadAllText($coldReport) | ConvertFrom-Json
            $failed = @($cold.checks | Where-Object { -not $_.ok })
            if ($cold.verdict -ne 'PASS' -or $failed.Count -gt 0) {
                Write-Bad "独立冷启动报告为 $($cold.verdict)，失败检查 $($failed.Count) 项"
                foreach ($f in $failed) { Write-Bad ("  - {0} :: {1}" -f $f.name, $f.detail) }
                $script:Failures.Add("独立冷启动报告 $($cold.verdict)（失败 $($failed.Count) 项）")
            } else {
                Write-Ok "独立冷启动报告 PASS：窗口='$($cold.mainWindowTitle)' 端口=$($cold.port) 检查 $($cold.checks.Count) 项全通过"
            }
            Copy-Item -LiteralPath $coldReport -Destination (Join-Path $EvidenceDir 'standalone-cold-start.json') -Force
        } catch {
            Write-Bad "独立冷启动报告无法解析: $($_.Exception.Message)"
            $script:Failures.Add('独立冷启动报告无法解析')
        }
    } else {
        Write-Bad '独立冷启动未生成报告，无法证明发布包可独立运行'
        $script:Failures.Add('独立冷启动未生成报告')
    }
} else {
    Write-Note '单文件产物缺失，跳过独立冷启动（已有失败项记录）'
}

# ---------------------------------------------------------------- 汇总

# RED 模式：即使各步骤都被记为「预期失败」，整体判定也必须为非零。
if ($isRedMode) {
    $script:Failures.Add("RED 模式（跳过类别: $SkipCategories）：预期失败，整体判定必须为非零。")
}

$Summary.steps  = $script:Steps.ToArray()
$Summary.notes  = $script:Notes.ToArray()
$Summary.finalVerdict = if ($script:Failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
$jsonPath = Join-Path $EvidenceDir 'verify-summary.json'
$Summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8

Write-Section '汇总'
Write-Host ("  模式          : {0}" -f $Summary.mode) -ForegroundColor Cyan
Write-Host ("  repo HEAD     : {0}" -f $Summary.repoHead) -ForegroundColor Cyan
Write-Host ("  tracked 复制  : {0}" -f $Summary.trackedCopied) -ForegroundColor Cyan
Write-Host ("  白名单复制    : {0} / 共 {1}（跳过 {2}，缺失 {3}）" -f $Summary.whitelistCopied, $Summary.whitelistTotal, @($Summary.whitelistSkipped).Count, @($Summary.whitelistMissing).Count) -ForegroundColor Cyan
Write-Host ("  唯一期望文件数: {0}（tracked {1} ∪ 白名单 {2}，重叠 {3}；快照内缺失 {4}）" -f $Summary.uniqueExpectedFiles, $Summary.trackedCopied, $Summary.whitelistCopied, $Summary.overlapWithTracked, @($Summary.expectedMissing).Count) -ForegroundColor Cyan
Write-Host ("  快照文件数    : {0}" -f $Summary.snapshotFileCount) -ForegroundColor Cyan
Write-Host ("  额外/泄漏文件 : {0}" -f @($Summary.unexpectedFiles).Count) -ForegroundColor Cyan
Write-Host ("  bin/obj       : {0}" -f $Summary.binObjFound) -ForegroundColor Cyan
Write-Host ("  已知未锁定边界: {0}" -f @($Summary.knownUnlockedBoundaries).Count) -ForegroundColor Cyan
Write-Host ("  失败项        : {0}" -f $script:Failures.Count) -ForegroundColor Cyan
Write-Host ''
foreach ($f in $script:Failures) { Write-Host "   - $f" -ForegroundColor Red }
foreach ($n in $script:Notes)    { Write-Host "   * $n" -ForegroundColor Yellow }
Write-Host ''
Write-Host ("  快照目录（保留供独立复验）: {0}" -f $SnapshotDir) -ForegroundColor Green
Write-Host ("  证据目录（务必保留）      : {0}" -f $EvidenceDir) -ForegroundColor Green

# ---------------------------------------------------------------- 可选清理

if ($CleanupSnapshot) {
    Write-Section '清理隔离快照目录（执行路径安全校验）'
    $resolved = (Resolve-Path -LiteralPath $SnapshotDir).Path
    $problems = @()

    # 证据目录若位于待删目录内，删除会连同证据一起丢失 -> 拒绝
    if ($EvidenceDir.StartsWith($resolved.TrimEnd('\','/') + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $EvidenceDir.TrimEnd('\','/') -eq $resolved.TrimEnd('\','/')) {
        $problems += "证据目录位于待删快照目录内（会丢失证据）: $EvidenceDir"
    }
    if (-not (Test-Path -LiteralPath $markerPath)) { $problems += '缺少本次运行创建标记' }
    elseif ((Get-Content -LiteralPath $markerPath -Raw).Trim() -ne $runToken) { $problems += '标记不匹配（非本次运行创建的目录）' }
    if (-not $resolved.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        $problems += "不在系统临时目录内 ($tempRoot)"
    }
    if ($resolved.TrimEnd('\','/') -eq $tempRoot.TrimEnd('\','/')) { $problems += '目标是临时根目录本身' }
    if ((Split-Path -Leaf $resolved) -notlike 'pineglow-snapshot-*') { $problems += '目录名不符合 pineglow-snapshot-* 约定' }

    if ($problems.Count -gt 0) {
        Write-Bad "拒绝删除: $($problems -join '; ')"
        $script:Failures.Add('CleanupSnapshot 安全校验未通过，已拒绝删除')
        $Summary.finalVerdict = 'FAIL'
        $Summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8
    } else {
        Remove-Item -LiteralPath $resolved -Recurse -Force
        Write-Ok "已删除 $resolved"
        Write-Note "证据目录未受影响，仍保留于: $EvidenceDir"
        $Summary.snapshotCleaned = $true
        $Summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8
    }
}

if ($script:Failures.Count -gt 0) {
    Write-Host ''
    Write-Host '  最终判定: FAIL' -ForegroundColor Red
    exit 1
}
Write-Host ''
Write-Host '  最终判定: PASS' -ForegroundColor Green
exit 0
