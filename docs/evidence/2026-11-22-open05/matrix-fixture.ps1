# OPEN-05 参数矩阵复跑夹具（2026-11-22，r3）
# 逐用例起独立 powershell 子进程，原始输出落到证据目录。
# 不修改被测脚本；子进程显式清空 PSExecutionPolicyPreference，不触碰机器执行策略。
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

function New-SelfConsistentRun([string]$Root, [string]$RunId) {
	$run = Join-Path $Root ('PKG-A\' + $RunId)
	New-Item -ItemType Directory -Force -Path $run | Out-Null
	[IO.File]::WriteAllText((Join-Path $run 'deliverable.md'), "# deliverable`n", $utf8)
	[IO.File]::WriteAllText((Join-Path $run 'note.txt'), "note`n", $utf8)
	$del  = (Get-FileHash -LiteralPath (Join-Path $run 'deliverable.md') -Algorithm SHA256).Hash
	$note = (Get-FileHash -LiteralPath (Join-Path $run 'note.txt') -Algorithm SHA256).Hash
	$manifest = '{"runId":"' + $RunId + '","files":[{"path":"deliverable.md","sha256":"' + $del + '"},{"path":"note.txt","sha256":"' + $note + '"}]}'
	[IO.File]::WriteAllText((Join-Path $run 'manifest.json'), $manifest, $utf8)
}

# “约定前”例外 run：目录名在白名单里，但没有 deliverable/manifest。
# 这条专门压 Substring 路径前缀：白名单命中与否必须与 -EvidenceRoot 的写法无关。
function New-PreConventionRun([string]$Root, [string]$RelPath) {
	$run = Join-Path $Root $RelPath
	New-Item -ItemType Directory -Force -Path $run | Out-Null
	[IO.File]::WriteAllText((Join-Path $run 'only.txt'), "x`n", $utf8)
}

$spaced = Join-Path $fixture 'tree 中文 空格'
New-SelfConsistentRun $spaced '20260101-000000Z'
New-PreConventionRun $spaced 'MR00\20261002-072004Z'

$spacedRepo = Join-Path $fixture 'repo 中文 空格'
$spacedDefaultTree = Join-Path $spacedRepo 'docs\market-execution'
New-SelfConsistentRun $spacedDefaultTree '20260102-000000Z'
New-PreConventionRun $spacedDefaultTree 'MR01\20261002-072004Z'

[IO.File]::WriteAllText((Join-Path $fixture 'plain-file.txt'), "not a directory`n", $utf8)

# ---------------- 执行引擎 ----------------
$script:total = 0
$script:pass  = 0

function Q([string]$value) { return ("'" + $value.Replace("'", "''") + "'") }

function Invoke-Case {
	param([string]$Name, [string]$ScriptPath, [string]$ArgText, [string]$ExpectPattern, [int]$ExpectExit, [string]$ExpectedSource, [string]$WorkDir = $Worktree)

	$invoke = "try { & '" + $ScriptPath.Replace("'", "''") + "' " + $ArgText + "; exit `$LASTEXITCODE } catch { [Console]::Error.WriteLine(`$_.Exception.Message); exit 1 }"
	$decoded = @(
		'[Console]::OutputEncoding = [System.Text.Encoding]::UTF8',
		"`$ErrorActionPreference = 'Stop'",
		$invoke
	) -join "`n"
	# 注：这里的“换行”只是 PowerShell 脚本自身的语句分隔，串成子进程时不含续行符错误。
	$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(($decoded -replace "`n", "`r`n")))

	$out  = Join-Path $OutDir ($Name + '.out.txt')
	$err  = Join-Path $OutDir ($Name + '.err.txt')
	$meta = Join-Path $OutDir ($Name + '.txt')
	Remove-Item -LiteralPath $out, $err, $meta -Force -ErrorAction SilentlyContinue

	$psi = New-Object System.Diagnostics.ProcessStartInfo
	$psi.FileName = 'cmd.exe'
	$psi.Arguments = '/c set PSExecutionPolicyPreference=&& powershell.exe -NoProfile -OutputFormat Text -EncodedCommand ' + $encoded + ' > "' + $out + '" 2> "' + $err + '"'
	$psi.UseShellExecute = $false
	$psi.CreateNoWindow = $true
	$psi.WorkingDirectory = $WorkDir
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
		"cwd=$WorkDir",
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
	Write-Host ("{0,-28} exit={1,-3} {2}" -f $Name, $exit, $verdict)
}

