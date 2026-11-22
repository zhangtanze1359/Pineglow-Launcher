param([switch]$StressOnly)
$ErrorActionPreference = 'Stop'
$raw = $PSScriptRoot
$tree = 'D:\Projects\_worktrees\pineglow-open04-review-r4'
Set-Location -LiteralPath $tree
$env:PATH = 'C:\Windows\System32;C:\Windows;' + $env:PATH
$env:TEMP = Join-Path $tree '.review-temp'
$env:TMP = $env:TEMP
$project = 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj'

function Invoke-ReviewRun {
    param([string]$Name, [string]$Filter, [int]$Expected)
    $argsForTest = @('test',$project,'-c','Release','--no-build','--nologo','--results-directory',$raw,'--logger',"trx;LogFileName=$Name.trx",'--logger','console;verbosity=detailed')
    if ($Filter) { $argsForTest += @('--filter', "FullyQualifiedName~$Filter") }
    $started = Get-Date
    $log = Join-Path $raw "$Name.txt"
    [pscustomobject]@{
        run = $Name; command = 'dotnet ' + (($argsForTest | ForEach-Object { '"' + $_ + '"' }) -join ' ')
        cwd = (Get-Location).Path; start = $started.ToString('o'); expected = $Expected
        pathPrefix = 'C:\Windows\System32;C:\Windows'; temp = $env:TEMP
    } | ConvertTo-Json -Compress | Add-Content -Encoding UTF8 -LiteralPath (Join-Path $raw 'commands.jsonl')
    & dotnet @argsForTest > $log
    $exitCode = $LASTEXITCODE
    $finished = Get-Date
    $trx = Join-Path $raw "$Name.trx"
    $count = $null
    if (Test-Path -LiteralPath $trx) {
        [xml]$xml = Get-Content -Raw -LiteralPath $trx
        $count = $xml.TestRun.ResultSummary.Counters
    }
    $match = @(Select-String -LiteralPath $log -Pattern '\[R06/upgrade-wait\] state=')
    $timing = ($match | ForEach-Object { $_.Line.Trim() }) -join ' | '
    $row = [pscustomobject]@{
        run = $Name; expected = $Expected; exit = $exitCode; passed = $count.passed
        failed = $count.failed; skipped = $count.notExecuted; total = $count.total
        timing = $timing; start = $started.ToString('o'); finish = $finished.ToString('o')
        seconds = [Math]::Round(($finished - $started).TotalSeconds,3)
    }
    $row | Export-Csv -NoTypeInformation -Encoding UTF8 -Append -LiteralPath (Join-Path $raw 'runs.csv')
    $row | ConvertTo-Json -Compress
}

if ($StressOnly) {
    Invoke-ReviewRun -Name 'full-stress-1' -Expected 1502
    return
}

for ($i = 1; $i -le 10; $i++) {
    Invoke-ReviewRun -Name ('target-{0:D2}' -f $i) -Filter 'ApplyScript_WaitsForRunningTargetThenReplaces' -Expected 1
}
for ($i = 1; $i -le 3; $i++) {
    Invoke-ReviewRun -Name "paths-$i" -Filter 'UpdateApplyScriptPathTests' -Expected 9
    Invoke-ReviewRun -Name "concurrency-$i" -Filter 'UpdateConcurrencyTests' -Expected 8
}
for ($i = 1; $i -le 2; $i++) {
    Invoke-ReviewRun -Name "full-$i" -Expected 1502
}


