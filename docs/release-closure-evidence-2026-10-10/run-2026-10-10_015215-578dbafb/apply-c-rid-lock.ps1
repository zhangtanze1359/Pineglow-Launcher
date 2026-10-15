param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-23: implement option 2 of the C proposal (publish dependency locking for all three RIDs)
# inside an ISOLATED copy, and validate it. The product tree is never touched.
#
# Design (validated in rounds 10 and 15):
#   - the SHARED packages.lock.json stays exactly as it is (non-RID form, one net10.0 section);
#   - each RID gets its own lock file, selected on the command line with -p:NuGetLockFilePath=...;
#   - the RID publish therefore runs WITH RestoreLockedMode=true, which the old design forbade.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

# Same guard as apply-proposed-fixes.ps1: a non-ASCII .ps1 without a UTF-8 BOM is read as GBK by
# PowerShell 5.1, and its Chinese anchors then silently stop matching.
$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}
$S = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services'
$T = Join-Path $Root 'dev\PineglowLauncher.Tests'
$W = Join-Path $Root '.github\workflows'
$report = New-Object System.Collections.Generic.List[object]

function Write-PreservingEncoding([string]$file, [string]$text) {
  $bytes = [IO.File]::ReadAllBytes($file)
  $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($hasBom)))
}

function Patch([string]$file, [string]$old, [string]$new, [string]$label) {
  $text = [IO.File]::ReadAllText($file)
  $usesCrlf = $text.Contains("`r`n")
  if ($usesCrlf) { $old = $old -replace "`r?`n", "`r`n"; $new = $new -replace "`r?`n", "`r`n" }
  else { $old = $old -replace "`r?`n", "`n"; $new = $new -replace "`r?`n", "`n" }
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne 1) { throw "[$label] expected exactly 1 match in $file but found $count" }
  Write-PreservingEncoding $file $text.Replace($old, $new)
  $report.Add([pscustomobject]@{ label = $label; file = $file.Substring($Root.Length + 1); ok = $true })
  Write-Output "[OK] $label"
}

$proj = 'dev/PineglowLauncher/Pineglow.Launcher.csproj'
$rids = @('win-x64', 'linux-x64', 'osx-x64')

# ---------------------------------------------------------------- 1. generate the per-RID lock files
Push-Location $Root
try {
  foreach ($rid in $rids) {
    $lock = "packages.$rid.lock.json"
    $out = & dotnet restore $proj -r $rid -p:PublishSingleFile=true -p:NuGetLockFilePath=$lock --force-evaluate 2>&1 | Out-String
    $code = $LASTEXITCODE
    Write-Output "[lock] $rid -> exit=$code"
    if ($code -ne 0) { throw "generating $lock failed: $out" }
    $report.Add([pscustomobject]@{ label = "generate $lock"; file = "dev/PineglowLauncher/$lock"; ok = $true })
  }
}
finally { Pop-Location }

# ---------------------------------------------------------------- 2. release.yml: locked RID publish
$rel = Join-Path $W 'release.yml'
$relOld = ' /p:DebugType=None /p:DebugSymbols=false /p:PublishDir=../../dist /p:Version=${{ steps.version.outputs.version }}'
$relNew = ' /p:DebugType=None /p:DebugSymbols=false /p:RestoreLockedMode=true /p:NuGetLockFilePath=packages.win-x64.lock.json /p:PublishDir=../../dist /p:Version=${{ steps.version.outputs.version }}'
Patch $rel $relOld $relNew 'release.yml: RID publish now locked against packages.win-x64.lock.json'

# ---------------------------------------------------------------- 3. build.yml: locked matrix publish
$bld = Join-Path $W 'build.yml'
$bldOld = 'run: dotnet publish dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -r ${{ matrix.rid }} --self-contained /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false'
$bldNew = 'run: dotnet publish dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -r ${{ matrix.rid }} --self-contained /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false /p:RestoreLockedMode=true /p:NuGetLockFilePath=packages.${{ matrix.rid }}.lock.json'
Patch $bld $bldOld $bldNew 'build.yml: matrix publish now locked against packages.<rid>.lock.json'

# ---------------------------------------------------------------- 4. reverse guard: the RID lock must exist and carry its own section
$guardOld = @'
            echo "OK: $f"
          done
'@
$guardNew = @'
            echo "OK: $f"
          done

          # C/RID lock: the per-RID lock files must exist and must actually describe their RID,
          # otherwise a missing or stale lock would silently fall back to an unlocked restore.
          for rid in win-x64 linux-x64 osx-x64; do
            f="dev/PineglowLauncher/packages.$rid.lock.json"
            if [ ! -f "$f" ]; then
              echo "::error file=$f::缺少 $rid 的 lock 文件（发布要用 -p:NuGetLockFilePath）"
              exit 1
            fi
            if ! grep -q "\"net10\.0/$rid\"" "$f"; then
              echo "::error file=$f::$f 里没有 net10.0/$rid 段，锁定还原会失败或退化"
              exit 1
            fi
            echo "OK: $f (含 net10.0/$rid 段)"
          done
'@
Patch $bld $guardOld $guardNew 'build.yml: guard asserts each RID lock exists and carries its own section'

