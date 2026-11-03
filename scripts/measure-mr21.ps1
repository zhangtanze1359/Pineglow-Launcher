# MR21 性能基线测量脚本（只测量，不改产品代码）
#
# 本包**只做测量**：卡片明确"本包初始生产代码只读"、"只完成测量不写性能提升"。
# 因此本脚本：
#   1. 冻结测试机与环境事实（OS / 核数 / SDK / WebView2 / 架构）；
#   2. 跑 N 次观测（默认 5 次；第 1 次为冷启动，其余为热），每次把**原始样本**落盘；
#   3. 跨观测汇总每个指标的 p50 / p95（含最小/最大），并写入 computed；
#   4. 冻结"基线预算"= 实测 p95 留出余量，并**明确标注它是实测基线而不是目标**。
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\measure-mr21.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\measure-mr21.ps1 -Runs 7 -Iterations 20

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$OutDir = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\MR21-install-launch-perf-baseline\20261003-090000Z',
    [int]$Runs = 5,
    [int]$Iterations = 20
)

$ErrorActionPreference = 'Stop'

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

function Percentile([double[]]$sorted, [int]$percent) {
    if ($sorted.Count -eq 0) { return 0 }
    $rank = [Math]::Ceiling($percent / 100.0 * $sorted.Count)
    $idx = [Math]::Min([Math]::Max($rank - 1, 0), $sorted.Count - 1)
    return $sorted[$idx]
}

$rawDir = Join-Path $OutDir 'raw'
$computedDir = Join-Path $OutDir 'computed'
New-Item -ItemType Directory -Force -Path $rawDir, $computedDir | Out-Null

Write-Head 'MR21 环境冻结'
$envFacts = [ordered]@{
    frozenAtUtc   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    osVersion     = [System.Environment]::OSVersion.VersionString
    logicalCores  = [Environment]::ProcessorCount
    dotnetSdk     = (& dotnet --version)
    nodeVersion   = (& node --version)
    processArch   = $env:PROCESSOR_ARCHITECTURE
    runs          = $Runs
    iterationsPerRun = $Iterations
    note          = '同一台机器、同一 SDK；不同机器成绩不可混用'
}
$wv = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
if (Test-Path $wv) { $envFacts['webView2Runtime'] = (Get-ItemProperty $wv).pv } else { $envFacts['webView2Runtime'] = 'not-detected' }
$envFacts | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $rawDir 'environment.json') -Encoding UTF8
$envFacts.GetEnumerator() | ForEach-Object { Write-Host ("  {0} = {1}" -f $_.Key, $_.Value) }

Write-Head "跑 $Runs 次观测（每次 $Iterations 迭代；第 1 次为冷启动）"
$allMetrics = @{}
for ($run = 1; $run -le $Runs; $run++) {
    $runDir = Join-Path $rawDir ("run-{0:D2}" -f $run)
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    $env:MR21_EVIDENCE_DIR = $runDir
    $env:MR21_ITERATIONS = "$Iterations"

    Push-Location $RepoRoot
    try {
        $log = & dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release `
            --filter 'FullyQualifiedName~LauncherPerfMeasurementTests' --logger 'console;verbosity=detailed' 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $log | Set-Content -LiteralPath (Join-Path $runDir 'console.log') -Encoding UTF8
    if ($exit -ne 0) { throw "第 $run 次测量失败（exit=$exit）；先修测量夹具，不要解读为性能问题。" }

    $lines = $log | Where-Object { $_ -match '^\s*MR21 \{' }
    $parsed = 0
    foreach ($line in $lines) {
        $json = $line.Substring($line.IndexOf('{'))
        $obj = $json | ConvertFrom-Json
        if (-not $allMetrics.ContainsKey($obj.metric)) { $allMetrics[$obj.metric] = @() }
        $allMetrics[$obj.metric] += , $obj
        $parsed++
    }
    Write-Host ("  run {0}: 采集 {1} 个指标" -f $run, $parsed)
    if ($parsed -eq 0) { throw "第 $run 次没有采集到任何指标（MR21 前缀行）；测量夹具可能没跑。" }
}

Write-Head '跨观测汇总（原始样本合并后重算百分位）'
$computed = [ordered]@{ generatedAtUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); runs = $Runs; metrics = [ordered]@{} }
$summaryLines = @()
foreach ($metric in ($allMetrics.Keys | Sort-Object)) {
    $runsForMetric = $allMetrics[$metric]
    $pooled = @()
    foreach ($r in $runsForMetric) { $pooled += $r.samples }
    $sorted = $pooled | Sort-Object
    $p50 = Percentile $sorted 50
    $p95 = Percentile $sorted 95
    $maxWs = ($runsForMetric | ForEach-Object { $_.workingSetMb } | Measure-Object -Maximum).Maximum
    $entry = [ordered]@{
        unit           = $runsForMetric[0].unit
        observations   = $runsForMetric.Count
        samples        = $sorted.Count
        min            = $sorted[0]
        p50            = $p50
        p95            = $p95
        max            = $sorted[$sorted.Count - 1]
        peakWorkingSetMb = $maxWs
        note           = $runsForMetric[0].note
        # 冻结预算 = 实测 p95 × 1.5（余量），明确标注是实测基线而非目标
        frozenBudgetP95 = [Math]::Round($p95 * 1.5, 3)
    }
    $computed.metrics[$metric] = $entry
    $summaryLines += ("  {0,-32} p50={1,8:N3} p95={2,8:N3} max={3,8:N3} {4}  → 冻结预算(p95×1.5)={5:N3}" -f $metric, $p50, $p95, $entry.max, $entry.unit, $entry.frozenBudgetP95)
}
$computed | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $computedDir 'perf-baseline.json') -Encoding UTF8
$summaryLines | ForEach-Object { Write-Host $_ }

Write-Head '测量边界（必须照抄进结论）'
Write-Host '本脚本只产出**服务层热路径的实测基线**，并冻结了预算。它没有证明任何"性能提升"——'
Write-Host '本包初始生产代码只读，未做任何优化；卡片明确"只完成测量不写性能提升"。'
Write-Host '以下**未测量**（需要桌面会话 / 真实账号 / 人参与）：'
Write-Host '  1. 真实 GUI 首屏可交互时间、WebView2 渲染、UI 阻塞；'
Write-Host '  2. 真实登录授权、真实启动游戏、菜单往返；'
Write-Host '  3. 全新机器上"无 .NET runtime"的便携产物首次启动（本机装了 SDK）。'
Write-Host "  4. WebView2 缺失时的降级文案（本机 WebView2=$($envFacts['webView2Runtime'])，无法观测缺失路径）。"
Write-Host ''
Write-Host ("原始数据：{0}" -f $rawDir)
Write-Host ("汇总数据：{0}" -f (Join-Path $computedDir 'perf-baseline.json'))
