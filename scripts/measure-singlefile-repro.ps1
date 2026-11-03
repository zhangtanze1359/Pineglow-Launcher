# MR19 补测：win-x64 单文件自包含 exe 的逐字节可复现性
#
# 为什么单独做：MR19 当时只实测了**托管程序集**跨强制全量重建逐字节一致，
# 并明确把"单文件自包含发布的最终 exe"标为**未测**（理由：自包含单文件含 ILC/原生输出，
# 需要单独研究是否逐字节可复现、如何固定时间戳/路径）。本脚本关掉这个缺口。
#
# 做法（两次**完全独立**的发布，不是"增量 vs 全量"）：
#   1. 每次先把该项目的 bin/obj **整体删除**，保证第二次不复用任何第一次的中间产物；
#   2. dotnet publish -c Release -r win-x64 --self-contained /p:PublishSingleFile=true -o <独立目录>；
#   3. 记录产物清单与逐文件 SHA256；
#   4. 比对两次的 exe 与全部文件；若不同，定位**首个不同字节偏移**并统计差异分布，
#      以便判断差异是"时间戳/路径"这类已知来源，还是真的不可复现。
#
# 已知副作用（必须守住）：RID 发布会把 Microsoft.NET.ILLink.Tasks 注入共享的 net10.0 段，
# 从而**改写 packages.lock.json**（8663 → 10868 字节）。本脚本在开始时记录其哈希，
# 结束时若发现被改写，就从 MR19 证据里的 `red/launcher-packages.lock.json.before`
# （提交态、8663 字节）恢复，并提示跑 BuildLockFileTests 复核。
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\measure-singlefile-repro.ps1

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$OutRoot = (Join-Path $env:TEMP ('pineglow-mr19-singlefile-' + [guid]::NewGuid().ToString('N'))),
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution',
    [switch]$KeepOutputs
)

$ErrorActionPreference = 'Stop'

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

$project = Join-Path $RepoRoot 'dev\PineglowLauncher\Pineglow.Launcher.csproj'
$projDir = Split-Path $project -Parent
$lockFile = Join-Path $projDir 'packages.lock.json'

if (-not (Test-Path -LiteralPath $project)) { throw "找不到项目：$project" }
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null

Write-Head '开始状态（必须守住的副作用基线）'
$lockBefore = (Get-FileHash -LiteralPath $lockFile -Algorithm SHA256).Hash
$lockSizeBefore = (Get-Item -LiteralPath $lockFile).Length
Write-Host ("  packages.lock.json  {0} bytes  SHA {1}" -f $lockSizeBefore, $lockBefore)
Write-Host ("  输出根（系统临时目录，脚本自建）：{0}" -f $OutRoot)

