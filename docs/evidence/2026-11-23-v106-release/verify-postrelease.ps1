# v1.0.6 发布产物自更新验证
# 目的：用刚发布的真实 v1.0.6 包，验证产品自己的下载→校验→替换链路
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Pkg    = 'D:/Projects/_tmp_v106/pkg.exe'
$Sandbox = 'D:/Projects/_worktrees/pg-v106-postrelease-20261123'
$RunDir  = Join-Path $Sandbox 'run'
$DataDir = Join-Path $RunDir 'data'
$Target  = Join-Path $RunDir 'Pineglow.Launcher.exe'
$Port    = 48123
$Api     = "http://127.0.0.1:$Port"

$log = New-Object System.Collections.Generic.List[string]
function Log($s) { $log.Add($s); Write-Host $s }

if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null

Log "=== v1.0.6 发布产物自更新验证 $(Get-Date -Format o) ==="
Log ""

# 目标 = 发布的 v1.0.6 exe 本身（复制一份待替换）
Copy-Item $Pkg $Target -Force
Set-Content -Path (Join-Path $RunDir 'portable.flag') -Value '' -NoNewline
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null

$beforeHash = (Get-FileHash $Target -Algorithm SHA256).Hash
Log "目标(初始) SHA256 = $beforeHash"
Log "目标大小          = $((Get-Item $Target).Length) bytes"
Log ""

# config 指向真实 GitHub 更新源（默认值，不改成 http://）
$cfg = @{ gameDirectory = (Join-Path $DataDir '.minecraft') } | ConvertTo-Json -Compress
Set-Content -Path (Join-Path $DataDir 'config.json') -Value $cfg -Encoding utf8
Log "config.json（不设 updateUrl，走产品默认真实源）: $cfg"
Log ""

# 启动发布产物
$proc = Start-Process -FilePath $Target -WorkingDirectory $RunDir -PassThru
Log "PID = $($proc.Id)"

$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    try { $h = Invoke-RestMethod -Uri "$Api/api/health" -TimeoutSec 3; $ready = $true; break } catch { }
}
Log "API 就绪 = $ready (等 $([math]::Round($i*0.5,1)) s)"
Log ""

if ($ready) {
    $html  = (Invoke-WebRequest -Uri "$Api/" -UseBasicParsing -TimeoutSec 15).Content
    $m     = [regex]::Match($html, 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"')
    $token = if ($m.Success) { $m.Groups[1].Value } else { $null }
    Log "session token = $(if($token){"已获取($($token.Length)位)"}else{'未获取'})"
    $hdr = @{ 'X-Pineglow-Token' = $token }
    Log ""

    Log "--- 检查更新（真实 GitHub 源）---"
    try {
        $chk = Invoke-RestMethod -Uri "$Api/api/update/check" -Headers $hdr -TimeoutSec 45
        Log ("currentVersion   = {0}" -f $chk.currentVersion)
        Log ("latestVersion    = {0}" -f $chk.latestVersion)
        Log ("updateAvailable  = {0}" -f $chk.updateAvailable)
        Log ("manifestSource   = {0}" -f $chk.manifestSource)
        Log ("usedFallback     = {0}" -f $chk.usedFallback)
    } catch { Log "check 失败: $($_.Exception.Message)" }
    Log ""

    Log "--- 下载更新 ---"
    try {
        $dl = Invoke-RestMethod -Uri "$Api/api/update/download" -Method Post -Headers $hdr -TimeoutSec 60
        Log ("state={0} version={1}" -f $dl.state, $dl.version)
    } catch { Log "download 失败: $($_.Exception.Message)" }
    Log ""

    Log "--- 轮询直到下载完成 ---"
    $prev = ''
    for ($i = 0; $i -lt 240; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $st = Invoke-RestMethod -Uri "$Api/api/update/status" -Headers $hdr -TimeoutSec 5
            $cur = "$($st.state)|$($st.version)|$([math]::Round([double]$st.progress,1))"
            if ($cur -ne $prev) { Log ("[{0}] state={1} progress={2}" -f $i, $st.state, [math]::Round([double]$st.progress,1)); $prev = $cur }
            if ($st.state -in @('downloaded','failed')) { break }
        } catch { }
    }
    Log ""

    Log "--- 应用更新 ---"
    try {
        $ap = Invoke-RestMethod -Uri "$Api/api/update/apply" -Method Post -Headers $hdr -TimeoutSec 60
        Log ("ok={0} state={1} message={2}" -f $ap.ok, $ap.state, $ap.message)
    } catch { Log "apply 失败: $($_.Exception.Message)" }
    Log ""

    Log "--- 等待替换 ---"
    for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Seconds 1; if ($proc.HasExited) { Log "进程已退出（${i}s）"; break } }
    Start-Sleep -Seconds 12

    $afterHash = (Get-FileHash $Target -Algorithm SHA256).Hash
    $afterSize = (Get-Item $Target).Length
    Log "目标(替换后) SHA256 = $afterHash"
    Log "目标大小            = $afterSize bytes"
    Log ("与发布产物一致 = {0}" -f ($afterHash -eq '43750657A75145CBC2F77A37B281C9E514A35CC16A6DB79E9B703BAC1C6361CD'))
    Log ("备份存在       = {0}" -f ([bool](Test-Path "$Target.bak-1.0.6")))
    $sp = Join-Path $DataDir 'updates/apply-state.json'
    if (Test-Path $sp) { Log "apply-state    = $(Get-Content $sp -Raw)" } else { Log "apply-state 不存在" }
    $upd = Get-ChildItem (Join-Path $DataDir 'updates') -File -EA SilentlyContinue
    if ($upd) { Log "updates 目录:"; $upd | ForEach-Object { Log ("  {0} ({1} bytes)" -f $_.Name, $_.Length) } }
}

Log ""
Log "--- 清理 ---"
if (-not $proc.HasExited) { try { $proc.Kill(); Log "已终止 PID $($proc.Id)" } catch { } }
$lg = Join-Path $DataDir 'logs'
if (Test-Path $lg) {
    Log "产品日志:"; Get-ChildItem $lg -Filter *.log | ForEach-Object {
        Log ("--- " + $_.Name)
        Get-Content $_.FullName | ForEach-Object { Log $_ }
    }
}

$out = 'D:/Projects/松光启动器/docs/evidence/2026-11-23-v106-release/raw/postrelease-verify.txt'
New-Item -ItemType Directory -Path (Split-Path $out) -Force | Out-Null
$log | Set-Content -Path $out -Encoding utf8
Log ""
Log "=== 完成，产物 $out ==="
