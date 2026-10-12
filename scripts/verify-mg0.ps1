# MG0 / MG0-pre 独立复验脚本（MR00 / MR01 / W1 / W2 / W3）
#
# 与 verify-mg1/mg2/mg3.ps1 平行存在（刻意不共用：让每个里程碑的证据自洽、已记录哈希保持稳定）。
#
# 本脚本要处理一个**格式差异**（这是复验方最容易踩的坑）：
#   - W1/W2/W3 共用 `W1-W3-route-closure/20261002-182304Z`，符合后来的约定（manifest.json + green/changed-files.sha256.txt）；
#   - MR00 的哈希清单叫 `baseline/input-manifest.sha256.txt`，而且**语义不同**：它是"源码输入清单"（214 条），
#     不是"本次改动的文件"；
#   - MR01 **没有任何哈希清单**，也**没有 manifest.json**。
# 本脚本显式声明这些来源，而不是假装格式统一。
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg0.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg0.ps1 -SkipTests

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

Write-Head 'MG0 / MG0-pre 复验：证据位置与格式'
$runDirs = @(
    @{ Id = 'MR00'; Path = Join-Path $EvidenceRoot 'MR00\20261002-072004Z'; HashFile = 'baseline\input-manifest.sha256.txt'; HashKind = '输入清单（非变更文件）'; HasManifest = $false },
    @{ Id = 'MR01'; Path = Join-Path $EvidenceRoot 'MR01\20261002-072004Z'; HashFile = ''; HashKind = '（无哈希清单）'; HasManifest = $false },
    @{ Id = 'W1-W3'; Path = Join-Path $EvidenceRoot 'W1-W3-route-closure\20261002-182304Z'; HashFile = 'green\changed-files.sha256.txt'; HashKind = '变更文件'; HasManifest = $true }
)
foreach ($r in $runDirs) {
    if (-not (Test-Path -LiteralPath $r.Path)) { throw "缺少证据目录：$($r.Path)" }
    $n = (Get-ChildItem -LiteralPath $r.Path -Recurse -File | Measure-Object).Count
    Write-Host ("{0,-8} {1} 文件  manifest={2}  哈希清单={3}（{4}）" -f $r.Id, $n, $r.HasManifest, ($(if ($r.HashFile) { $r.HashFile } else { '无' })), $r.HashKind)
}
if (Test-Path -LiteralPath (Join-Path $runDirs[2].Path 'manifest.json')) {
    $wm = Get-Content -LiteralPath (Join-Path $runDirs[2].Path 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host ("         W1-W3 manifest.result：{0}" -f (($wm.result | Out-String).Trim() -replace "`r?`n", ' | '))
    Write-Host ("         W1-W3 manifest.knownLimits：{0}" -f (($wm.knownLimits | Out-String).Trim() -replace "`r?`n", ' | '))
}

Write-Head '证据漂移检查（扫描全部 run，区分"被更晚里程碑覆盖"与"无法解释"）'
$mg0Runs = @{ '20261002-072004Z' = 'MR00/MR01'; '20261002-182304Z' = 'W1-W3' }
$allRecords = @()
foreach ($pkgRoot in (Get-ChildItem -LiteralPath $EvidenceRoot -Directory)) {
    foreach ($run in (Get-ChildItem -LiteralPath $pkgRoot.FullName -Directory -ErrorAction SilentlyContinue)) {
        # 两种已知的哈希清单位置都要读
        foreach ($rel in @('green\changed-files.sha256.txt', 'baseline\input-manifest.sha256.txt')) {
            $hashFile = Join-Path $run.FullName $rel
            if (-not (Test-Path -LiteralPath $hashFile)) { continue }
            foreach ($line in (Get-Content -LiteralPath $hashFile -Encoding UTF8)) {
                if ($line -match '^([0-9A-Fa-f]{64})\s+(.+)$') {
                    $allRecords += [pscustomobject]@{ Run = $run.Name; Package = $pkgRoot.Name; Source = $rel; Sha = $Matches[1].ToUpper(); Path = $Matches[2].Trim() }
                }
            }
        }
    }
}
Write-Host ("  扫描到 {0} 条记录，覆盖 {1} 个 run" -f $allRecords.Count, ($allRecords | Select-Object -ExpandProperty Run -Unique).Count)

$latest = @{}
foreach ($rec in $allRecords) {
    if (-not $latest.ContainsKey($rec.Path) -or $latest[$rec.Path].Run -lt $rec.Run) { $latest[$rec.Path] = $rec }
}

$match = 0; $unexplained = 0; $superseded = 0; $missing = 0
foreach ($path in ($latest.Keys | Sort-Object)) {
    $rec = $latest[$path]
    $full = Join-Path $RepoRoot ($path -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) {
        Write-Host ("  [缺失] {0}（记录于 {1}）" -f $path, $rec.Run) -ForegroundColor Red
        $missing++
        continue
    }
    $now = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
    if ($now -eq $rec.Sha) { $match++; continue }

    if ($mg0Runs.ContainsKey($rec.Run)) {
        Write-Host ("  [漂移-本里程碑] {0}`n         记录({1}/{2})={3}`n         当前            ={4}" -f $path, $rec.Run, $mg0Runs[$rec.Run], $rec.Sha, $now) -ForegroundColor Red
        $unexplained++
    }
    else {
        $superseded++
    }
}
$superseded += ($allRecords.Count - $latest.Count)
Write-Host ("  一致={0}  本里程碑内无法解释的漂移={1}  被更晚里程碑覆盖的历史记录={2}  文件缺失={3}" -f $match, $unexplained, $superseded, $missing)
if ($unexplained -gt 0 -or $missing -gt 0) {
    Write-Host '  注意：本里程碑内的漂移必须逐条解释，否则证据不可用于复验。' -ForegroundColor Yellow
}

if (-not $SkipTests) {
    Write-Head '重新运行全量测试'
    Push-Location $RepoRoot
    try {
        Write-Host '--- C# ---'
        dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release
        $csharpExit = $LASTEXITCODE
        Write-Host '--- Node ---'
        $tests = @(Get-ChildItem -LiteralPath '.\ui\tests' -Filter '*.test.cjs' -File | Sort-Object Name | ForEach-Object FullName)
        node --test --test-reporter=spec @tests
        $nodeExit = $LASTEXITCODE
        Write-Host '--- 台账一致性 ---'
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'audit-plan-ledger.ps1')
        $ledgerExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    Write-Host ("  C# exit={0}  Node exit={1}  台账审计 exit={2}（都应为 0）" -f $csharpExit, $nodeExit, $ledgerExit)
}

Write-Head '复验边界（必须照抄进结论）'
Write-Host '本脚本只证明：证据目录齐全（含格式差异已显式声明）、漂移已分类、全量测试与台账审计通过。'
Write-Host '它**不能**替代下列独立复验：'
Write-Host '  1. 抽查 MR00 的 214 条输入清单是否真的覆盖了构建所需输入（脚本只校验哈希，不判断完整性）；'
Write-Host '  2. MR01 的文档产出（outputs/docs/*）是否与当时的源码事实一致（需人读）；'
Write-Host '  3. W1-W3 的真实 GUI / WebView2 / 真实游戏行为（当时已标未验证）；'
Write-Host '  4. MR00/MR01 没有 manifest.json —— 本脚本只声明这一事实，不代为补写结论。'
Write-Host '执行者最多能写"证据就绪"；放行必须分别记录自动验证、独立复验与用户批准。'
