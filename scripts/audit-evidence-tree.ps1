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
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    # 与 audit-plan-ledger.ps1 / verify-mg*.ps1 保持一致：证据树在**工作树**里，
    # 不在当前检出目录（D:\松光启动器\docs\market-execution 并不存在）。
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $EvidenceRoot)) {
    $fallback = Join-Path $RepoRoot 'docs\market-execution'
    if (Test-Path -LiteralPath $fallback) { $EvidenceRoot = $fallback }
    else { throw "找不到证据树：$EvidenceRoot（也不在 $fallback）" }
}

Write-Host ''
Write-Host '== 证据树交叉一致性审计 ==' -ForegroundColor Cyan
Write-Host ("  证据树 = {0}" -f $EvidenceRoot)

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

foreach ($run in $runs) {
    $deliverable = Join-Path $run.FullName 'deliverable.md'
    $manifestPath = Join-Path $run.FullName 'manifest.json'

    if (-not (Test-Path -LiteralPath $deliverable)) { $noDeliverable += $run.FullName.Substring($EvidenceRoot.Length + 1) }
    if (-not (Test-Path -LiteralPath $manifestPath)) { $noManifest += $run.FullName.Substring($EvidenceRoot.Length + 1); continue }

    $manifest = $null
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        $badJson += $run.FullName.Substring($EvidenceRoot.Length + 1)
        continue
    }

    if ($manifest.runId -and $manifest.runId -ne $run.Name) {
        $badRunId += ("{0}（manifest.runId={1}）" -f $run.FullName.Substring($EvidenceRoot.Length + 1), $manifest.runId)
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
