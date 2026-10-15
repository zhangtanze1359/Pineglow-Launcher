param(
  [Parameter(Mandatory)][string]$FixtureRoot,
  [Parameter(Mandatory)][string]$OutDir,
  [string]$VersionId = '1.21.11',
  [int]$PrefixBytes = 1048576,     # 1 MiB of the real jar, kept as a truncated *.download temp
  [int]$StartupTimeoutSec = 120,
  [int]$TimeoutSec = 600
)
# Round-11 probe (own tool, product source untouched).
# Deterministic proof of the byte-level resume path (MinecraftDownloader.DownloadFileAsync):
#   - the client jar is removed and replaced by a TRUNCATED "<jar>.download" temp,
#   - the install is re-issued: if the code path resumes, the product logs
#     "继续下载：<file>（已完成 <size>）" and completes the file without restarting from zero,
#   - the finished jar is checked against the SHA-1 captured BEFORE truncation.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

function Get-FreePort {
  $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start()
  $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); return $p
}
function OwnedProcs([string]$exePath) {
  @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $exePath) })
}
function Api([int]$port, [string]$token, [string]$method, [string]$path, $body, [int]$timeoutSec = 60) {
  $hdr = @{ 'X-Pineglow-Token' = $token }
  try {
    if ($null -ne $body) {
      $json = if ($body -is [string]) { $body } else { $body | ConvertTo-Json -Compress }
      $r = Invoke-RestMethod "http://127.0.0.1:$port$path" -Method $method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $timeoutSec
    } else { $r = Invoke-RestMethod "http://127.0.0.1:$port$path" -Method $method -Headers $hdr -TimeoutSec $timeoutSec }
    return [pscustomobject]@{ ok = $true; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 8) }
  } catch {
    $d = ''; try { $d = [string]$_.ErrorDetails.Message } catch { }
    if (-not $d) { $d = $_.Exception.Message }
    return [pscustomobject]@{ ok = $false; body = $null; text = $d }
  }
}
function Sha1Hex([string]$path) {
  $sha = [Security.Cryptography.SHA1]::Create()
  $fs = [IO.File]::OpenRead($path)
  try { return ([BitConverter]::ToString($sha.ComputeHash($fs)) -replace '-', '').ToLowerInvariant() } finally { $fs.Dispose(); $sha.Dispose() }
}

$cold = $FixtureRoot
$targetExe = Join-Path $cold 'Pineglow.Launcher.exe'
$mc = Join-Path $cold 'data\.minecraft'
$jar = Join-Path $mc "versions\$VersionId\$VersionId.jar"
$temp = "$jar.download"
$log = Get-ChildItem (Join-Path $cold 'data\logs') -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
$logPath = if ($log) { $log.FullName } else { Join-Path $cold 'data\logs\launcher.log' }

$result = [ordered]@{
  probe = 'range-resume-proof'; fixture = $cold; version = $VersionId
  jarPath = $jar; tempPath = $temp
  originalJarSha1 = ''; tempBytesWritten = 0; logLineCountBefore = 0
  messagesSeen = @()
  resumeLogLines = @(); finalJarSha1 = ''; jarMatchesOriginal = $false
  downloadStatus = ''; seconds = 0; leftoverTemp = $false
  ownedProcessesStopped = $false
  timeHK = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$proc = $null
try {
  if (-not (Test-Path -LiteralPath $jar)) { throw "找不到要截断的 jar：$jar" }
  $result.originalJarSha1 = Sha1Hex $jar
  $logBefore = if (Test-Path -LiteralPath $logPath) { (Get-Content -LiteralPath $logPath -Encoding UTF8 | Measure-Object -Line).Lines } else { 0 }
  $result.logLineCountBefore = $logBefore

  # write the first N bytes into the *.download temp, then remove the complete jar
  $in = [IO.File]::OpenRead($jar)
  $buf = New-Object byte[] $PrefixBytes
  $read = $in.Read($buf, 0, $PrefixBytes)
  $in.Dispose()
  [IO.File]::WriteAllBytes($temp, $buf[0..($read - 1)])
  $result.tempBytesWritten = $read
  Remove-Item -LiteralPath $jar -Force

  foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
  $port = Get-FreePort
  $env:PINEGLOW_API_PORT = "$port"
  $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $cold 'extract'
  $proc = Start-Process -FilePath $targetExe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $OutDir 'range-stdout.log') -RedirectStandardError (Join-Path $OutDir 'range-stderr.log')
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

  $dl = Api $port $token 'POST' '/api/versions/download' @{ versionId = $VersionId } 120
  $sw = [Diagnostics.Stopwatch]::StartNew(); $state = ''
  # NOTE: MinecraftDownloader's log callback is wired to the task entry's Message, NOT to the launcher
  # log file, so the resume evidence must come from the /api/downloads message stream.
  $messages = New-Object System.Collections.Generic.List[string]
  while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
    Start-Sleep -Milliseconds 400
    $st = Api $port $token 'GET' '/api/downloads' $null 60
    if ($st.ok -and $st.body) {
      $tasks = $null
      foreach ($prop in @('downloads', 'tasks', 'items')) { if ($st.body.PSObject.Properties.Name -contains $prop) { $tasks = $st.body.$prop; break } }
      if ($null -eq $tasks -and $st.body -is [array]) { $tasks = $st.body }
      $mine = @($tasks | Where-Object { $_.versionId -eq $VersionId } | Select-Object -First 1)
      if ($mine.Count -gt 0) {
        $state = [string]$mine[0].status
        $msg = [string]$mine[0].message
        if ($msg -and ($messages.Count -eq 0 -or $messages[$messages.Count - 1] -ne $msg)) { $messages.Add($msg) }
        if ($state -in @('completed', 'failed', 'cancelled', 'error')) { break }
      }
    }
    if (Test-Path -LiteralPath $jar) { break }   # the jar is back: resume finished
  }
  $sw.Stop()
  $result.downloadStatus = $state
  $result.seconds = [int]$sw.Elapsed.TotalSeconds
  $result.messagesSeen = $messages.ToArray()
}
finally {
  if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
  foreach ($o in (OwnedProcs $targetExe)) { Stop-Process -Id $o.ProcessId -Force -ErrorAction SilentlyContinue }
  $result.ownedProcessesStopped = (@(OwnedProcs $targetExe).Count -eq 0)
  Start-Sleep -Seconds 1
  if (Test-Path -LiteralPath $jar) {
    $result.finalJarSha1 = Sha1Hex $jar
    $result.jarMatchesOriginal = ($result.finalJarSha1 -eq $result.originalJarSha1)
  }
  $result.leftoverTemp = (Test-Path -LiteralPath $temp)
  if (Test-Path -LiteralPath $logPath) {
    $lines = Get-Content -LiteralPath $logPath -Encoding UTF8
    $result.resumeLogLines = @($lines | Where-Object { $_ -match '继续下载|已完成' } | Select-Object -Last 6)
  }
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'range-resume.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if (-not $result.jarMatchesOriginal) { exit 3 }
# The authoritative resume channel is the API message stream (messagesSeen), not a product log file.
if (@($result.messagesSeen | Where-Object { $_ -match '继续下载' }).Count -eq 0) { exit 4 }
exit 0
