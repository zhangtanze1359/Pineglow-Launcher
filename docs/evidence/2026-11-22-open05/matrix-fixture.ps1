# OPEN-05 参数矩阵复跑夹具（2026-11-22）
# 只做三件事：建夹具树、逐用例起独立 powershell 进程、把原始输出落到证据目录。
# 不修改任何脚本，不修改机器执行策略（子进程显式清空 PSExecutionPolicyPreference）。
param(
	[string]$Worktree = 'D:/Projects/_worktrees/pg-open05-20261122',
	[string]$OutDir   = 'D:/Projects/松光启动器/docs/evidence/2026-11-22-open05/raw'
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---------------- 夹具树 ----------------
$fixture = Join-Path $Worktree '_fixture-open05'
if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'empty-root') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'no-default\docs') | Out-Null

$script:runCount = 0
function New-SelfConsistentRun([string]$Root, [string]$RunId) {
	$run = Join-Path $Root ('PKG-A\' + $RunId)
	New-Item -ItemType Directory -Force -Path $run | Out-Null
	[IO.File]::WriteAllText((Join-Path $run 'deliverable.md'), "# deliverable`n", $utf8)
	[IO.File]::WriteAllText((Join-Path $run 'note.txt'), "note`n", $utf8)
	$del  = (Get-FileHash -LiteralPath (Join-Path $run 'deliverable.md') -Algorithm SHA256).Hash
	$note = (Get-FileHash -LiteralPath (Join-Path $run 'note.txt') -Algorithm SHA256).Hash
	$manifest = '{"runId":"' + $RunId + '","files":[{"path":"deliverable.md","sha256":"' + $del + '"},{"path":"note.txt","sha256":"' + $note + '"}]}'
	[IO.File]::WriteAllText((Join-Path $run 'manifest.json'), $manifest, $utf8)
	$script:runCount++
}

# 含空格与中文的合法证据树（显式 -EvidenceRoot 用）
$spaced = Join-Path $fixture 'tree 中文 空格'
New-SelfConsistentRun $spaced '20260101-000000Z'
# 含空格与中文的 -RepoRoot，证据树在其 docs\market-execution 下（默认根用）
$spacedRepo = Join-Path $fixture 'repo 中文 空格'
New-SelfConsistentRun (Join-Path $spacedRepo 'docs\market-execution') '20260102-000000Z'
# 普通文件（-EvidenceRoot 指向文件）
[IO.File]::WriteAllText((Join-Path $fixture 'plain-file.txt'), "not a directory`n", $utf8)

# ---------------- 执行引擎 ----------------
# 参数一律用有序数组 + 位置拼装：PowerShell 5.1 里把 @{...} 直接传给 [hashtable]
# 形参在某些调用形态下会被拆成 object[]，属于工具脚本自身的坑，不在被测脚本范围内。
$script:total = 0
$script:pass  = 0

function Invoke-Case {
	param([string]$Name, [string]$ScriptPath, [string[]]$ArgLines, [string]$ExpectPattern, [int]$ExpectExit, [string]$ExpectedSource)

	$quoted = @()
	foreach ($line in $ArgLines) { $quoted += $line }
	$argText = ($quoted -join ' ')
	$invoke = "try { & '" + $ScriptPath.Replace("'", "''") + "' " + $argText + "; exit `$LASTEXITCODE } catch { [Console]::Error.WriteLine(`$_.Exception.Message); exit 1 }"
	$decoded = @(
		'[Console]::OutputEncoding = [System.Text.Encoding]::UTF8',
		"`$ErrorActionPreference = 'Stop'",
		$invoke
	) -join "`n"
	$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($decoded))

	$out  = Join-Path $OutDir ($Name + '.out.txt')
	$err  = Join-Path $OutDir ($Name + '.err.txt')
	$meta = Join-Path $OutDir ($Name + '.txt')
	Remove-Item -LiteralPath $out, $err, $meta -Force -ErrorAction SilentlyContinue

	$psi = New-Object System.Diagnostics.ProcessStartInfo
	$psi.FileName = 'cmd.exe'
	$psi.Arguments = '/c set PSExecutionPolicyPreference=&& powershell.exe -NoProfile -OutputFormat Text -EncodedCommand ' + $encoded + ' > "' + $out + '" 2> "' + $err + '"'
	$psi.UseShellExecute = $false
	$psi.CreateNoWindow = $true
	$psi.WorkingDirectory = $Worktree
	$p = [System.Diagnostics.Process]::Start($psi)
	$p.WaitForExit()
	$exit = $p.ExitCode

	$stdout = if (Test-Path $out) { Get-Content -LiteralPath $out -Encoding UTF8 -Raw } else { '' }
	$stderr = if (Test-Path $err) { Get-Content -LiteralPath $err -Encoding UTF8 -Raw } else { '' }
	$patternHit = (($stdout -match $ExpectPattern) -or ($stderr -match $ExpectPattern))
	$verdict = if (($exit -eq $ExpectExit) -and $patternHit) { 'PASS' } else { 'FAIL' }

	$report = @(
		"case=$Name",
		"worktree=$Worktree",
		"time=$((Get-Date).ToString('o'))",
		"command=powershell.exe -NoProfile -OutputFormat Text -EncodedCommand <base64>  (child env: PSExecutionPolicyPreference cleared)",
		"decoded=$decoded",
		"expect_exit=$ExpectExit",
		"expect_pattern=$ExpectPattern",
		"expect_source=$ExpectedSource",
		"actual_exit=$exit",
		"stdout_file=$out",
		"stderr_file=$err",
		"verdict=$verdict"
	) -join "`n"
	[IO.File]::WriteAllText($meta, $report + "`n", $utf8)

	$script:total++
	if ($verdict -eq 'PASS') { $script:pass++ }
	Write-Host ("{0,-30} exit={1,-3} {2}" -f $Name, $exit, $verdict)
}

