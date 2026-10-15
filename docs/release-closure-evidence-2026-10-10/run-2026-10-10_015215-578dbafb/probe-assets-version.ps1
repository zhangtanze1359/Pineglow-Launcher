param(
  [Parameter(Mandatory)][string]$ExePath,
  [Parameter(Mandatory)][string]$SourceRoot,
  [Parameter(Mandatory)][string]$OutDir,
  [string[]]$Assets = @('index.html', 'js/api.js', 'js/i18n.js', 'js/launcher.js', 'css/pineglow.css', 'css/figma.css')
)
# Round-2 probe (own tool, product source untouched).
# Purpose: prove the UI actually served by the fixed single-file EXE is the SAME version as that
# EXE's own source -- not just "index.html is not the stale marker".
# The fixture deliberately keeps a stale external UI/ next to the exe, which is exactly the
# upgrade layout that used to win. Every asset fetched over HTTP is compared byte-for-byte
# with the source tree; only index.html is normalised for the by-design session-token value.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$root = Join-Path ([IO.Path]::GetTempPath()) ('pineglow-uiprobe-assets-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $root 'UI'))
[IO.File]::WriteAllText((Join-Path $root 'portable.flag'), '')
$oldHtml = (git -C $SourceRoot show 'HEAD:ui/index.html') -join "`n"
[IO.File]::WriteAllBytes((Join-Path $root 'UI/index.html'), [Text.Encoding]::UTF8.GetBytes('<!-- PG_STALE_UI -->' + $oldHtml))
$target = Join-Path $root 'Pineglow.Launcher.exe'
Copy-Item -LiteralPath $ExePath -Destination $target

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:PINEGLOW_API_PORT = [string]$port
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $root 'extract'

function Get-NormalizedHash([byte[]]$Bytes) {
  $text = [Text.Encoding]::UTF8.GetString($Bytes)
  $text = $text -replace '(name="pineglow-session-token" content=")[^"]*"', '$1__PINEGLOW_SESSION_TOKEN__"'
  return ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($text))) -replace '-', '')
}

$rows = New-Object System.Collections.Generic.List[object]
$result = [ordered]@{
  probe           = 'served-assets-are-same-version-as-exe'
  fixtureRoot     = $root
  artifactSha256  = (Get-FileHash -LiteralPath $target).Hash
  staleExternalUi = $true
  apiPort         = $port
  healthReady     = $false
  assets          = @()
  allAssetsMatchSource = $false
  ownedProcessStopped  = $false
  timeHK          = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$p = $null
try {
  $p = Start-Process -FilePath $target -WorkingDirectory $root -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $OutDir 'assets-stdout.log') -RedirectStandardError (Join-Path $OutDir 'assets-stderr.log')
  for ($i = 0; $i -lt 60; $i++) {
    if ($p.HasExited) { break }
    try {
      $h = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -TimeoutSec 2 -UseBasicParsing
      if ($h.StatusCode -eq 200) { $result.healthReady = $true; break }
    } catch { }
    Start-Sleep -Milliseconds 500
  }
  if ($result.healthReady) {
    foreach ($rel in $Assets) {
      $url = "http://127.0.0.1:$port/" + $rel
      $row = [ordered]@{ asset = $rel; httpStatus = $null; servedSha256 = $null; sourceSha256 = $null; matches = $false; note = '' }
      try {
        $resp = Invoke-WebRequest $url -TimeoutSec 10 -UseBasicParsing
        $row.httpStatus = [int]$resp.StatusCode
        $bytes = $resp.RawContentStream.ToArray()
        $srcPath = Join-Path $SourceRoot ('ui/' + $rel)
        if (-not (Test-Path -LiteralPath $srcPath)) { $row.note = 'source file absent'; $rows.Add([pscustomobject]$row); continue }
        $srcBytes = [IO.File]::ReadAllBytes($srcPath)
        $servedHash = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)) -replace '-', '')
        $row.servedSha256 = $servedHash
        $row.sourceSha256 = (Get-FileHash -LiteralPath $srcPath).Hash
        if ($rel -eq 'index.html') {
          $row.matches = ((Get-NormalizedHash $bytes) -eq $row.sourceSha256)
          $row.note = 'index.html normalised for the by-design session token before comparison'
        } else {
          $row.matches = ($servedHash -eq $row.sourceSha256)
        }
        if (-not $row.matches) { [IO.File]::WriteAllBytes((Join-Path $OutDir ('served-' + ($rel -replace '[/\\]', '_'))), $bytes) }
      }
      catch { $row.note = 'request failed: ' + $_.Exception.Message }
      $rows.Add([pscustomobject]$row)
    }
  }
}
finally {
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(5000) | Out-Null }
  $result.ownedProcessStopped = ($p -and $p.HasExited)
  $result.assets = $rows.ToArray()
  $result.allAssetsMatchSource = (@($rows | Where-Object { $_.matches -ne $true }).Count -eq 0) -and $rows.Count -gt 0
  $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'assets-version.json') -Encoding utf8
  $result | ConvertTo-Json -Depth 6 | Write-Output
}
if (-not $result.healthReady) { exit 2 }
if (-not $result.allAssetsMatchSource) { exit 1 }
exit 0
