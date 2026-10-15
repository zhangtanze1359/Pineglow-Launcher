# Read-only unit verification of the exact production collection block.
param([string]$SourceScript='D:\松光启动器\scripts\verify-source-snapshot.ps1')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText($SourceScript)
$start=$source.IndexOf('$expectedSet = @{}')
$finish=$source.IndexOf("Write-Section '步骤 2/7",$start)
if($start -lt 0 -or $finish -le $start){throw 'Production collection block not found'}
$block=[scriptblock]::Create($source.Substring($start,$finish-$start))
function Write-Ok {param($m)}
function Write-Bad {param($m)}
function Write-Note {param($m)}
function Test-Path {param($LiteralPath) return $script:probePresent.ContainsKey([IO.Path]::GetFileName($LiteralPath))}
function Copy-RelativeFile {param($Rel,$Dest) if(-not $script:probePresent.ContainsKey($Rel)){throw 'Attempt to copy missing fixture'};[void]$script:probeCopied.Add($Rel)}
$results=@()
foreach($case in @(
  @{id='overlap-union';present=@('tracked.txt','shared.txt','white.txt');count=3;failures=0},
  @{id='tracked-missing';present=@('shared.txt','white.txt');count=2;failures=1},
  @{id='whitelist-missing';present=@('tracked.txt','shared.txt');count=2;failures=1},
  @{id='shared-input-missing';present=@('tracked.txt','white.txt');count=2;failures=2}
)) {
  $RepoRoot='D:\virtual-collection-fixture';$SnapshotDir='D:\virtual-collection-output'
  $tracked=@('tracked.txt','shared.txt');$cats=[ordered]@{A=@('shared.txt','white.txt')};$skipSet=@()
  $script:probePresent=@{};foreach($p in $case.present){$script:probePresent[$p]=$true}
  $script:probeCopied=[Collections.Generic.List[string]]::new()
  $script:Failures=[Collections.Generic.List[string]]::new()
  $Summary=[ordered]@{trackedCopied=0;whitelistCopied=0;whitelistSkipped=@();whitelistMissing=@();uniqueExpectedFiles=0;overlapWithTracked=0}
  . $block
  if($Summary.uniqueExpectedFiles -ne $case.count){throw 'Union count failed: '+$case.id}
  if($script:Failures.Count -ne $case.failures){throw 'Missing guard failed: '+$case.id}
  $results+= [pscustomobject]@{id=$case.id;uniqueCount=$Summary.uniqueExpectedFiles;failures=$script:Failures.Count;copied=$script:probeCopied.ToArray();status='PASS'}
}
$sha=(Get-FileHash -LiteralPath $SourceScript -Algorithm SHA256).Hash
[ordered]@{scope='Exact production collection block with mocked filesystem existence and copy operations; no files deleted';source=$SourceScript;sourceSha256=$sha;checks=$results;status='PASS'}|ConvertTo-Json -Depth 5
