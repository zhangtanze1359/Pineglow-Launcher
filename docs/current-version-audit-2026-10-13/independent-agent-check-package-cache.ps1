$ErrorActionPreference = 'Stop'
$sdkPath = 'C:\Program Files\dotnet\sdk\10.0.301'
foreach ($dll in @('NuGet.Common.dll','NuGet.Versioning.dll','NuGet.Frameworks.dll','NuGet.Packaging.dll')) {
    Add-Type -Path (Join-Path $sdkPath $dll)
}
$pkgMap = @{}
foreach ($lockRel in @('dev/PineglowLauncher/packages.lock.json','dev/PineglowLauncher.Tests/packages.lock.json')) {
    $lock = Get-Content -LiteralPath $lockRel -Raw | ConvertFrom-Json -AsHashtable
    foreach ($entry in $lock.dependencies['net10.0'].GetEnumerator()) {
        if ($entry.Value.type -ne 'Project') { $pkgMap[$entry.Key.ToLower()+'/'+$entry.Value.resolved] = $entry.Value.contentHash }
    }
}
$checks = @()
foreach ($id in @($pkgMap.Keys | Sort-Object)) {
    $parts = $id.Split('/')
    $dir = Join-Path (Join-Path $env:USERPROFILE '.nuget/packages') $id
    $archive = Join-Path $dir ($parts[0]+'.'+$parts[1]+'.nupkg')
    $metadata = Get-Content -LiteralPath (Join-Path $dir '.nupkg.metadata') -Raw | ConvertFrom-Json
    $rawHash = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($archive)))
    $shaFile = (Get-Content -LiteralPath ($archive+'.sha512') -Raw).Trim()
    $reader = [NuGet.Packaging.PackageArchiveReader]::new($archive)
    try {
        $normalized = $reader.GetContentHash([Threading.CancellationToken]::None)
        $signature = $reader.GetPrimarySignatureAsync([Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $null = $reader.ValidateIntegrityAsync($signature.SignatureContent, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $signature.SignedCms.CheckSignature($true)
        $checks += [pscustomobject]@{
            id = $id
            normalizedContentHashMatches = ($normalized -eq $pkgMap[$id])
            metadataContentHashMatches = ($metadata.contentHash -eq $pkgMap[$id])
            rawShaFileMatches = ($rawHash -eq $shaFile)
            signatureHashIntegrity = $true
            cmsCryptographicSignatureValid = $true
        }
    } finally { $reader.Dispose() }
}
if ($checks.Count -ne $pkgMap.Count -or $checks.Count -eq 0) { throw 'Package count gate failed' }
$bad = @($checks | Where-Object { -not $_.normalizedContentHashMatches -or -not $_.metadataContentHashMatches -or -not $_.rawShaFileMatches })
[ordered]@{
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    count = $checks.Count
    failures = $bad.Count
    scope = 'NuGet SDK GetContentHash compares signature-normalized contentHash to source lock; ValidateIntegrityAsync validates archive vs signed hash; SignedCms.CheckSignature(true) verifies cryptographic signature without live revocation or certificate trust check; raw archive hash compared only with .nupkg.sha512'
    nugetSource = 'https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs'
    checks = $checks
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'independent-agent-package-cache-normalized-integrity.json') -Encoding utf8
Write-Output "PackageCount=$($checks.Count);Failures=$($bad.Count)"
if ($bad.Count -gt 0) { exit 1 }
