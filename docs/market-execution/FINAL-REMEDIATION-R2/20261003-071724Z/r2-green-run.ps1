# r2-green-run.ps1 - rebuild the R2 isolated copy and run every required check (S1-S4 verification).
# ASCII-only on purpose (this host parses BOM-less UTF-8 scripts as ANSI).
param(
	[Parameter(Mandatory = $true)][string]$SourceRoot,
	[Parameter(Mandatory = $true)][string]$RunDir,
	[Parameter(Mandatory = $true)][string]$Dest,
	[Parameter(Mandatory = $true)][string]$ProbeBoundary,
	[Parameter(Mandatory = $true)][string]$ProbeFinal,
	[Parameter(Mandatory = $true)][string]$ProbeIndependent,
	[switch]$Full
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$extraInputs = @(
	"$ProbeBoundary|dev/PineglowLauncher.Tests/RemediationBoundaryProbes.cs",
	"$ProbeFinal|dev/PineglowLauncher.Tests/FinalAcceptanceProbes.cs",
	"$ProbeIndependent|dev/PineglowLauncher.Tests/IndependentRouteProbes.cs"
)

& "$RunDir\make-snapshot.ps1" -SourceRoot $SourceRoot -Dest $Dest -ManifestPath "$RunDir\green-snapshot-manifest.json" -ExtraInputs $extraInputs
$Dest | Out-File -LiteralPath "$RunDir\green-copy-root.txt" -Encoding utf8

foreach ($probe in @(
		"$Dest\dev\PineglowLauncher.Tests\RemediationBoundaryProbes.cs",
		"$Dest\dev\PineglowLauncher.Tests\FinalAcceptanceProbes.cs",
		"$Dest\dev\PineglowLauncher.Tests\IndependentRouteProbes.cs")) {
	if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw "probe file missing in copy: $probe" }
}

# 1) 上一轮独立复验的 5 个边界探针（RED: 1 pass / 4 fail）
& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-boundary-probes' -Filter 'FullyQualifiedName~RemediationBoundaryProbes'
# 2) 本轮常驻回归（15）
& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-r2-regressions' -NoBuild -NoRestore -Filter 'FullyQualifiedName~RemediationBoundaryRegressionTests'
# 3) R0 凭据隔离专项（13）
& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-r0-secrets' -NoBuild -NoRestore -Filter 'FullyQualifiedName~ConfigPreservationTests|FullyQualifiedName~SecretStoreBackendTests'
# 4) 前轮 20 验收探针 + 12 常驻回归（32）
& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-existing-probes' -NoBuild -NoRestore -Filter 'FullyQualifiedName~FinalAcceptanceProbes|FullyQualifiedName~IndependentRouteProbes|FullyQualifiedName~FinalAcceptanceRegressionTests'

if ($Full) {
	& "$RunDir\invoke-test.ps1" -Root $Dest -OutDir "$RunDir\green" -Name 'green-full-csharp'
}