$realTree = Join-Path $Worktree 'docs\market-execution'
$et = Join-Path $Worktree 'scripts\audit-evidence-tree.ps1'
$pl = Join-Path $Worktree 'scripts\audit-plan-ledger.ps1'
$plan = Join-Path $Worktree 'docs\market-task-plan-2026-10-03.md'

# ---- audit-evidence-tree.ps1：入口与参数矩阵 ----
Invoke-Case 'tree-default'          $et ('-RepoRoot ' + (Q $Worktree)) '扫描到 61 个 run 目录' 1 '默认根'
Invoke-Case 'tree-override-same'    $et ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q $realTree)) '扫描到 61 个 run 目录' 1 '显式-同树'
Invoke-Case 'tree-blank'            $et ('-RepoRoot ' + (Q $Worktree) + " -EvidenceRoot ' '") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空白'
Invoke-Case 'tree-empty'            $et ('-RepoRoot ' + (Q $Worktree) + " -EvidenceRoot ''") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空串'
Invoke-Case 'tree-missing'          $et ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q (Join-Path $fixture 'does-not-exist'))) '\[EVIDENCE_ROOT_MISSING\]' 1 '显式不存在'
Invoke-Case 'tree-missing-default'  $et ('-RepoRoot ' + (Q (Join-Path $fixture 'no-default'))) '\[DEFAULT_ROOT_MISSING\]' 1 '省略+默认根缺失'
Invoke-Case 'tree-file'             $et ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q (Join-Path $fixture 'plain-file.txt'))) '\[EVIDENCE_ROOT_NOT_DIRECTORY\]' 1 '显式普通文件'
Invoke-Case 'tree-cjk-default'      $et ('-RepoRoot ' + (Q $spacedRepo)) '扫描到 2 个 run 目录.*证据树内部自洽' 0 '省略+中文 RepoRoot'
Invoke-Case 'tree-spaced-cjk'       $et ('-RepoRoot ' + (Q $fixture) + ' -EvidenceRoot ' + (Q $spaced)) '扫描到 2 个 run 目录.*证据树内部自洽' 0 '显式空格中文目录'
Invoke-Case 'tree-empty-tree'       $et ('-RepoRoot ' + (Q $fixture) + ' -EvidenceRoot ' + (Q (Join-Path $fixture 'empty-root'))) '扫描到 0 个 run 目录.*证据树内部自洽' 0 '空证据树'

