# 实包验证 · 阶段一：构建产物与准备沙盒
# 说明：PowerShell 5.1 对无 BOM UTF-8 会误判编码，本文件必须带 BOM 保存。
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$V106Exe = 'D:/Projects/_worktrees/pg-e2e-v106-20261123/dev/PineglowLauncher/bin/Release/net10.0/win-x64/publish/Pineglow.Launcher.exe'
$V105Exe = 'D:/Projects/_worktrees/pg-e2e-v105-20261123/dev/PineglowLauncher/bin/Release/net10.0/win-x64/publish/Pineglow.Launcher.exe'

$Sandbox = 'D:/Projects/_worktrees/pg-e2e-sandbox-20261123'
if (Test-Path $Sandbox) { Remove-Item $Sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $Sandbox -Force | Out-Null

$log = New-Object System.Collections.Generic.List[string]
function Log($s) { $log.Add($s); Write-Host $s }

Log "=== 阶段一 开始 $(Get-Date -Format o) ==="
Log ""
Log "--- V2 产物自检 ---"

foreach ($pair in @(@('v1.0.6', $V106Exe), @('v1.0.5', $V105Exe))) {
    $ver = $pair[0]; $exe = $pair[1]
    if (-not (Test-Path $exe)) { throw "缺少产物: $exe" }
    $size = (Get-Item $exe).Length
    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash
    Log ("{0} exe: {1} bytes  SHA256={2}" -f $ver, $size, $hash)
}

# --- 沙盒：模拟用户机器上"正在运行的 v1.0.5" ---
Log ""
Log "--- V4 搭建沙盒 ---"
$RunDir = Join-Path $Sandbox 'run'
New-Item -ItemType Directory -Path $RunDir -Force | Out-Null

# 目标文件 = 真实 v1.0.5 二进制（不是替身），这样"替换后是否真换成 v1.0.6"才可验证
$TargetExe = Join-Path $RunDir 'Pineglow.Launcher.exe'
Copy-Item $V105Exe $TargetExe -Force
Set-Content -Path (Join-Path $RunDir 'portable.flag') -Value '' -NoNewline
Log ("目标(旧版) = 真实 v1.0.5 二进制, SHA256={0}" -f (Get-FileHash $TargetExe -Algorithm SHA256).Hash)
Log "portable.flag 已放置 -> 数据目录 = $RunDir\data"

$DataDir = Join-Path $RunDir 'data'
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
Log "数据目录: $DataDir"

# --- 待升级的"新版本包" ---
$PkgDir = Join-Path $Sandbox 'pkg'
New-Item -ItemType Directory -Path $PkgDir -Force | Out-Null
$PkgName = 'Pineglow-Launcher-1.0.6-win-x64-single-file.exe'
$Pkg = Join-Path $PkgDir $PkgName
Copy-Item $V106Exe $Pkg -Force
$PkgHash = (Get-FileHash $Pkg -Algorithm SHA256).Hash
$PkgSize = (Get-Item $Pkg).Length
Log ("新版本包: {0} ({1} bytes) SHA256={2}" -f $PkgName, $PkgSize, $PkgHash)

# --- 清单 ---
$Port = 49123
$Manifest = @{
    version     = '1.0.6'
    url         = "http://127.0.0.1:$Port/$PkgName"
    fallbackUrl = ''
    size        = $PkgSize
    sha256      = $PkgHash.ToLowerInvariant()
    notes       = 'E2E verification manifest'
    mandatory   = $false
} | ConvertTo-Json -Compress

$ServeRoot = Join-Path $Sandbox 'serve'
New-Item -ItemType Directory -Path $ServeRoot -Force | Out-Null
Copy-Item $Pkg (Join-Path $ServeRoot $PkgName) -Force
Set-Content -Path (Join-Path $ServeRoot 'update.json') -Value $Manifest -Encoding utf8
Log "清单: $Manifest"

# --- config.json 指向本地清单 ---
$Config = @{
    updateUrl     = "http://127.0.0.1:$Port/update.json"
    gameDirectory = (Join-Path $DataDir '.minecraft')
} | ConvertTo-Json -Compress
Set-Content -Path (Join-Path $DataDir 'config.json') -Value $Config -Encoding utf8
Log "config.json: $Config"

# --- 写阶段产物 ---
$outFile = Join-Path $PSScriptRoot 'raw/phase1-prepare.txt'
New-Item -ItemType Directory -Path (Split-Path $outFile) -Force | Out-Null
$log | Set-Content -Path $outFile -Encoding utf8
Log ""
Log "=== 阶段一完成 ==="
Log ("沙盒:   {0}" -f $RunDir)
Log ("服务根: {0}" -f $ServeRoot)
Log ("产物:   {0}" -f $outFile)
