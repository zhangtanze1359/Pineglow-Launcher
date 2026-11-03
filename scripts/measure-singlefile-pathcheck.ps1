# MR19 补测之二：单文件自包含 exe 是否受**构建路径**影响
#
# 为什么做：MR19 的单文件可复现性实验（run 20261003-150000Z）两次发布都在**同一路径**
# `D:\松光启动器` 下，报告明确把"跨路径"列为**未测**，并指出"单文件自包含的经典风险正是嵌入路径"。
# 本脚本关掉这一项。
#
# 做法（同一份源码、三个不同长度的路径，各发布一次）：
#   ① 就地发布          D:\松光启动器
#   ② 快照-短路径        <TEMP>\pgA\
#   ③ 快照-长路径        <TEMP>\pg-long-path-check-<32hex>\
#   快照内容 = git ls-files 的全部 tracked 文件 + 白名单 scripts/source-snapshot-inputs.txt 的 95 个未跟踪文件
#   （与 verify-source-snapshot.ps1 同一口径；**不复制**工作区里那 1200+ 个用户资产）。
#
# 判定：
#   三者 SHA 相同 → 路径**没有**被嵌入 → 可复现性可扩展到"同机、不同路径"
#   任一不同     → 定位首个不同字节并打印上下文（路径被嵌入就是这种表现）
#
# 已知副作用（必须守住）：RID 发布会改写**就地**项目的 packages.lock.json
# （8663 → 10868 字节）。脚本结束时从 MR19 证据的提交态副本还原并复核哈希。
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\measure-singlefile-pathcheck.ps1

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution',
    [string]$WorkRoot = (Join-Path $env:TEMP ('pineglow-pathcheck-' + [guid]::NewGuid().ToString('N'))),
    [switch]$SkipInPlace
)

$ErrorActionPreference = 'Stop'

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

function Get-FirstDiff([string]$pA, [string]$pB) {
    $ba = [IO.File]::ReadAllBytes($pA)
    $bb = [IO.File]::ReadAllBytes($pB)
    $n = [Math]::Min($ba.Length, $bb.Length)
    $first = -1; $count = 0
    for ($i = 0; $i -lt $n; $i++) {
        if ($ba[$i] -ne $bb[$i]) { if ($first -lt 0) { $first = $i }; $count++ }
    }
    return [pscustomobject]@{ LenA = $ba.Length; LenB = $bb.Length; FirstDiff = $first; DiffBytes = $count; Total = $n }
}

$project = Join-Path $RepoRoot 'dev\PineglowLauncher\Pineglow.Launcher.csproj'
$lockFile = Join-Path (Split-Path $project -Parent) 'packages.lock.json'
if (-not (Test-Path -LiteralPath $project)) { throw "找不到项目：$project" }
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

Write-Head '路径长度对照（本次实验的自变量）'
$paths = [ordered]@{
    'in-place'  = $RepoRoot
    'snap-short' = (Join-Path $env:TEMP 'pgA')
    'snap-long'  = (Join-Path $env:TEMP 'pg-long-path-check-0123456789abcdef0123456789abcdef')
}
foreach ($k in $paths.Keys) { Write-Host ("  {0,-11} {1,4} 字符  {2}" -f $k, $paths[$k].Length, $paths[$k]) }

$lockBefore = (Get-FileHash -LiteralPath $lockFile -Algorithm SHA256).Hash
Write-Host ("  就地锁文件基线：{0} bytes  SHA {1}" -f (Get-Item -LiteralPath $lockFile).Length, $lockBefore)

