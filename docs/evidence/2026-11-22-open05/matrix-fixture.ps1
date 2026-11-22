# OPEN-05 参数矩阵复跑夹具（2026-11-22，r4）
# 逐用例起独立 powershell 子进程，原始输出落到证据目录。
# 不修改被测脚本；子进程显式清空 PSExecutionPolicyPreference，不触碰机器执行策略。
#
# v4 相对 r3 的变化（第 2 轮独立复验整改）：
#   1. 每个用例声明**完整调用语句**（$CallBody），所以 splat 是真正的哈希表 splat，
#      不会被参数拼接伪装成直接传参。
#   2. 新增盘符根用例：夹具树本身 subst 成卷根，与同一目录的绝对路径对照。
#   3. 路径写法对照覆盖三棵树（自洽夹具 / 含历史白名单 run 的夹具 / 真实树）。
#   4. 每条元数据都记录实际调用语句原文，便于复验方逐字核对。
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

# “约定前”例外 run：目录名在 $preConvention 白名单里，但没有 deliverable/manifest。
# 这类 run 是否被正确豁免，直接检验 run 相对路径的截取是否正确。
function New-PreConventionRun([string]$Root, [string]$RelPath) {
	$run = Join-Path $Root $RelPath
	New-Item -ItemType Directory -Force -Path $run | Out-Null
	[IO.File]::WriteAllText((Join-Path $run 'only.txt'), "x`n", $utf8)
}

# 树 A：自洽（只有自洽 run，期望 0 处 / exit 0）
$selfTree = Join-Path $fixture 'tree-self'
New-SelfConsistentRun $selfTree '20260101-000000Z'

# 树 B：含历史白名单例外（期望 0 处 / exit 0 —— 白名单必须命中）
$legacyTree = Join-Path $fixture 'tree 中文 空格'
New-SelfConsistentRun $legacyTree '20260101-000000Z'
New-PreConventionRun $legacyTree 'MR00\20261002-072004Z'

# 树 C：含一个真实发现（期望 1 处 / exit 1）
$dirtyTree = Join-Path $fixture 'tree-with-finding'
New-SelfConsistentRun $dirtyTree '20260101-000000Z'
$bad = Join-Path $dirtyTree 'PKG-A\20260102-000000Z'
New-Item -ItemType Directory -Force -Path $bad | Out-Null
[IO.File]::WriteAllText((Join-Path $bad 'deliverable.md'), "# d`n", $utf8)
[IO.File]::WriteAllText((Join-Path $bad 'notelisted.txt'), "n`n", $utf8)
[IO.File]::WriteAllText((Join-Path $bad 'manifest.json'), '{"runId":"20260102-000000Z","files":[{"path":"deliverable.md","sha256":"' + (Get-FileHash -LiteralPath (Join-Path $bad 'deliverable.md') -Algorithm SHA256).Hash + '"}]}', $utf8)

# 中文 RepoRoot，默认根在 docs\market-execution
$spacedRepo = Join-Path $fixture 'repo 中文 空格'
$spacedDefaultTree = Join-Path $spacedRepo 'docs\market-execution'
New-SelfConsistentRun $spacedDefaultTree '20260102-000000Z'
New-PreConventionRun $spacedDefaultTree 'MR01\20261002-072004Z'

[IO.File]::WriteAllText((Join-Path $fixture 'plain-file.txt'), "not a directory`n", $utf8)

