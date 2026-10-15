#Requires -Version 5.1
<#
.SYNOPSIS
    在**仓库之外的临时副本**里应用三个 blocker 的补丁并构建，然后跑复现测试（第 74 轮）。
.DESCRIPTION
    纪律：**绝不改工作区**。做法：把仓库（排除 .git/bin/obj/node_modules/docs）复制到 %TEMP%，
    在副本里 `git init` → `git apply` 三个补丁（+回归测试）→ `dotnet build -c Release`。
    之后由调用方用这个**打过补丁的构建**去跑复现测试。
#>
param(
    [Parameter(Mandatory)][string] $RepoRoot,
    [Parameter(Mandatory)][string] $DestRoot,
    [Parameter(Mandatory)][string] $PatchDir,
    [string] $JsonPath
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = if ($JsonPath) { Split-Path -Parent $JsonPath } else { $env:TEMP }
$logPath = Join-Path $outDir 'patchbuild-step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }

$repo = (Resolve-Path -LiteralPath $RepoRoot).Path
if (Test-Path $DestRoot) { Remove-Item -LiteralPath $DestRoot -Recurse -Force -EA SilentlyContinue }
[void](New-Item -ItemType Directory -Path $DestRoot -Force)
Log ("复制仓库（排除 .git/bin/obj/node_modules/docs）: $repo -> $DestRoot")
$rc = Start-Process -FilePath 'robocopy' -ArgumentList @($repo, $DestRoot, '/E', '/XD', '.git', 'bin', 'obj', 'node_modules', 'docs', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:1', '/W:1') -Wait -PassThru -WindowStyle Hidden
Log ("robocopy exit=" + $rc.ExitCode + "（0–7 视为成功）")
if ($rc.ExitCode -ge 8) { Log '复制失败'; exit 2 }

Push-Location $DestRoot
try {
    Log 'git init（仅为让 git apply 可用）'
    $g = & git init -q 2>&1; Log ("git init: " + ($g -join ' '))
    $patches = @(
        'proposed-fix-versionresolver.diff',
        'proposed-test-versionresolver.diff',
        'proposed-fix-installer-url.diff',
        'proposed-fix-configstore-default.diff'
    )
    foreach ($p in $patches) {
        $f = Join-Path $PatchDir $p
        if (-not (Test-Path $f)) { Log ("跳过（不存在）: " + $p); continue }
        $chk = & git apply --check $f 2>&1
        if ($LASTEXITCODE -ne 0) { Log ("❌ --check 失败: " + $p + " :: " + ($chk -join ' | ')); continue }
        $ap = & git apply $f 2>&1
        Log ("已应用 " + $p + $(if ($LASTEXITCODE -eq 0) { "（ok）" } else { "（失败: " + ($ap -join ' | ') + "）" }))
    }
    Log '构建（Release）'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $b = & dotnet build 'dev\PineglowLauncher\Pineglow.Launcher.csproj' -c Release --nologo 2>&1
    $code = $LASTEXITCODE
    $sw.Stop()
    Log ("build exit=" + $code + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s")
    ($b | Select-Object -Last 6) | ForEach-Object { Log ("  " + $_) }
    $out = Join-Path $DestRoot 'dev\PineglowLauncher\bin\Release\net10.0'
    $exe = Join-Path $out 'Pineglow.Launcher.exe'
    Log ("构建产物: " + $exe + " 存在=" + (Test-Path $exe))
    # 让这个构建以"便携模式"运行：在构建输出目录放 portable.flag（data/ 会落在同目录）
    [void](New-Item -ItemType File -Path (Join-Path $out 'portable.flag') -Force)
    Log ("已放 portable.flag（便携数据目录将落在 " + (Join-Path $out 'data') + "）")
    if ($JsonPath) {
        [ordered]@{
            generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
            destRoot = $DestRoot
            buildExit = $code
            exePath = $exe
            exeExists = (Test-Path $exe)
            dataDir = (Join-Path $out 'data')
        } | ConvertTo-Json -Depth 4 | Set-Content -Path $JsonPath -Encoding utf8
    }
    if ($code -ne 0) { exit 1 }
} finally { Pop-Location }
exit 0
