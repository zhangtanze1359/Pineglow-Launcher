# invoke-test.ps1 - run one recorded dotnet test invocation inside an isolated copy.
param(
	[Parameter(Mandatory = $true)][string]$Root,
	[Parameter(Mandatory = $true)][string]$OutDir,
	[Parameter(Mandatory = $true)][string]$Name,
	[string]$Filter = '',
	[switch]$NoBuild,
	[switch]$NoRestore
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Set-Location -LiteralPath $Root
New-Item -ItemType Directory -Path (Join-Path $OutDir 'test-results') -Force | Out-Null

$argv = @('test', '.\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj', '-c', 'Release',
	'--logger', "trx;LogFileName=$Name.trx", '--results-directory', (Join-Path $OutDir 'test-results'))
if ($Filter) { $argv += @('--filter', $Filter) }
if ($NoBuild) { $argv += '--no-build' }
if ($NoRestore) { $argv += '--no-restore' }

$log = Join-Path $OutDir "$Name.log"
$commandText = 'dotnet ' + ($argv -join ' ')
$start = (Get-Date).ToUniversalTime()
@("# cwd: $Root", "# command: $commandText", "# utcStart: $($start.ToString('o'))") |
	Out-File -LiteralPath $log -Encoding utf8

& dotnet @argv 2>&1 | Out-File -LiteralPath $log -Append -Encoding utf8
$code = $LASTEXITCODE
$end = (Get-Date).ToUniversalTime()
@("# utcEnd: $($end.ToString('o'))", "# exitCode: $code", "# elapsedSeconds: $([math]::Round(($end - $start).TotalSeconds, 1))") |
	Out-File -LiteralPath $log -Append -Encoding utf8
Write-Output "$Name exit=$code elapsed=$([math]::Round(($end - $start).TotalSeconds,1))s log=$log"
