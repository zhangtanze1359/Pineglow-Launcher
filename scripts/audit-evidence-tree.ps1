#requires -Version 5.1
<#
.SYNOPSIS
证据树交叉一致性审计（第 57 轮新增）。

为什么需要它：`audit-plan-ledger.ps1` 检查的是「台账 ↔ 证据目录」；
`verify-mg*.ps1` 检查的是「里程碑 ↔ 文件哈希」。两者都**不检查证据目录内部的自洽性** ——
例如 manifest 里记的文件哈希是否与目录里真实文件一致、runId 是否与目录名一致。

而"先写 manifest、之后又改了文件"是**很容易发生**的错误（本会话已多次出现同族问题：
忘写 changed-files、索引数字没同步、写了 manifest 又补文件）。本脚本专门抓这一类。

检查项：
  1. 每个 run 目录必须同时有 deliverable.md 与 manifest.json；
  2. manifest.json 必须能解析，且 runId 等于目录名；
  3. manifest 的 files[] 里的 sha256 必须与目录里真实文件**逐一相符**（这是最容易过期的字段）；
  4. manifest 的 files[] 必须覆盖目录里除 manifest.json 以外的**全部**文件；
  5. changed-files.sha256.txt 里记录的路径必须**存在于工作区**（防止写包内相对路径）。

默认证据树 = `-RepoRoot` 下的 `docs/market-execution`（OPEN-05：旧默认指向已不存在的历史交付根
`C:\Users\Administrator\.codex\worktrees\ed41\...`，在仓库里直接跑只会得到
「找不到证据树」或落到不存在的目录）。显式 `-EvidenceRoot` 优先，且**必须存在**：显式给错就当场失败，
不再静默回退到默认根 —— 静默回退会让人以为「审计了给定的树」，实际审计的是另一棵。

三档入口判定（r2 补强：**用参数绑定状态区分「省略」与「显式给了空值」**）：
  1. 省略 `-EvidenceRoot` → 用 `-RepoRoot` 下的 `docs/market-execution`；该目录必须存在，否则报
     `[DEFAULT_ROOT_MISSING]`。
  2. 显式给了空字符串或全空白 → 这是**参数错误**（`[EVIDENCE_ROOT_BLANK]`），不是「用默认」：
     原样传进来的空白值如果被当成省略，就等于「不传参也能过」，静默用另一棵树。
  3. 显式给了非空值 → 必须是**已存在的目录**；不存在报 `[EVIDENCE_ROOT_MISSING]`，
     存在但是普通文件报 `[EVIDENCE_ROOT_NOT_DIRECTORY]`，两者都在审计之前拒绝、都不回退。
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    # OPEN-05：默认值不再是历史交付根 ed41，而是「-RepoRoot 下的 docs/market-execution」——
    # 与 audit-plan-ledger.ps1 保持一致；证据树就在本仓库里，默认值不再依赖任何外部目录。
    # 注意：这里**保留**默认值 ''，判定「是否显式指定」一律走 $PSBoundParameters ——
    # 默认值只用于类型稳定，不代表语义；`-EvidenceRoot ''` 与「不写该参数」是两件不同的事。
    [string]$EvidenceRoot = ''
)

$ErrorActionPreference = 'Stop'

$defaultEvidenceRoot = Join-Path $RepoRoot 'docs\market-execution'
$evidenceRootExplicitlyBound = $PSBoundParameters.ContainsKey('EvidenceRoot')

