param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $EvidenceRoot)) { throw "找不到证据树：$EvidenceRoot" }

<#
.SYNOPSIS
「声称的用例名 ↔ 真实用例名」交叉审计（第 58 轮）。

要抓的错误类型：**幽灵用例** —— deliverable.md 里点名了某个用例（例如
"RED：`Foo_Bar_Baz` → 1 失败"），但仓库里**根本没有**这个方法。
这类错误靠人眼很难发现（名字看起来很像真的），却会直接毁掉证据的可复验性。

做法：
  1. 收集**真实**用例名：C# 的 `public ... Task|void <名字>(`（测试项目内）、
     JS 的 `test('...')` / `test("...")` 名称；
  2. 从每个 run 的 deliverable.md 里抽取"看起来像用例名"的标识符
     （判定：无空格、至少两个下划线、总长 ≥ 12、只含字母数字下划线）；
  3. 报告**声称但不存在**的名字。

注意（口径）：
  * 只检查"声称 → 存在"这一个方向；**不**要求每个用例都被点名（那不现实）；
  * 抽出来的是**候选**，因此报告里会连上下文一起给出，便于人工确认；
  * 早期 run（约定建立前）同样纳入，不做例外 —— 若它们有问题正好暴露出来。
#>
$repoRoot = $RepoRoot
$evidenceRoot = $EvidenceRoot

Write-Host ''
Write-Host '== 声称用例名 ↔ 真实用例名 交叉审计 ==' -ForegroundColor Cyan

# ---------- 1) 收集真实用例名 ----------
$real = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)

$csFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'dev\PineglowLauncher.Tests') -Filter '*.cs' -File
foreach ($f in $csFiles) {
    $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
    if ([string]::IsNullOrEmpty($text)) { continue }
    foreach ($m in [regex]::Matches($text, 'public\s+(?:async\s+)?(?:Task|void)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(')) {
        [void]$real.Add($m.Groups[1].Value)
    }
}

$jsFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'ui\tests') -Filter '*.cjs' -File
foreach ($f in $jsFiles) {
    $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
    if ([string]::IsNullOrEmpty($text)) { continue }
    foreach ($m in [regex]::Matches($text, "test\(\s*'([^']+)'")) { [void]$real.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($text, 'test\(\s*"([^"]+)"')) { [void]$real.Add($m.Groups[1].Value) }
}

Write-Host ("  真实用例名：C# {0} 个文件 / JS {1} 个文件 → 去重后 {2} 个名字" -f $csFiles.Count, $jsFiles.Count, $real.Count)

# ---------- 2) 抽取声称的候选名 ----------
$claimed = @{}   # name -> list of "package/run"
$runs = 0
foreach ($pkg in (Get-ChildItem -LiteralPath $evidenceRoot -Directory)) {
    foreach ($run in (Get-ChildItem -LiteralPath $pkg.FullName -Directory -ErrorAction SilentlyContinue)) {
        $doc = Join-Path $run.FullName 'deliverable.md'
        if (-not (Test-Path -LiteralPath $doc)) { continue }
        $runs++
        $text = Get-Content -LiteralPath $doc -Raw -Encoding UTF8
        if ([string]::IsNullOrEmpty($text)) { continue }
        # 含「注入」字样的行是**故意写的假名**（RED 用来证明本审计会失败），跳过。
        # 这是窄口径：只有带这个标记的行才豁免，正常声称不受影响。
        $text = ($text -split "`n" | Where-Object { $_ -notmatch '注入' }) -join "`n"
        foreach ($m in [regex]::Matches($text, '\b([A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+)\b')) {
            $name = $m.Groups[1].Value
            $underscores = ([regex]::Matches($name, '_')).Count
            if ($underscores -lt 2) { continue }
            if ($name.Length -lt 12) { continue }
            # 误报过滤：全大写（常量/环境变量，如 PINEGLOW_API_PORT）、尾随下划线（截断）
            if ($name -cnotmatch '[a-z]') { continue }
            # 用例名是 PascalCase（含大写字母）；纯小写多为状态字符串（如 replace_in_progress）
            if ($name -cnotmatch '[A-Z]') { continue }
            if ($name.EndsWith('_')) { continue }
            $key = "$($pkg.Name)/$($run.Name)"
            if (-not $claimed.ContainsKey($name)) { $claimed[$name] = @() }
            if ($claimed[$name] -notcontains $key) { $claimed[$name] += $key }
        }
    }
}

Write-Host ("  扫描 {0} 个 deliverable.md，抽出 {1} 个候选名" -f $runs, $claimed.Count)

# ---------- 3) 报告"声称但不存在" ----------
# 有正当理由的历史引用：这些名字**确实不存在**，但 deliverable 里**明确写了它们被删除/改名**（A16 修复）。
# 事后修改历史证据不可取，因此登记为例外并写明理由。
$knownHistorical = @{
    'Quarantine_LockedForReading_ThrowsToday_ButNeverLosesTheSourceOrTheRecord' = 'A16 修复删除了这个"会抛出"的过渡用例（MR04 的 deliverable 已写明"删除"）'
    'Quarantine_LockedForReading_ShouldRecordPerItemErrorInsteadOfThrowing' = 'A16 修复把它改名成 Quarantine_LockedForReading_RecordsPerItemError_WithoutLosingSourceOrRecord（MR04 已写明"改名"）'
}

$ghosts = @()
foreach ($name in ($claimed.Keys | Sort-Object)) {
    if (-not $real.Contains($name) -and -not $knownHistorical.ContainsKey($name)) {
        $ghosts += ("{0}   ← {1}" -f $name, (($claimed[$name] | Select-Object -First 3) -join ', '))
    }
}

Write-Host ''
if ($ghosts.Count -eq 0) {
    Write-Host ('  [OK] 没有任何"声称但不存在"的用例名（已排除 {0} 个有理由的历史引用）' -f $knownHistorical.Count) -ForegroundColor Green
    Write-Host ''
    Write-Host '== 结论：deliverable 里点名的用例都能在测试代码里找到' -ForegroundColor Green
    exit 0
}

Write-Host ("  [幽灵用例] 声称但仓库里找不到（{0} 个）" -f $ghosts.Count) -ForegroundColor Yellow
foreach ($g in ($ghosts | Select-Object -First 40)) { Write-Host ("      " + $g) }
if ($ghosts.Count -gt 40) { Write-Host ("      … 另有 {0} 个" -f ($ghosts.Count - 40)) }
Write-Host ''
Write-Host ("== 结论：发现 {0} 个候选幽灵用例，需逐条人工确认（可能是名字写错，也可能确实缺用例）" -f $ghosts.Count) -ForegroundColor Red
exit 1
