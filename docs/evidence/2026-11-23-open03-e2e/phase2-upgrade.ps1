# 实包验证 · 阶段二：真实二进制走完整升级链
# 被测：真实构建的 v1.0.5 单文件 exe，用它自己的 LocalApiService 完成 v1.0.5 -> v1.0.6
# token 获取：GET / 返回的 index.html 中 <meta name="pineglow-session-token"> 被替换成真实 token
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Sandbox   = 'D:/Projects/_worktrees/pg-e2e-sandbox-20261123'
$RunDir    = Join-Path $Sandbox 'run'
$ServeRoot = Join-Path $Sandbox 'serve'
$DataDir   = Join-Path $RunDir 'data'
$TargetExe = Join-Path $RunDir 'Pineglow.Launcher.exe'
$Port      = 48123
$ServePort = 49123
$Api       = "http://127.0.0.1:$Port"
$V106Hash  = '8CCB0D69E1715C229EBA77B2204975736193E8D5CE1C4A5F0EF3BFF9ABADCBD0'

$log = New-Object System.Collections.Generic.List[string]
function Log($s) { $log.Add($s); Write-Host $s }
function SaveLog($name) { $log | Set-Content -Path (Join-Path $PSScriptRoot "raw/$name") -Encoding utf8 }

Log "=== 阶段二 开始 $(Get-Date -Format o) ==="
Log ""

# ---------- 替换前状态 ----------
Log "--- 替换前 ---"
$beforeHash = (Get-FileHash $TargetExe -Algorithm SHA256).Hash
Log "目标 SHA256 = $beforeHash ($((Get-Item $TargetExe).Length) bytes)"
$updatesDir = Join-Path $DataDir 'updates'
$statePath  = Join-Path $updatesDir 'apply-state.json'
Log "apply-state 路径: $statePath (存在=$([bool](Test-Path $statePath)))"
Log ""

# ---------- 清单服务 ----------
$serveScript = {
    param($root, $port)
    $l = New-Object System.Net.HttpListener
    $l.Prefixes.Add("http://127.0.0.1:$port/")
    $l.Start()
    while ($l.IsListening) {
        try {
            $ctx = $l.GetContext()
            $rel = $ctx.Request.Url.AbsolutePath.TrimStart('/')
            $file = Join-Path $root $rel
            if (Test-Path $file -PathType Leaf) {
                $bytes = [System.IO.File]::ReadAllBytes($file)
                $ctx.Response.ContentType = $(if ($rel -like '*.json') { 'application/json' } else { 'application/octet-stream' })
                $ctx.Response.ContentLength64 = $bytes.Length
                $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            } else { $ctx.Response.StatusCode = 404 }
            $ctx.Response.Close()
        } catch { break }
    }
}
$serveJob = Start-Job -ScriptBlock $serveScript -ArgumentList $ServeRoot, $ServePort
Start-Sleep -Seconds 2
try {
    $m = Invoke-WebRequest -Uri "http://127.0.0.1:$ServePort/update.json" -UseBasicParsing -TimeoutSec 10
    Log "清单服务就绪: HTTP $($m.StatusCode)"
} catch {
    Log "清单不可达: $($_.Exception.Message)"; SaveLog 'phase2-fail.txt'
    Stop-Job $serveJob -EA SilentlyContinue; Remove-Job $serveJob -Force -EA SilentlyContinue
    exit 1
}
Log ""

# ---------- 启动真实 v1.0.5 ----------
Log "--- 启动真实 v1.0.5 单文件 exe ---"
$proc = Start-Process -FilePath $TargetExe -WorkingDirectory $RunDir -PassThru
Log "PID = $($proc.Id)"

$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $h = Invoke-RestMethod -Uri "$Api/api/health" -TimeoutSec 3
        Log "健康检查通过（等 $([math]::Round($i*0.5,1)) s）: $($h | ConvertTo-Json -Compress)"
        $ready = $true; break
    } catch { }
}
if (-not $ready) { Log "API 未就绪；进程存活=$(if($proc.HasExited){'否'}else{'是'})" }
Log ""

