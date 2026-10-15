# seal-evidence.ps1 - write manifest.json for one evidence package, then a detached manifest.sha256.
# The manifest never lists itself (F-05 lesson: no self-reference).
param(
	[Parameter(Mandatory = $true)][string]$Package,
	[Parameter(Mandatory = $true)][string]$RunId,
	[string]$Title = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$exempt = @('manifest.json', 'manifest.sha256')
$records = New-Object System.Collections.Generic.List[object]
foreach ($file in Get-ChildItem -LiteralPath $Package -Recurse -File | Sort-Object FullName) {
	if ($exempt -contains $file.Name) { continue }
	$rel = $file.FullName.Substring($Package.Length).TrimStart('\') -replace '\\', '/'
	$records.Add([pscustomobject]@{
			path      = $rel
			sizeBytes = $file.Length
			sha256    = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
		})
}

$manifest = [pscustomobject]@{
	runId     = $RunId
	title     = $Title
	utc       = (Get-Date).ToUniversalTime().ToString('o')
	root      = $Package
	generator = 'FINAL-REMEDIATION/20261003-052200Z seal-evidence.ps1'
	count     = $records.Count
	files     = $records
}
$json = $manifest | ConvertTo-Json -Depth 5
$manifestPath = Join-Path $Package 'manifest.json'
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))

$sha = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
$shaPath = Join-Path $Package 'manifest.sha256'
[System.IO.File]::WriteAllText($shaPath, "$sha  manifest.json`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Output "sealed $($records.Count) files; manifest.json sha256=$sha"
