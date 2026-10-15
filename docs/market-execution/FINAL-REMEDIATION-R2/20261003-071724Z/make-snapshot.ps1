# make-snapshot.ps1 - build a hash-bound isolated copy from the explicit input whitelist
# (working-tree versions of git ls-files + scripts/source-snapshot-inputs.txt). No git mutation.
# NOTE: this file is deliberately ASCII-only (the local PowerShell parses BOM-less UTF-8 scripts as ANSI).
param(
	[Parameter(Mandatory = $true)][string]$SourceRoot,
	[Parameter(Mandatory = $true)][string]$Dest,
	[Parameter(Mandatory = $true)][string]$ManifestPath,
	# Extra inputs as "absolute source|relative path inside the snapshot" (files that live in the
	# delivery root, not in the source tree, e.g. the independent-acceptance probe files).
	[string[]]$ExtraInputs = @()
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$tracked = @(& git -C $SourceRoot ls-files)
if ($LASTEXITCODE -ne 0) { throw "git ls-files failed" }

$whitelistFile = Join-Path $SourceRoot 'scripts\source-snapshot-inputs.txt'
$extra = @(Get-Content -LiteralPath $whitelistFile -Encoding UTF8 |
	Where-Object { $_ -and -not $_.StartsWith('#') } |
	ForEach-Object { $_.Trim() } |
	Where-Object { $_ })

$all = @($tracked + $extra | Sort-Object -Unique)
if (Test-Path -LiteralPath $Dest) { Remove-Item -LiteralPath $Dest -Recurse -Force }
New-Item -ItemType Directory -Path $Dest -Force | Out-Null

$extraRels = New-Object System.Collections.Generic.List[string]
foreach ($item in $ExtraInputs) {
	$parts = $item.Split('|')
	if ($parts.Count -ne 2) { throw "ExtraInputs needs 'absolute-source|relative-path': $item" }
	$srcExtra = $parts[0]
	$relExtra = ($parts[1] -replace '\\', '/')
	if (-not (Test-Path -LiteralPath $srcExtra -PathType Leaf)) { throw "extra input not found: $srcExtra" }
	$dstExtra = Join-Path $Dest ($relExtra -replace '/', '\')
	$dstExtraDir = Split-Path -Parent $dstExtra
	if (-not (Test-Path -LiteralPath $dstExtraDir)) { New-Item -ItemType Directory -Path $dstExtraDir -Force | Out-Null }
	Copy-Item -LiteralPath $srcExtra -Destination $dstExtra -Force
	if (-not (Test-Path -LiteralPath $dstExtra -PathType Leaf)) { throw "extra input copy failed: $srcExtra -> $dstExtra" }
	$extraRels.Add($relExtra)
	$all += $relExtra
}
$all = @($all | Sort-Object -Unique)

$records = New-Object System.Collections.Generic.List[object]
$missing = New-Object System.Collections.Generic.List[string]
foreach ($rel in $all) {
	if ($extraRels -contains $rel) {
		# already copied from the delivery root; only record its hash (it does not exist under SourceRoot)
		$fiExtra = Get-Item -LiteralPath (Join-Path $Dest ($rel -replace '/', '\'))
		$records.Add([pscustomobject]@{
				path = $rel; sizeBytes = $fiExtra.Length
				sha256 = (Get-FileHash -LiteralPath $fiExtra.FullName -Algorithm SHA256).Hash
				source = 'delivery-root extra input'
			})
		continue
	}
	$src = Join-Path $SourceRoot ($rel -replace '/', '\')
	if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { $missing.Add($rel); continue }
	$dst = Join-Path $Dest ($rel -replace '/', '\')
	$dstDir = Split-Path -Parent $dst
	if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
	Copy-Item -LiteralPath $src -Destination $dst -Force
	$fi = Get-Item -LiteralPath $dst
	$sha = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
	$records.Add([pscustomobject]@{ path = $rel; sizeBytes = $fi.Length; sha256 = $sha })
}

$manifest = [pscustomobject]@{
	utc          = (Get-Date).ToUniversalTime().ToString('o')
	sourceRoot   = $SourceRoot
	snapshotRoot = $Dest
	tracked      = $tracked.Count
	extra        = $extra.Count
	extraInputs  = @($extraRels)
	count        = $records.Count
	missing      = @($missing)
	files        = $records
}
$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($ManifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "snapshot=$Dest files=$($records.Count) tracked=$($tracked.Count) extra=$($extra.Count) extraInputs=$($extraRels.Count) missing=$($missing.Count)"
if ($missing.Count -gt 0) { Write-Output ("missing: " + ($missing -join ', ')) }
