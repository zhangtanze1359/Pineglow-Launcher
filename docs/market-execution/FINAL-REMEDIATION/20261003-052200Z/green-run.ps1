# green-run.ps1 - rebuild the post-fix isolated copy and run every required GREEN check.
# NOTE: this file is deliberately ASCII-only (the local PowerShell parses BOM-less UTF-8 scripts as ANSI).
param(
	[Parameter(Mandatory = $true)][string]$SourceRoot,
	[Parameter(Mandatory = $true)][string]$RunDir,
	[Parameter(Mandatory = $true)][string]$Dest,
	[Parameter(Mandatory = $true)][string]$ProbeFinal,
	[Parameter(Mandatory = $true)][string]$ProbeIndependent,
	[switch]$Full
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# The two independent-acceptance probe files exist only in the delivery root (not in the source
# tree, not in git). They are passed as explicit extra inputs so that green-snapshot-manifest.json
# hash-binds them too, instead of "copied once and then unmanaged".
$extraInputs = @(
	"$ProbeFinal|dev/PineglowLauncher.Tests/FinalAcceptanceProbes.cs",
	"$ProbeIndependent|dev/PineglowLauncher.Tests/IndependentRouteProbes.cs"
)

& "$RunDir\make-snapshot.ps1" -SourceRoot $SourceRoot -Dest $Dest -ManifestPath "$RunDir\green-snapshot-manifest.json" -ExtraInputs $extraInputs
$Dest | Out-File -LiteralPath "$RunDir\green-copy-root.txt" -Encoding utf8

foreach ($probe in @("$Dest\dev\PineglowLauncher.Tests\FinalAcceptanceProbes.cs", "$Dest\dev\PineglowLauncher.Tests\IndependentRouteProbes.cs")) {
	if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw "probe file missing in copy: $probe" }
}
Write-Output "probes in copy: FinalAcceptanceProbes.cs + IndependentRouteProbes.cs"

& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-r0-secrets' -Filter 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests'
& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-r1-r3-focused' -NoBuild -NoRestore -Filter 'FullyQualifiedName~FileRecovery|FullyQualifiedName~ModReplacement|FullyQualifiedName~WorldBackupScope|FullyQualifiedName~FinalAcceptanceRegressionTests'
& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-independent-probes' -NoBuild -NoRestore -Filter 'FullyQualifiedName~FinalAcceptanceProbes|FullyQualifiedName~IndependentRouteProbes'

if ($Full) {
	# The full entry point deliberately does NOT use --no-build: the suite must run against the
	# binary that was just built from the hash-bound snapshot.
	& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-full-csharp-final'
}