# ---------- 建快照 ----------
function New-Snapshot([string]$dest, [string]$label) {
    Write-Head ("建快照：{0} → {1}" -f $label, $dest)
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null

    $tracked = & git -C $RepoRoot ls-files
    $copied = 0
    foreach ($rel in $tracked) {
        $src = Join-Path $RepoRoot ($rel -replace '/', '\')
        if (-not (Test-Path -LiteralPath $src)) { continue }
        $dst = Join-Path $dest ($rel -replace '/', '\')
        New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $copied++
    }
    Write-Host ("  tracked 复制 {0} / {1}" -f $copied, $tracked.Count)

    $wlPath = Join-Path $RepoRoot 'scripts\source-snapshot-inputs.txt'
    $wl = Get-Content -LiteralPath $wlPath -Encoding UTF8
    $extra = 0; $missing = @()
    foreach ($line in $wl) {
        if ($line.Trim() -eq '' -or $line.StartsWith('#')) { continue }
        $rel = $line.Trim()
        $src = Join-Path $RepoRoot ($rel -replace '/', '\')
        if (-not (Test-Path -LiteralPath $src)) { $missing += $rel; continue }
        $dst = Join-Path $dest ($rel -replace '/', '\')
        New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $extra++
    }
    Write-Host ("  白名单复制 {0}；缺失 {1}" -f $extra, $missing.Count)
    if ($missing.Count -gt 0) { $missing | ForEach-Object { Write-Host ("    [缺失] {0}" -f $_) -ForegroundColor Red } }

    $bytes = (Get-ChildItem -LiteralPath $dest -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("  快照合计 {0} 个文件、{1:N1} MB" -f (Get-ChildItem -LiteralPath $dest -Recurse -File | Measure-Object).Count, ($bytes / 1MB))
    return [pscustomobject]@{ Dest = $dest; Tracked = $copied; Extra = $extra; Missing = $missing.Count }
}

# ---------- 发布 ----------
function Invoke-Publish([string]$workDir, [string]$outDir, [string]$label) {
    Write-Head ("发布：{0}" -f $label)
    $proj = Join-Path $workDir 'dev\PineglowLauncher\Pineglow.Launcher.csproj'
    if (-not (Test-Path -LiteralPath $proj)) { throw "快照里找不到项目：$proj" }
    # 清 bin/obj 保证独立构建（只清该快照内的目录）
    foreach ($sub in 'bin', 'obj') {
        $target = Join-Path (Split-Path $proj -Parent) $sub
        if (Test-Path -LiteralPath $target) {
            $resolved = (Resolve-Path -LiteralPath $target).Path
            if (-not $resolved.StartsWith($workDir, [StringComparison]::OrdinalIgnoreCase)) { throw "拒绝删除：$resolved" }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
    Push-Location $workDir
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $log = & dotnet publish $proj -c Release -r win-x64 --self-contained /p:PublishSingleFile=true -o $outDir 2>&1
        $exit = $LASTEXITCODE
        $sw.Stop()
    }
    finally { Pop-Location }

    $logDir = Join-Path $WorkRoot 'logs'
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $log | Set-Content -LiteralPath (Join-Path $logDir ("{0}.log" -f $label)) -Encoding UTF8

    $exe = Join-Path $outDir 'Pineglow.Launcher.exe'
    $exists = Test-Path -LiteralPath $exe
    $sha = if ($exists) { (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash } else { '(未产出)' }
    $size = if ($exists) { (Get-Item -LiteralPath $exe).Length } else { 0 }
    Write-Host ("  exit={0}  用时={1}s  exe={2} bytes  SHA {3}" -f $exit, [int]$sw.Elapsed.TotalSeconds, $size, $sha)
    if ($exit -ne 0) { $log | Select-Object -Last 12 | ForEach-Object { Write-Host ("    " + $_) -ForegroundColor DarkYellow } }
    return [pscustomobject]@{ Label = $label; WorkDir = $workDir; OutDir = $outDir; Exe = $exe; Sha = $sha; Size = $size; Exit = $exit }
}

# ---------- 执行 ----------
$results = @()

# ① 就地
if (-not $SkipInPlace) {
    $results += Invoke-Publish -workDir $RepoRoot -outDir (Join-Path $WorkRoot 'publish-inplace') -label 'in-place'
}
else {
    Write-Host '（-SkipInPlace：跳过就地发布，改为复用最近一次的单文件产物）'
}

# ② 短路径快照
$snapShort = New-Snapshot -dest $paths['snap-short'] -label 'snap-short'
$results += Invoke-Publish -workDir $paths['snap-short'] -outDir (Join-Path $WorkRoot 'publish-snap-short') -label 'snap-short'

# ③ 长路径快照
$snapLong = New-Snapshot -dest $paths['snap-long'] -label 'snap-long'
$results += Invoke-Publish -workDir $paths['snap-long'] -outDir (Join-Path $WorkRoot 'publish-snap-long') -label 'snap-long'

Write-Head '三方比对（单文件 exe）'
foreach ($r in $results) { Write-Host ("  {0,-11} {1} bytes  SHA {2}" -f $r.Label, $r.Size, $r.Sha) }
$shas = $results | Where-Object { $_.Exit -eq 0 } | Select-Object -ExpandProperty Sha -Unique
$allSame = ($shas.Count -eq 1) -and (($results | Where-Object { $_.Exit -eq 0 }).Count -eq $results.Count)
Write-Host ("  全部一致 = {0}（不同的 SHA 个数 = {1}）" -f $allSame, $shas.Count)

if (-not $allSame -and ($results | Where-Object { $_.Exit -eq 0 }).Count -ge 2) {
    Write-Head '差异定位（首个不同字节）'
    $ok = $results | Where-Object { $_.Exit -eq 0 }
    for ($i = 1; $i -lt $ok.Count; $i++) {
        $d = Get-FirstDiff $ok[0].Exe $ok[$i].Exe
        Write-Host ("  {0} vs {1}：长度 {2}/{3}；首个不同偏移={4}（0x{5:X}）；不同字节 {6}/{7}（{8:P4}）" -f `
            $ok[0].Label, $ok[$i].Label, $d.LenA, $d.LenB, $d.FirstDiff, [Math]::Max(0, $d.FirstDiff), $d.DiffBytes, $d.Total, ($d.DiffBytes / [Math]::Max(1, $d.Total)))
        if ($d.FirstDiff -ge 0) {
            $ba = [IO.File]::ReadAllBytes($ok[0].Exe); $bb = [IO.File]::ReadAllBytes($ok[$i].Exe)
            $from = [Math]::Max(0, $d.FirstDiff - 8); $to = [Math]::Min($d.Total - 1, $d.FirstDiff + 39)
            for ($k = $from; $k -le $to; $k += 16) {
                $end = [Math]::Min($k + 15, $to)
                Write-Host ("    {0:X8}  A: {1}" -f $k, (($ba[$k..$end] | ForEach-Object { $_.ToString('X2') }) -join ' '))
                Write-Host ("               B: {0}" -f (($bb[$k..$end] | ForEach-Object { $_.ToString('X2') }) -join ' '))
            }
        }
    }
}

Write-Head '守住副作用：就地锁文件'
$lockAfter = (Get-FileHash -LiteralPath $lockFile -Algorithm SHA256).Hash
if ($lockAfter -eq $lockBefore) { Write-Host ("  未被改写（{0} bytes）" -f (Get-Item -LiteralPath $lockFile).Length) }
else {
    Write-Host ("  已被改写：{0} bytes → {1} bytes" -f (Get-Item -LiteralPath $lockFile).Length, (Get-Item -LiteralPath $lockFile).Length) -ForegroundColor Yellow
    $reference = Join-Path $EvidenceRoot 'MR19-reproducible-build-rid\20261003-070000Z\red\launcher-packages.lock.json.before'
    Copy-Item -LiteralPath $reference -Destination $lockFile -Force
    Write-Host ("  已从提交态副本还原：{0} bytes  SHA {1}（与开始一致 = {2}）" -f (Get-Item -LiteralPath $lockFile).Length, (Get-FileHash -LiteralPath $lockFile -Algorithm SHA256).Hash, ((Get-FileHash -LiteralPath $lockFile -Algorithm SHA256).Hash -eq $lockBefore))
    Write-Host '  提示：请跑 dotnet test --filter BuildLockFileTests 复核不变量守卫。'
}

Write-Head '结论（只写实测）'
if ($allSame) {
    Write-Host '  同一份源码在三个不同长度的路径下发布，单文件 exe **逐字节一致** → 路径没有被嵌入。' -ForegroundColor Green
    Write-Host '  可复现性可扩展到：同机、同 SDK、**不同构建路径**。'
}
else {
    Write-Host '  不同路径下的产物**不一致** → 路径（或其它环境因素）被嵌入了，差异见上。' -ForegroundColor Yellow
}
Write-Host ("  工作目录保留在：{0}" -f $WorkRoot)
Write-Host '  仍未测：跨机器、跨 SDK、跨操作系统。'
