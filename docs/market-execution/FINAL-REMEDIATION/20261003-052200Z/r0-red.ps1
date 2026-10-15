# r0-red.ps1 - R0 RED evidence: reproduce the pre-fix credential-intent leak in an ISOLATED copy.
#
# Method (identical to the independent acceptance's credential proof):
#   * in the copy ONLY, patch SecretStore.Load/Save so they log the intended operation and
#     return BEFORE reaching CredReadW/CredWriteW/CredDeleteW -- real credentials are never touched;
#   * run the 5 ConfigPreservationTests with PINEGLOW_ACCEPTANCE_SECRET_EVENTS pointing at a log;
#   * count the delete-intents / read-stubs.
param(
	[Parameter(Mandatory = $true)][string]$Root,
	[Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $OutDir 'test-results') -Force | Out-Null

$source = Join-Path $Root 'dev\PineglowLauncher\Pineglow.Launcher.Services\SecretStore.cs'
$text = [System.IO.File]::ReadAllText($source)

$read = "`n`t`tFile.AppendAllText(Environment.GetEnvironmentVariable(""PINEGLOW_ACCEPTANCE_SECRET_EVENTS"")!, JsonSerializer.Serialize(new { name, operation = ""read-stub"" }) + Environment.NewLine);`n`t`treturn string.Empty;"
$write = "`n`t`tFile.AppendAllText(Environment.GetEnvironmentVariable(""PINEGLOW_ACCEPTANCE_SECRET_EVENTS"")!, JsonSerializer.Serialize(new { name, operation = string.IsNullOrEmpty(value) ? ""delete-intent"" : ""write-intent"" }) + Environment.NewLine);`n`t`treturn;"

$patternLoad = '(public static string Load\(string name\)\s*\{\s*ValidateName\(name\);)'
$patternSave = '(public static void Save\(string name, string\? value\)\s*\{\s*ValidateName\(name\);)'

$text = [regex]::Replace($text, $patternLoad, { param($m) $m.Groups[1].Value + $read }, 1)
$text = [regex]::Replace($text, $patternSave, { param($m) $m.Groups[1].Value + $write }, 1)

if ($text -notmatch 'read-stub' -or $text -notmatch 'delete-intent') {
	throw 'instrumentation failed: markers not found in patched source'
}

[System.IO.File]::WriteAllText($source, $text, (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText((Join-Path $OutDir 'SecretStore.instrumented-copy.cs'), $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Output 'instrumented isolated copy; native credential reads/writes bypassed'

$events = Join-Path $OutDir 'credential-intents.jsonl'
if (Test-Path $events) { Remove-Item -LiteralPath $events -Force }
$env:PINEGLOW_ACCEPTANCE_SECRET_EVENTS = $events

Set-Location -LiteralPath $Root
$argv = @('test', '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj', '-c', 'Release',
	'--filter', 'FullyQualifiedName~ConfigPreservationTests',
	'--logger', 'trx;LogFileName=red-credential-proof.trx',
	'--results-directory', (Join-Path $OutDir 'test-results'))
$log = Join-Path $OutDir 'red-credential-proof.log'
$start = (Get-Date).ToUniversalTime()
@("# cwd: $Root", "# command: dotnet $($argv -join ' ')", "# utcStart: $($start.ToString('o'))") |
	Out-File -LiteralPath $log -Encoding utf8

& dotnet @argv 2>&1 | Out-File -LiteralPath $log -Append -Encoding utf8
$code = $LASTEXITCODE
$end = (Get-Date).ToUniversalTime()
@("# utcEnd: $($end.ToString('o'))", "# exitCode: $code") | Out-File -LiteralPath $log -Append -Encoding utf8

$deleteIntents = 0
$writeIntents = 0
$readStubs = 0
if (Test-Path $events) {
	$lines = Get-Content -LiteralPath $events
	$deleteIntents = ($lines | Select-String -SimpleMatch 'delete-intent').Count
	$writeIntents = ($lines | Select-String -SimpleMatch 'write-intent').Count
	$readStubs = ($lines | Select-String -SimpleMatch 'read-stub').Count
}

$summary = [pscustomobject]@{
	utc              = $end.ToString('o')
	root             = $Root
	exitCode         = $code
	deleteIntents    = $deleteIntents
	writeIntents     = $writeIntents
	readStubs        = $readStubs
	eventLines       = if (Test-Path $events) { (Get-Content -LiteralPath $events).Count } else { 0 }
}
$summary | ConvertTo-Json -Depth 3 | Out-File -LiteralPath (Join-Path $OutDir 'red-summary.json') -Encoding utf8
Write-Output ("R0 RED: exit=$code delete-intent=$deleteIntents write-intent=$writeIntents read-stub=$readStubs")
