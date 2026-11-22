$ErrorActionPreference = 'Stop'
$oldRaw = 'docs/evidence/2026-11-23-open04/raw'
$patterns = [ordered]@{
  target = @('open04-layer1.txt', 1, 10)
  paths = @('open04-layer2.txt', 9, 5)
  concurrency = @('open04-layer2.txt', 8, 5)
  full = @('open04-layer3-full.txt', 1502, 3)
  stress = @('open04-layer4-stress.txt', 1502, 3)
}
$matches = @{}
$total = 0
Get-Date -Format o
(Get-Location).Path
git rev-parse HEAD
foreach ($group in $patterns.Keys) {
  $file, $count, $expectedRounds = $patterns[$group]
  $rows = @(Select-String -LiteralPath "$oldRaw/$file" -Pattern "exit=0 passed=$count failed=0 skipped=0 total=$count\b")
  if ($rows.Count -ne $expectedRounds) { throw "$group count=$($rows.Count), expected=$expectedRounds" }
  $matches[$group] = $rows
  $total += $rows.Count * $count
  Write-Output "$group count=$($rows.Count) per_round=$count subtotal=$($rows.Count * $count)"
  $rows | ForEach-Object { Write-Output "$($_.LineNumber):$($_.Line.Trim())" }
}
$fullMean = ($matches.full | ForEach-Object { [double]([regex]::Match($_.Line,'elapsed=([0-9.]+)s').Groups[1].Value) } | Measure-Object -Average).Average
$stressMean = ($matches.stress | ForEach-Object { [double]([regex]::Match($_.Line,'elapsed=([0-9.]+)s').Groups[1].Value) } | Measure-Object -Average).Average
$timeOnly = @(Select-String -LiteralPath "$oldRaw/open04-timing-waited.txt" -Pattern 'exit=0 passed= failed= state=applied')
if ($timeOnly.Count -ne 8 -or $total -ne 9107) { throw 'Historical evidence totals mismatch' }
Write-Output "total=$total; baseline_mean=$fullMean; stress_mean=$stressMean; elapsed_increase_percent=$([Math]::Round(100*($stressMean-$fullMean)/$fullMean, 7)); timing_only_excluded=$($timeOnly.Count)"