if ($ready) {
    # ---------- 取 session token ----------
    Log "--- 从 index.html 提取 session token ---"
    $token = $null
    try {
        $html = (Invoke-WebRequest -Uri "$Api/" -UseBasicParsing -TimeoutSec 15).Content
        $m = [regex]::Match($html, 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"')
        if ($m.Success) { $token = $m.Groups[1].Value; Log "token 已获取，长度=$($token.Length)，前8位=$($token.Substring(0,8))…" }
        else { Log "未在 index.html 中找到 token 占位符" }
    } catch { Log "取 index.html 失败: $($_.Exception.Message)" }
    Log ""

    $hdr = @{ 'X-Pineglow-Token' = $token }

    # ---------- 1. 检查更新 ----------
    Log "--- 1. GET /api/update/check ---"
    try {
        $check = Invoke-RestMethod -Uri "$Api/api/update/check" -Headers $hdr -TimeoutSec 30
        Log ($check | ConvertTo-Json -Compress -Depth 6)
    } catch { Log "check 失败: $($_.Exception.Message)" }
    Log ""

    # ---------- 2. 下载 ----------
    Log "--- 2. POST /api/update/download ---"
    try {
        $dl = Invoke-RestMethod -Uri "$Api/api/update/download" -Method Post -Headers $hdr -TimeoutSec 60
        Log ($dl | ConvertTo-Json -Compress -Depth 6)
    } catch { Log "download 失败: $($_.Exception.Message)" }
    Log ""

    # ---------- 3. 等下载 ----------
    Log "--- 3. 轮询 /api/update/status ---"
    $last = ''
    for ($i = 0; $i -lt 180; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $st = Invoke-RestMethod -Uri "$Api/api/update/status" -Headers $hdr -TimeoutSec 5
            $j = $st | ConvertTo-Json -Compress -Depth 6
            if ($j -ne $last) { Log ("[{0}] {1}" -f $i, $j); $last = $j }
            if ($st.state -in @('downloaded','ready','applying','applied','failed','idle') -and $i -gt 2) {
                if ($st.state -ne 'downloading') { break }
            }
        } catch { Log ("[{0}] status 失败: {1}" -f $i, $_.Exception.Message) }
    }
    Log ""

    # ---------- 4. 应用 ----------
    Log "--- 4. POST /api/update/apply ---"
    try {
        $ap = Invoke-RestMethod -Uri "$Api/api/update/apply" -Method Post -Headers $hdr -TimeoutSec 60
        Log ($ap | ConvertTo-Json -Compress -Depth 6)
    } catch { Log "apply 失败: $($_.Exception.Message)" }
    Log ""

    # ---------- 5. 观察替换 ----------
    Log "--- 5. 等待进程退出与替换 ---"
    $exitedAt = -1
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep -Seconds 1
        if ($proc.HasExited) { $exitedAt = $i; Log "启动器进程已退出（${i} s）"; break }
    }
    if ($exitedAt -lt 0) { Log "启动器进程 90 s 内未退出" }
    Start-Sleep -Seconds 15

    $afterHash = (Get-FileHash $TargetExe -Algorithm SHA256).Hash
    Log "目标 SHA256 = $afterHash ($((Get-Item $TargetExe).Length) bytes)"
    Log ("已替换为 v1.0.6 = {0}" -f ($afterHash -eq $V106Hash))
    $bak = "$TargetExe.bak-1.0.6"
    Log ("备份存在 = {0}" -f ([bool](Test-Path $bak)))
    if (Test-Path $bak) { Log ("备份 SHA256 = {0}" -f (Get-FileHash $bak -Algorithm SHA256).Hash) }
    if (Test-Path $statePath) { Log "apply-state = $(Get-Content $statePath -Raw)" } else { Log "apply-state 不存在" }
    $upd = Get-ChildItem $updatesDir -File -EA SilentlyContinue
    if ($upd) { Log "updates 目录:"; $upd | ForEach-Object { Log ("  {0} ({1} bytes)" -f $_.Name, $_.Length) } }
}

# ---------- 清理 ----------
Log ""
Log "--- 清理 ---"
if (-not $proc.HasExited) { try { $proc.Kill(); Log "已终止启动器进程 PID $($proc.Id)" } catch { Log "终止失败: $($_.Exception.Message)" } }
Stop-Job $serveJob -EA SilentlyContinue; Remove-Job $serveJob -Force -EA SilentlyContinue
Log "清单服务已停止"

SaveLog 'phase2-upgrade.txt'
Log ""
Log "=== 阶段二完成 ==="
