#Requires -Version 5.1
<#
.SYNOPSIS
    **批准后的一键执行脚本**（第 82 轮，**默认只干跑**）：把 6 份补丁按正确组合应用到工作区，并跑 9 步验证清单。
.DESCRIPTION
    这个脚本**不会**被本会话自动执行——它存在的意义是：你说"同意"之后，我跑一条命令，而不是临时拼命令。
    用法：
      # 1) 干跑（只检查、不改任何文件）：打印将要应用的补丁、将要跑的步骤，并逐份 git apply --check
      powershell -File apply-and-verify.ps1 -RepoRoot D:\松光启动器 -DryRun
      # 2) 真正执行（会改工作区源码！）
      powershell -File apply-and-verify.ps1 -RepoRoot D:\松光启动器 -B1Variant Root -EvidenceDir <证据目录>
    组合规则（重要）：**B1 的两种修法二选一**——`-B1Variant Root`（治根：VersionResolver 合并 + 回归测试）
    或 `-B1Variant Minimal`（最小兜底：LauncherService 补参数）。其余四份（B0/B2/B2b）始终应用。
#>
param(
    [Parameter(Mandatory)][string] $RepoRoot,
    [ValidateSet('Root', 'Minimal')][string] $B1Variant = 'Root',
    [string] $PatchDir,
    [string] $EvidenceDir,
    [switch] $DryRun
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
if (-not $PatchDir) { $PatchDir = Join-Path $PSScriptRoot '..\run-20261006-2130-patch-set' }
$PatchDir = (Resolve-Path -LiteralPath $PatchDir).Path
$repo = (Resolve-Path -LiteralPath $RepoRoot).Path
if (-not $EvidenceDir) { $EvidenceDir = Join-Path $env:TEMP ('approval-run-' + (Get-Date).ToString('yyyyMMdd-HHmmss')) }
[void](New-Item -ItemType Directory -Path $EvidenceDir -Force)
$logPath = Join-Path $EvidenceDir 'apply-and-verify.log'
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }

# ---- 补丁组合
$patches = [ordered]@{
    'B0-默认实例/账号落盘'   = 'proposed-fix-configstore-default.diff'
    'B1-加载器 arguments'    = $(if ($B1Variant -eq 'Root') { 'proposed-fix-versionresolver.diff' } else { 'proposed-fix-B-launcherservice.diff' })
    'B1-回归测试(仅治根)'    = $(if ($B1Variant -eq 'Root') { 'proposed-test-versionresolver.diff' } else { $null })
    'B2-安装器 URL'          = 'proposed-fix-installer-url.diff'
    'B2b-launcher_profiles'  = 'proposed-fix-forge-launcher-profile.diff'
}
$selected = @()
foreach ($k in $patches.Keys) { $v = $patches[$k]; if ($v) { $selected += [pscustomobject]@{ label = $k; file = (Join-Path $PatchDir $v) } } }

Log ('=== 计划应用 ' + $selected.Count + ' 份补丁（B1Variant=' + $B1Variant + '）===')
$allOk = $true
foreach ($s in $selected) {
    if (-not (Test-Path $s.file)) { Log ('  ❌ 缺文件: ' + $s.file); $allOk = $false; continue }
    $chk = & git -C $repo apply --check $s.file 2>&1
    $ok = ($LASTEXITCODE -eq 0)
    if (-not $ok) { $allOk = $false }
    Log ('  ' + $(if ($ok) { '✅' } else { '❌' }) + ' ' + $s.label + '  ← ' + (Split-Path $s.file -Leaf))
    if (-not $ok) { ($chk | Select-Object -First 2) | ForEach-Object { Log ('       ' + $_) } }
}

$steps = @(
    '1) dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release   （期望 904/904，含新增回归测试）',
    '2) 用新构建跑 Vanilla 1.18.2 启动（期望命令行含 -Djava.library.path、LWJGL/声音/图集、0 崩溃）',
    '3) 用新构建跑 Fabric 真实包启动（期望从"必崩"变为进渲染管线）',
    '4) 用新构建跑 Quilt 真实包启动（回归：应仍正常）',
    '5) Forge 真实包导入（期望不再 404；安装器在正常网络下应到 Successfully installed client into launcher.）',
    '6) 默认实例/账号复现脚本（期望从 REPRODUCED 变为不再复现）',
    '7) scripts/verify-source-snapshot.ps1 全量门槛（期望 GREEN / 失败 0）',
    '8) 记录新的单文件 EXE sha256（旧 5009cf73… 作废）',
    '9) 更新复评/手册/进展报告与决策包状态'
)
Log '=== 批准后将执行的 9 步 ==='
$steps | ForEach-Object { Log ('  ' + $_) }

if ($DryRun) {
    Log ('=== 干跑结束：补丁检查 ' + $(if ($allOk) { '全部通过 ✅' } else { '有失败 ❌' }) + '；未改动任何文件 ===')
    $st = & git -C $repo status --porcelain
    Log ('  工作区：modified=' + @($st | Where-Object { $_.Substring(0, 2) -eq ' M' }).Count + ' staged=' + @($st | Where-Object { $_.Substring(0, 1) -ne ' ' -and $_.Substring(0, 2) -ne '??' }).Count + ' HEAD=' + (& git -C $repo rev-parse --short HEAD))
    exit $(if ($allOk) { 0 } else { 1 })
}

# ---- 真正执行：应用补丁
Log '=== 应用补丁（会修改工作区源码）==='
foreach ($s in $selected) {
    $ap = & git -C $repo apply $s.file 2>&1
    Log ('  ' + $(if ($LASTEXITCODE -eq 0) { '✅ 已应用 ' } else { '❌ 失败 ' }) + $s.label + $(if ($LASTEXITCODE -eq 0) { '' } else { ' :: ' + ($ap -join ' | ') }))
}
Log '=== 接下来请按 9 步执行（本脚本只负责"正确地打上补丁"）==='
Log '  提示：改了 dev/** ⇒ 产物字节变化 ⇒ 既有 sha256 证据作废，必须重跑第 7 步并记录新 sha256。'
exit 0