if (-not $evidenceRootExplicitlyBound) {
    # ① 省略参数：这是唯一允许落到默认根的情形。
    $evidenceRootSource = '默认（-RepoRoot\docs\market-execution）'
    $EvidenceRoot = $defaultEvidenceRoot
    if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
        throw ("[DEFAULT_ROOT_MISSING] 找不到证据树：默认根目录不存在 {0}（默认根取自 -RepoRoot = {1}；请用 -EvidenceRoot 指定实际证据树）" -f $EvidenceRoot, $RepoRoot)
    }
}
elseif ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    # ② 显式给了空/空白：参数错误。把它当"省略"会让空白值静默变成另一棵树。
    throw ("[EVIDENCE_ROOT_BLANK] 参数错误：-EvidenceRoot 被显式指定，但值是空字符串或全空白（[{0}]）。省略该参数才会用默认根 {1}；要指定证据树请给出真实目录。" -f $EvidenceRoot, $defaultEvidenceRoot)
}
else {
    # ③ 显式给了非空值：必须是已存在的目录，审计之前就拒绝。
    $evidenceRootSource = '显式 -EvidenceRoot'
    if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
        if (Test-Path -LiteralPath $EvidenceRoot) {
            throw ("[EVIDENCE_ROOT_NOT_DIRECTORY] 显式指定的证据树不是目录：{0}（该路径存在，但是普通文件或其他非容器对象；-EvidenceRoot 必须是目录）" -f $EvidenceRoot)
        }
        throw ("[EVIDENCE_ROOT_MISSING] 显式指定的证据树不存在：{0}（显式路径不回退到默认根 {1}；要用默认值请省略 -EvidenceRoot）" -f $EvidenceRoot, $defaultEvidenceRoot)
    }
}

