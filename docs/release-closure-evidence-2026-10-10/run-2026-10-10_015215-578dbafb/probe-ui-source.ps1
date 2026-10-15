param(
  [Parameter(Mandatory)][string]$ExePath,
  [Parameter(Mandatory)][ValidateSet('legacy','clean')][string]$Mode,
  [string]$SourceRoot = 'D:\松光启动器',
  [Parameter(Mandatory)][string]$OutDir
)
# 独立复验探针（本轮自有工具，不修改产品源码）。
# legacy : EXE 旁保留旧版本外置 UI/（旧 HEAD index.html + 唯一标记）
# clean  : EXE 旁没有 UI/（干净安装布局）
# 预期判别：若“外置 UI 存在”是唯一变量，则 legacy 服务旧标记，clean 服务当前内嵌 UI。
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$root = Join-Path ([IO.Path]::GetTempPath()) ('pineglow-uiprobe-' + $Mode + '-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root -Force)
[IO.File]::WriteAllText((Join-Path $root 'portable.flag'), '')

$expectedLegacyMarker = 'PG_CLOSURE_LEGACY_UI'
if ($Mode -eq 'legacy') {
  [void](New-Item -ItemType Directory -Path (Join-Path $root 'UI'))
  $oldHtml = (git -C $SourceRoot show 'HEAD:ui/index.html') -join "`n"
  $legacyBytes = [Text.Encoding]::UTF8.GetBytes('<!-- ' + $expectedLegacyMarker + ' -->' + $oldHtml)
  [IO.File]::WriteAllBytes((Join-Path $root 'UI/index.html'), $legacyBytes)
}

$target = Join-Path $root 'Pineglow.Launcher.exe'
Copy-Item -LiteralPath $ExePath -Destination $target

# 当前源码的 ui/index.html 指纹（用于判断服务的是不是当前版本界面）
$currentIndex = Join-Path $SourceRoot 'ui/index.html'
$currentHash = if (Test-Path -LiteralPath $currentIndex) { (Get-FileHash -LiteralPath $currentIndex -Algorithm SHA256).Hash } else { $null }
$legacyHash  = if ($Mode -eq 'legacy') { (Get-FileHash -LiteralPath (Join-Path $root 'UI/index.html') -Algorithm SHA256).Hash } else { $null }

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()

foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -like 'PINEGLOW_*' })) { Remove-Item -LiteralPath ('Env:' + $item.Name) }
$env:PINEGLOW_API_PORT = [string]$port
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $root 'extract'

$result = [ordered]@{
  mode                = $Mode
  fixtureRoot         = $root
  artifactSha256      = (Get-FileHash -LiteralPath $target).Hash
  sourceUiIndexSha256 = $currentHash
  externalUiSha256    = $legacyHash
  externalUiPresent   = ($Mode -eq 'legacy')
  apiPort             = $port
  healthReady         = $false
  legacyMarkerServed  = $false
  externalUiServed    = $false
  servedIndexSha256   = $null
  servedIndexMatchesCurrentSource = $false
  ownedProcessStopped = $false
  timeHK              = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([datetime]::UtcNow, 'China Standard Time').ToString('o')
}

$p = $null
try {
  $p = Start-Process -FilePath $target -WorkingDirectory $root -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $root 'stdout.log') -RedirectStandardError (Join-Path $root 'stderr.log')
  for ($i = 0; $i -lt 60; $i++) {
    if ($p.HasExited) { break }
    try {
      $health = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -TimeoutSec 2 -UseBasicParsing
      if ($health.StatusCode -eq 200) { $result.healthReady = $true; break }
    } catch { }
    Start-Sleep -Milliseconds 500
  }
  if ($result.healthReady) {
    $resp = Invoke-WebRequest "http://127.0.0.1:$port/" -TimeoutSec 10 -UseBasicParsing
    $bytes = $resp.RawContentStream.ToArray()
    $result.legacyMarkerServed = ([Text.Encoding]::UTF8.GetString($bytes)).Contains($expectedLegacyMarker)
    $sha = [Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    $result.servedIndexSha256 = ([BitConverter]::ToString($sha) -replace '-', '')
    $result.externalUiServed = ($null -ne $legacyHash) -and ($result.servedIndexSha256 -eq $legacyHash)
    $result.servedIndexMatchesCurrentSource = ($null -ne $currentHash) -and ($result.servedIndexSha256 -eq $currentHash)
    $redacted = [Text.Encoding]::UTF8.GetString($bytes) -replace '(name="pineglow-session-token"\s+content=")[^"]+', '$1REDACTED'
    [IO.File]::WriteAllText((Join-Path $OutDir ("served-index-$Mode.html")), $redacted, (New-Object Text.UTF8Encoding($false)))
  }
}
finally {
  if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(5000) | Out-Null }
  $result.ownedProcessStopped = ($p -and $p.HasExited)
  $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir ("ui-source-$Mode.json")) -Encoding utf8
  $result | ConvertTo-Json -Depth 5 | Write-Output
}
if (-not $result.healthReady) { exit 2 }
if ($Mode -eq 'legacy') {
  # 期望（修复后）：新 EXE 服务当前内嵌 UI，而不是外置旧 UI
  if ($result.legacyMarkerServed -or $result.externalUiServed) { exit 1 }
  exit 0
} else {
  # 干净安装：必须服务当前内嵌 UI
  if ($result.legacyMarkerServed) { exit 1 }
  if ($null -ne $currentHash -and -not $result.servedIndexMatchesCurrentSource) { exit 3 }
  exit 0
}
