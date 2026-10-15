param(
  [Parameter(Mandatory)][string]$FixtureRoot,
  [Parameter(Mandatory)][string]$OutDir,
  [int]$StartupTimeoutSec = 120
)
# Round-7 probe (own tool, product source untouched).
# Reachability test for the documented loader flow: the product exposes exactly one version-install
# entry point (POST /api/versions/download {versionId}). This probe asks it for (1) a real Mojang
# version id as a control and (2) loader version ids of the four promised loaders.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function Api([int]$port, [string]$token, [string]$method, [string]$path, $body, [int]$timeoutSec = 60) {
  $hdr = @{ 'X-Pineglow-Token' = $token }
  try {
    if ($null -ne $body) {
      $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Compress }
      $r = Invoke-RestMethod "http://127.0.0.1:$port$path" -Method $method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $timeoutSec
    } else {
      $r = Invoke-RestMethod "http://127.0.0.1:$port$path" -Method $method -Headers $hdr -TimeoutSec $timeoutSec
    }
    return [pscustomobject]@{ ok = $true; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 8) }
  } catch {
    $detail = ''
    try { $detail = [string]$_.ErrorDetails.Message } catch { }
    if (-not $detail) { $detail = $_.Exception.Message }
    return [pscustomobject]@{ ok = $false; body = $null; text = $detail }
  }
}

$cold = $FixtureRoot
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'

$result = [ordered]@{
  probe = 'loader-install-reachability'; fixture = $cold
  apiPort = 0; healthReady = $false; attempts = @()
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$proc = $null
try {
  $port = Get-FreePort
  $result.apiPort = $port
  $env:PINEGLOW_API_PORT = "$port"
  $proc = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $OutDir 'reach-stdout.log') -RedirectStandardError (Join-Path $OutDir 'reach-stderr.log')
  $token = $null
  $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
  while ((Get-Date) -lt $deadline -and -not $token) {
    Start-Sleep -Milliseconds 700
    try {
      $h = Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 5
      if ($h.Content -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }
    } catch { }
  }
  if (-not $token) { throw 'launcher did not become ready' }
  $result.healthReady = $true

  # control: a plain Mojang id the launcher already has on disk (queued again is harmless/idempotent)
  $candidates = @(
    @{ kind = 'control-vanilla'; id = '1.21.11' },
    @{ kind = 'fabric';          id = 'fabric-loader-0.17.2-1.21.11' },
    @{ kind = 'quilt';           id = 'quilt-loader-0.28.0-1.21.11' },
    @{ kind = 'forge';           id = '1.21.11-forge-61.0.1' },
    @{ kind = 'neoforge';        id = 'neoforge-21.11.0' }
  )
  $rows = New-Object System.Collections.Generic.List[object]
  foreach ($c in $candidates) {
    $r = Api $port $token 'POST' '/api/versions/download' @{ versionId = $c.id } 120
    Start-Sleep -Seconds 4
    $list = Api $port $token 'GET' '/api/downloads' $null 60
    $state = ''
    if ($list.ok -and $list.body) {
      $tasks = $null
      foreach ($prop in @('downloads', 'tasks', 'items')) { if ($list.body.PSObject.Properties.Name -contains $prop) { $tasks = $list.body.$prop; break } }
      if ($null -eq $tasks -and $list.body -is [array]) { $tasks = $list.body }
      $mine = @($tasks | Where-Object { $_.versionId -eq $c.id } | Select-Object -First 1)
      if ($mine.Count -gt 0) { $state = "[$($mine[0].status)] $($mine[0].message)" }
    }
    $rows.Add([pscustomobject]@{ kind = $c.kind; versionId = $c.id; accepted = $r.ok; response = $r.text.Substring(0, [Math]::Min(200, $r.text.Length)); taskState = $state })
  }
  $result.attempts = $rows.ToArray()
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  foreach ($o in @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $targetExe) })) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'loader-reachability.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if (-not $result.healthReady) { exit 2 }
exit 0
