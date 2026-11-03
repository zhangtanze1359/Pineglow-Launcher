# MG2 独立复验脚本（MR08 / MR09 / MR11 / MR12）
#
# 它做三件事：
#   1. 汇总四个包的 manifest.json 结果；
#   2. 证据漂移检查：把各包 changed-files.sha256.txt 里记录的哈希与**当前工作区**逐一比对，
#      区分"被后续包正常覆盖"与"无法解释的漂移"；
#   3. 重新跑一遍 C# 与 Node 全量测试，与各包声称的数字对照。
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg2.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg2.ps1 -SkipTests

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$packages = @(
    @{ Id = 'MR08'; Dir = 'MR08-diagnostic-report-history' },
    @{ Id = 'MR09'; Dir = 'MR09-diagnostic-benchmark' },
    @{ Id = 'MR11'; Dir = 'MR11-mrpack-spec-import-rollback' },
    @{ Id = 'MR12'; Dir = 'MR12-curseforge-rate-limit' }
)

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

Write-Head 'MG2 复验：包与 manifest'
$runDirs = @()
foreach ($pkg in $packages) {
    $pkgRoot = Join-Path $EvidenceRoot $pkg.Dir
    if (-not (Test-Path -LiteralPath $pkgRoot)) { throw "缺少证据目录：$pkgRoot" }
    $run = Get-ChildItem -LiteralPath $pkgRoot -Directory | Sort-Object Name | Select-Object -Last 1
    $manifestPath = Join-Path $run.FullName 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "$($pkg.Id) 缺少 manifest.json：$manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $runDirs += [pscustomobject]@{ Id = $pkg.Id; Run = $run.Name; Path = $run.FullName; Manifest = $manifest }
    Write-Host ("{0}  run={1}" -f $pkg.Id, $run.Name)
    foreach ($prop in $manifest.result.PSObject.Properties) {
        Write-Host ("      {0} = {1}" -f $prop.Name, $prop.Value)
    }
}

Write-Head '证据漂移检查（记录哈希 vs 当前工作区）'
# 按 run 名称（即时间序）从早到晚，取每个路径**最后一次**被记录的哈希作为"应有值"。
#
# 重要修正（2026-10-03）：必须扫描**全部 run**，不能只扫 MG2 自己的四个包。
# 旧实现只从 MG2 的 run 构建 latest，于是"某文件后来被更晚的包改过"会被误报成
# **无法解释的漂移**（实测误报 6 条：api.js/i18n.js/launcher.js 被 MR17/MR20 改过，
# LocalApiService.cs 被 MR03b 改过，白名单被 MR17/MR21/MR24 改过）。
$mg2RunSet = @{}
foreach ($entry in $runDirs) { $mg2RunSet[$entry.Run] = $true }

$allRecords = @()
foreach ($pkgRoot in (Get-ChildItem -LiteralPath $EvidenceRoot -Directory)) {
    foreach ($run in (Get-ChildItem -LiteralPath $pkgRoot.FullName -Directory -ErrorAction SilentlyContinue)) {
        foreach ($rel in @('green\changed-files.sha256.txt', 'baseline\input-manifest.sha256.txt')) {
            $hashFile = Join-Path $run.FullName $rel
            if (-not (Test-Path -LiteralPath $hashFile)) { continue }
            foreach ($line in (Get-Content -LiteralPath $hashFile -Encoding UTF8)) {
                if ($line -match '^([0-9A-Fa-f]{64})\s+(.+)$') {
                    $allRecords += [pscustomobject]@{ Run = $run.Name; Sha = $Matches[1].ToUpper(); Path = $Matches[2].Trim() }
                }
            }
        }
    }
}

$latest = @{}
foreach ($rec in $allRecords) {
    if (-not $latest.ContainsKey($rec.Path) -or $latest[$rec.Path].Run -lt $rec.Run) { $latest[$rec.Path] = $rec }
}

$match = 0; $drift = 0; $superseded = 0; $missing = 0
foreach ($path in ($latest.Keys | Sort-Object)) {
    $full = Join-Path $RepoRoot ($path -replace '/', '\')
    if (-not (Test-Path -LiteralPath $full)) {
        Write-Host ("  [缺失] {0}" -f $path) -ForegroundColor Red
        $missing++
        continue
    }
    $now = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
    if ($now -eq $latest[$path].Sha) {
        $match++
    }
    else {
        # 最新记录若来自 MG2 自己的包 → 本里程碑内无法解释；否则是被更晚的包正常覆盖。
        if ($mg2RunSet.ContainsKey($latest[$path].Run)) {
            Write-Host ("  [漂移-本里程碑] {0}`n         记录({1})={2}`n         当前            ={3}" -f $path, $latest[$path].Run, $latest[$path].Sha, $now) -ForegroundColor Red
            $drift++
        }
        else {
            Write-Host ("  [覆盖-更晚的包] {0}（记录 {1}）" -f $path, $latest[$path].Run) -ForegroundColor DarkGray
            $superseded++
        }
    }
}
$superseded = $allRecords.Count - $latest.Count
Write-Host ("  一致={0}  无法解释的漂移={1}  被后续包覆盖的历史记录={2}  文件缺失={3}" -f $match, $drift, $superseded, $missing)
if ($drift -gt 0 -or $missing -gt 0) {
    Write-Host '  注意：漂移必须逐条解释（改了什么、属于哪个包），否则证据不可用于复验。' -ForegroundColor Yellow
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
    }
    finally {
        Pop-Location
    }
    Write-Host ("  C# exit={0}  Node exit={1}（应为 0）" -f $csharpExit, $nodeExit)
}

Write-Head '复验边界（必须照抄进结论）'
Write-Host '本脚本只证明：证据目录齐全、记录的哈希与当前工作区一致、全量测试通过。'
Write-Host '它**不能**替代下列独立复验：'
Write-Host '  1. 抽查实现是否真的满足各包验收（脚本不读代码）；'
Write-Host '  2. 真实 GUI / 真实游戏 / 真实网络的行为（本仓库环境无法覆盖）；'
Write-Host '  3. MR09 的结论部分需要**另行授权的真实模型评测**才有数据。'
Write-Host '执行者最多能写"证据就绪"；放行必须分别记录自动验证、独立复验与用户批准。'