# ---------------- 盘符根映射（仅本夹具创建，结束解除）----------------
# 把「含历史白名单 run」的树映射成卷根，检验 Substring 是否对卷根多跳一个分隔符。
$driveLetter = $null
$driveLabel = 'Z'
foreach ($c in @('Z','Y','X','W','V')) { if (-not (Test-Path -LiteralPath ($c + ':\'))) { $driveLetter = $c; break } }
if (-not $driveLetter) { throw '找不到空闲盘符用于 subst 夹具' }
$driveRoot = $driveLetter + ':\'
& cmd.exe /c "subst $driveLetter`: `"$legacyTree`"" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "subst 失败：$driveLetter -> $legacyTree" }
Write-Host ("  盘符夹具：{0} -> {1}" -f $driveRoot, $legacyTree)

try {
	# ---------------- 执行引擎 ----------------
	$script:total = 0
	$script:pass  = 0

	function Q([string]$value) { return ("'" + $value.Replace("'", "''") + "'") }

	function Invoke-Case {
		param([string]$Name, [string]$ScriptPath, [string]$CallBody, [string]$ExpectPattern, [int]$ExpectExit, [string]$ExpectedSource, [string]$WorkDir = $Worktree)

		$invoke = "try { " + $CallBody + "; exit `$LASTEXITCODE } catch { [Console]::Error.WriteLine(`$_.Exception.Message); exit 1 }"
		$decoded = @(
			'[Console]::OutputEncoding = [System.Text.Encoding]::UTF8',
			"`$ErrorActionPreference = 'Stop'",
			$invoke
		) -join "`r`n"
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
		$psi.WorkingDirectory = $WorkDir
		$p = [System.Diagnostics.Process]::Start($psi)
		$p.WaitForExit()
		$exit = $p.ExitCode

		$stdout = if (Test-Path $out) { Get-Content -LiteralPath $out -Encoding UTF8 -Raw } else { '' }
		$stderr = if (Test-Path $err) { Get-Content -LiteralPath $err -Encoding UTF8 -Raw } else { '' }
		$patternHit = (($stdout -match $ExpectPattern) -or ($stderr -match $ExpectPattern))
		$scanned = ($stdout -match '扫描到') -or ($stdout -match '解析到')
		$verdict = if (($exit -eq $ExpectExit) -and $patternHit) { 'PASS' } else { 'FAIL' }

		$report = @(
			"case=$Name",
			"worktree=$Worktree",
			"cwd=$WorkDir",
			"time=$((Get-Date).ToString('o'))",
			"command=powershell.exe -NoProfile -OutputFormat Text -EncodedCommand <base64>  (child env: PSExecutionPolicyPreference cleared)",
			"call_body=$CallBody",
			"decoded=$decoded",
			"expect_exit=$ExpectExit",
			"expect_pattern=$ExpectPattern",
			"expect_source=$ExpectedSource",
			"actual_exit=$exit",
			"stdout_shows_scan=$scanned",
			"stdout_file=$out",
			"stderr_file=$err",
			"verdict=$verdict"
		) -join "`n"
		[IO.File]::WriteAllText($meta, $report + "`n", $utf8)

		$script:total++
		if ($verdict -eq 'PASS') { $script:pass++ }
		Write-Host ("{0,-30} exit={1,-3} {2}" -f $Name, $exit, $verdict)
	}

	$realTree = Join-Path $Worktree 'docs\market-execution'
	$et = Join-Path $Worktree 'scripts\audit-evidence-tree.ps1'
	$pl = Join-Path $Worktree 'scripts\audit-plan-ledger.ps1'
	$plan = Join-Path $Worktree 'docs\market-task-plan-2026-10-03.md'

	# ================= audit-evidence-tree.ps1 =================
	# --- 入口与参数矩阵 ---
	Invoke-Case 'tree-default' $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree)) '扫描到 61 个 run 目录' 1 '默认根'
	Invoke-Case 'tree-override-same' $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q $realTree)) '扫描到 61 个 run 目录' 1 '显式-同树'
	Invoke-Case 'tree-blank' $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot ' '") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空白'
	Invoke-Case 'tree-empty' $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot ''") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空串'

	# 真正的哈希表 splat：显式空值必须仍判为「显式」，不能退回默认根
	$splatBody = @'
