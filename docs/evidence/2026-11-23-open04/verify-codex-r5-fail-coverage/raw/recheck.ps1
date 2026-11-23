$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$mainRoot = 'D:/Projects/松光启动器'
$before = '63dadf7'
$candidate = '453f18f'
$evidence = 'docs/evidence/2026-11-23-open04'
$r4 = "$evidence/verify-codex-r4-fail-wording/raw"
$pathTests = 'dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs'
$recoveryTests = 'dev/PineglowLauncher.Tests/UpdateRecoveryTests.cs'
$service = 'dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs'
$workflow = '.github/workflows/build.yml'
$handoff = 'docs/handoff/HANDOFF-2026-11-23-open04-execution.md'

Get-Date -Format o
Get-Location
git rev-parse --show-toplevel
git rev-parse HEAD
git status --short --untracked-files=all
"git -C '$mainRoot' diff --exit-code $before $candidate -- dev/ scripts/test-inventory.txt $workflow global.json"
git -C $mainRoot diff --exit-code $before $candidate -- dev/ scripts/test-inventory.txt $workflow global.json
"exit=$LASTEXITCODE"
"git -C '$mainRoot' -c core.quotePath=false diff --name-only $before $candidate"
$changed = @(git -C $mainRoot -c core.quotePath=false diff --name-only $before $candidate)
"exit=$LASTEXITCODE"
$changed
$docCount = @($changed | Where-Object { $_ -like 'docs/*' }).Count
"paths=$($changed.Count) docs=$docCount other=$($changed.Count - $docCount)"
git -C $mainRoot rev-parse HEAD

"rg -n <six literal patterns> README.md SELF-AUDIT.md execution-handoff.md docs/open-items.md"
rg -n '残留 Job|0 个临时文件|压力最高约|8 核忙循环|属并发写|压力生效' "$evidence/README.md" "$evidence/SELF-AUDIT.md" $handoff docs/open-items.md
"exit=$LASTEXITCODE"
rg -n '^' "$evidence/raw/open04-layer4-stress.txt"
rg -n -A 7 '^finally' "$evidence/layer4-stress.ps1"

"Get-FileHash -Algorithm SHA256 $pathTests $service"
Get-FileHash -Algorithm SHA256 $pathTests,$service | Format-List Hash,Path
rg -n 'tasklist.exe|ping.exe|fc.exe|MaxRunningProcessWaitIterations' $service
rg -n 'OperatingSystem.IsWindows\(\)|\[Fact\]|RunApplyScript\(|WaitForExit\(30000\)|ReadToEnd' $pathTests
rg -n 'RunApplyScript\(|RunApplyScriptCore\(|WaitForExit\(30000\)|ReadToEnd' $recoveryTests
rg -n -A 3 'Run tests \(windows \+ ubuntu\)' --hidden $workflow

"Recount counted execution runs from the four original summaries; exclude the timing-only log"
$expectedRuns = @(10, 10, 3, 3)
$layerLogs = @('open04-layer1.txt','open04-layer2.txt','open04-layer3-full.txt','open04-layer4-stress.txt')
$allPassed = 0
$allFailed = 0
$allSkipped = 0
$allTotal = 0
$fullMeans = @()
for ($layer = 0; $layer -lt $layerLogs.Count; $layer++) {
    $file = $layerLogs[$layer]
    $rows = @(
        Select-String -LiteralPath "$evidence/raw/$file" -Pattern 'exit=(\d+) passed=(\d+) failed=(\d+) skipped=(\d+) total=(\d+)(?: elapsed=([\d.]+)s)?' |
            ForEach-Object { $_.Matches[0] }
    )
    if ($rows.Count -ne $expectedRuns[$layer]) { throw "Unexpected row count in $file" }
    foreach ($group in ($rows | Group-Object { $_.Groups[2].Value })) {
        "$file passed_each=$($group.Name) runs=$($group.Count)"
    }
    foreach ($row in $rows) {
        $exit,$passed,$failed,$skipped,$total = $row.Groups[1..5].Value | ForEach-Object { [int]$_ }
        if ($exit -ne 0 -or $passed -ne $total -or $failed -ne 0 -or $skipped -ne 0) {
            throw "Unexpected original execution row: $row"
        }
        $allPassed += $passed
        $allFailed += $failed
        $allSkipped += $skipped
        $allTotal += $total
    }
    if ($layer -ge 2) {
        $durations = @($rows | ForEach-Object { [decimal]::Parse($_.Groups[6].Value, [cultureinfo]::InvariantCulture) })
        $mean = ($durations | Measure-Object -Average).Average
        "$file elapsed_seconds=$($durations -join ',') average=$mean"
        $fullMeans += $mean
    }
}
"old_run_total passed=$allPassed failed=$allFailed skipped=$allSkipped total=$allTotal"
"time_delta_percent={0:F6}" -f (($fullMeans[1] - $fullMeans[0]) / $fullMeans[0] * 100)
rg -n 'passed= failed=' "$evidence/raw/open04-timing-waited.txt"

"python $r4/audit-trx.py"
python "$r4/audit-trx.py"
$auditExit = $LASTEXITCODE
"exit=$auditExit"
if ($auditExit -ne 0) { throw 'Archived TRX verification failed' }
rg -n 'R06/upgrade-wait|R06/upgrade-rollback|R06/upgrade-backupfail|R06/upgrade-deny-ace' --encoding utf-16le "$r4/full-stress-1.txt"
Get-Date -Format o
