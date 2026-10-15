#Requires -Version 5.1
<#
.SYNOPSIS
    MR00 步骤 1：对 SourceRoot 与 DeliveryRoot 做只读基线核对。

.DESCRIPTION
    逐条执行基础只读命令并留下取证记录。不修改任何项目文件。
    SourceRoot 全程只读；本脚本自身只写 EvidenceRoot。
#>
[CmdletBinding()]
param(
    [string] $EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution',
    [string] $RunId,
    [string] $SourceRoot = 'D:\松光启动器',
    [string] $DeliveryRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not $RunId) { throw "必须显式传入 -RunId" }
$helper = Join-Path $EvidenceRoot "MR00\$RunId\tooling\Invoke-Evidence.ps1"
if (-not (Test-Path -LiteralPath $helper)) { throw "找不到取证助手: $helper" }
$logDir = Join-Path $EvidenceRoot "MR00\$RunId\logs"

function Run-Ev {
    param([string] $Name, [string] $Command, [string] $Cwd)
    $lp = Join-Path $logDir "$Name.json"
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $helper -LogPath $lp -Command $Command -Cwd $Cwd 2>&1
    $code = $LASTEXITCODE
    Write-Host ("  {0,-34} exit={1}" -f $Name, $code)
    return $code
}

$results = New-Object System.Collections.Generic.List[object]
function Track { param([string] $Name, [string] $Command, [string] $Cwd, [int] $Code)
    $results.Add([pscustomobject]@{ name = $Name; command = $Command; cwd = $Cwd; exitCode = $Code }) | Out-Null
}

Write-Host ''
Write-Host '=== SourceRoot 只读基线 ===' -ForegroundColor Cyan
$src = @(
    @{ n = 'src-toplevel';        c = 'git rev-parse --show-toplevel' },
    @{ n = 'src-branch';          c = 'git branch --show-current' },
    @{ n = 'src-head';            c = 'git rev-parse HEAD' },
    @{ n = 'src-status-all';      c = 'git status --short --untracked-files=all' },
    @{ n = 'src-diff-stat';       c = 'git diff --stat' },
    @{ n = 'src-diff-check';      c = 'git diff --check' },
    @{ n = 'src-global-json';     c = 'type global.json' },
    @{ n = 'src-dotnet-sdks';     c = 'dotnet --list-sdks' },
    @{ n = 'src-dotnet-runtimes'; c = 'dotnet --list-runtimes' },
    @{ n = 'src-node-version';    c = 'node --version' },
    @{ n = 'src-npm-version';     c = 'npm --version' },
    @{ n = 'src-ls-files-count';  c = 'git ls-files | find /c /v ""' },
    @{ n = 'src-ps-version';      c = 'powershell -NoProfile -Command "$PSVersionTable.PSVersion.ToString()"' }
)
foreach ($s in $src) {
    $code = Run-Ev -Name $s.n -Command $s.c -Cwd $SourceRoot
    Track -Name $s.n -Command $s.c -Cwd $SourceRoot -Code $code
}

Write-Host ''
Write-Host '=== DeliveryRoot 只读基线 ===' -ForegroundColor Cyan
$dlv = @(
    @{ n = 'dlv-toplevel';   c = 'git rev-parse --show-toplevel' },
    @{ n = 'dlv-branch';     c = 'git branch --show-current' },
    @{ n = 'dlv-head';       c = 'git rev-parse HEAD' },
    @{ n = 'dlv-status-all'; c = 'git status --short --untracked-files=all' },
    @{ n = 'dlv-diff-stat';  c = 'git diff --stat' },
    @{ n = 'dlv-ls-files';   c = 'git ls-files | find /c /v ""' }
)
foreach ($s in $dlv) {
    $code = Run-Ev -Name $s.n -Command $s.c -Cwd $DeliveryRoot
    Track -Name $s.n -Command $s.c -Cwd $DeliveryRoot -Code $code
}

$summaryPath = Join-Path $logDir 'baseline-commands.json'
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host ''
Write-Host "命令记录: $summaryPath" -ForegroundColor Green
$fail = @($results | Where-Object { $_.exitCode -ne 0 })
Write-Host "命令总数: $($results.Count)  非零: $($fail.Count)"
foreach ($f in $fail) { Write-Host "  [非零] $($f.name) exit=$($f.exitCode)" -ForegroundColor Yellow }
exit 0
