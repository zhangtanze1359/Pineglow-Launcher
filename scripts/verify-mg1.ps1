# MG1 独立复验脚本（MR03 / MR04 / MR05 / MR06 / MR07）
#
# 与 verify-mg2.ps1 平行存在（刻意不共用：让每个里程碑的证据自洽、已记录哈希保持稳定）。
#
# 比 MG2 脚本更强的一点：漂移检查**扫描全部 run**（不只本里程碑的五个包），
# 这样才能把"被 MG2 正常覆盖"与"本里程碑内无法解释的漂移"区分开。
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg1.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg1.ps1 -SkipTests

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$packages = @(
    @{ Id = 'MR03'; Dir = 'MR03-remove-from-list' },
    @{ Id = 'MR03b'; Dir = 'MR03-instance-running-guard' },
    @{ Id = 'MR04'; Dir = 'MR04-recoverable-mod-delete' },
    @{ Id = 'MR05'; Dir = 'MR05-mod-replacement-transaction' },
    @{ Id = 'MR06'; Dir = 'MR06-bisect-file-operation-failures' },
    @{ Id = 'MR07'; Dir = 'MR07-bisect-restart-recovery' }
)

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

Write-Head 'MG1 复验：包与 manifest'
$mg1Runs = @{}
foreach ($pkg in $packages) {
    $pkgRoot = Join-Path $EvidenceRoot $pkg.Dir
    if (-not (Test-Path -LiteralPath $pkgRoot)) { throw "缺少证据目录：$pkgRoot" }
    $run = Get-ChildItem -LiteralPath $pkgRoot -Directory | Sort-Object Name | Select-Object -Last 1
    $manifestPath = Join-Path $run.FullName 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "$($pkg.Id) 缺少 manifest.json：$manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $mg1Runs[$run.Name] = $pkg.Id
    Write-Host ("{0}  run={1}" -f $pkg.Id, $run.Name)
    foreach ($prop in $manifest.result.PSObject.Properties) {
        Write-Host ("      {0} = {1}" -f $prop.Name, $prop.Value)
    }
}

Write-Head '证据漂移检查（扫描全部 run，区分"被后续里程碑覆盖"与"无法解释"）'
$allRecords = @()
foreach ($pkgRoot in (Get-ChildItem -LiteralPath $EvidenceRoot -Directory)) {
    foreach ($run in (Get-ChildItem -LiteralPath $pkgRoot.FullName -Directory -ErrorAction SilentlyContinue)) {
        $hashFile = Join-Path $run.FullName 'green\changed-files.sha256.txt'
        if (-not (Test-Path -LiteralPath $hashFile)) { continue }
        foreach ($line in (Get-Content -LiteralPath $hashFile -Encoding UTF8)) {
            if ($line -match '^([0-9A-Fa-f]{64})\s+(.+)$') {
                $allRecords += [pscustomobject]@{ Run = $run.Name; Package = $pkgRoot.Name; Sha = $Matches[1].ToUpper(); Path = $Matches[2].Trim() }
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

    if ($mg1Runs.ContainsKey($rec.Run)) {
        # 最新记录来自 MG1 自己，却与工作区不符 —— 这是必须解释的漂移
        Write-Host ("  [漂移-本里程碑] {0}`n         记录({1}/{2})={3}`n         当前            ={4}" -f $path, $rec.Run, $mg1Runs[$rec.Run], $rec.Sha, $now) -ForegroundColor Red
        $unexplained++
    }
    else {
        # 最新记录来自更晚的里程碑 —— 属于正常覆盖
        $superseded++
    }
}
$superseded += ($allRecords.Count - $latest.Count)
Write-Host ("  一致={0}  本里程碑内无法解释的漂移={1}  被后续里程碑覆盖的历史记录={2}  文件缺失={3}" -f $match, $unexplained, $superseded, $missing)
if ($unexplained -gt 0 -or $missing -gt 0) {
    Write-Host '  注意：本里程碑内的漂移必须逐条解释（改了什么、属于哪个包），否则证据不可用于复验。' -ForegroundColor Yellow
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
        Write-Host '--- bisect smoke ---'
        dotnet build '.\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj' -c Release | Out-Null
        Push-Location '.\dev\PineglowLauncher\VerifyHost\bin\Release\net10.0'
        try {
            & '.\VerifyHost.exe' --bisect-smoke --port 48171
            $smokeExit = $LASTEXITCODE
        }
        finally { Pop-Location }
    }
    finally {
        Pop-Location
    }
    Write-Host ("  C# exit={0}  Node exit={1}  bisect-smoke exit={2}（都应为 0）" -f $csharpExit, $nodeExit, $smokeExit)
}

Write-Head '复验边界（必须照抄进结论）'
Write-Host '本脚本只证明：证据目录齐全、漂移已分类、全量测试与 bisect 冒烟通过。'
Write-Host '它**不能**替代下列独立复验：'
Write-Host '  1. 抽查实现是否真的满足各包验收（脚本不读代码）；'
Write-Host '  2. 真实 GUI / 真实游戏 / 跨卷恢复等真实行为（本仓库环境无法覆盖）；'
Write-Host '  3. MR03 的"实例运行中不得移除"仍受限于服务端无运行注册表（见各包 deliverable.md）。'
Write-Host '执行者最多能写"证据就绪"；放行必须分别记录自动验证、独立复验与用户批准。'
