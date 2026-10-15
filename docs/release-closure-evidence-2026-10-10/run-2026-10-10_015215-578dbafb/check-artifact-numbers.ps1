<#
.SYNOPSIS
  检查审计产物（决策单 / 报告 / 索引）里的数字是否与工作树一致。

.DESCRIPTION
  为什么需要它：本会话里"派生文档漂移"被抓到过五次（报告 ×2、决策单 ×3、索引 ×1），
  每次都是靠人工复查发现的 —— 而人工复查每次都要重新数一遍文件。
  这个脚本把"数"和"比"固定下来，以后每轮跑一次即可。

  它**只读**：不改任何文件，也不碰产品目录。

.PARAMETER EvidenceRoot
  证据根目录，默认 docs\release-closure-evidence-2026-10-10。
#>
[CmdletBinding()]
param(
  [string]$RepoRoot = (Get-Location).Path,
  [string]$EvidenceRoot = 'docs\release-closure-evidence-2026-10-10'
)

# 注意：不能设成 Stop —— git 往 stderr 写 CRLF 警告时，PowerShell 5.1 会把它当成 NativeCommandError 抛出来。
# 需要中断的地方都已经显式 throw 了。
$ErrorActionPreference = 'Continue'
$ev = Join-Path $RepoRoot $EvidenceRoot
if (-not (Test-Path $ev)) { throw "找不到证据目录：$ev" }
$run = Join-Path $ev 'run-2026-10-10_015215-578dbafb'
$sheet = Join-Path $ev '决策单.md'
$report = Join-Path $ev 'release-readiness-distance-report.md'
$index = Join-Path $run 'INDEX.md'
foreach ($p in @($sheet, $report, $index)) { if (-not (Test-Path $p)) { throw "找不到：$p" } }

