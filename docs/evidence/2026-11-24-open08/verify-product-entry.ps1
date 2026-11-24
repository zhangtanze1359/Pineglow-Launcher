# OPEN-08 验收项 1：通过**产品自身入口**验证 CurseForge 下载
#
# 与之前那次验证的区别（第 3 轮复核指出的关键缺陷）：
#   之前用 Python 直调 CurseForge API —— 那只验证了"Key 有效"，没验证"产品能用这个 Key 下载"。
#   本次走产品自己的 LocalApiService：GET /api/content/search → POST /api/content/install，
#   并记录候选 EXE 的 SHA-256，把证据绑定到这个具体的发布产物。
#
# 前置：需一个在构建期注入了真实 Key 的 win-x64 发布产物（由 prepare 步骤产出）。
#
# 安全：本脚本不读取也不打印任何 Key 值。产品从构建期元数据里自己取。
param(
  [Parameter(Mandatory = $true)][string]$ExePath,
  [string]$Sandbox = 'D:/Projects/_worktrees/pg-o8-auth'
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Api = 'http://127.0.0.1:48123'
$log = New-Object System.Collections.Generic.List[string]
function Log($s) { $log.Add($s); Write-Host $s }

Log "=== OPEN-08 产品入口验证 $(Get-Date -Format o) ==="
Log ""

if (-not (Test-Path $ExePath)) { Log "FAIL: 找不到 EXE"; exit 1 }

# --- 绑定候选：EXE 哈希 ---
$exeHash = (Get-FileHash $ExePath -Algorithm SHA256).Hash
$exeSize = (Get-Item $ExePath).Length
Log "--- 候选产物 ---"
Log "EXE: $ExePath"
Log "大小: $exeSize bytes"
Log "SHA-256: $exeHash"
Log ""

# --- 沙盒 ---
if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
$RunDir = Join-Path $Sandbox 'run'
$DataDir = Join-Path $RunDir 'data'
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null

$TargetExe = Join-Path $RunDir 'Pineglow.Launcher.exe'
Copy-Item $ExePath $TargetExe -Force
Set-Content -Path (Join-Path $RunDir 'portable.flag') -Value '' -NoNewline

# 关键：**不配置 CurseForgeApiKey**。产品必须自己用构建期注入的 Key。
# 这是本轮要证明的核心——"用户零配置也能用"。
$cfg = @{ gameDirectory = (Join-Path $DataDir '.minecraft') } | ConvertTo-Json -Compress
Set-Content -Path (Join-Path $DataDir 'config.json') -Value $cfg -Encoding utf8
Log "config.json（**故意不含 CurseForgeApiKey**）: $cfg"
Log ""

# --- 启动产品 ---
Log "--- 启动产品产物 ---"
$proc = Start-Process -FilePath $TargetExe -WorkingDirectory $RunDir -PassThru
Log "PID = $($proc.Id)"

$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    try { $h = Invoke-RestMethod -Uri "$Api/api/health" -TimeoutSec 3; $ready = $true; break } catch { }
}
Log "API 就绪 = $ready (等 $([math]::Round($i*0.5,1)) s)"
if (-not $ready) { Log "FAIL: API 未就绪"; $log | Set-Content (Join-Path $PSScriptRoot 'raw/product-entry-curseforge.txt') -Encoding utf8; exit 1 }
Log ""

# --- 取 session token ---
$html = (Invoke-WebRequest -Uri "$Api/" -UseBasicParsing -TimeoutSec 15).Content
$m = [regex]::Match($html, 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"')
$token = if ($m.Success) { $m.Groups[1].Value } else { '' }
$hdr = @{ 'X-Pineglow-Token' = $token }
Log "session token = $(if($token){'已获取'}else{'未获取'})"
Log ""

$verdict = 'FAIL'

if ($token) {
    # --- 1. 产品搜索 CurseForge ---
    Log "--- 1. GET /api/content/search?source=curseforge（产品入口，零配置）---"
    $url = "$Api/api/content/search?q=jei&source=curseforge&category=mod"
    try {
        $res = Invoke-RestMethod -Uri $url -Headers $hdr -TimeoutSec 40
        $json = $res | ConvertTo-Json -Compress -Depth 6
        Log "  响应: $($json.Substring(0, [Math]::Min(400, $json.Length)))"
        $items = @($res.results)
        Log "  返回条目数: $($items.Count)"
        if ($items.Count -gt 0) {
            $first = $items[0]
            Log "  首条: id=$($first.id) name=$($first.name) source=$($first.source)"
        }
    } catch {
        Log "  搜索失败: $($_.Exception.Message)"
    }
    Log ""

    # --- 2. 产品安装（真实下载）---
    Log "--- 2. POST /api/content/install（真实下载）---"
    try {
        $inst = Invoke-RestMethod -Uri "$Api/api/content/install" -Method Post -Headers $hdr `
          -ContentType 'application/json' -TimeoutSec 60 `
          -Body (@{ source='curseforge'; projectId=238222; category='mod' } | ConvertTo-Json -Compress)
        Log "  响应: $($inst | ConvertTo-Json -Compress -Depth 5)"
    } catch {
        Log "  安装请求失败: $($_.Exception.Message)"
        Log "  （若返回 400 curseforge_key_required，说明产品没拿到 Key）"
    }
    Log ""

    # --- 3. 等下载落盘 ---
    Log "--- 3. 观察落盘（轮询 mods 目录）---"
    $modsDir = Join-Path $DataDir '.minecraft/mods'
    for ($i = 0; $i -lt 120; $i++) {
        Start-Sleep -Seconds 1
        if (Test-Path $modsDir) {
            $files = Get-ChildItem $modsDir -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -eq '.jar' }
            if ($files) {
                foreach ($f in $files) {
                    Log ("  落盘: {0} ({1} bytes) SHA256={2}" -f $f.Name, $f.Length, (Get-FileHash $f.FullName -Algorithm SHA256).Hash)
                }
                $verdict = 'PASS'
                break
            }
        }
    }
    if ($verdict -ne 'PASS') { Log "  等待超时，未观察到 .jar 落盘" }
}

Log ""
Log "--- 产品日志（如有）---"
$lg = Join-Path $DataDir 'logs'
if (Test-Path $lg) {
    Get-ChildItem $lg -Filter *.log | ForEach-Object {
        Log "--- $($_.Name)"
        Get-Content $_.FullName | Select-Object -Last 25 | ForEach-Object { Log $_ }
    }
}

Log ""
Log "--- 清理 ---"
if (-not $proc.HasExited) { try { $proc.Kill(); Log "已终止 PID $($proc.Id)" } catch { } }

Log ""
Log "RESULT: $verdict"
Log "（本验证：EXE=$exeHash；产品零配置 CurseForgeApiKey）"

$out = Join-Path $PSScriptRoot 'raw/product-entry-curseforge.txt'
New-Item -ItemType Directory -Path (Split-Path $out) -Force | Out-Null
$log | Set-Content -Path $out -Encoding utf8
Log ""
Log "=== 完成，产物 $out ==="
if ($verdict -ne 'PASS') { exit 1 }
