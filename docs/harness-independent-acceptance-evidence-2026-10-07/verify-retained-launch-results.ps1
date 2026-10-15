param([Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference='Stop'
$report=Get-Content -LiteralPath (Join-Path $Evidence 'owned-launches.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$expected=@('Forge-1.20.1','NeoForge-1.21.1','Vanilla-1.18.2','Fabric-1.18.2','Quilt-1.20.1','Fabric-1.21.1')
if(@($report.results).Count -ne $expected.Count){throw 'Expected six observed launches'}
foreach($row in $report.results){
  $tag=$row.kind+'-'+$row.base
  if($tag -notin $expected){throw "Unexpected case: $tag"}
  $expected=@($expected | Where-Object {$_ -ne $tag})
  if($row.status -ne 'PASS' -or -not $row.logFresh -or -not $row.alive -or -not $row.javaCorrect -or $row.placeholderCount -ne 0){throw "Result failure: $tag"}
  $command=Get-Content -LiteralPath (Join-Path $Evidence ($tag+'-cmdline.txt')) -Raw -Encoding UTF8
  if($command -match '\$\{[A-Za-z_]+\}'){throw "Unresolved placeholder: $tag"}
  $lines=@(Get-Content -LiteralPath (Join-Path $Evidence ($tag+'-game.log')) -Encoding UTF8)
  if($lines.Count -ne $row.logLines){throw "Log count mismatch: $tag"}
  if(@($lines|Where-Object {$_ -match 'Backend library: LWJGL'}).Count -lt 1){throw "No renderer evidence: $tag"}
  if(@($lines|Where-Object {$_ -match 'Created: .*atlas'}).Count -lt 1){throw "No atlas evidence: $tag"}
  if(@($lines|Where-Object {$_ -match 'has crashed|UnsatisfiedLinkError|ResolutionException|InvalidModuleDescriptor|Failed to locate library'}).Count -gt 0){throw "Crash marker: $tag"}
}
if($expected.Count -ne 0){throw 'Missing case'}
$cleanup=Get-Content -LiteralPath (Join-Path $Evidence 'process-cleanup-final.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if(@($cleanup.remainingOwnedProcesses).Count -ne 0){throw 'Owned child process remains'}
Write-Output 'Retained evidence validation PASS: six structured launch results match fresh retained logs and commands; owned processes cleaned. This does not replace the original launch wrapper exit code.'
exit 0