# ---------------------------------------------------------------- 5. rewrite the two workflow invariants
$wit = Join-Path $T 'WorkflowInvariantTests.cs'
$invOld = @'
	/* ===================== 4. RID 发布不得使用锁定还原（已实测互斥） ===================== */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void RidPublish_MustNotUseRestoreLockedMode(string workflow)
	{
		string[] lines = ReadWorkflow(workflow).Split('\n');
		// 只看**可执行行**：注释里正当地解释了"RID 发布 + RestoreLockedMode 会 NU1004"，
		// 若不排除注释，本用例会被自己的说明文字误伤（本包第一次运行就是这样失败的）。
		var publishLines = lines
			.Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal))
			.Where(l => l.Contains("dotnet publish") && l.Contains("-r "))
			.ToList();
		Assert.True(publishLines.Count >= 1, $"{workflow} 应至少有一条 RID 发布命令");

		foreach (string line in publishLines)
		{
			Assert.False(line.Contains("RestoreLockedMode", StringComparison.OrdinalIgnoreCase),
				$"{workflow} 的 RID 发布不得使用 RestoreLockedMode —— 实测必然 NU1004（与 --locked-mode 互斥）：{line.Trim()}");
		}

		// 互斥的理由应当被写在注释里，避免以后有人"顺手加上锁定"
		Assert.True(ReadWorkflow(workflow).Contains("NU1004"),
			$"{workflow} 应把 NU1004 互斥的理由写在注释里，防止后人误加锁定");
		_out.WriteLine($"[A11] {workflow}：{publishLines.Count} 条 RID 发布均未使用 RestoreLockedMode，且注释记录了 NU1004 理由");
	}
'@
$invNew = @'
	/* ============ 4. RID 发布必须锁定在**该 RID 自己的** lock 上（C 之后的取舍） ============ */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void RidPublish_MustUseRestoreLockedModeWithItsOwnLockFile(string workflow)
	{
		string[] lines = ReadWorkflow(workflow).Split('\n');
		// 只看**可执行行**：注释里正当地解释了锁定策略，若不排除注释，本用例会被自己的说明文字误伤。
		var publishLines = lines
			.Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal))
			.Where(l => l.Contains("dotnet publish") && l.Contains("-r "))
			.ToList();
		Assert.True(publishLines.Count >= 1, $"{workflow} 应至少有一条 RID 发布命令");

		foreach (string line in publishLines)
		{
			// C：RID 发布**必须**锁定，但必须锁定在**该 RID 自己的** lock 文件上。
			// 旧的取舍（"RID 发布不得锁定"）建立在"只有一份 lock"之上；改成每 RID 一份 lock 之后，
			// 那条禁令的根据消失，取而代之的是下面两条更精确的要求。
			Assert.True(line.Contains("RestoreLockedMode", StringComparison.OrdinalIgnoreCase),
				$"{workflow} 的 RID 发布必须带 RestoreLockedMode，否则发布产物没有任何锁定保证：{line.Trim()}");
			Assert.True(line.Contains("NuGetLockFilePath", StringComparison.OrdinalIgnoreCase),
				$"{workflow} 的 RID 发布必须用 -p:NuGetLockFilePath 指定该 RID 的 lock，否则会去改写共享 lock：{line.Trim()}");
		}

		_out.WriteLine($"[A11] {workflow}：{publishLines.Count} 条 RID 发布均带 RestoreLockedMode 且指向各自的 RID lock");
	}
'@
Patch $wit $invOld $invNew 'WorkflowInvariantTests: invariant 4 rewritten (RID publish must be locked on its own lock)'

$lockInvOld = @'
	/* ===================== 5. 锁定文件必须只有一份（不得出现 RID 专用 lock） ===================== */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void Workflows_DoNotReferenceRidSpecificLockFiles(string workflow)
	{
		string text = ReadWorkflow(workflow);
		foreach (string rid in new[] { "win-x64", "win-arm64", "linux-x64", "osx-x64" })
		{
			Assert.False(text.Contains($"packages.{rid}.lock.json", StringComparison.OrdinalIgnoreCase),
				$"{workflow} 不得引用 RID 专用 lock 文件（本项目只有一份 lock）：packages.{rid}.lock.json");
		}
	}
'@
$lockInvNew = @'
	/* ============ 5. 每 RID 一份 lock：被引用的那份必须真的存在（C 之后的取舍） ============ */

	[Theory]
	[InlineData("build.yml")]
	[InlineData("release.yml")]
	public void Workflows_OnlyReferenceRidLockFilesThatExist(string workflow)
	{
		string text = ReadWorkflow(workflow);
		// C：每 RID 一份 lock，发布时用 -p:NuGetLockFilePath 指过去。
		// 这里守的是"指了但文件不在"——那会让锁定还原变成"没有 lock 可锁"，比不锁定更危险，
		// 因为它看起来是锁定的。共享的 packages.lock.json 仍必须保持非 RID 形态（由第 1 组用例守着）。
		foreach (string rid in new[] { "win-x64", "linux-x64", "osx-x64" })
		{
			string referenced = $"packages.{rid}.lock.json";
			if (!text.Contains(referenced, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			Assert.True(File.Exists(Path.Combine(RepoRoot, "dev", "PineglowLauncher", referenced)),
				$"{workflow} 引用了 {referenced}，但仓库里没有这个文件：锁定还原会退化");
		}

		Assert.DoesNotContain("packages.win-arm64.lock.json", text, StringComparison.OrdinalIgnoreCase);
	}
'@
Patch $wit $lockInvOld $lockInvNew 'WorkflowInvariantTests: invariant 5 rewritten (referenced RID lock must exist)'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'c-patch-report.json') -Encoding utf8
Write-Output ("c-patch steps applied = {0}" -f $report.Count)