$splatArgs = @{ RepoRoot = '${repoRoot}'; EvidenceRoot = '' }
& '${script}' @splatArgs
'@
	$splatBody = $splatBody.Replace('${repoRoot}', $Worktree).Replace('${script}', $et)
	Invoke-Case 'tree-splat-empty' $et (($splatBody -split "`n" | Where-Object { $_.Trim() }) -join '; ') '\[EVIDENCE_ROOT_BLANK\]' 1 '哈希表 splat 显式空值（非直接传参）'

	Invoke-Case 'tree-missing' $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'does-not-exist'))) '\[EVIDENCE_ROOT_MISSING\]' 1 '显式不存在'
	Invoke-Case 'tree-file' $et ("& " + (Q $et) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'plain-file.txt'))) '\[EVIDENCE_ROOT_NOT_DIRECTORY\]' 1 '显式普通文件'
	Invoke-Case 'tree-missing-default' $et ("& " + (Q $et) + " -RepoRoot " + (Q (Join-Path $fixture 'no-default'))) '\[DEFAULT_ROOT_MISSING\]' 1 '省略+默认根缺失'
	Invoke-Case 'tree-reporoot-empty' $et ("& " + (Q $et) + " -RepoRoot ''") '为空字符串' 1 'RepoRoot 空串（参数绑定错误）'
	Invoke-Case 'tree-cjk-default' $et ("& " + (Q $et) + " -RepoRoot " + (Q $spacedRepo)) '扫描到 2 个 run 目录.*证据树内部自洽' 0 '省略+中文 RepoRoot'
	Invoke-Case 'tree-spaced-cjk' $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $legacyTree)) '扫描到 2 个 run 目录.*证据树内部自洽' 0 '显式空格中文目录'
	Invoke-Case 'tree-empty-tree' $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q (Join-Path $fixture 'empty-root'))) '扫描到 0 个 run 目录.*证据树内部自洽' 0 '空证据树'

	# --- 路径写法：五档 × 三棵树（自洽 / 含白名单 / 含真实发现）---
	$pathForms = @(
		@{ n = 'abs';   f = { param($t) Q $t } },
		@{ n = 'trail'; f = { param($t) Q ($t + '\') } },
		@{ n = 'rel';   f = { param($t) Q (Split-Path $t -Leaf) } },
		@{ n = 'rel-dot'; f = { param($t) Q ('.' + '\' + (Split-Path $t -Leaf)) } }
	)
	$trees = @(
		@{ name = 'self';  path = $selfTree;   expect = '证据树内部自洽（7 项检查全部通过）'; exit = 0; runs = 1 },
		@{ name = 'legacy'; path = $legacyTree; expect = '证据树内部自洽（7 项检查全部通过）'; exit = 0; runs = 2 },
		@{ name = 'dirty'; path = $dirtyTree;  expect = '发现 1 处不一致';                      exit = 1; runs = 2 }
	)
	foreach ($t in $trees) {
		foreach ($pf in $pathForms) {
			$a = & $pf.f $t.path
			Invoke-Case ("tree-form-{0}-{1}" -f $t.name, $pf.n) $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + $a) ("扫描到 " + $t.runs + " 个 run 目录.*" + $t.expect) $t.exit '同一树的路径写法' $fixture
		}
		# ..\ 相对写法：从别的目录启动
		$piece = '..\' + (Split-Path $t.path -Leaf)
		Invoke-Case ("tree-form-{0}-dotdot" -f $t.name) $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $piece)) ("扫描到 " + $t.runs + " 个 run 目录.*" + $t.expect) $t.exit '同一树的路径写法' (Join-Path $fixture 'empty-root')
	}

	# --- 盘符根（第 2 轮复验阻断项）---
	# 同一棵树：卷根写法必须与普通绝对路径给出相同结论。
	Invoke-Case 'tree-drive-root' $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $driveRoot)) '扫描到 2 个 run 目录.*证据树内部自洽（7 项检查全部通过）' 0 ("卷根 " + $driveRoot + "（对照普通绝对路径）") $fixture
	Invoke-Case 'tree-drive-trail' $et ("& " + (Q $et) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q ($legacyTree + '\'))) '扫描到 2 个 run 目录.*证据树内部自洽（7 项检查全部通过）' 0 '同一树普通绝对路径（卷根对照）' $fixture

	# ================= audit-plan-ledger.ps1 =================
	Invoke-Case 'ledger-default' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree)) '解析到 31 个包行' 1 '默认根+默认台账'
	Invoke-Case 'ledger-override-same' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q $realTree)) '解析到 31 个包行' 1 '显式-同树'
	Invoke-Case 'ledger-blank' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot ' '") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空白'
	Invoke-Case 'ledger-empty' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot ''") '\[EVIDENCE_ROOT_BLANK\]' 1 '显式空串'
	Invoke-Case 'ledger-missing' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'does-not-exist'))) '\[EVIDENCE_ROOT_MISSING\]' 1 '显式不存在'
	Invoke-Case 'ledger-file' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'plain-file.txt'))) '\[EVIDENCE_ROOT_NOT_DIRECTORY\]' 1 '显式普通文件'
	Invoke-Case 'ledger-missing-default' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q (Join-Path $fixture 'no-default'))) '\[DEFAULT_ROOT_MISSING\]' 1 '省略+默认根缺失'
	Invoke-Case 'ledger-cjk-default' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $spacedRepo) + " -PlanPath " + (Q $plan)) '解析到 31 个包行' 1 '省略+中文 RepoRoot'
	Invoke-Case 'ledger-spaced-cjk' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $fixture) + " -EvidenceRoot " + (Q $legacyTree) + " -PlanPath " + (Q $plan)) '解析到 31 个包行' 1 '显式空格中文目录'
	Invoke-Case 'ledger-empty-tree' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q (Join-Path $fixture 'empty-root'))) '解析到 31 个包行' 1 '空证据树'
	Invoke-Case 'ledger-pathform-trailing' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q ($realTree + '\'))) '解析到 31 个包行' 1 '同一树的路径写法'
	Invoke-Case 'ledger-pathform-rel' $pl ("& " + (Q $pl) + " -RepoRoot " + (Q $Worktree) + " -EvidenceRoot " + (Q 'docs\market-execution')) '解析到 31 个包行' 1 '同一树的路径写法' $Worktree

	Write-Host ''
	Write-Host ("cases={0} PASS={1} FAIL={2}" -f $script:total, $script:pass, ($script:total - $script:pass))
	$finalExit = if ($script:pass -ne $script:total) { 1 } else { 0 }
}
finally {
	& cmd.exe /c "subst $driveLetter`: /d" | Out-Null
	Write-Host ("  已解除盘符映射：{0}" -f $driveRoot)
}
exit $finalExit
