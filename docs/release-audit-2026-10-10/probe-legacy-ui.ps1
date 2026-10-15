param([Parameter(Mandatory)][string]$ExePath,[string]$SourceRoot='D:\松光启动器')
$ErrorActionPreference='Stop'
$e=$PSScriptRoot
$root=Join-Path ([IO.Path]::GetTempPath()) ('pineglow-legacy-ui-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path (Join-Path $root 'UI'))
[IO.File]::WriteAllText((Join-Path $root 'portable.flag'),'')
$oldHtml=(git -C $SourceRoot show 'HEAD:ui/index.html') -join "`n"
[IO.File]::WriteAllText((Join-Path $root 'UI/index.html'), ('<!-- PG_AUDIT_OLD_UI -->'+$oldHtml), (New-Object Text.UTF8Encoding($false)))
$target=Join-Path $root 'Pineglow.Launcher.exe'
Copy-Item -LiteralPath $ExePath -Destination $target
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $port=([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
foreach($item in @(Get-ChildItem Env: | Where-Object {$_.Name -like 'PINEGLOW_*'})){Remove-Item -LiteralPath ('Env:'+$item.Name)}
$env:PINEGLOW_API_PORT=[string]$port
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR=Join-Path $root 'extract'
$p=$null
$result=[ordered]@{fixtureRoot=$root;sourceOldHtml='git HEAD c8aa286 ui/index.html + unique audit marker';artifactSha256=(Get-FileHash -LiteralPath $target).Hash;apiPort=$port;healthReady=$false;legacyUiServed=$false;failureClass='product-upgrade';scope='EXE-only replacement into old installation retaining external UI directory';timeHK=[TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow,'China Standard Time').ToString('o')}
try{
 $p=Start-Process -FilePath $target -WorkingDirectory $root -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root 'stdout.log') -RedirectStandardError (Join-Path $root 'stderr.log')
 for($i=0;$i -lt 40;$i++){
  if($p.HasExited){break}
  try{
   $health=Invoke-WebRequest "http://127.0.0.1:$port/api/health" -TimeoutSec 2
   if($health.StatusCode -eq 200){$result.healthReady=$true;break}
  }catch{}
  Start-Sleep -Milliseconds 500
 }
 if($result.healthReady){
  $html=(Invoke-WebRequest "http://127.0.0.1:$port/" -TimeoutSec 5).Content
  $result.legacyUiServed=$html.Contains('PG_AUDIT_OLD_UI')
  $result.expectedCurrentUiServed=(-not $result.legacyUiServed)
  $result.servedLength=$html.Length
  [IO.File]::WriteAllText((Join-Path $e 'legacy-served-index.html'), ($html -replace '(name="pineglow-session-token"\s+content=")[^"]+','$1REDACTED'))
 }
}finally{
 if($p -and -not $p.HasExited){Stop-Process -Id $p.Id -Force; $p.WaitForExit(5000) | Out-Null}
 $result.ownedProcessStopped=($p -and $p.HasExited)
 $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $e 'legacy-ui.json') -Encoding utf8
 $result | ConvertTo-Json -Depth 5 | Write-Output
}
if(-not $result.healthReady){exit 2}
if($result.legacyUiServed){exit 1}
exit 0
