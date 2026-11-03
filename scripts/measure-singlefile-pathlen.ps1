# MR19 补测之三：判别实验 —— 单文件 exe 的差异来自"路径字符串"还是"路径长度/对齐"
#
# 背景：run 20261003-160000Z 已实测"跨路径不可复现"（8/45/93 字符三个路径 → 三个不同 SHA），
# 但**机制未定**。该报告写明需要一次判别实验：
#   "用相同长度但不同内容的路径再发一次 —— 若'同长度不同内容'也导致不同，
#     则更像'路径字符串被嵌入'；若只有长度不同才不同，则更像'长度/对齐派生'。"
# 本脚本做这个判别实验。
#
# 设计（**同一棵快照树**，用重命名改变路径，避免三份 233MB 拷贝）：
#   P1 = <W>\pgA            （长度 L，内容 A）
#   P2 = <W>\pgB            （长度 L，内容 B —— 与 P1 只差一个字符）
#   P3 = <W>\pgAAAAAAAA     （长度 L+7，内容 C）
#   每次发布前都清 bin/obj，保证是独立构建。
#
# 判读（写进结论时必须照此表述）：
#   P1 == P2            → 同长度下"路径内容"不影响产物
#   P1 != P2            → **路径字符串被嵌入**（内容影响产物）
#   P2 != P3            → 长度（或长度派生的对齐/偏移）影响产物
#   P1 == P2 == P3      → 与 run 160000Z 的结论矛盾，必须复核
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\measure-singlefile-pathlen.ps1

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$WorkRoot = (Join-Path $env:TEMP ('pineglow-pathlen-' + [guid]::NewGuid().ToString('N'))),
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution'
)

$ErrorActionPreference = 'Stop'

function Write-Head([string]$text) {
    Write-Host ''
    Write-Host "== $text" -ForegroundColor Cyan
}

# 带重试的目录移动：MSBuild/Roslyn 句柄释放需要一点时间，直接 Rename 会被拒。
function Move-WithRetry([string]$From, [string]$To, [int]$Tries = 12) {
    for ($i = 1; $i -le $Tries; $i++) {
        try {
            if (Test-Path -LiteralPath $To) { Remove-Item -LiteralPath $To -Recurse -Force }
            Move-Item -LiteralPath $From -Destination $To -Force -ErrorAction Stop
            Write-Host ("  移动成功（第 {0} 次尝试）" -f $i)
            return
        }
        catch {
            if ($i -eq $Tries) { throw }
            Write-Host ("  第 {0} 次移动被拒（句柄未释放？），300ms 后重试" -f $i) -ForegroundColor DarkYellow
            & dotnet build-server shutdown *> $null
            Start-Sleep -Milliseconds 300
        }
    }
}
function Get-FirstDiff([string]$pA, [string]$pB) {
    $ba = [IO.File]::ReadAllBytes($pA); $bb = [IO.File]::ReadAllBytes($pB)
    $n = [Math]::Min($ba.Length, $bb.Length)
    $first = -1; $count = 0
    for ($i = 0; $i -lt $n; $i++) { if ($ba[$i] -ne $bb[$i]) { if ($first -lt 0) { $first = $i }; $count++ } }
    return [pscustomobject]@{ LenA = $ba.Length; LenB = $bb.Length; FirstDiff = $first; DiffBytes = $count; Total = $n }
}

if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'dev\PineglowLauncher\Pineglow.Launcher.csproj'))) { throw "找不到项目" }
New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

Write-Head '判别实验的三条路径（自变量）'
$p1 = Join-Path $WorkRoot 'pgA'
$p2 = Join-Path $WorkRoot 'pgB'
$p3 = Join-Path $WorkRoot 'pgAAAAAAAA'
foreach ($p in @($p1, $p2, $p3)) { Write-Host ("  {0,4} 字符  {1}" -f $p.Length, $p) }
Write-Host ("  对照（上一轮已测）：45 字符与 93 字符路径得到过**不同**的 SHA")

