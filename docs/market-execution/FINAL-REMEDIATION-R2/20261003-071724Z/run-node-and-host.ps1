# run-node-and-host.ps1 - Node front-end tests, Release VerifyHost build, and the Bisect smoke run.
param(
	[Parameter(Mandatory = $true)][string]$Root,
	[Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
Set-Location -LiteralPath $Root

function Invoke-Recorded {
	param([string]$Name, [string[]]$Argv, [string]$CommandText)
	$log = Join-Path $OutDir "$Name.log"
	$start = (Get-Date).ToUniversalTime()
	@("# cwd: $Root", "# command: $CommandText", "# utcStart: $($start.ToString('o'))") |
		Out-File -LiteralPath $log -Encoding utf8
	& $Argv[0] @($Argv[1..($Argv.Count - 1)]) 2>&1 | Out-File -LiteralPath $log -Append -Encoding utf8
	$code = $LASTEXITCODE
	$end = (Get-Date).ToUniversalTime()
	@("# utcEnd: $($end.ToString('o'))", "# exitCode: $code") | Out-File -LiteralPath $log -Append -Encoding utf8
	Write-Output "$Name exit=$code elapsed=$([math]::Round(($end - $start).TotalSeconds,1))s"
	return $code
}

$nodeInputs = @(Get-ChildItem -LiteralPath (Join-Path $Root 'ui\tests') -Filter '*.test.cjs' |
	Select-Object -ExpandProperty FullName)
$nodeInputs | Out-File -LiteralPath (Join-Path $OutDir 'node-inputs.txt') -Encoding utf8
$nodeArgv = @('node', '--test') + $nodeInputs
Invoke-Recorded -Name 'green-node' -Argv $nodeArgv -CommandText ('node --test <' + $nodeInputs.Count + ' files from ui/tests/*.test.cjs>') | Out-Null

Invoke-Recorded -Name 'green-verifyhost-build' `
	-Argv @('dotnet', 'build', '.\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj', '-c', 'Release') `
	-CommandText 'dotnet build .\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj -c Release' | Out-Null

$exe = Join-Path $Root 'dev\PineglowLauncher\VerifyHost\bin\Release\net10.0\VerifyHost.exe'
Invoke-Recorded -Name 'green-bisect-smoke' -Argv @($exe, '--bisect-smoke') -CommandText "$exe --bisect-smoke" | Out-Null
