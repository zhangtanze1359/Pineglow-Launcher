# OPEN-08 构建期注入验证
# 目的：确认三种构建配置下，CurseForge Key 的解析行为正确
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Wt = 'D:/Projects/_worktrees/pg-open08-20261124'
$log = New-Object System.Collections.Generic.List[string]
function Log($s) { $log.Add($s); Write-Host $s }

Log "=== OPEN-08 构建期注入验证 $(Get-Date -Format o) ==="
Log ""

$env:PATH = "C:\Windows\System32;C:\Windows;" + $env:PATH
$env:TEMP = "$Wt\.cache\tmp"; $env:TMP = $env:TEMP
Set-Location $Wt

# 场景 1：不带 Key 构建
Log "--- 场景 1：构建机未提供 Key ---"
Remove-Item Env:PINEGLOW_CURSEFORGE_BUILD_KEY -ErrorAction SilentlyContinue
$b1 = & dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release --no-incremental 2>&1
Log (($b1 | Select-String 'curseforge-key') | ForEach-Object { $_.Line.Trim() })
Log "构建退出码: $LASTEXITCODE"
$dll = Join-Path $Wt 'dev/PineglowLauncher/bin/Release/net10.0/Pineglow.Launcher.dll'
$bytes = [System.IO.File]::ReadAllBytes($dll)
$hasMeta = [System.Text.Encoding]::UTF8.GetString($bytes).Contains('PineglowCurseForgeApiKey')
Log "程序集含 Key 元数据: $hasMeta  (期望 False)"
Log ""

# 场景 2：带 Key 构建
Log "--- 场景 2：构建机提供 Key ---"
$testKey = 'VERIFYKEY0123456789ABCDEF'
$env:PINEGLOW_CURSEFORGE_BUILD_KEY = $testKey
$b2 = & dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release --no-incremental 2>&1
Log (($b2 | Select-String 'curseforge-key') | ForEach-Object { $_.Line.Trim() })
Log "构建退出码: $LASTEXITCODE"
$bytes2 = [System.IO.File]::ReadAllBytes($dll)
$txt2 = [System.Text.Encoding]::UTF8.GetString($bytes2)
Log "程序集含 Key 元数据键: $($txt2.Contains('PineglowCurseForgeApiKey'))  (期望 True)"
Log "程序集含 Key 值:       $($txt2.Contains($testKey))  (期望 True)"
Log ""

# 场景 3：跑 SecretHygieneTests（该场景下应全部通过）
Log "--- 场景 3：带 Key 时跑守卫测试 ---"
$t = & dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter "FullyQualifiedName~SecretHygieneTests" --logger "console;verbosity=minimal" 2>&1
Log (($t | Select-String '已通过!|失败:|通过数') | ForEach-Object { $_.Line.Trim() })
Log "测试退出码: $LASTEXITCODE"
Log ""

Remove-Item Env:PINEGLOW_CURSEFORGE_BUILD_KEY -ErrorAction SilentlyContinue

# 场景 4：源码树零字面量（再次确认，跨场景不变）
Log "--- 场景 4：源码树零 Key 字面量 ---"
$marker = [char]36 + '2a' + [char]36 + '10' + [char]36
$offenders = Get-ChildItem -Path (Join-Path $Wt 'dev/PineglowLauncher') -Recurse -Filter *.cs |
  Where-Object { $_.FullName -notmatch '/(bin|obj)/' } |
  Where-Object { (Get-Content $_.FullName -Raw) -match [regex]::Escape($marker) }
Log "命中文件数: $($offenders.Count)  (期望 0)"
if ($offenders) { $offenders | ForEach-Object { Log "  $($_.FullName)" } }
Log ""

$out = 'D:/Projects/松光启动器/docs/evidence/2026-11-24-open08/raw/build-injection-verify.txt'
New-Item -ItemType Directory -Path (Split-Path $out) -Force | Out-Null
$log | Set-Content -Path $out -Encoding utf8
Log "=== 完成，产物 $out ==="