# 校验通过后统一规范成绝对前缀：下面用 Substring 计算 run 的相对路径，
# 直接拿调用方传入的字符串当长度基准会被相对路径（'docs\market-execution'）、
# 末尾多余反斜杠或 '.'/'..' 段打乱，导致 $preConvention 白名单匹配失效，
# 同一棵树换个写法就多报“缺 deliverable/manifest”。规范一次即与写法无关。
# 盘符根（'D:\'）要保留尾部反斜杠：去掉会退化成 'D:'，改变显示与扫描语义。
$resolvedRoot = (Resolve-Path -LiteralPath $EvidenceRoot).ProviderPath
$EvidenceRoot = if ($resolvedRoot -match '^[A-Za-z]:\\$') { $resolvedRoot } else { $resolvedRoot.TrimEnd('\', '/') }

Write-Host ''
Write-Host '== 证据树交叉一致性审计 ==' -ForegroundColor Cyan
Write-Host ("  证据树 = {0}" -f $EvidenceRoot)
Write-Host ("  来源   = {0}" -f $evidenceRootSource)

$runs = @()
foreach ($pkg in (Get-ChildItem -LiteralPath $EvidenceRoot -Directory)) {
    foreach ($run in (Get-ChildItem -LiteralPath $pkg.FullName -Directory -ErrorAction SilentlyContinue)) {
        $runs += $run
    }
}
Write-Host ("  扫描到 {0} 个 run 目录" -f $runs.Count)

# 约定建立前的 run：本仓库最早那几次运行还没有 deliverable/manifest 约定，
# 事后补造会污染历史证据 —— 因此显式登记为例外（而不是让审计永远红着）。
$preConvention = @(
    'MR00\20261002-072004Z',
    'MR01\20261002-072004Z',
    'MR02\20261003-110000Z',
    'gap-register-2026-10-03\20261003-190000Z'
)

$noDeliverable = @()
$noManifest = @()
$badJson = @()
$badRunId = @()
$hashMismatch = @()
$notListed = @()
$missingPath = @()

# run 相对路径的报告前缀长度：**只在这里计算一次**。
# 不能到处写 $EvidenceRoot.Length + 1：卷根（'Z:\'）末尾已经带分隔符，
# 再 +1 会把 'MR00\...' 截成 'R00\...'，历史白名单 $preConvention 就再也命中不了，
# 同一棵自洽的树在卷根写法下凭空多报「缺 deliverable / manifest」。
# 规则：根目录以分隔符结尾时直接用它的长度；否则多跳一个分隔符。
$relativePathStart = if ($EvidenceRoot.EndsWith('\') -or $EvidenceRoot.EndsWith('/')) {
	$EvidenceRoot.Length
} else {
	$EvidenceRoot.Length + 1
}

foreach ($run in $runs) {
    $deliverable = Join-Path $run.FullName 'deliverable.md'
    $manifestPath = Join-Path $run.FullName 'manifest.json'
    $runRelative = $run.FullName.Substring($relativePathStart)

    if (-not (Test-Path -LiteralPath $deliverable)) { $noDeliverable += $runRelative }
    if (-not (Test-Path -LiteralPath $manifestPath)) { $noManifest += $runRelative; continue }

    $manifest = $null
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        $badJson += $runRelative
        continue
    }

    if ($manifest.runId -and $manifest.runId -ne $run.Name) {
        $badRunId += ("{0}（manifest.runId={1}）" -f $runRelative, $manifest.runId)
    }

    # 3 + 4：manifest.files[] 与实际文件
    $listed = @{}
    if ($manifest.files) {
        foreach ($f in $manifest.files) {
            $rel = $f.path -replace '/', '\'
            # manifest 若记录了自己的哈希，那是**自指**、永远不可能满足 —— 跳过（无害但无意义）
            if ($rel -eq 'manifest.json') { continue }
            $listed[$rel] = $f.sha256
            $full = Join-Path $run.FullName $rel
            if (-not (Test-Path -LiteralPath $full)) {
                $hashMismatch += ("{0} :: 记录了但文件不存在：{1}" -f $run.Name, $f.path)
                continue
            }
            $actual = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
            if ($actual -ne $f.sha256.ToUpper()) {
                $hashMismatch += ("{0} :: {1} 记录的哈希与文件不符（manifest 过期？）" -f $run.Name, $f.path)
            }
        }
    }

    foreach ($file in (Get-ChildItem -LiteralPath $run.FullName -Recurse -File)) {
        $rel = $file.FullName.Substring($run.FullName.Length + 1)
        if ($rel -eq 'manifest.json') { continue }
        if (-not $listed.ContainsKey($rel)) {
            $notListed += ("{0} :: {1}" -f $run.Name, $rel)
        }
    }
}

# 5：changed-files 的路径必须存在
foreach ($run in $runs) {
    $hashFile = Join-Path $run.FullName 'green\changed-files.sha256.txt'
    if (-not (Test-Path -LiteralPath $hashFile)) { continue }
    foreach ($line in (Get-Content -LiteralPath $hashFile -Encoding UTF8)) {
        if ($line -match '^([0-9A-Fa-f]{64})\s+(.+)$') {
            $path = $Matches[2].Trim() -replace '/', '\'
            $full = Join-Path $RepoRoot $path
            if (-not (Test-Path -LiteralPath $full)) {
                $missingPath += ("{0} :: {1}" -f $run.Name, $Matches[2].Trim())
            }
        }
    }
}

# 约定前的 run 不计入"缺 deliverable/manifest"
$noDeliverable = @($noDeliverable | Where-Object { $preConvention -notcontains $_ })
$noManifest = @($noManifest | Where-Object { $preConvention -notcontains $_ })

function Report([string]$title, $items) {
    if ($items.Count -eq 0) {
        Write-Host ("  [OK] {0}" -f $title) -ForegroundColor Green
    } else {
        Write-Host ("  [{0}] {1}（{2} 项）" -f $title, $title, $items.Count) -ForegroundColor Yellow
        foreach ($i in ($items | Select-Object -First 20)) { Write-Host ("      " + $i) }
        if ($items.Count -gt 20) { Write-Host ("      … 另有 {0} 项" -f ($items.Count - 20)) }
    }
}

Report '缺少 deliverable.md' $noDeliverable
Report '缺少 manifest.json' $noManifest
Report 'manifest.json 无法解析' $badJson
Report 'runId 与目录名不符' $badRunId
Report 'manifest 文件哈希不符/文件缺失' $hashMismatch
Report '目录里有文件未登记进 manifest' $notListed
Report 'changed-files 记录的路径不存在' $missingPath

$total = $noDeliverable.Count + $noManifest.Count + $badJson.Count + $badRunId.Count + $hashMismatch.Count + $notListed.Count + $missingPath.Count
Write-Host ''
if ($total -eq 0) {
    Write-Host '== 结论：证据树内部自洽（7 项检查全部通过）' -ForegroundColor Green
    exit 0
}

Write-Host ("== 结论：发现 {0} 处不一致，需要逐项处理" -f $total) -ForegroundColor Red
exit 1