function Q([string]$value) { return ("'" + $value.Replace("'", "''") + "'") }

$realTree = Join-Path $Worktree 'docs\market-execution'
$et = Join-Path $Worktree 'scripts\audit-evidence-tree.ps1'
$pl = Join-Path $Worktree 'scripts\audit-plan-ledger.ps1'
$plan = Join-Path $Worktree 'docs\market-task-plan-2026-10-03.md'

Invoke-Case 'tree-default'          $et @(('-RepoRoot ' + (Q $Worktree))) '扫描到 61 个 run 目录.*发现 609 处不一致' 1 '默认根'
Invoke-Case 'tree-override-same'    $et @(('-RepoRoot ' + (Q $Worktree)), ('-EvidenceRoot ' + (Q $realTree))) '扫描到 61 个 run 目录.*发现 609 处不一致' 1 '显式-同树'
Invoke-Case 'tree-blank'            $et @(('-RepoRoot ' + (Q $Worktree)), "-EvidenceRoot ' '") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空白'
Invoke-Case 'tree-empty'            $et @(('-RepoRoot ' + (Q $Worktree)), "-EvidenceRoot ''") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空串'
Invoke-Case 'tree-missing'          $et @(('-RepoRoot ' + (Q $Worktree)), ('-EvidenceRoot ' + (Q (Join-Path $fixture 'does-not-exist')))) '\[EVIDENCE_ROOT_MISSING\]' 1 '显式不存在'
Invoke-Case 'tree-file'             $et @(('-RepoRoot ' + (Q $Worktree)), ('-EvidenceRoot ' + (Q (Join-Path $fixture 'plain-file.txt')))) '\[EVIDENCE_ROOT_NOT_DIRECTORY\]' 1 '显式普通文件'
Invoke-Case 'tree-missing-default'  $et @(('-RepoRoot ' + (Q (Join-Path $fixture 'no-default')))) '\[DEFAULT_ROOT_MISSING\]' 1 '省略+默认根缺失'
Invoke-Case 'tree-spaced-cjk'       $et @(('-RepoRoot ' + (Q $fixture)), ('-EvidenceRoot ' + (Q $spaced))) '扫描到 1 个 run 目录.*结论：证据树内部自洽' 0 '显式空格中文目录'
Invoke-Case 'tree-cjk-default'      $et @(('-RepoRoot ' + (Q $spacedRepo))) '扫描到 1 个 run 目录.*结论：证据树内部自洽' 0 '省略+中文 RepoRoot'
Invoke-Case 'tree-empty-tree'       $et @(('-RepoRoot ' + (Q $fixture)), ('-EvidenceRoot ' + (Q (Join-Path $fixture 'empty-root')))) '扫描到 0 个 run 目录.*结论：证据树内部自洽' 0 '空证据树'

Invoke-Case 'ledger-default'        $pl @(('-RepoRoot ' + (Q $Worktree))) '解析到 31 个包行.*发现 4 处不一致' 1 '默认根+默认台账'
Invoke-Case 'ledger-override-same'  $pl @(('-RepoRoot ' + (Q $Worktree)), ('-EvidenceRoot ' + (Q $realTree))) '解析到 31 个包行.*发现 4 处不一致' 1 '显式-同树'
Invoke-Case 'ledger-blank'          $pl @(('-RepoRoot ' + (Q $Worktree)), "-EvidenceRoot ' '") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空白'
Invoke-Case 'ledger-empty'          $pl @(('-RepoRoot ' + (Q $Worktree)), "-EvidenceRoot ''") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空串'
Invoke-Case 'ledger-missing'        $pl @(('-RepoRoot ' + (Q $Worktree)), ('-EvidenceRoot ' + (Q (Join-Path $fixture 'does-not-exist')))) '\[EVIDENCE_ROOT_MISSING\]' 1 '显式不存在'
Invoke-Case 'ledger-file'           $pl @(('-RepoRoot ' + (Q $Worktree)), ('-EvidenceRoot ' + (Q (Join-Path $fixture 'plain-file.txt')))) '\[EVIDENCE_ROOT_NOT_DIRECTORY\]' 1 '显式普通文件'
Invoke-Case 'ledger-missing-default' $pl @(('-RepoRoot ' + (Q (Join-Path $fixture 'no-default')))) '\[DEFAULT_ROOT_MISSING\]' 1 '省略+默认根缺失'
Invoke-Case 'ledger-spaced-cjk'     $pl @(('-RepoRoot ' + (Q $fixture)), ('-EvidenceRoot ' + (Q $spaced)), ('-PlanPath ' + (Q $plan))) '解析到 31 个包行.*孤儿证据目录.*PKG-A' 1 '显式空格中文目录'
Invoke-Case 'ledger-cjk-default'    $pl @(('-RepoRoot ' + (Q $spacedRepo)), ('-PlanPath ' + (Q $plan))) '解析到 31 个包行.*孤儿证据目录.*PKG-A' 1 '省略+中文 RepoRoot'
Invoke-Case 'ledger-empty-tree'     $pl @(('-RepoRoot ' + (Q $Worktree)), ('-EvidenceRoot ' + (Q (Join-Path $fixture 'empty-root')))) '解析到 31 个包行.*缺证据目录.*发现 20 处不一致' 1 '空证据树（检查2 必然报缺目录）'

Write-Host ''
Write-Host ("fixtureRuns={0} cases={1} PASS={2} FAIL={3}" -f $script:runCount, $script:total, $script:pass, ($script:total - $script:pass))
if ($script:pass -ne $script:total) { exit 1 } else { exit 0 }
