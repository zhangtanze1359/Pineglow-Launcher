# capture-baseline.ps1 - read-only baseline of the source root (HEAD, worktree status, allowed-file SHA256).
param(
	[Parameter(Mandatory = $true)][string]$SourceRoot,
	[Parameter(Mandatory = $true)][string]$OutDir,
	[Parameter(Mandatory = $true)][string]$Tag
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$allowed = @(
	'dev/PineglowLauncher/Pineglow.Launcher.Services/SecretStore.cs',
	'dev/PineglowLauncher/Pineglow.Launcher.Services/FileRecoveryService.cs',
	'dev/PineglowLauncher/Pineglow.Launcher.Services/ModReplacementService.cs',
	'dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs',
	'dev/PineglowLauncher/Pineglow.Launcher.Services/ConfigStore.cs',
	'dev/PineglowLauncher.Tests/ConfigPreservationTests.cs',
	'dev/PineglowLauncher.Tests/FileRecoveryTests.cs',
	'dev/PineglowLauncher.Tests/ModReplacementTests.cs',
	'dev/PineglowLauncher.Tests/WorldBackupScopeTests.cs',
	'scripts/source-snapshot-inputs.txt'
)

$head = (& git -C $SourceRoot rev-parse HEAD).Trim()
$status = @(& git -C $SourceRoot status --short --untracked-files=all)
$statusText = ($status -join "`n")
[System.IO.File]::WriteAllText((Join-Path $OutDir "source-status-$Tag.txt"), $statusText, (New-Object System.Text.UTF8Encoding($false)))

$files = New-Object System.Collections.Generic.List[object]
foreach ($rel in $allowed) {
	$p = Join-Path $SourceRoot ($rel -replace '/', '\')
	if (-not (Test-Path -LiteralPath $p -PathType Leaf)) {
		$files.Add([pscustomobject]@{ path = $rel; exists = $false; sizeBytes = $null; sha256 = $null })
		continue
	}
	$fi = Get-Item -LiteralPath $p
	$files.Add([pscustomobject]@{
			path = $rel; exists = $true; sizeBytes = $fi.Length
			sha256 = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash
		})
}

$doc = [pscustomobject]@{
	utc               = (Get-Date).ToUniversalTime().ToString('o')
	sourceRoot        = $SourceRoot
	head              = $head
	statusLineCount   = ($status | Where-Object { $_ }).Count
	trackedFileCount  = @(& git -C $SourceRoot ls-files).Count
	allowedFileHashes = $files
}
$json = $doc | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $OutDir "source-$Tag.json"), $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "head=$head statusLines=$($doc.statusLineCount) tracked=$($doc.trackedFileCount)"