$runs = @()
foreach ($n in 1..2) {
    Write-Head ("第 {0} 次发布（先整体删除 bin/obj）" -f $n)
    $outDir = Join-Path $OutRoot ("publish-{0}" -f $n)

    # 安全删除：只删该项目的 bin/obj，且先解析并打印绝对路径供核对
    foreach ($sub in 'bin', 'obj') {
        $target = Join-Path $projDir $sub
        if (Test-Path -LiteralPath $target) {
            $resolved = (Resolve-Path -LiteralPath $target).Path
            if (-not $resolved.StartsWith($projDir, [StringComparison]::OrdinalIgnoreCase)) {
                throw "拒绝删除：解析后的路径不在项目目录下 → $resolved"
            }
            Write-Host ("  删除 {0}" -f $resolved)
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }

    Push-Location $RepoRoot
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $log = & dotnet publish $project -c Release -r win-x64 --self-contained `
            /p:PublishSingleFile=true -o $outDir 2>&1
        $exit = $LASTEXITCODE
        $sw.Stop()
    }
    finally {
        Pop-Location
    }
    $log | Set-Content -LiteralPath (Join-Path $OutRoot ("publish-{0}.log" -f $n)) -Encoding UTF8

    $files = @()
    foreach ($f in (Get-ChildItem -LiteralPath $outDir -Recurse -File | Sort-Object FullName)) {
        $files += [pscustomobject]@{
            Rel  = $f.FullName.Substring($outDir.Length + 1)
            Sha  = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
            Size = $f.Length
        }
    }
    $exe = $files | Where-Object { $_.Rel -eq 'Pineglow.Launcher.exe' }
    Write-Host ("  exit={0}  用时={1}s  文件数={2}" -f $exit, [int]$sw.Elapsed.TotalSeconds, $files.Count)
    Write-Host ("  单文件 exe：{0} bytes  SHA {1}" -f $exe.Size, $exe.Sha)

    $runs += [pscustomobject]@{ Run = $n; Exit = $exit; Seconds = [int]$sw.Elapsed.TotalSeconds; OutDir = $outDir; Files = $files }
}

Write-Head '逐字节比对'
$a = $runs[0]; $b = $runs[1]
$exeA = ($a.Files | Where-Object { $_.Rel -eq 'Pineglow.Launcher.exe' })
$exeB = ($b.Files | Where-Object { $_.Rel -eq 'Pineglow.Launcher.exe' })
$exeIdentical = ($exeA.Sha -eq $exeB.Sha)

$namesA = $a.Files.Rel; $namesB = $b.Files.Rel
$onlyA = @($namesA | Where-Object { $namesB -notcontains $_ })
$onlyB = @($namesB | Where-Object { $namesA -notcontains $_ })
$common = @($namesA | Where-Object { $namesB -contains $_ })
$diffFiles = @()
foreach ($rel in $common) {
    $fa = $a.Files | Where-Object { $_.Rel -eq $rel }
    $fb = $b.Files | Where-Object { $_.Rel -eq $rel }
    if ($fa.Sha -ne $fb.Sha) { $diffFiles += [pscustomobject]@{ Rel = $rel; ShaA = $fa.Sha; ShaB = $fb.Sha; SizeA = $fa.Size; SizeB = $fb.Size } }
}

Write-Host ("  单文件 exe 逐字节一致 = {0}" -f $exeIdentical)
Write-Host ("  文件清单差异：仅第1次有 {0} 个；仅第2次有 {1} 个" -f $onlyA.Count, $onlyB.Count)
Write-Host ("  内容不同的文件数 = {0} / 共有文件 {1} 个" -f $diffFiles.Count, $common.Count)
foreach ($d in $diffFiles | Select-Object -First 12) {
    Write-Host ("    {0}`n      1: {1} ({2} bytes)`n      2: {2} → {3} ({4} bytes)" -f $d.Rel, $d.ShaA, $d.SizeA, $d.ShaB, $d.SizeB)
}

# 若 exe 不同，定位首个不同字节并统计差异分布（判断是否为时间戳/路径类差异）
$firstDiff = -1
$diffByteCount = 0
$totalBytes = 0
if (-not $exeIdentical) {
    Write-Head '差异定位（首个不同字节 + 差异分布）'
    $pA = Join-Path $a.OutDir 'Pineglow.Launcher.exe'
    $pB = Join-Path $b.OutDir 'Pineglow.Launcher.exe'
    $ba = [IO.File]::ReadAllBytes($pA)
    $bb = [IO.File]::ReadAllBytes($pB)
    $totalBytes = [Math]::Min($ba.Length, $bb.Length)
    for ($i = 0; $i -lt $totalBytes; $i++) {
        if ($ba[$i] -ne $bb[$i]) {
            if ($firstDiff -lt 0) { $firstDiff = $i }
            $diffByteCount++
        }
    }
    Write-Host ("  长度：1={0} bytes，2={1} bytes" -f $ba.Length, $bb.Length)
    Write-Host ("  首个不同字节偏移 = {0}（0x{1:X}）" -f $firstDiff, $firstDiff)
    Write-Host ("  不同字节数 = {0} / {1}（{2:P4}）" -f $diffByteCount, $totalBytes, ($diffByteCount / $totalBytes))
    # 打印首个差异附近的十六进制上下文，便于人判断像不像时间戳/路径
    if ($firstDiff -ge 0) {
        $from = [Math]::Max(0, $firstDiff - 16)
        $to = [Math]::Min($totalBytes - 1, $firstDiff + 47)
        Write-Host '  上下文（1 vs 2）：'
        for ($i = $from; $i -le $to; $i += 16) {
            $end = [Math]::Min($i + 15, $to)
            $h1 = ($ba[$i..$end] | ForEach-Object { $_.ToString('X2') }) -join ' '
            $h2 = ($bb[$i..$end] | ForEach-Object { $_.ToString('X2') }) -join ' '
            Write-Host ("    {0:X8}  1: {1}" -f $i, $h1)
            Write-Host ("              2: {0}" -f $h2)
        }
    }
}

Write-Head '守住副作用：packages.lock.json 是否被改写'
$lockAfter = (Get-FileHash -LiteralPath $lockFile -Algorithm SHA256).Hash
$lockSizeAfter = (Get-Item -LiteralPath $lockFile).Length
if ($lockAfter -eq $lockBefore) {
    Write-Host ("  未被改写（{0} bytes，SHA 一致）" -f $lockSizeAfter)
}
else {
    Write-Host ("  已被改写：{0} → {1} bytes" -f $lockSizeBefore, $lockSizeAfter) -ForegroundColor Yellow
    $reference = Join-Path $EvidenceRoot 'MR19-reproducible-build-rid\20261003-070000Z\red\launcher-packages.lock.json.before'
    if (Test-Path -LiteralPath $reference) {
        Copy-Item -LiteralPath $reference -Destination $lockFile -Force
        $restored = (Get-FileHash -LiteralPath $lockFile -Algorithm SHA256).Hash
        Write-Host ("  已从 MR19 证据的提交态副本恢复：{0} bytes  SHA {1}" -f (Get-Item -LiteralPath $lockFile).Length, $restored)
        Write-Host ("  恢复后与开始状态一致 = {0}" -f ($restored -eq $lockBefore))
        Write-Host '  提示：请跑 dotnet test --filter BuildLockFileTests 复核不变量守卫。'
    }
    else {
        Write-Host ("  找不到提交态副本，无法自动恢复：{0}" -f $reference) -ForegroundColor Red
    }
}

Write-Head '结论（只写实测，不写推测）'
if ($exeIdentical -and $diffFiles.Count -eq 0 -and $onlyA.Count -eq 0 -and $onlyB.Count -eq 0) {
    Write-Host '  两次完全独立的 win-x64 单文件自包含发布：产物**逐字节一致**。' -ForegroundColor Green
}
else {
    Write-Host '  两次完全独立的发布**不逐字节一致**；差异分布见上（不要据此宣称"可复现"）。' -ForegroundColor Yellow
}
Write-Host ("  输出保留在：{0}" -f $OutRoot)
if (-not $KeepOutputs) {
    Write-Host '  （如需保留请传 -KeepOutputs；默认保留，便于人工复核。）'
}
