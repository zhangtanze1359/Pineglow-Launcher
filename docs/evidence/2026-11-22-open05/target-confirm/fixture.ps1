# 目标分支参数行为确认（2026-11-22）
# 在 aa64a57 的干净检出上执行；不修改被测脚本，不触碰机器执行策略。
param(
	[string]$Worktree = 'D:/Projects/_worktrees/pg-open05-targetconf-20261122',
	[string]$OutDir   = 'D:/Projects/松光启动器/docs/evidence/2026-11-22-open05/target-confirm/raw'
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$fixture = Join-Path $Worktree '_targetconf-fixture'
if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'empty-root') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'no-default\docs') | Out-Null

function New-Run([string]$Root, [string]$RunId) {
	$run = Join-Path $Root ('PKG-A\' + $RunId)
	New-Item -ItemType Directory -Force -Path $run | Out-Null
	[IO.File]::WriteAllText((Join-Path $run 'deliverable.md'), "# deliverable`n", $utf8)
	[IO.File]::WriteAllText((Join-Path $run 'note.txt'), "note`n", $utf8)
	$del  = (Get-FileHash -LiteralPath (Join-Path $run 'deliverable.md') -Algorithm SHA256).Hash
	$note = (Get-FileHash -LiteralPath (Join-Path $run 'note.txt') -Algorithm SHA256).Hash
	[IO.File]::WriteAllText((Join-Path $run 'manifest.json'), ('{"runId":"' + $RunId + '","files":[{"path":"deliverable.md","sha256":"' + $del + '"},{"path":"note.txt","sha256":"' + $note + '"}]}'), $utf8)
}
function New-PreConventionRun([string]$Root, [string]$RelPath) {
	$run = Join-Path $Root $RelPath
	New-Item -ItemType Directory -Force -Path $run | Out-Null
	[IO.File]::WriteAllText((Join-Path $run 'only.txt'), "x`n", $utf8)
}

$selfTree = Join-Path $fixture 'tree-self'
New-Run $selfTree '20260101-000000Z'
$legacyTree = Join-Path $fixture 'tree 中文 空格'
New-Run $legacyTree '20260101-000000Z'
New-PreConventionRun $legacyTree 'MR00\20261002-072004Z'
[IO.File]::WriteAllText((Join-Path $fixture 'plain-file.txt'), "not a directory`n", $utf8)

$driveLetter = $null
foreach ($c in @('Z','Y','X','W','V')) { if (-not (Test-Path -LiteralPath ($c + ':\'))) { $driveLetter = $c; break } }
if (-not $driveLetter) { throw '找不到空闲盘符' }
$driveRoot = $driveLetter + ':\'
& cmd.exe /c "subst $driveLetter`: `"$legacyTree`"" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "subst 失败" }
Write-Host ("  盘符映射：{0} -> {1}" -f $driveRoot, $legacyTree)

try {
	$script:total = 0; $script:pass = 0
	function Q([string]$v) { return ("'" + $v.Replace("'", "''") + "'") }

	function Invoke-Case {
		param([string]$Name, [string]$ScriptPath, [string]$CallBody, [string]$ExpectPattern, [int]$ExpectExit, [string]$Source, [string]$WorkDir = $Worktree)
		$invoke = "try { " + $CallBody + "; exit `$LASTEXITCODE } catch { [Console]::Error.WriteLine(`$_.Exception.Message); exit 1 }"
		$decoded = @('[Console]::OutputEncoding = [System.Text.Encoding]::UTF8', "`$ErrorActionPreference = 'Stop'", $invoke) -join "`r`n"
		$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($decoded))
		$out = Join-Path $OutDir ($Name + '.out.txt'); $err = Join-Path $OutDir ($Name + '.err.txt'); $meta = Join-Path $OutDir ($Name + '.txt')
		Remove-Item -LiteralPath $out, $err, $meta -Force -ErrorAction SilentlyContinue
		$psi = New-Object System.Diagnostics.ProcessStartInfo
		$psi.FileName = 'cmd.exe'
		$psi.Arguments = '/c set PSExecutionPolicyPreference=&& powershell.exe -NoProfile -OutputFormat Text -EncodedCommand ' + $encoded + ' > "' + $out + '" 2> "' + $err + '"'
		$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true; $psi.WorkingDirectory = $WorkDir
		$p = [System.Diagnostics.Process]::Start($psi); $p.WaitForExit(); $exit = $p.ExitCode
		$stdout = if (Test-Path $out) { Get-Content -LiteralPath $out -Encoding UTF8 -Raw } else { '' }
		$stderr = if (Test-Path $err) { Get-Content -LiteralPath $err -Encoding UTF8 -Raw } else { '' }
		$hit = ($stdout -match $ExpectPattern) -or ($stderr -match $ExpectPattern)
		$verdict = if (($exit -eq $ExpectExit) -and $hit) { 'PASS' } else { 'FAIL' }
		$report = @(
			"case=$Name", "worktree=$Worktree", "cwd=$WorkDir", "time=$((Get-Date).ToString('o'))",
			"checkout_HEAD=$(git -C $Worktree rev-parse HEAD)",
			"call_body=$CallBody", "decoded=$decoded",
			"expect_exit=$ExpectExit", "expect_pattern=$ExpectPattern", "expect_source=$Source",
			"actual_exit=$exit", "stdout_file=$out", "stderr_file=$err", "verdict=$verdict"
		) -join "`n"
		[IO.File]::WriteAllText($meta, $report + "`n", $utf8)
		$script:total++; if ($verdict -eq 'PASS') { $script:pass++ }
		Write-Host ("{0,-26} exit={1,-3} {2}" -f $Name, $exit, $verdict)
	}

	$realTree = Join-Path $Worktree 'docs\market-execution'
	$et = Join-Path $Worktree 'scripts\audit-evidence-tree.ps1'
	$pl = Join-Path $Worktree 'scripts\audit-plan-ledger.ps1'
	$plan = Join-Path $Worktree 'docs\market-task-plan-2026-10-03.md'

	# --- A. 入口参数 ---
	Invoke-Case 'entry-tree-default'      $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree)) '扫描到 61 个 run 目录' 1 '省略参数走默认根'
	Invoke-Case 'entry-tree-blank'        $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot ' '") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空白'
	Invoke-Case 'entry-tree-empty'        $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot ''") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空串'
	Invoke-Case 'entry-tree-missing'      $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'nope'))) '\[EVIDENCE_ROOT_MISSING\]' 1 '显式不存在'
	Invoke-Case 'entry-tree-file'         $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'plain-file.txt'))) '\[EVIDENCE_ROOT_NOT_DIRECTORY\]' 1 '显式普通文件'
	Invoke-Case 'entry-tree-no-default'   $et ("& " + (Q $et) + " -RepoRoot " + (Q (Join-Path $fixture 'no-default'))) '\[DEFAULT_ROOT_MISSING\]' 1 '默认根缺失'
	Invoke-Case 'entry-ledger-default'    $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree)) '解析到 31 个包行' 1 '台账默认根'
	Invoke-Case 'entry-ledger-blank'      $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot ' '") '\[EVIDENCE_ROOT_BLANK\]' 1 '台账显式空白'
	Invoke-Case 'entry-ledger-missing'    $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'nope'))) '\[EVIDENCE_ROOT_MISSING\]' 1 '台账显式不存在'
	Invoke-Case 'entry-ledger-file'       $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'plain-file.txt'))) '\[EVIDENCE_ROOT_NOT_DIRECTORY\]' 1 '台账显式普通文件'
	Invoke-Case 'entry-ledger-no-default' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q (Join-Path $fixture 'no-default'))) '\[DEFAULT_ROOT_MISSING\]' 1 '台账默认根缺失'
	Invoke-Case 'entry-splat-empty'       $et ("`$splatArgs = @{ RepoRoot = " + (Q $Worktree) + "; EvidenceRoot = '' }; & " + (Q $et) + " @splatArgs") '\[EVIDENCE_ROOT_BLANK\]' 1 '哈希表 splat 显式空值'
	Invoke-Case 'entry-spaced-cjk'        $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $legacyTree)) '扫描到 2 个 run 目录.*证据树内部自洽' 0 '空格中文目录'

	# --- B. 路径写法 × 两棵树（目标分支确认：写法不得改变结论）---
	foreach ($t in @(@{n='self';p=$selfTree;runs=1}, @{n='legacy';p=$legacyTree;runs=2})) {
		$leaf = Split-Path $t.p -Leaf
		Invoke-Case ("form-{0}-abs" -f $t.n)  $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $t.p)) ("扫描到 " + $t.runs + " 个 run 目录.*证据树内部自洽") 0 '绝对路径' $fixture
		Invoke-Case ("form-{0}-trail" -f $t.n) $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q ($t.p + '\'))) ("扫描到 " + $t.runs + " 个 run 目录.*证据树内部自洽") 0 '末尾反斜杠' $fixture
		Invoke-Case ("form-{0}-rel" -f $t.n)  $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $leaf)) ("扫描到 " + $t.runs + " 个 run 目录.*证据树内部自洽") 0 '相对路径' $fixture
		Invoke-Case ("form-{0}-dotdot" -f $t.n) $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q ('..\' + $leaf))) ("扫描到 " + $t.runs + " 个 run 目录.*证据树内部自洽") 0 '..相对' (Join-Path $fixture 'empty-root')
	}

	# --- C. 卷根（本轮修复的关键点）---
	Invoke-Case 'drive-root' $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $driveRoot)) '扫描到 2 个 run 目录.*证据树内部自洽（7 项检查全部通过）' 0 ("卷根 " + $driveRoot) $fixture
	Invoke-Case 'drive-abs-control' $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $legacyTree)) '扫描到 2 个 run 目录.*证据树内部自洽（7 项检查全部通过）' 0 '同一树普通绝对路径（卷根对照）' $fixture

	# --- D. 真实树：默认根与显式根必须一致 ---
	Invoke-Case 'real-default'  $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree)) '扫描到 61 个 run 目录' 1 '真实树默认根'
	Invoke-Case 'real-explicit' $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q $realTree)) '扫描到 61 个 run 目录' 1 '真实树显式根'
	Invoke-Case 'real-ledger-explicit' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q $realTree)) '解析到 31 个包行' 1 '台账真实树显式根'

	Write-Host ''
	Write-Host ("cases={0} PASS={1} FAIL={2}" -f $script:total, $script:pass, ($script:total - $script:pass))
	$finalExit = if ($script:pass -ne $script:total) { 1 } else { 0 }
}
finally {
	& cmd.exe /c "subst $driveLetter`: /d" | Out-Null
	Write-Host ("  已解除盘符映射：{0}" -f $driveRoot)
}
exit $finalExit
