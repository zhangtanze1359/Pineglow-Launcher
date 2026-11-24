# OPEN-07 销项验证：产品经默认更新链（Gitee 主源）检查更新
param(
  [Parameter(Mandatory = $true)][string]$ExePath,
  [string]$Sandbox = 'D:/Projects/_worktrees/pg-o7-sandbox'
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Api = 'http://127.0.0.1:48123'
$log = New-Object System.Collections.Generic.List[string]
function Log($s) { $log.Add($s); Write-Host $s }

Log "=== OPEN-07 产品链路验证 $(Get-Date -Format o) ==="
Log ""

$exeHash = (Get-FileHash $ExePath -Algorithm SHA256).Hash
$exeSize = (Get-Item $ExePath).Length
Log "--- 被测产物 ---"
Log "EXE: $ExePath"
Log "大小: $exeSize bytes"
Log "SHA-256: $exeHash"
Log ""

if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
$RunDir = Join-Path $Sandbox 'run'
$DataDir = Join-Path $RunDir 'data'
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
$TargetExe = Join-Path $RunDir 'Pineglow.Launcher.exe'
Copy-Item $ExePath $TargetExe -Force
Set-Content -Path (Join-Path $RunDir 'portable.flag') -Value '' -NoNewline

# **不设 updateUrl** —— 让产品用内置默认链（Gitee 主源 → GitHub 备用）
$cfg = @{ gameDirectory = (Join-Path $DataDir '.minecraft') } | ConvertTo-Json -Compress
Set-Content -Path (Join-Path $DataDir 'config.json') -Value $cfg -Encoding utf8
Log "config.json（**不含 updateUrl**，走产品默认链）: $cfg"
Log ""

Log "--- 启动产物 ---"
$proc = Start-Process -FilePath $TargetExe -WorkingDirectory $RunDir -PassThru
Log "PID = $($proc.Id)"
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    try { $null = Invoke-RestMethod -Uri "$Api/api/health" -TimeoutSec 3; $ready = $true; break } catch { }
}
Log "API 就绪 = $ready"
if (-not $ready) { $log | Set-Content (Join-Path $PSScriptRoot 'raw/product-gitee-verify.txt') -Encoding utf8; exit 1 }
Log ""

$html = (Invoke-WebRequest -Uri "$Api/" -UseBasicParsing -TimeoutSec 15).Content
$m = [regex]::Match($html, 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"')
$hdr = @{ 'X-Pineglow-Token' = $m.Groups[1].Value }

Log "--- GET /api/update/check（产品默认链）---"
$verdict = 'FAIL'
try {
    $chk = Invoke-RestMethod -Uri "$Api/api/update/check" -Headers $hdr -TimeoutSec 45
    Log "  currentVersion  = $($chk.currentVersion)"
    Log "  latestVersion   = $($chk.latestVersion)"
    Log "  manifestSource  = $($chk.manifestSource)"
    Log "  usedFallback    = $($chk.usedFallback)"
    Log "  manifestSources:"
    foreach ($a in @($chk.manifestSources)) {
        if ($a) { Log "    - ok=$($a.ok)  url=$($a.url)  reason=$($a.reason)" }
    }

    # 销项判据（修正版）：成功取到清单时，manifestSource 指向 Gitee 且 usedFallback=False。
    #
    # 为什么不用 manifestSources[].ok：那个字段是**失败时**才填充的（见 check 处理器的
    # fetch.FailureSummary 分支）。成功时它为空数组，用它的 ok 判 PASS 会永远失败
    # （2026-11-24 实测踩过：产品明明用 Gitee 主源成功，却被判 FAIL）。
    $srcIsGitee = $chk.manifestSource -like '*gitee.com*'
    $noFallback = ($chk.usedFallback -eq $false)
    $gotVersion = -not [string]::IsNullOrWhiteSpace([string]$chk.latestVersion)

    if ($gotVersion -and $srcIsGitee -and $noFallback) {
        Log ""
        Log "  >>> 产品经 Gitee 主源成功取到清单，且未走备用源 —— OPEN-07 核心判据达成"
        $verdict = 'PASS'
    } else {
        Log ""
        Log "  >>> 判据未满足：gotVersion=$gotVersion srcIsGitee=$srcIsGitee noFallback=$noFallback"
    }
} catch {
    Log "  check 失败: $($_.Exception.Message)"
}

Log ""
Log "--- 产品日志 ---"
$lg = Join-Path $DataDir 'logs'
if (Test-Path $lg) {
    Get-ChildItem $lg -Filter *.log | ForEach-Object {
        Get-Content $_.FullName | Select-String 'update|清单' | Select-Object -Last 12 | ForEach-Object { Log "  $_" }
    }
}

Log ""
if (-not $proc.HasExited) { try { $proc.Kill(); Log "已终止 PID $($proc.Id)" } catch { } }
Log ""
Log "RESULT: $verdict  (EXE=$exeHash)"

$out = Join-Path $PSScriptRoot 'raw/product-gitee-verify.txt'
New-Item -ItemType Directory -Path (Split-Path $out) -Force | Out-Null
$log | Set-Content -Path $out -Encoding utf8
Log "=== 产物 $out ==="
if ($verdict -ne 'PASS') { exit 1 }
