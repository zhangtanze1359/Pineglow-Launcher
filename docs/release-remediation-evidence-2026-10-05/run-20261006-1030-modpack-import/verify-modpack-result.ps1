#Requires -Version 5.1
<#
.SYNOPSIS
    **事后核对**模式：对已经跑完（或仍在跑）的导入结果做逐项验收。
    用于主脚本因自身缺陷中断、但**服务端导入仍在继续**的情况（第 43 轮就是这么救回来的）。

.DESCRIPTION
    只用三样东西：仍在运行的启动器端口、它的便携数据目录、以及原始 .mrpack（作为验收基准）。
    会话令牌从 `http://127.0.0.1:<port>/` 的 meta 标签现取，不需要重启任何东西。
#>
param(
    [Parameter(Mandatory)][int] $Port,
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][string] $MrpackPath,
    [string] $JsonPath,
    [int] $SettleSeconds = 60
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Sha1File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA1).Hash.ToLower() }
function DirBytes([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $items = @(Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue)
    if ($items.Count -eq 0) { return 0 }
    return [int64](($items | Measure-Object -Property Length -Sum).Sum)
}
$checks = New-Object System.Collections.Generic.List[object]
function Add-C($name, $ok, $detail) {
    $checks.Add([pscustomobject]@{ name = $name; ok = [bool]$ok; detail = $detail }) | Out-Null
    $c = if ($ok) { 'Green' } else { 'Red' }; $m = if ($ok) { '  [OK]  ' } else { '  [FAIL]' }
    Write-Host ("{0} {1} :: {2}" -f $m, $name, $detail) -ForegroundColor $c
}

$cold = (Resolve-Path -LiteralPath $ColdDir).Path
$dataDir = Join-Path $cold 'data'
$gameDir = Join-Path $dataDir '.minecraft'
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path

Write-Host ''
Write-Host '== 整合包导入结果事后核对 ==' -ForegroundColor Cyan
Write-Host "  端口=$Port  冷目录=$cold"

# ---- 令牌
$html = (Invoke-WebRequest "http://127.0.0.1:$Port/" -UseBasicParsing -TimeoutSec 20).Content
if ($html -notmatch 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { Write-Host '取不到会话令牌' -ForegroundColor Red; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $Matches[1] }

# ---- 等磁盘稳定（导入是同步的，服务端可能仍在写）
$prev = -1; $stable = 0; $sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 3600) {
    $now = DirBytes $gameDir
    if ($now -eq $prev) { $stable++ } else { $stable = 0 }
    Write-Host ("     {0,5}s  游戏目录={1,8:N1} MB  runtimes={2,7:N1} MB  稳定计数={3}" -f [int]$sw.Elapsed.TotalSeconds, ($now / 1MB), ((DirBytes (Join-Path $dataDir 'runtimes')) / 1MB), $stable) -ForegroundColor DarkGray
    if ($stable -ge 3 -and $sw.Elapsed.TotalSeconds -ge $SettleSeconds) { break }
    $prev = $now
    Start-Sleep -Seconds 20
}
$settleSec = [int]$sw.Elapsed.TotalSeconds
$gameDirBytes = DirBytes $gameDir
Add-C '导入已停止写盘（磁盘连续 3 次采样不变）' ($stable -ge 3) ("stableCount=$stable 采样 ${settleSec}s 后 gameDir=$([math]::Round($gameDirBytes/1MB,1))MB")

# ---- 基准：.mrpack 清单
$zip = [IO.Compression.ZipFile]::OpenRead($pack)
$entry = $zip.Entries | Where-Object { $_.FullName -eq 'modrinth.index.json' }
$sr = New-Object IO.StreamReader($entry.Open()); $manifestText = $sr.ReadToEnd(); $sr.Dispose()
$overrides = @($zip.Entries | Where-Object { $_.FullName -like 'overrides/*' -and $_.Length -gt 0 } | ForEach-Object { $_.FullName })
$zip.Dispose()
$manifest = $manifestText | ConvertFrom-Json
$depMc = [string]$manifest.dependencies.minecraft
$depLoader = [string]$manifest.dependencies.'fabric-loader'
$files = @($manifest.files)
Write-Host ("  包: {0}  minecraft={1} loader={2} files={3}（声明 {4:N1} MB）overrides={5}" -f $manifest.name, $depMc, $depLoader, $files.Count, (($files | Measure-Object fileSize -Sum).Sum / 1MB), $overrides.Count)

# ---- 实例
$list = Invoke-RestMethod "http://127.0.0.1:$Port/api/instances" -Headers $hdr -TimeoutSec 60
$inst = @($list.instances) | Select-Object -Last 1
Add-C '实例已登记到 /api/instances' ($inst -ne $null) ("count=" + @($list.instances).Count + " id=" + $inst.id + " name=" + $inst.name + " version=" + $inst.minecraftVersion)
$instDir = [string]$inst.gameDirectory
Add-C '实例游戏目录存在' (Test-Path -LiteralPath $instDir) $instDir

# ---- mods 逐文件 SHA1
$ok = 0; $missing = @(); $bad = @()
foreach ($mf in $files) {
    $rel = ([string]$mf.path) -replace '/', '\'
    $p = Join-Path $instDir $rel
    if (-not (Test-Path -LiteralPath $p)) { $missing += $rel; continue }
    $want = ([string]$mf.hashes.sha1).ToLower()
    $got = Sha1File $p
    if ($got -eq $want) { $ok++ } else { $bad += "$rel got=$($got.Substring(0,8)) want=$($want.Substring(0,8))" }
}
Add-C '清单里每个 mod 都在且 SHA1 一致' (($missing.Count -eq 0) -and ($bad.Count -eq 0)) ("ok=$ok missing=$($missing.Count) bad=$($bad.Count)" + $(if ($missing.Count) { "  缺失样例: " + (($missing | Select-Object -First 3) -join ', ') } else { '' }) + $(if ($bad.Count) { "  不符: " + (($bad | Select-Object -First 2) -join '; ') } else { '' }))

# ---- overrides
$ovMissing = @(); $ovChecked = 0
foreach ($n in $overrides) { if ($ovChecked -ge 60) { break }; $rel = $n -replace '^overrides/', '' -replace '/', '\'; if (-not (Test-Path -LiteralPath (Join-Path $instDir $rel))) { $ovMissing += $rel }; $ovChecked++ }
Add-C 'overrides 已应用（抽查 60 条）' ($ovMissing.Count -eq 0) ("checked=$ovChecked missing=$($ovMissing.Count)" + $(if ($ovMissing.Count) { "  样例: " + (($ovMissing | Select-Object -First 3) -join ', ') } else { '' }))

# ---- 原版版本
$expSize = 0; $expSha = ''; $expAssetsId = ''
try {
    $vm = Invoke-RestMethod 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json' -TimeoutSec 60
    $e = $vm.versions | Where-Object { $_.id -eq $depMc } | Select-Object -First 1
    if ($e) { $vd = Invoke-RestMethod $e.url -TimeoutSec 60; $expSize = [int64]$vd.downloads.client.size; $expSha = ([string]$vd.downloads.client.sha1).ToLower(); $expAssetsId = [string]$vd.assetIndex.id }
} catch { }
$jar = @(Get-ChildItem (Join-Path $instDir 'versions') -Recurse -File -Filter "$depMc.jar" -ErrorAction SilentlyContinue) | Select-Object -First 1
Add-C '原版 client.jar 落盘' ($jar -ne $null) ([string]$jar.FullName)
$jarSize = 0; $jarSha = ''
if ($jar) { $jarSize = (Get-Item -LiteralPath $jar.FullName).Length; $jarSha = Sha1File $jar.FullName
    if ($expSize -gt 0) {
        Add-C 'client.jar 大小与官方一致' ($jarSize -eq $expSize) "got=$jarSize want=$expSize"
        Add-C 'client.jar SHA1 与官方一致' ($jarSha -eq $expSha) ("got=" + $jarSha.Substring(0, 12) + " want=" + $expSha.Substring(0, 12))
    } }

# ---- assets 逐对象
$idxPath = Join-Path $instDir ("assets\indexes\$expAssetsId.json")
Add-C 'assets 索引落盘' (Test-Path -LiteralPath $idxPath) $idxPath
$assetStat = @{ objects = 0; missing = 0; truncated = 0 }
if ((Test-Path -LiteralPath $idxPath) -and $expAssetsId) {
    $idxDoc = [IO.File]::ReadAllText($idxPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $uniq = @{}
    foreach ($prop in $idxDoc.objects.PSObject.Properties) { $h = [string]$prop.Value.hash; if ($h -and -not $uniq.ContainsKey($h)) { $uniq[$h] = [int64]$prop.Value.size } }
    $objDir = Join-Path $instDir 'assets\objects'
    foreach ($h in $uniq.Keys) {
        $fp = Join-Path (Join-Path $objDir $h.Substring(0, 2)) $h
        if (-not (Test-Path -LiteralPath $fp)) { $assetStat.missing++ } elseif ((Get-Item -LiteralPath $fp).Length -ne [int64]$uniq[$h]) { $assetStat.truncated++ }
    }
    $assetStat.objects = $uniq.Count
    Add-C 'assets 每个对象都在且字节一致' (($assetStat.missing -eq 0) -and ($assetStat.truncated -eq 0)) "objects=$($assetStat.objects) missing=$($assetStat.missing) truncated=$($assetStat.truncated)"
}

# ---- 加载器
$loaderDirs = @(Get-ChildItem (Join-Path $instDir 'versions') -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
Add-C '加载器版本目录存在（fabric）' (($loaderDirs -join ' ') -match 'fabric') ("versions: " + ($loaderDirs -join ', '))

# ---- Java 自动安装
$rtDir = Join-Path $dataDir 'runtimes'
$javaExe = Get-ChildItem $rtDir -Recurse -File -Filter 'java.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
Add-C 'Java 被产品自动安装到 runtimes/' ($javaExe -ne $null) ([string]$javaExe.FullName)
$javaOut = ''
if ($javaExe) {
    $prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $javaOut = (& $javaExe.FullName -version 2>&1 | Out-String)
    $ErrorActionPreference = $prevEap
    Add-C '自动安装的 Java 可执行' ($javaOut -match 'version') (($javaOut -split "`n")[0].Trim())
}

# ---- 库与 mods 计数
$libJars = @(Get-ChildItem (Join-Path $instDir 'libraries') -Recurse -File -Filter '*.jar' -ErrorAction SilentlyContinue)
$modJars = @(Get-ChildItem (Join-Path $instDir 'mods') -Recurse -File -Filter '*.jar' -ErrorAction SilentlyContinue)
Add-C 'libraries 有 jar' ($libJars.Count -gt 0) "jars=$($libJars.Count)"
Add-C 'mods 目录 jar 数 ≥ 清单数' ($modJars.Count -ge $files.Count) ("mods=$($modJars.Count) 清单=$($files.Count)")

$log = Get-ChildItem (Join-Path $dataDir 'logs') -File -Filter 'launcher-*.log' -ErrorAction SilentlyContinue | Select-Object -First 1
$logTail = @(); if ($log) { $logTail = @(Get-Content $log.FullName -Tail 60 | ForEach-Object { [string]$_ }) }

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    mode = 'verify-only（主脚本中断后的事后核对）'
    coldDir = $cold
    port = $Port
    settleSeconds = $settleSec
    mrpack = [ordered]@{ name = $manifest.name; bytes = (Get-Item $pack).Length; minecraft = $depMc; fabricLoader = $depLoader; files = $files.Count; overridesEntries = $overrides.Count }
    instance = [ordered]@{ id = $inst.id; name = [string]$inst.name; gameDirectory = $instDir; minecraftVersion = [string]$inst.minecraftVersion }
    mods = [ordered]@{ ok = $ok; missing = $missing.Count; bad = $bad.Count; samplesMissing = @($missing | Select-Object -First 5); samplesBad = @($bad | Select-Object -First 5) }
    originalClient = [ordered]@{ expectedSize = $expSize; expectedSha1 = $expSha; gotSize = $jarSize; gotSha1 = $jarSha }
    assets = $assetStat
    loaderVersionDirs = $loaderDirs
    modJarCount = $modJars.Count
    libraryJarCount = $libJars.Count
    javaExe = [string]$javaExe
    javaVersionOutput = ($javaOut -split "`n" | Select-Object -First 2)
    gameDirBytes = $gameDirBytes
    checks = $checks
    failures = @($checks | Where-Object { -not $_.ok })
    logTail = $logTail
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

$fails = @($checks | Where-Object { -not $_.ok }).Count
Write-Host ''
if ($fails -eq 0) { Write-Host "整合包导入结果核对判定: PASS（$($checks.Count) 项 0 失败）" -ForegroundColor Green; exit 0 }
Write-Host "整合包导入结果核对判定: FAIL（$fails / $($checks.Count) 项失败）" -ForegroundColor Red
exit 1
