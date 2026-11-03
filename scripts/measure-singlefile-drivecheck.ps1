# MR19 补测之四：托管程序集是否也受路径影响？（并顺带判定 0x1AC 是否盘符效应）
#
# 为什么这个比"机制好奇心"重要：
#   MR19 的头条结论是"**托管程序集跨强制全量重建逐字节一致**"，但它的测法是
#     dotnet build -c Release
#     dotnet build -c Release --no-incremental
#   —— **两次都在同一路径**。也就是说，MR19 的头条结论**与单文件 exe 一样，只证明了"同路径"**。
#   本脚本补上"跨路径/跨盘"这一维。
#
# 设计（3 次构建，每次都清 bin/obj）：
#   A = <TEMP>\pgA        （长度 L，C 盘）
#   B = <TEMP>\pgB        （长度 L，C 盘，与 A 只差一个字符）—— 分离"路径内容"效应
#   C = D:\<L-3 个字符>   （长度 L，D 盘）              —— 分离"盘符/根"效应
#   三者长度相同，因此任何差异都只能来自"内容"或"盘符"，不来自长度。
#
# 记录三样产物：bin 里的托管程序集 .dll、单文件发布 .exe、以及 .pdb（作为对照）。
# 判读：
#   dllA == dllB == dllC  → 托管程序集与路径无关（MR19 的头条结论可跨路径成立）
#   dllA != dllB          → 托管程序集也嵌路径内容
#   dllA != dllC          → 盘符/根有影响
#   exeA vs exeC 的首个差异位置 → 若在 0x1AC，则支持"0x1AC 是盘符/根区域"的猜测
#
# 用法（本机只有 Windows PowerShell 5.1，没有 pwsh）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\measure-singlefile-drivecheck.ps1

param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$WorkRoot = (Join-Path $env:TEMP ('pineglow-drivecheck-' + [guid]::NewGuid().ToString('N'))),
    [switch]$KeepTrees
)

$ErrorActionPreference = 'Stop'

function Write-Head([string]$text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }

function Get-FirstDiff([string]$pA, [string]$pB) {
    $ba = [IO.File]::ReadAllBytes($pA); $bb = [IO.File]::ReadAllBytes($pB)
    $n = [Math]::Min($ba.Length, $bb.Length); $first = -1; $count = 0
    for ($i = 0; $i -lt $n; $i++) { if ($ba[$i] -ne $bb[$i]) { if ($first -lt 0) { $first = $i }; $count++ } }
    return [pscustomobject]@{ LenA = $ba.Length; LenB = $bb.Length; FirstDiff = $first; DiffBytes = $count; Total = $n }
}

function Move-WithRetry([string]$From, [string]$To, [int]$Tries = 12) {
    for ($i = 1; $i -le $Tries; $i++) {
        try {
            if (Test-Path -LiteralPath $To) { Remove-Item -LiteralPath $To -Recurse -Force }
            Move-Item -LiteralPath $From -Destination $To -Force -ErrorAction Stop
            Write-Host ("  移动成功（第 {0} 次尝试）" -f $i); return
        }
        catch {
            if ($i -eq $Tries) { throw }
            Write-Host ("  第 {0} 次移动被拒（MSBuild 句柄？），300ms 后重试" -f $i) -ForegroundColor DarkYellow
            & dotnet build-server shutdown *> $null; Start-Sleep -Milliseconds 300
        }
    }
}

New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null

# 三个等长路径
$leaf = 'pgA'
$lenTarget = (Join-Path $WorkRoot 'pgA').Length          # 基准长度 L
$dLeafLen = $lenTarget - 3                                # "D:\" 占 3 个字符
$dLeaf = 'd' * $dLeafLen
$pathA = Join-Path $WorkRoot 'pgA'
$pathB = Join-Path $WorkRoot 'pgB'
$pathC = 'D:\' + $dLeaf

