# 需要显式授权才能执行的诊断评测脚本（MR09）
#
# 它**不会**调用任何真实模型：
#   - 只跑 DiagnosticBenchmarkTests（案例集结构 + 规则基线 + 评分器干跑 + 零外发）；
#   - 真实模型评测需要用户明确授权、固定模型/参数/日期与费用额度，另行发起。
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-diagnostic-benchmark.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-diagnostic-benchmark.ps1 -CaseSet D:\path\to\diagnostic-cases.json

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$CaseSet = '',
    [string]$EvidenceDir = ''
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

$defaultCaseSet = Join-Path $RepoRoot 'dev\PineglowLauncher.Tests\TestAssets\Diagnostics\diagnostic-cases.json'
if ([string]::IsNullOrWhiteSpace($CaseSet)) { $CaseSet = $defaultCaseSet }

Write-Step 'MR09 诊断评测（合成提供方，零外部请求）'
Write-Host "仓库根：$RepoRoot"
Write-Host "案例集：$CaseSet"

if (-not (Test-Path -LiteralPath $CaseSet)) {
    throw "案例集不存在：$CaseSet"
}

$cases = Get-Content -LiteralPath $CaseSet -Raw -Encoding UTF8 | ConvertFrom-Json
$hash = (Get-FileHash -LiteralPath $CaseSet -Algorithm SHA256).Hash
Write-Host ("案例数：{0}；冻结于 {1}；SHA256={2}" -f $cases.cases.Count, $cases.frozenAt, $hash)

if ($cases.cases.Count -lt 30) {
    throw ("案例数不足 30（实际 {0}）；评测结论不可用。" -f $cases.cases.Count)
}

Write-Step '运行评测工具（不含任何真实模型调用）'
Push-Location $RepoRoot
try {
    dotnet test '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj' -c Release `
        --filter 'FullyQualifiedName~DiagnosticBenchmarkTests' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) {
        throw "评测工具本身失败（exit=$LASTEXITCODE）；先修工具，不要解读为模型质量结论。"
    }
}
finally {
    Pop-Location
}

Write-Step '结论边界'
Write-Host '以上只证明：案例集完整、规则基线刻画准确、评分器与危险回答判定可用、全程零外发。'
Write-Host '它**没有**评测任何真实模型。真实模型评测需要：'
Write-Host '  1) 用户明确授权外发与费用额度；2) 固定模型/version/参数/日期；'
Write-Host '  3) 每例记录成本与延迟，至少三次观察非确定性；4) 20 开发集 + 10 保留集分组。'

if (-not [string]::IsNullOrWhiteSpace($EvidenceDir)) {
    if (-not (Test-Path -LiteralPath $EvidenceDir)) {
        New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
    }
    $summary = [ordered]@{
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        caseSet        = $CaseSet
        caseSetSha256  = $hash
        caseCount      = $cases.cases.Count
        realModelRan   = $false
        note           = '合成提供方干跑；真实模型评测需另行授权。'
    }
    $outPath = Join-Path $EvidenceDir 'benchmark-run.json'
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $outPath -Encoding UTF8
    Write-Host "证据已写入：$outPath"
}