Push-Location $RepoRoot
try {
  # ---- measure (the same way every time) ----
  $untracked = @(git ls-files --others --exclude-standard 2>$null)
  $modified = @(git diff --name-only 2>$null)
  $mine = @($untracked | Where-Object { $_ -match '^docs/release-closure-evidence-2026-10-10/' })
  $core = @($untracked | Where-Object { $_ -match '^(dev|ui|scripts|\.github)/' -or $_ -eq 'global.json' })
  $other = @($untracked | Where-Object { $_ -notmatch '^docs/release-closure-evidence-2026-10-10/' -and $_ -notmatch '^(dev|ui|scripts|\.github)/' -and $_ -ne 'global.json' })
  $scripts = @(Get-ChildItem $run -Filter 'apply-*.ps1' -File)
  $records = @(Get-ChildItem $run -Filter 'round*.md' -File)

  $m = [ordered]@{
    'untrackedTotal'   = $untracked.Count
    'modifiedTracked'  = $modified.Count
    'optionA'          = $untracked.Count + $modified.Count
    'optionAPrime'     = $untracked.Count - $mine.Count + $modified.Count
    'optionB'          = $core.Count + $modified.Count
    'myEvidenceFiles'  = $mine.Count
    'recoveredDocs'    = $other.Count
    'coreUntracked'    = $core.Count
    'applyScripts'     = $scripts.Count
    'roundRecords'     = $records.Count
    'reportLines'      = (Get-Content $report -Encoding UTF8 | Measure-Object).Count
    'staged'           = @(git diff --cached --name-only 2>$null).Count
  }

  Write-Output '=== measured from the working tree ==='
  $m.GetEnumerator() | ForEach-Object { Write-Output ("  {0,-18} = {1}" -f $_.Key, $_.Value) }

  # ---- what the artifacts claim ----
  $sheetText = [IO.File]::ReadAllText($sheet)
  $reportText = [IO.File]::ReadAllText($report)
  $indexText = [IO.File]::ReadAllText($index)

  $claims = @(
    @{ name = '决策单：全仓未跟踪';   expect = $m.untrackedTotal;  pattern = '全仓未跟踪文件\*\*\s*\|\s*\*\*(\d+)' ; text = $sheetText },
    @{ name = '决策单：我的证据数';   expect = $m.myEvidenceFiles; pattern = '其中 \*\*(\d+) 个是本审计写进'            ; text = $sheetText },
    @{ name = '决策单：(a) 规模';     expect = $m.optionA;         pattern = '\| \*\*\(a\) 全部提交\*\*.*?\| \*\*(\d+)\*\* \|' ; text = $sheetText },
    @{ name = '决策单：aprime 规模';    expect = $m.optionAPrime;    pattern = '\| \*\*\(a′\) .*?\| \*\*(\d+)\*\* \|'           ; text = $sheetText },
    @{ name = '决策单：(b) 规模';     expect = $m.optionB;         pattern = '\| \*\*\(b\) .*?\| \*\*(\d+)\*\* \|'      ; text = $sheetText },
    @{ name = '决策单：脚本数';       expect = $m.applyScripts;    pattern = '应用方式\*\*：(\d+) 个 `apply'               ; text = $sheetText },
    @{ name = '决策单：轮次记录数';   expect = $m.roundRecords;    pattern = '另有 (\d+) 份轮次记录'                     ; text = $sheetText },
    @{ name = '决策单：报告行数';     expect = $m.reportLines;     pattern = '距离评估报告有 (\d+) 行'                   ; text = $sheetText },
    @{ name = '索引标题：文件数';     expect = $m.roundRecords;    pattern = '# 轮次记录索引（(\d+) 份文件'              ; text = $indexText }
  )

  Write-Output ''
  Write-Output '=== claims vs measured ==='
  $bad = 0
  foreach ($c in $claims) {
    $mm = [regex]::Match($c.text, $c.pattern)
    if (-not $mm.Success) { Write-Output ("  [MISS] {0}  —— 正则没匹配到（文档措辞可能变了）" -f $c.name); $bad++; continue }
    $got = [int]$mm.Groups[1].Value
    if ($got -eq $c.expect) { Write-Output ("  [OK]   {0}: {1}" -f $c.name, $got) }
    else { Write-Output ("  [DRIFT] {0}: 文档写 {1}，实测 {2}" -f $c.name, $got, $c.expect); $bad++ }
  }

  # ---- 构成式验算（第 111 轮加） ----
  # 为什么要有这一段：决策单里 "A + B + C = N" 这类"构成"栏，总数我每次都会重测，
  # 而**构成是我手写的拆分** —— 第 87 轮 (a) 行漏了 163、第 110 轮 (a′) 行漏了 163、
  # 第 110 轮 (a) 行还差 1。**这三处都不是总数错，是拆分错。**
  # 所以：把每一行的加数加起来，和它自己写的总数比。
  Write-Output ''
  Write-Output '=== 构成式验算（加数之和 vs 该行写的总数） ==='
  $compBad = 0
  $sheetLines = Get-Content $sheet -Encoding UTF8
  for ($i = 0; $i -lt $sheetLines.Count; $i++) {
    $line = $sheetLines[$i]
    if ($line -notmatch '^\| \*\*\((a|a′|b)\)') { continue }
    $seg = ($line -split '\|')[2]
    $summands = @([regex]::Matches($seg, '(\d[\d,]*)') | ForEach-Object { [int]($_.Value -replace ',', '') })
    $totalM = [regex]::Match($line, '\| \*\*(\d+)\*\* \|')
    if ($summands.Count -lt 2 -or -not $totalM.Success) {
      Write-Output ("  [SKIP] 第 {0} 行：解析不出加数或总数" -f ($i + 1)); continue
    }
    $sum = ($summands | Measure-Object -Sum).Sum
    $tot = [int]$totalM.Groups[1].Value
    if ($sum -eq $tot) { Write-Output ("  [OK]   第 {0} 行：{1} = {2}" -f ($i + 1), ($summands -join ' + '), $tot) }
    else { Write-Output ("  [SUM]  第 {0} 行：{1} = {2}，但该行写的总数是 {3}" -f ($i + 1), ($summands -join ' + '), $sum, $tot); $compBad++ }
  }

  Write-Output ''
  $bad += $compBad
  if ($bad -eq 0) { Write-Output '判定：产物里的数字与工作树一致（0 处漂移），且构成式全部相加相等。'; exit 0 }
  Write-Output ("判定：发现 {0} 处需要处理（漂移 / 措辞变了 / 构成式不相等）。" -f $bad)
  exit 1
}
finally { Pop-Location }