Write-Head '三个等长路径（自变量）'
foreach ($p in @($pathA, $pathB, $pathC)) { Write-Host ("  {0,4} 字符  {1}" -f $p.Length, $p) }
if (($pathA.Length -ne $pathB.Length) -or ($pathA.Length -ne $pathC.Length)) { throw '三个路径长度必须相同' }
if (-not (Test-Path 'D:\')) { throw 'D: 盘不存在，无法做盘符对比' }

# ---------- 建一次快照 ----------
Write-Head '建快照（tracked + 白名单）'
$snap = $pathA
if (Test-Path -LiteralPath $snap) { Remove-Item -LiteralPath $snap -Recurse -Force }
New-Item -ItemType Directory -Force -Path $snap | Out-Null
$tracked = & git -C $RepoRoot ls-files
$n1 = 0
foreach ($rel in $tracked) {
    $src = Join-Path $RepoRoot ($rel -replace '/', '\')
    if (-not (Test-Path -LiteralPath $src)) { continue }
    $dst = Join-Path $snap ($rel -replace '/', '\')
    New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst -Force; $n1++
}
$wl = Get-Content -LiteralPath (Join-Path $RepoRoot 'scripts\source-snapshot-inputs.txt') -Encoding UTF8
$n2 = 0
foreach ($line in $wl) {
    if ($line.Trim() -eq '' -or $line.StartsWith('#')) { continue }
    $src = Join-Path $RepoRoot ($line.Trim() -replace '/', '\')
    if (-not (Test-Path -LiteralPath $src)) { continue }
    $dst = Join-Path $snap ($line.Trim() -replace '/', '\')
    New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst -Force; $n2++
}
Write-Host ("  源码输入 {0} 个（tracked {1} + 白名单 {2}）" -f ($n1 + $n2), $n1, $n2)

function Build-At([string]$dir, [string]$label, [string]$outDir) {
    Write-Head ("发布：{0}（{1} 字符）" -f $label, $dir.Length)
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
        $log = & dotnet publish $proj -c Release -r win-x64 --self-contained /p:PublishSingleFile=true /nodeReuse:false -o $outDir 2>&1
        $exit = $LASTEXITCODE
    }
    finally { Pop-Location }
    & dotnet build-server shutdown *> $null
    $logDir = Join-Path $WorkRoot 'logs'; New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $log | Set-Content -LiteralPath (Join-Path $logDir ("{0}.log" -f $label)) -Encoding UTF8

    $dll = Join-Path $dir 'dev\PineglowLauncher\bin\Release\net10.0\win-x64\Pineglow.Launcher.dll'
    $pdb = Join-Path $dir 'dev\PineglowLauncher\bin\Release\net10.0\win-x64\Pineglow.Launcher.pdb'
    $exe = Join-Path $outDir 'Pineglow.Launcher.exe'
    function Info($p) { if (Test-Path -LiteralPath $p) { [pscustomobject]@{ Path = $p; Sha = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash; Size = (Get-Item -LiteralPath $p).Length } } else { [pscustomobject]@{ Path = $p; Sha = '(未产出)'; Size = 0 } } }
    $iDll = Info $dll; $iPdb = Info $pdb; $iExe = Info $exe
    Write-Host ("  exit={0}" -f $exit)
    Write-Host ("  dll {0,9} bytes  {1}" -f $iDll.Size, $iDll.Sha)
    Write-Host ("  pdb {0,9} bytes  {1}" -f $iPdb.Size, $iPdb.Sha)
    Write-Host ("  exe {0,9} bytes  {1}" -f $iExe.Size, $iExe.Sha)
    return [pscustomobject]@{ Label = $label; Dir = $dir; Len = $dir.Length; Exit = $exit; Dll = $iDll; Pdb = $iPdb; Exe = $iExe }
}

# 建快照时先放到 stage（**不参与比较**的路径），再**复制**到三个被测路径。
# 为什么不用"重命名/移动"：实测 MSBuild 常驻节点会一直持有项目目录，
# 即使 build-server shutdown 后移动仍被拒（11 次重试全败）。复制一份只有 ~5MB，代价更低且更稳。
$stage = Join-Path $WorkRoot 'stage'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
Move-WithRetry -From $snap -To $stage

function Copy-Snapshot([string]$dest) {
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    # 注意：通配符必须用 -Path；-LiteralPath 会把 * 当字面量，导致复制 0 个文件（实测踩到）
    Copy-Item -Path (Join-Path $stage '*') -Destination $dest -Recurse -Force
    $bytes = (Get-ChildItem -LiteralPath $dest -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("  复制到 {0}（{1:N1} MB）" -f $dest, ($bytes / 1MB))
}

$res = @()
Copy-Snapshot -dest $pathA
$res += Build-At -dir $pathA -label 'A-C-pgA' -outDir (Join-Path $WorkRoot 'publish-A')
Copy-Snapshot -dest $pathB
$res += Build-At -dir $pathB -label 'B-C-pgB' -outDir (Join-Path $WorkRoot 'publish-B')
Copy-Snapshot -dest $pathC
$res += Build-At -dir $pathC -label 'C-D-pgD' -outDir (Join-Path $WorkRoot 'publish-C')

$A = $res[0]; $B = $res[1]; $C = $res[2]

Write-Head '判读 1：托管程序集（MR19 的头条结论）'
Write-Host ("  A {0}" -f $A.Dll.Sha)
Write-Host ("  B {0}" -f $B.Dll.Sha)
Write-Host ("  C {0}" -f $C.Dll.Sha)
$dllAB = ($A.Dll.Sha -eq $B.Dll.Sha); $dllAC = ($A.Dll.Sha -eq $C.Dll.Sha)
Write-Host ("  A == B（同盘、同长度、内容不同） = {0}" -f $dllAB) -ForegroundColor $(if ($dllAB) { 'Green' } else { 'Yellow' })
Write-Host ("  A == C（同长度、盘符不同）       = {0}" -f $dllAC) -ForegroundColor $(if ($dllAC) { 'Green' } else { 'Yellow' })
if ($dllAB -and $dllAC) { Write-Host '  → 托管程序集与路径**无关**：MR19 的头条结论可跨路径/跨盘成立。' -ForegroundColor Green }
elseif (-not $dllAB) { Write-Host '  → 托管程序集**嵌入了路径内容**：MR19 的头条结论同样只在同路径下成立。' -ForegroundColor Yellow }
else { Write-Host '  → 托管程序集对路径内容不敏感，但**受盘符/根影响**。' -ForegroundColor Yellow }

Write-Head '判读 2：单文件 exe 与 0x1AC'
Write-Host ("  A {0}" -f $A.Exe.Sha)
Write-Host ("  B {0}" -f $B.Exe.Sha)
Write-Host ("  C {0}" -f $C.Exe.Sha)
Write-Host ("  A == B = {0}    A == C = {1}" -f ($A.Exe.Sha -eq $B.Exe.Sha), ($A.Exe.Sha -eq $C.Exe.Sha))
foreach ($pair in @(@('A vs B（同盘、内容不同）', $A, $B), @('A vs C（跨盘、同长度）', $A, $C))) {
    if ($pair[1].Exe.Sha -eq $pair[2].Exe.Sha) { Write-Host ("  {0}：完全相同" -f $pair[0]); continue }
    $d = Get-FirstDiff $pair[1].Exe.Path $pair[2].Exe.Path
    Write-Host ("  {0}：首个差异 0x{1:X}，不同字节 {2}/{3}（{4:P4}）" -f $pair[0], [Math]::Max(0, $d.FirstDiff), $d.DiffBytes, $d.Total, ($d.DiffBytes / [Math]::Max(1, $d.Total)))
}

Write-Head '判读 3：pdb（对照，预期会嵌路径）'
Write-Host ("  A {0}" -f $A.Pdb.Sha)
Write-Host ("  B {0}" -f $B.Pdb.Sha)
Write-Host ("  C {0}" -f $C.Pdb.Sha)
Write-Host ("  A == B = {0}    A == C = {1}" -f ($A.Pdb.Sha -eq $B.Pdb.Sha), ($A.Pdb.Sha -eq $C.Pdb.Sha))
Write-Host '  说明：PDB 嵌入源文件路径是**预期行为**，与"二进制可复现"是两件事，不要混为一谈。'

Write-Head '清理 D 盘临时树'
if (-not $KeepTrees) {
    if (Test-Path -LiteralPath $pathC) {
        $r = (Resolve-Path -LiteralPath $pathC).Path
        $ok = $r.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase) -and ((Split-Path $r -Leaf) -eq $dLeaf)
        Write-Host ("  待删：{0}  在 D: 根下且名字匹配 = {1}" -f $r, $ok)
        if ($ok) {
            for ($i = 1; $i -le 10; $i++) {
                try { Remove-Item -LiteralPath $r -Recurse -Force -ErrorAction Stop; Write-Host ("  已删除（第 {0} 次尝试）" -f $i); break }
                catch {
                    if ($i -eq 10) { Write-Host ("  !! 10 次仍被拒（句柄未释放），请手工删除：{0}" -f $r) -ForegroundColor Red; break }
                    & dotnet build-server shutdown *> $null; Start-Sleep -Milliseconds 400
                }
            }
        }
        else { Write-Host '  !! 拒绝删除' -ForegroundColor Red }
    }
    else { Write-Host '  D 盘临时树不存在（可能已移动/删除）' }
}
else { Write-Host ("  -KeepTrees：保留 {0}" -f $pathC) }

Write-Head '结论边界'
Write-Host '本实验只回答"托管程序集/exe 是否受路径内容与盘符影响"，并给出差异位置。'
Write-Host '它不解释 0x1AC 或其它偏移处的具体字段语义。'
Write-Host ("工作目录保留在：{0}" -f $WorkRoot)
