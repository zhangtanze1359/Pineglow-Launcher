param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][string]$Evidence,[int]$ObserveSeconds=35)
$ErrorActionPreference='Stop'
$rootPath=(Resolve-Path -LiteralPath $Root).Path
if (-not ([IO.Path]::GetFileName($rootPath) -match '^pineglow-acceptance-[0-9a-f]{32}$')) { throw 'Unexpected owned root' }
$log=Join-Path $Evidence 'owned-launches.log'
function Note([string]$m) { [IO.File]::AppendAllText($log,([DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(8)).ToString('o')+' '+$m+"\n"),[Text.UTF8Encoding]::new($false)) }
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start(); $port=$listener.LocalEndpoint.Port; $listener.Stop()
$env:PINEGLOW_API_PORT="$port"
$app=$null; $ownedJava=[Collections.Generic.List[int]]::new(); $results=[Collections.Generic.List[object]]::new()
try {
  $app=Start-Process -FilePath (Join-Path $rootPath 'Pineglow.Launcher.exe') -WorkingDirectory $rootPath -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Evidence 'app-stdout.log') -RedirectStandardError (Join-Path $Evidence 'app-stderr.log')
  $token=$null; $deadline=(Get-Date).AddSeconds(90)
  while ((Get-Date) -lt $deadline) {
    try { $html=(Invoke-WebRequest "http://127.0.0.1:$port/" -TimeoutSec 3).Content
      if($html -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token=$Matches[1]; break } } catch {}
    Start-Sleep -Milliseconds 500
  }
  if(-not $token){throw 'App token unavailable'}
  $headers=@{'X-Pineglow-Token'=$token}
  $state=Invoke-RestMethod "http://127.0.0.1:$port/api/state" -Headers $headers -TimeoutSec 20
  foreach($case in @(@{kind='Forge';base='1.20.1';java=17},@{kind='NeoForge';base='1.21.1';java=21},@{kind='Vanilla';base='1.18.2';java=17},@{kind='Fabric';base='1.18.2';java=17},@{kind='Quilt';base='1.20.1';java=17},@{kind='Fabric';base='1.21.1';java=21})) {
    $instance=@($state.instances|Where-Object {$_.loaderType -eq $case.kind -and ($_.baseMinecraftVersion -eq $case.base -or $_.minecraftVersion -like ('*'+$case.base+'*'))}) | Select-Object -Last 1
    if(-not $instance){$results.Add(@{kind=$case.kind;base=$case.base;status='NOT_RUN';reason='Fixture instance missing'});continue}
    $dir=[IO.Path]::GetFullPath($instance.gameDirectory)
    if(-not $dir.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture points outside owned root'}
    $gameLog=Join-Path $dir 'logs/latest.log'
    if(Test-Path -LiteralPath $gameLog){[IO.File]::Move($gameLog,$gameLog+'.before-'+[guid]::NewGuid().ToString('N'))}
    $started=Get-Date; $javaPid=0; $caseResult=[ordered]@{kind=$case.kind;base=$case.base;instanceId=$instance.id;status='FAIL';expectedJava=$case.java}
    try {
      $reply=Invoke-RestMethod "http://127.0.0.1:$port/api/instances/$($instance.id)/launch" -Method Post -Headers $headers -ContentType 'application/json' -Body '{}' -TimeoutSec 90
      $javaPid=[int]$reply.pid
      if($javaPid -le 0){throw 'No Java PID returned'}
      $ownedJava.Add($javaPid)
      $process=Get-CimInstance Win32_Process -Filter "ProcessId=$javaPid"
      $command=[string]$process.CommandLine
      $javaPath=[string]$process.ExecutablePath
      $tag=$case.kind+'-'+$case.base
      [IO.File]::WriteAllText((Join-Path $Evidence ($tag+'-cmdline.txt')),$command,[Text.UTF8Encoding]::new($false))
      $deadline=(Get-Date).AddSeconds($ObserveSeconds)
      while((Get-Date) -lt $deadline){Start-Sleep -Seconds 2;if(-not (Get-Process -Id $javaPid -ErrorAction SilentlyContinue)){break}}
      $fresh=(Test-Path -LiteralPath $gameLog) -and ((Get-Item -LiteralPath $gameLog).LastWriteTime -gt $started)
      $lines=if($fresh){@(Get-Content -LiteralPath $gameLog)}else{@()}
      $lines | Set-Content (Join-Path $Evidence ($tag+'-game.log')) -Encoding utf8
      $lwjgl=@($lines|Where-Object {$_ -match 'Backend library: LWJGL'}).Count
      $atlas=@($lines|Where-Object {$_ -match 'Created: .*atlas'}).Count
      $crash=@($lines|Where-Object {$_ -match 'has crashed|UnsatisfiedLinkError|ResolutionException|InvalidModuleDescriptor|Failed to locate library'}).Count
      $alive=[bool](Get-Process -Id $javaPid -ErrorAction SilentlyContinue)
      $javaCorrect=$javaPath -match ('java-'+$case.java+'-')
      $placeholderCount=[regex]::Matches($command,'\$\{[A-Za-z_]+\}').Count
      $caseResult.javaPath=$javaPath;$caseResult.javaCorrect=$javaCorrect;$caseResult.logFresh=$fresh;$caseResult.logLines=$lines.Count;$caseResult.lwjgl=$lwjgl;$caseResult.atlas=$atlas;$caseResult.crash=$crash;$caseResult.alive=$alive;$caseResult.placeholderCount=$placeholderCount
      if($fresh -and $lwjgl -gt 0 -and $atlas -gt 0 -and $crash -eq 0 -and $alive -and $javaCorrect -and $placeholderCount -eq 0){$caseResult.status='PASS'}
    } catch {$caseResult.error=$_.Exception.Message}
    finally {if($javaPid -gt 0){Stop-Process -Id $javaPid -Force -ErrorAction SilentlyContinue}}
    $results.Add($caseResult); Note ($caseResult|ConvertTo-Json -Compress); Start-Sleep -Seconds 2
  }
} finally {
  foreach($javaPid in $ownedJava){Stop-Process -Id $javaPid -Force -ErrorAction SilentlyContinue}
  if($app){Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue}
  @{root=$rootPath;observeSeconds=$ObserveSeconds;results=@($results.ToArray());scope='Fresh copied offline fixtures; actual product API/Java/logs; world entry not tested'}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $Evidence 'owned-launches.json') -Encoding utf8
}
if(@($results|Where-Object {$_.status -ne 'PASS'}).Count -gt 0){exit 1}
