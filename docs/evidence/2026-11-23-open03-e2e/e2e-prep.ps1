# 实包验证脚本 · v1.0.5 → v1.0.6
# 真实二进制 + 真实 HTTP 清单 + 真实替换脚本
$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$V106Dir = 'D:/Projects/_worktrees/pg-e2e-v106-20261123/dev/PineglowLauncher/bin/Release/net10.0/win-x64/publish'
$V105Dir = 'D:/Projects/_worktrees/pg-e2e-v105-20261123/dev/PineglowLauncher/bin/Release/net10.0/win-x64/publish'
$V106Exe = Join-Path $V106Dir 'Pineglow.Launcher.exe'
$V105Exe = Join-Path $V105Dir 'Pineglow.Launcher.exe'

$Sandbox = 'D:/Projects/_worktrees/pg-e2e-sandbox-20261123'
if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $Sandbox -Force | Out-Null

$results = New-Object System.Collections.Generic.List[string]
function Log($s) { $results.Add($s); Write-Host $s }

Log "=== 实包验证 开始 $(Get-Date -Format o) ==="

# ---------- V2: 产物自检 ----------
Log ""
Log "--- V2 产物自检 ---"
$v106Size = (Get-Item $V106Exe).Length
$v105Size = (Get-Item $V105Exe).Length
$v106Hash = (Get-FileHash $V106Exe -Algorithm SHA256).Hash
$v105Hash = (Get-FileHash $V105Exe -Algorithm SHA256).Hash
Log "v1.0.6 exe: $v106Size bytes  SHA256=$v106Hash"
Log "v1.0.5 exe: $v105Size bytes  SHA256=$v105Hash"

# ---------- V3: 本地清单服务 ----------
Log ""
Log "--- V3 启动本地清单服务 ---"

# 沙盒：portable 模式，数据全在 exe 同目录
$RunDir = Join-Path $Sandbox 'run'
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
$TargetExe = Join-Path $RunDir 'Pineglow.Launcher.exe'

# 升级目标是测试替身：ping.exe 改名，模拟"正在运行的旧版"
Copy-Item 'C:/Windows/System32/ping.exe' $TargetExe -Force
# 放置 portable.flag → 数据目录 = $RunDir/data
Set-Content -Path (Join-Path $RunDir 'portable.flag') -Value '' -NoNewline

# 拷贝 v1.0.6 作为"新版本下载产物"
$NewPkg = Join-Path $Sandbox 'Pineglow-Launcher-1.0.6-win-x64-single-file.exe'
Copy-Item $V106Exe $NewPkg -Force
$NewPkgHash = (Get-FileHash $NewPkg -Algorithm SHA256).Hash
$NewPkgSize = (Get-Item $NewPkg).Length

# 清单：指向本地托管的 v1.0.6
$ServeRoot = Join-Path $Sandbox 'serve'
New-Item -ItemType Directory -Path $ServeRoot -Force | Out-Null
Copy-Item $NewPkg (Join-Path $ServeRoot 'Pineglow-Launcher-1.0.6-win-x64-single-file.exe') -Force

$port = 49123
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$port/")
$listener.Start()
Log "清单服务已启动: http://127.0.0.1:$port/"

# 清单内容必须在端口确定后写
$manifest = @{
    version     = '1.0.6'
    url         = "http://127.0.0.1:$port/Pineglow-Launcher-1.0.6-win-x64-single-file.exe"
    fallbackUrl = ''
    size        = $NewPkgSize
    sha256      = $NewPkgHash.ToLowerInvariant()
    notes       = 'E2E 验证用清单'
    mandatory   = $false
} | ConvertTo-Json -Compress
Set-Content -Path (Join-Path $ServeRoot 'update.json') -Value $manifest -Encoding utf8
Log "update.json: $manifest"

# 后台应答循环
$serveJob = Start-Job -ArgumentList $listener, $ServeRoot -ScriptBlock {
    param($l, $root)
    while ($l.IsListening) {
        try {
            $ctx = $l.GetContext()
            $rel = $ctx.Request.Url.AbsolutePath.TrimStart('/')
            $file = Join-Path $root $rel
            if (Test-Path $file -PathType Leaf) {
                $bytes = [System.IO.File]::ReadAllBytes($file)
                $ctx.Response.ContentType = if ($rel -like '*.json') { 'application/json' } else { 'application/octet-stream' }
                $ctx.Response.ContentLength64 = $bytes.Length
                $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                $ctx.Response.OutputStream.Close()
            } else {
                $ctx.Response.StatusCode = 404
                $ctx.Response.Close()
            }
        } catch { break }
    }
}

Start-Sleep -Seconds 1

# ---------- V4: 准备配置，指向本地清单 ----------
Log ""
Log "--- V4 准备便携配置，指向本地清单 ---"
$dataDir = Join-Path $RunDir 'data'
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
$config = @{
    updateUrl     = "http://127.0.0.1:$port/update.json"
    gameDirectory = (Join-Path $dataDir '.minecraft')
} | ConvertTo-Json -Compress
Set-Content -Path (Join-Path $dataDir 'config.json') -Value $config -Encoding utf8
Log "config.json: $config"

# 自检：清单可达（必须在服务仍在运行时检查）
try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port/update.json" -UseBasicParsing -TimeoutSec 10
    Log "清单可达: HTTP $($r.StatusCode), $($r.RawContentLength) bytes"
    $body = $r.Content
    Log "清单内容回读: $body"
} catch {
    Log "清单不可达: $($_.Exception.Message)"
}

$listener.Stop()
Stop-Job $serveJob -ErrorAction SilentlyContinue
Remove-Job $serveJob -Force -ErrorAction SilentlyContinue

$outFile = Join-Path $PSScriptRoot 'raw/e2e-prep.txt'
New-Item -ItemType Directory -Path (Split-Path $outFile) -Force | Out-Null
$results | Set-Content -Path $outFile -Encoding utf8
Log ""
Log "=== 准备阶段完成，产物见 $outFile ==="
