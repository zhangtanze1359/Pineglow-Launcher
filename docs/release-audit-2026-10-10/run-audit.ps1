param([string]$SourceRoot='D:\松光启动器')
$ErrorActionPreference='Stop'
$evidence=$PSScriptRoot
$recovery='C:\Users\Administrator\Documents\Codex\2026-10-10\松光启动器恢复'
$manifest=Get-Content -LiteralPath (Join-Path $recovery 'delivered-source-verification.json') -Raw | ConvertFrom-Json
$snapshot=Join-Path ([IO.Path]::GetTempPath()) ('pineglow-release-audit-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $snapshot)
$before=@()
foreach($item in $manifest){
  $src=Join-Path $SourceRoot $item.path
  $hash=(Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
  $before += [pscustomobject]@{path=$item.path;expected=$item.expected;actual=$hash;exact=($hash -eq $item.expected)}
  $dst=Join-Path $snapshot $item.path
  [void](New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force)
  Copy-Item -LiteralPath $src -Destination $dst
}
$before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'input-hashes-before.json') -Encoding utf8
if(@($before | Where-Object {-not $_.exact}).Count){throw 'Recovered source hash mismatch'}
$tracked=@(git -C $SourceRoot ls-files)
$white=@(Get-Content -LiteralPath (Join-Path $SourceRoot 'scripts/source-snapshot-inputs.txt') | Where-Object {$_ -and -not $_.StartsWith('#')})
$union=@($tracked+$white | Sort-Object -Unique)
$meta=[ordered]@{timeHK=[TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow,'China Standard Time').ToString('o');sourceRoot=$SourceRoot;sourceHead=(git -C $SourceRoot rev-parse HEAD);snapshot=$snapshot;recoveredCount=$before.Count;trackedCount=$tracked.Count;whitelistCount=$white.Count;snapshotUnion=$union.Count;omittedByExistingSnapshot=@($manifest | Where-Object {$_.path -notin $union} | Select-Object -ExpandProperty path);steps=@()}
$meta | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'run.json') -Encoding utf8
git -C $SourceRoot status --short --untracked-files=all | Set-Content -LiteralPath (Join-Path $evidence 'git-status-before.txt') -Encoding utf8
$configBefore=@()
foreach($suffix in @('','.bak','.prev')){
 $p=Join-Path $env:APPDATA ('MCMiniLauncher/config.json'+$suffix)
 $configBefore += [pscustomobject]@{path=$p;exists=(Test-Path -LiteralPath $p);hash=$(if(Test-Path -LiteralPath $p){(Get-FileHash -LiteralPath $p).Hash}else{$null})}
}
$configBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'user-config-before.json') -Encoding utf8
function Run-Step([string]$Name,[string]$Exe,[string[]]$Arguments){
 $start=[datetime]::UtcNow
 $log=Join-Path $evidence ($Name+'.log')
 Write-Output ('START '+$Name)
 & $Exe @Arguments *> $log
 $code=$LASTEXITCODE
 $step=[pscustomobject]@{name=$Name;cwd=(Get-Location).Path;exe=$Exe;arguments=$Arguments;startUtc=$start.ToString('o');endUtc=[datetime]::UtcNow.ToString('o');exitCode=$code;log=$log}
 $meta.steps += $step
 $meta | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'run.json') -Encoding utf8
 Write-Output ('END '+$Name+' exit='+$code)
 return $code
}
Push-Location $snapshot
try {
 Run-Step 'inventory' 'node' @('scripts/run-inventory-tests.cjs','--check','--json',(Join-Path $evidence 'test-inventory.json')) | Out-Host
 Run-Step 'ui-closure' 'node' @('scripts/check-ui-resource-closure.cjs','--root','ui','--json',(Join-Path $evidence 'ui-closure.json')) | Out-Host
 Run-Step 'node-all' 'node' @('scripts/run-inventory-tests.cjs') | Out-Host
 $restore=Run-Step 'restore-locked' 'dotnet' @('restore','dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj','--locked-mode')
 $restore | Out-Host
 if($restore[-1] -ne 0){throw 'Locked restore failed'}
 Run-Step 'focused' 'dotnet' @('test','dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj','-c','Release','--no-restore','--filter','FullyQualifiedName~LoaderLaunchAssemblyTests|FullyQualifiedName~JavaSelectionMetadataTests','--logger','trx;LogFileName=focused.trx','--results-directory',$evidence) | Out-Host
 Run-Step 'csharp-all' 'dotnet' @('test','dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj','-c','Release','--no-build','--no-restore','--logger','trx;LogFileName=all.trx','--results-directory',$evidence) | Out-Host
 Run-Step 'verifyhost' 'dotnet' @('build','dev/PineglowLauncher/VerifyHost/VerifyHost.csproj','-c','Release') | Out-Host
 $lockBefore=(Get-FileHash dev/PineglowLauncher/packages.lock.json).Hash
 Run-Step 'rid-locked' 'dotnet' @('restore','dev/PineglowLauncher/Pineglow.Launcher.csproj','-r','win-x64','-p:PublishSingleFile=true','--locked-mode') | Out-Host
 $publish=Run-Step 'publish' 'dotnet' @('publish','dev/PineglowLauncher/Pineglow.Launcher.csproj','-c','Release','-r','win-x64','--self-contained','-p:PublishSingleFile=true','-p:EnableCompressionInSingleFile=true','-p:DebugType=None','-p:DebugSymbols=false','-p:Version=1.0.1','-p:PublishDir=../../audit-dist')
 $publish | Out-Host
 if($publish[-1] -eq 0){
   $exe=Join-Path $snapshot 'audit-dist/Pineglow.Launcher.exe'
   $meta.artifact=[ordered]@{path=$exe;size=(Get-Item -LiteralPath $exe).Length;sha256=(Get-FileHash -LiteralPath $exe).Hash;files=@(Get-ChildItem -LiteralPath (Split-Path $exe) -Recurse -File | Select-Object Name,Length);lockBefore=$lockBefore;lockAfter=(Get-FileHash dev/PineglowLauncher/packages.lock.json).Hash}
   Run-Step 'standalone-cold' 'pwsh' @('-NoProfile','-File','scripts/verify-single-file-standalone.ps1','-ExePath',$exe,'-ExpectedUiRoot',(Join-Path $snapshot 'ui'),'-JsonPath',(Join-Path $evidence 'standalone-cold.json')) | Out-Host
 }
} finally {
 Pop-Location
 $after=@($before | ForEach-Object { [pscustomobject]@{path=$_.path;before=$_.actual;after=(Get-FileHash -LiteralPath (Join-Path $SourceRoot $_.path)).Hash} })
 $after | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'input-hashes-after.json') -Encoding utf8
 $meta.sourceChanges=@($after | Where-Object {$_.before -ne $_.after}).Count
 $configAfter=@($configBefore | ForEach-Object { [pscustomobject]@{path=$_.path;exists=(Test-Path -LiteralPath $_.path);hash=$(if(Test-Path -LiteralPath $_.path){(Get-FileHash -LiteralPath $_.path).Hash}else{$null})} })
 $configAfter | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'user-config-after.json') -Encoding utf8
 $meta.userConfigUnchanged=(($configBefore | ConvertTo-Json -Compress) -eq ($configAfter | ConvertTo-Json -Compress))
 $meta | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'run.json') -Encoding utf8
}
