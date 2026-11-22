# OPEN-04 第 4 层：全量测试 + 后台 CPU/IO 压力，检验时序假设
# 只用合成负载（本进程内线程占满核 + 临时目录反复读写），不触碰用户数据。
param(
	[string]$Worktree = 'D:/Projects/_worktrees/pg-open04-20261123',
	[int]$Rounds = 3,
	[int]$CpuWorkers = 8
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$outFile = Join-Path $Worktree '.cache\open04-layer4-stress.txt'
$lines = @(
	"# OPEN-04 layer 4: FULL suite + synthetic CPU/IO stress, $Rounds rounds",
	"# cwd=$Worktree",
	"# time=$((Get-Date).ToString('o'))",
	"# cpuWorkers=$CpuWorkers (synthetic busy loops in background jobs)",
	"# IO stress: repeated write+read of 8MB temp file in .cache/stress",
	"# purpose: amplify the WaitForExit(30000) timing risk documented in ANALYSIS.md",
	""
)

$stressDir = Join-Path $Worktree '.cache\stress'
New-Item -ItemType Directory -Force -Path $stressDir | Out-Null

# --- 合成 CPU 负载：纯计算忙循环，不调用任何外部工具 ---
$cpuJobs = @()
for ($i = 0; $i -lt $CpuWorkers; $i++) {
	$cpuJobs += Start-Job -ScriptBlock {
		$end = (Get-Date).AddMinutes(30)
		$x = 0.0
		while ((Get-Date) -lt $end) {
			for ($j = 0; $j -lt 200000; $j++) { $x += [Math]::Sqrt($j) }
			if ($x -gt 1e12) { $x = 0.0 }
		}
	}
}
# --- 合成 IO 负载 ---
$ioJob = Start-Job -ScriptBlock {
	param($dir)
	$end = (Get-Date).AddMinutes(30)
	$payload = New-Object byte[] (8 * 1024 * 1024)
	while ((Get-Date) -lt $end) {
		$p = Join-Path $dir ('stress-' + [Guid]::NewGuid().ToString('N') + '.bin')
		[IO.File]::WriteAllBytes($p, $payload)
		$null = [IO.File]::ReadAllBytes($p)
		Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
	}
} -ArgumentList $stressDir

$lines += "stress jobs started: cpu=$($cpuJobs.Count) io=1"

try {
	$env:PATH = 'C:\Windows\System32;C:\Windows;' + $env:PATH
	$env:TEMP = Join-Path $Worktree '.cache\tmp'
	$env:TMP = $env:TEMP
	New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null

	Push-Location $Worktree
	$pf = 0; $ff = 0
	for ($i = 1; $i -le $Rounds; $i++) {
		$sw = [Diagnostics.Stopwatch]::StartNew()
		$r = dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --no-build --nologo 2>&1 | Out-String
		$sw.Stop()
		$code = $LASTEXITCODE
		$f = [regex]::Match($r, '失败:\s*(\d+)')
		$p = [regex]::Match($r, '通过:\s*(\d+)')
		$s = [regex]::Match($r, '已跳过:\s*(\d+)')
		$t = [regex]::Match($r, '总计:\s*(\d+)')
		$lines += ("stress run {0}: exit={1} passed={2} failed={3} skipped={4} total={5} elapsed={6:N1}s" -f $i, $code, $p.Groups[1].Value, $f.Groups[1].Value, $s.Groups[1].Value, $t.Groups[1].Value, $sw.Elapsed.TotalSeconds)
		if ($code -eq 0) { $pf++ } else {
			$ff++
			$lines += "---- FAILED RUN: relevant output ----"
			# 只保留与本用例相关的失败块，避免证据文件过大
			$block = ($r -split "`n") | Where-Object { $_ -match 'WaitsForRunningTarget|waited=|state=|Assert|失败' }
			$lines += ($block -join "`n")
			$lines += "---- end ----"
		}
	}
	Pop-Location
	$lines += ""
	$lines += "summary: stress passes=$pf fails=$ff"
}
finally {
	foreach ($j in $cpuJobs) { Stop-Job $j -ErrorAction SilentlyContinue; Remove-Job $j -Force -ErrorAction SilentlyContinue }
	Stop-Job $ioJob -ErrorAction SilentlyContinue
	Remove-Job $ioJob -Force -ErrorAction SilentlyContinue
	Get-ChildItem -LiteralPath $stressDir -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
	$lines += "stress jobs stopped and temp files removed"
	[IO.File]::WriteAllLines($outFile, $lines, $utf8)
}

$lines | Select-Object -Last 16