# ---------- 建一次快照 ----------
Write-Head '建快照（tracked + 白名单；只建一次，后面靠重命名改变路径）'
$snap = $p1
if (Test-Path -LiteralPath $snap) { Remove-Item -LiteralPath $snap -Recurse -Force }
New-Item -ItemType Directory -Force -Path $snap | Out-Null
$tracked = & git -C $RepoRoot ls-files
$copied = 0
foreach ($rel in $tracked) {
    $src = Join-Path $RepoRoot ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $src)) { continue }
    $dst = Join-Path $snap ($rel -replace '/', '\')
    New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst -Force; $copied++
}
$wl = Get-Content -LiteralPath (Join-Path $RepoRoot 'scripts\source-snapshot-inputs.txt') -Encoding UTF8
$extra = 0
foreach ($line in $wl) {
    if ($line.Trim() -eq '' -or $line.StartsWith('#')) { continue }
    $src = Join-Path $RepoRoot ($line.Trim() -replace '/', '\')
    if (-not (Test-Path -LiteralPath $src)) { continue }
    $dst = Join-Path $snap ($line.Trim() -replace '/', '\')
    New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst -Force; $extra++
}
Write-Host ("  tracked {0} + 白名单 {1} = {2} 个源码输入" -f $copied, $extra, ($copied + $extra))

# ---------- 在指定路径上发布 ----------
function Publish-At([string]$dir, [string]$label, [string]$outDir) {
    Write-Head ("发布：{0}（路径 {1} 字符）" -f $label, $dir.Length)
    $proj = Join-Path $dir 'dev\PineglowLauncher\Pineglow.Launcher.csproj'
    if (-not (Test-Path -LiteralPath $proj)) { throw "找不到项目：$proj" }
    foreach ($sub in 'bin', 'obj') {
        $t = Join-Path (Split-Path $proj -Parent) $sub
        if (Test-Path -LiteralPath $t) {
            $r = (Resolve-Path -LiteralPath $t).Path
            if (-not $r.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase)) { throw "拒绝删除：$r" }
            Remove-Item -LiteralPath $r -Recurse -Force
        }
    }
    Push-Location $dir
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $log = & dotnet publish $proj -c Release -r win-x64 --self-contained /p:PublishSingleFile=true -o $outDir 2>&1
        $exit = $LASTEXITCODE; $sw.Stop()
    }
    finally { Pop-Location }
    # MSBuild 常驻工作进程会一直持有该目录的句柄，导致后续重命名被拒（实测 "Access ... is denied"）。
    # 用官方方式关掉 build server，让句柄释放。
    & dotnet build-server shutdown *> $null
    $logDir = Join-Path $WorkRoot 'logs'; New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $log | Set-Content -LiteralPath (Join-Path $logDir ("{0}.log" -f $label)) -Encoding UTF8
    $exe = Join-Path $outDir 'Pineglow.Launcher.exe'
    $sha = if (Test-Path -LiteralPath $exe) { (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash } else { '(未产出)' }
    $size = if (Test-Path -LiteralPath $exe) { (Get-Item -LiteralPath $exe).Length } else { 0 }
    Write-Host ("  exit={0}  用时={1}s  {2} bytes  SHA {3}" -f $exit, [int]$sw.Elapsed.TotalSeconds, $size, $sha)
    return [pscustomobject]@{ Label = $label; Path = $dir; PathLen = $dir.Length; Exe = $exe; Sha = $sha; Size = $size; Exit = $exit }
}

$results = @()
$results += Publish-At -dir $p1 -label 'P1-pgA' -outDir (Join-Path $WorkRoot 'publish-P1')

# 重命名 P1 → P2（同长度、不同内容）
Write-Head '重命名：pgA → pgB（长度不变，内容变）'
Move-WithRetry -From $p1 -To $p2
Write-Host ("  {0} → {1}" -f $p1, $p2)
$results += Publish-At -dir $p2 -label 'P2-pgB' -outDir (Join-Path $WorkRoot 'publish-P2')

# 重命名 P2 → P3（长度 +7）
Write-Head '重命名：pgB → pgAAAAAAAA（长度 +7）'
Move-WithRetry -From $p2 -To $p3
Write-Host ("  {0} → {1}" -f $p2, $p3)
$results += Publish-At -dir $p3 -label 'P3-pgAAAAAAAA' -outDir (Join-Path $WorkRoot 'publish-P3')

Write-Head '判读'
foreach ($r in $results) { Write-Host ("  {0,-16} {1,4} 字符  {2} bytes  SHA {3}" -f $r.Label, $r.PathLen, $r.Size, $r.Sha) }
$p1r = $results[0]; $p2r = $results[1]; $p3r = $results[2]
$same12 = ($p1r.Sha -eq $p2r.Sha)
$same23 = ($p2r.Sha -eq $p3r.Sha)
Write-Host ''
Write-Host ("  P1 == P2（同长度、不同内容） = {0}" -f $same12) -ForegroundColor $(if ($same12) { 'Green' } else { 'Yellow' })
Write-Host ("  P2 == P3（同内容、长度 +7）   = {0}" -f $same23) -ForegroundColor $(if ($same23) { 'Green' } else { 'Yellow' })
Write-Host ''
if ($same12 -and -not $same23) {
    Write-Host '  → 同长度下"路径内容"不影响产物；只有**长度**不同才改变产物。' -ForegroundColor Green
    Write-Host '  → 机制更像"长度/对齐（或长度派生的偏移）被写入产物"，而不是"路径字符串被嵌入"。'
}
elseif (-not $same12) {
    Write-Host '  → **路径字符串被嵌入**：同样长度、只差一个字符也会改变产物。' -ForegroundColor Yellow
    Write-Host '  → 且 P2 与 P3 不同，说明长度同样有影响（两个因素都起作用）。'
}
elseif ($same12 -and $same23) {
    Write-Host '  → 三个路径产物全同，与 run 20261003-160000Z 的结论矛盾，必须复核。' -ForegroundColor Red
}

if (-not $same12) {
    Write-Head 'P1 vs P2 差异定位（同长度、不同内容）'
    $d = Get-FirstDiff $p1r.Exe $p2r.Exe
    Write-Host ("  长度 {0}/{1}；首个不同偏移={2}（0x{3:X}）；不同字节 {4}/{5}（{6:P4}）" -f $d.LenA, $d.LenB, $d.FirstDiff, [Math]::Max(0, $d.FirstDiff), $d.DiffBytes, $d.Total, ($d.DiffBytes / [Math]::Max(1, $d.Total)))
    if ($d.FirstDiff -ge 0) {
        $ba = [IO.File]::ReadAllBytes($p1r.Exe); $bb = [IO.File]::ReadAllBytes($p2r.Exe)
        $from = [Math]::Max(0, $d.FirstDiff - 8); $to = [Math]::Min($d.Total - 1, $d.FirstDiff + 39)
        for ($k = $from; $k -le $to; $k += 16) {
            $end = [Math]::Min($k + 15, $to)
            Write-Host ("    {0:X8}  P1: {1}" -f $k, (($ba[$k..$end] | ForEach-Object { $_.ToString('X2') }) -join ' '))
            Write-Host ("               P2: {0}" -f (($bb[$k..$end] | ForEach-Object { $_.ToString('X2') }) -join ' '))
        }
    }
}

Write-Head '守住副作用：工作区锁文件（本次不在就地发布，应当完全未被触碰）'
$lock = Join-Path $RepoRoot 'dev\PineglowLauncher\packages.lock.json'
Write-Host ("  {0} bytes  SHA {1}" -f (Get-Item -LiteralPath $lock).Length, (Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash)
$expected = 'FBBC9AE67B8E8CF2E3AA63C48242BC08AC713BE47830D337914CC39C85EE0672'
Write-Host ("  与提交态一致 = {0}" -f ((Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash -eq $expected))

Write-Head '结论边界'
Write-Host '本实验只回答"同长度不同内容是否改变产物"，据此**区分**两种机制方向。'
Write-Host '它**不**定位具体字段（PE 时间戳 / bundle 清单 / approot 路径都仍是候选）。'
Write-Host ("工作目录保留在：{0}" -f $WorkRoot)