# ---- 回归：路径写法不得改变审计结论（Codex 第 1 轮阻断项）----
# 同一棵夹具树、同样 2 个 run（1 个自洽 + 1 个白名单例外），只换 -EvidenceRoot 写法。
$sameTreeArgs = @(
	@{ n = 'tree-pathform-abs';     a = (Q $spaced);                                 w = $fixture },
	@{ n = 'tree-pathform-trailing';a = (Q ($spaced + '\'));                         w = $fixture },
	@{ n = 'tree-pathform-rel';     a = (Q 'tree 中文 空格');                         w = $fixture },
	@{ n = 'tree-pathform-rel-dot'; a = (Q '.\tree 中文 空格');                       w = $fixture },
	@{ n = 'tree-pathform-dotdot';  a = (Q ('..\' + (Split-Path $spaced -Leaf)));     w = (Join-Path $fixture 'empty-root') }
)
foreach ($c in $sameTreeArgs) {
	Invoke-Case $c.n $et ('-RepoRoot ' + (Q $fixture) + ' -EvidenceRoot ' + $c.a) '扫描到 2 个 run 目录.*证据树内部自洽（7 项检查全部通过）' 0 '同一树的路径写法' $c.w
}

# ---- audit-plan-ledger.ps1 ----
Invoke-Case 'ledger-default'         $pl ('-RepoRoot ' + (Q $Worktree)) '解析到 31 个包行' 1 '默认根+默认台账'
Invoke-Case 'ledger-override-same'   $pl ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q $realTree)) '解析到 31 个包行' 1 '显式-同树'
Invoke-Case 'ledger-blank'           $pl ('-RepoRoot ' + (Q $Worktree) + " -EvidenceRoot ' '") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空白'
Invoke-Case 'ledger-empty'           $pl ('-RepoRoot ' + (Q $Worktree) + " -EvidenceRoot ''") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空串'
Invoke-Case 'ledger-missing'         $pl ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q (Join-Path $fixture 'does-not-exist'))) '\[EVIDENCE_ROOT_MISSING\]' 1 '显式不存在'
Invoke-Case 'ledger-missing-default' $pl ('-RepoRoot ' + (Q (Join-Path $fixture 'no-default'))) '\[DEFAULT_ROOT_MISSING\]' 1 '省略+默认根缺失'
Invoke-Case 'ledger-file'            $pl ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q (Join-Path $fixture 'plain-file.txt'))) '\[EVIDENCE_ROOT_NOT_DIRECTORY\]' 1 '显式普通文件'
Invoke-Case 'ledger-cjk-default'     $pl ('-RepoRoot ' + (Q $spacedRepo) + ' -PlanPath ' + (Q $plan)) '解析到 31 个包行' 1 '省略+中文 RepoRoot'
Invoke-Case 'ledger-spaced-cjk'      $pl ('-RepoRoot ' + (Q $fixture) + ' -EvidenceRoot ' + (Q $spaced) + ' -PlanPath ' + (Q $plan)) '解析到 31 个包行' 1 '显式空格中文目录'
Invoke-Case 'ledger-empty-tree'      $pl ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q (Join-Path $fixture 'empty-root'))) '解析到 31 个包行' 1 '空证据树'

# ledger 的路径写法不应改变计数（它在内部不做 Substring，回归确认无同类缺陷）
Invoke-Case 'ledger-pathform-trailing' $pl ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q ($realTree + '\'))) '解析到 31 个包行' 1 '同一树的路径写法'
Invoke-Case 'ledger-pathform-rel'      $pl ('-RepoRoot ' + (Q $Worktree) + ' -EvidenceRoot ' + (Q 'docs\market-execution')) '解析到 31 个包行' 1 '同一树的路径写法' $Worktree

# ---- 边界（常驻，原为一次性探针）----
# ① -RepoRoot ''：PowerShell 参数绑定期就拒绝，仍在扫描前失败；错误码不是 [DEFAULT_ROOT_MISSING]，属既有行为。
Invoke-Case 'tree-reporoot-empty'   $et ("-RepoRoot ''") '为空字符串' 1 'RepoRoot 空串（参数绑定错误）'
# ② splat 显式传空值：与直接写 -EvidenceRoot '' 同为「显式」，必须报 BLANK 而不是退回默认根。
Invoke-Case 'tree-splat-empty'      $et ("-RepoRoot " + (Q $Worktree) + " -EvidenceRoot ''") '\[EVIDENCE_ROOT_BLANK\]' 1 'splat 形式显式空值'

Write-Host ''
Write-Host ("cases={0} PASS={1} FAIL={2}" -f $script:total, $script:pass, ($script:total - $script:pass))
if ($script:pass -ne $script:total) { exit 1 } else { exit 0 }
