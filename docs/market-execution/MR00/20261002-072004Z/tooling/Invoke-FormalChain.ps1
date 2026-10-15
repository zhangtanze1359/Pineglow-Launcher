#Requires -Version 5.1
<#
.SYNOPSIS
    MR00 步骤 4：在 ExecutionRoot 内执行正式验证命令链，逐步记录退出码与测试数量。

.DESCRIPTION
    每一步都是独立进程、独立日志、独立退出码；任何一步非零都不会被后续步骤的成功覆盖。
    不使用 Start-Process -Wait（会等待 VBCSCompiler.exe 后代而挂死）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ExecutionRoot,
    [Parameter(Mandatory = $true)][string] $LogDir,
    [string] $StepTool
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if (-not $StepTool) { throw '必须传入 -StepTool' }
if (-not (Test-Path -LiteralPath $StepTool)) { throw "找不到步骤工具: $StepTool" }

$launcher = 'dev/PineglowLauncher/Pineglow.Launcher.csproj'
$tests    = 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj'
$verify   = 'dev/PineglowLauncher/VerifyHost/VerifyHost.csproj'

$steps = @(
    @{ n = '02-restore-launcher-locked'; c = "dotnet restore $launcher --locked-mode" },
    @{ n = '03-restore-tests-locked';    c = "dotnet restore $tests --locked-mode" },
    @{ n = '04-build-launcher-release';  c = "dotnet build $launcher -c Release --no-restore" },
    @{ n = '05-dotnet-test-release';     c = "dotnet test $tests -c Release --no-restore --logger `"console;verbosity=minimal`"" },
    @{ n = '06-build-verifyhost';        c = "dotnet build $verify -c Release" },
    @{ n = '07-publish-locked-probe';    c = "dotnet publish $launcher -c Release -r win-x64 --self-contained /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false -p:RestoreLockedMode=true" },
    @{ n = '08-publish-single-file';     c = "dotnet publish $launcher -c Release -r win-x64 --self-contained /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false" }
)

$results = New-Object System.Collections.Generic.List[object]
foreach ($s in $steps) {
    Write-Host ''
    Write-Host "---- $($s.n) ----" -ForegroundColor DarkCyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File $StepTool `
        -LogDir $LogDir -Name $s.n -Command $s.c -Cwd $ExecutionRoot
    $code = $LASTEXITCODE
    $j = Join-Path $LogDir "$($s.n).json"
    $meta = if (Test-Path $j) { Get-Content $j -Encoding UTF8 | ConvertFrom-Json } else { $null }
    $results.Add([pscustomobject]@{
        name = $s.n
        command = $s.c
        exitCode = $code
        logProduced = $(if ($meta) { $meta.logProduced } else { $null })
        verifierDefect = $(if ($meta) { $meta.verifierDefect } else { $null })
        durationSec = $(if ($meta) { $meta.durationSec } else { $null })
        csPassed = $(if ($meta) { $meta.csPassed } else { $null })
        csFailed = $(if ($meta) { $meta.csFailed } else { $null })
        csTotal = $(if ($meta) { $meta.csTotal } else { $null })
        buildErrors = $(if ($meta) { $meta.buildErrors } else { $null })
    })
    Write-Host "   => $($s.n) exit=$code"
}

Write-Host ''
Write-Host '================ 步骤汇总 ================' -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
($results | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $LogDir 'formal-steps.json') -Encoding UTF8
$nonZero = @($results | Where-Object { $_.exitCode -ne 0 })
Write-Host "步骤总数: $($results.Count)   非零: $($nonZero.Count)"
foreach ($x in $nonZero) { Write-Host "  [非零] $($x.name) exit=$($x.exitCode)" -ForegroundColor Yellow }
