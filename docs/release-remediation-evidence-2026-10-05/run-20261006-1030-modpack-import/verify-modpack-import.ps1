#Requires -Version 5.1
<#
.SYNOPSIS
    真实整合包导入端到端验收（R06「整合包导入」）：用**真实 Modrinth .mrpack** 走产品自己的导入接口，
    再逐项核对落盘结果（mods 哈希、原版版本、assets、以及被触发的 Java 自动安装）。

.DESCRIPTION
    为什么这一轮值得跑：整合包导入是 `JavaRuntimeService.InstallAsync`（产品唯一的 Java 自动安装路径）的
    **唯一调用点**，同时它会安装原版版本、下载 mods、应用 overrides —— 一次跑掉 R06 里三个子项。
    诚实边界：导入是**同步**的（HTTP 请求直到导入结束才返回），期间只有磁盘增长可作进度证据；
    "进游戏"依旧需要真机。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $MrpackPath,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120,
    [int] $ImportTimeoutSec = 3000
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-FreePort {
    for ($i = 0; $i -lt 60; $i++) {
        $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop()
        if ($p -gt 1024) { return $p }
    }
    throw '找不到空闲端口'
}
function Sha1File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA1).Hash.ToLower() }
# 注意（第 43 轮踩过）：StrictMode 下，空管道的 Measure-Object **没有 Sum 属性**，访问它会让脚本整段中断。
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

# ---------------------------------------------------------------- 1) 读 .mrpack 清单（作为验收基准）
Write-Host ''
Write-Host '== 真实整合包导入验收 ==' -ForegroundColor Cyan
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path
$zip = [IO.Compression.ZipFile]::OpenRead($pack)
$entry = $zip.Entries | Where-Object { $_.FullName -eq 'modrinth.index.json' }
$sr = New-Object IO.StreamReader($entry.Open()); $manifestText = $sr.ReadToEnd(); $sr.Dispose()
$overrideNames = @($zip.Entries | Where-Object { $_.FullName -like 'overrides/*' -and $_.Length -gt 0 } | ForEach-Object { $_.FullName })
$zip.Dispose()
$manifest = $manifestText | ConvertFrom-Json
$depMc = [string]$manifest.dependencies.minecraft
$depLoader = [string]$manifest.dependencies.'fabric-loader'
$manifestFiles = @($manifest.files)
$declaredBytes = ($manifestFiles | Measure-Object fileSize -Sum).Sum
Write-Host ("  包: {0}  ({1:N0} B)" -f (Split-Path $pack -Leaf), (Get-Item $pack).Length)
Write-Host ("  名称={0}  minecraft={1}  fabric-loader={2}  files={3} 声明体积={4:N1} MB  overrides 条目={5}" -f `
    $manifest.name, $depMc, $depLoader, $manifestFiles.Count, ($declaredBytes / 1MB), $overrideNames.Count)
Add-C '清单可解析且依赖齐全' (($depMc -and $depLoader) -ne $null -and $manifestFiles.Count -gt 0) "mc=$depMc loader=$depLoader files=$($manifestFiles.Count)"

# 官方原版元数据（核对版本安装）
$mcEntry = $null; $expClientSize = 0; $expClientSha1 = ''; $expAssetsId = ''; $expAssetsTotal = 0
try {
    $vm = Invoke-RestMethod 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json' -TimeoutSec 60
    $mcEntry = $vm.versions | Where-Object { $_.id -eq $depMc } | Select-Object -First 1
    if ($mcEntry) {
        $vdoc = Invoke-RestMethod $mcEntry.url -TimeoutSec 60
        $expClientSize = [int64]$vdoc.downloads.client.size
        $expClientSha1 = ([string]$vdoc.downloads.client.sha1).ToLower()
        $expAssetsId = [string]$vdoc.assetIndex.id
        $expAssetsTotal = [int64]$vdoc.assetIndex.totalSize
    }
} catch { }
Add-C '取到官方原版元数据（用于核对）' ($mcEntry -ne $null -and $expClientSize -gt 0) "client=$expClientSize assetsIndex=$expAssetsId"

# ---------------------------------------------------------------- 2) 启动候选产物
$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('松光 整合包导入 ' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'
$dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$port = Get-FreePort
$extract = Join-Path $runDir 'bundle-extract'
[void](New-Item -ItemType Directory -Path $extract -Force)
$oldPort = $env:PINEGLOW_API_PORT; $oldBase = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
$env:PINEGLOW_API_PORT = "$port"; $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extract
try {
    $proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru `
        -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
} finally {
    $env:PINEGLOW_API_PORT = $oldPort; $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $oldBase
}
$token = $null
$deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    if ((Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) -eq $null) { break }
    try {
        $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) {
            $html = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content
            if ($html -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }
            break
        }
    } catch { }
}
if (-not $token) { Write-Host '启动器未就绪' -ForegroundColor Red; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $token }
$gameDir = Join-Path $dataDir '.minecraft'
Write-Host "  运行目录: $cold"

# ---------------------------------------------------------------- 3) 后台上传导入（同步接口 → 用 job 以便轮询磁盘进度）
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
$opId = [guid]::NewGuid().ToString('N')
$payloadPath = Join-Path $runDir 'import-payload.json'
$respPath = Join-Path $runDir 'import-response.json'
$payload = [ordered]@{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64; operationId = $opId } | ConvertTo-Json -Compress
[IO.File]::WriteAllText($payloadPath, $payload, (New-Object Text.UTF8Encoding($false)))
Write-Host ''
Write-Host '  -- 触发 POST /api/instances/import（同步接口，后台执行 + 轮询磁盘增长）--' -ForegroundColor Yellow
$job = Start-Job -ScriptBlock {
    param($port, $token, $payloadPath, $respPath)
    $body = [IO.File]::ReadAllText($payloadPath)
    try {
        $r = Invoke-RestMethod "http://127.0.0.1:$port/api/instances/import" -Method Post -Headers @{ 'X-Pineglow-Token' = $token } `
            -ContentType 'application/json' -Body $body -TimeoutSec 3000
        [IO.File]::WriteAllText($respPath, ($r | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
    } catch {
        [IO.File]::WriteAllText($respPath, ("ERROR: " + $_.Exception.Message), (New-Object Text.UTF8Encoding($false)))
    }
} -ArgumentList $port, $token, $payloadPath, $respPath

$timeline = New-Object System.Collections.Generic.List[object]
$sw = [Diagnostics.Stopwatch]::StartNew()
$lastLog = -30
while ($sw.Elapsed.TotalSeconds -lt $ImportTimeoutSec) {
    if ($job.State -ne 'Running') { break }
    Start-Sleep -Seconds 5
    if ($sw.Elapsed.TotalSeconds - $lastLog -ge 30) {
        $lastLog = [int]$sw.Elapsed.TotalSeconds
        $bytes = 0
        $bytes = DirBytes $gameDir
        $rt = Join-Path $dataDir 'runtimes'
        $rtInfo = ''
        if (Test-Path $rt) { $rtInfo = "  runtimes=" + [math]::Round((DirBytes $rt) / 1MB, 1) + "MB" }
        $timeline.Add([pscustomobject]@{ sec = [int]$sw.Elapsed.TotalSeconds; gameDirMB = [math]::Round($bytes / 1MB, 1); runtimes = $rtInfo }) | Out-Null
        Write-Host ("     … {0,4}s  游戏目录={1,8:N1} MB{2}" -f [int]$sw.Elapsed.TotalSeconds, ($bytes / 1MB), $rtInfo) -ForegroundColor DarkGray
    }
}
$sw.Stop()
$jobState = $job.State
Remove-Job $job -Force
$importResponse = ''
if (Test-Path $respPath) { $importResponse = [IO.File]::ReadAllText($respPath) }
Write-Host ("  导入用时=$([int]$sw.Elapsed.TotalSeconds)s  job=$jobState")
Write-Host ("  响应: " + ($importResponse.Substring(0, [Math]::Min(300, $importResponse.Length))))
$importOk = $importResponse -match '"ok"\s*:\s*true'
Add-C '导入接口返回 ok=true' $importOk ($importResponse.Substring(0, [Math]::Min(200, $importResponse.Length)))

$instanceId = ''
if ($importOk) { try { $instanceId = [string](($importResponse | ConvertFrom-Json).instanceId) } catch { } }

# ---------------------------------------------------------------- 4) 核对落盘
Write-Host ''
Write-Host '  -- 核对落盘结果 --' -ForegroundColor Yellow
$instList = Invoke-RestMethod "http://127.0.0.1:$port/api/instances" -Headers $hdr -TimeoutSec 60
$inst = @($instList.instances) | Where-Object { $_.id -eq $instanceId } | Select-Object -First 1
if (-not $inst) { $inst = @($instList.instances) | Select-Object -Last 1 }
$instDir = [string]$inst.gameDirectory
Add-C '实例出现在 /api/instances' ($inst -ne $null) ("id=" + $inst.id + " name=" + $inst.name + " version=" + $inst.minecraftVersion)
Add-C '实例游戏目录存在' (Test-Path -LiteralPath $instDir) $instDir

# 4a) mods 逐文件哈希（清单里的 43 项）
$modOk = 0; $modBad = @(); $modMissing = @()
foreach ($mf in $manifestFiles) {
    $rel = ([string]$mf.path) -replace '/', '\'
    $p = Join-Path $instDir $rel
    if (-not (Test-Path -LiteralPath $p)) { $modMissing += $rel; continue }
    $want = ([string]$mf.hashes.sha1).ToLower()
    $got = Sha1File $p
    if ($got -eq $want) { $modOk++ } else { $modBad += "$rel got=$($got.Substring(0,8)) want=$($want.Substring(0,8))" }
}
Add-C '清单里每个 mod 都在且 SHA1 一致' (($modMissing.Count -eq 0) -and ($modBad.Count -eq 0)) ("ok=$modOk missing=$($modMissing.Count) bad=$($modBad.Count)" + $(if ($modMissing.Count) { " 缺失样例: " + ($modMissing | Select-Object -First 3 -join ', ') } else { '' }) + $(if ($modBad.Count) { " 不符样例: " + ($modBad | Select-Object -First 2 -join '; ') } else { '' }))

# 4b) overrides 是否应用（取前若干条存在性 + 体积）
$ovMissing = @()
$ovChecked = 0
foreach ($n in $overrideNames) {
    if ($ovChecked -ge 40) { break }
    $rel = $n -replace '^overrides/', '' -replace '/', '\'
    if (-not (Test-Path -LiteralPath (Join-Path $instDir $rel))) { $ovMissing += $rel }
    $ovChecked++
}
Add-C 'overrides 已应用到实例目录（抽查 40 条）' ($ovMissing.Count -eq 0) ("checked=$ovChecked missing=$($ovMissing.Count)" + $(if ($ovMissing.Count) { " 样例: " + ($ovMissing | Select-Object -First 3 -join ', ') } else { '' }))

# 4c) 原版版本安装（client.jar 大小+SHA1 与官方一致）
$jarCandidates = @(Get-ChildItem (Join-Path $instDir 'versions') -Recurse -File -Filter "$depMc.jar" -ErrorAction SilentlyContinue)
$jar = $jarCandidates | Select-Object -First 1
Add-C '原版 client.jar 落盘' ($jar -ne $null) ([string]$jar.FullName)
if ($jar -and $expClientSize -gt 0) {
    $sz = (Get-Item -LiteralPath $jar.FullName).Length
    $sh = Sha1File $jar.FullName
    Add-C 'client.jar 大小与官方一致' ($sz -eq $expClientSize) "got=$sz want=$expClientSize"
    Add-C 'client.jar SHA1 与官方一致' ($sh -eq $expClientSha1) ("got=" + $sh.Substring(0, 12) + " want=" + $expClientSha1.Substring(0, 12))
}
# 4d) assets 完整性（按 hash 逐对象，去重后比对）
$idxPath = Join-Path $instDir ("assets\indexes\$expAssetsId.json")
Add-C 'assets 索引落盘' (Test-Path -LiteralPath $idxPath) $idxPath
if ((Test-Path -LiteralPath $idxPath) -and $expAssetsId) {
    $idxDoc = [IO.File]::ReadAllText($idxPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $uniq = @{}
    foreach ($prop in $idxDoc.objects.PSObject.Properties) { $h = [string]$prop.Value.hash; if ($h -and -not $uniq.ContainsKey($h)) { $uniq[$h] = [int64]$prop.Value.size } }
    $objDir = Join-Path $instDir 'assets\objects'
    $miss = 0; $short = 0
    foreach ($h in $uniq.Keys) {
        $fp = Join-Path (Join-Path $objDir $h.Substring(0, 2)) $h
        if (-not (Test-Path -LiteralPath $fp)) { $miss++ } elseif ((Get-Item -LiteralPath $fp).Length -ne [int64]$uniq[$h]) { $short++ }
    }
    Add-C 'assets 每个对象都在且字节一致' (($miss -eq 0) -and ($short -eq 0)) "objects=$($uniq.Count) missing=$miss truncated=$short"
}
# 4e) 被触发的 Java 自动安装（产品唯一 InstallAsync 路径）
$rtDir = Join-Path $dataDir 'runtimes'
$javaExe = $null
if (Test-Path $rtDir) { $javaExe = Get-ChildItem $rtDir -Recurse -File -Filter 'java.exe' -ErrorAction SilentlyContinue | Select-Object -First 1 }
Add-C 'Java 被产品自动安装到 runtimes/' ($javaExe -ne $null) ([string]$javaExe.FullName)
$javaVersionText = ''
if ($javaExe) {
    $prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $javaVersionText = (& $javaExe.FullName -version 2>&1 | Out-String)
    $ErrorActionPreference = $prevEap
    Add-C '自动安装的 Java 可执行' ($javaVersionText -match 'version') (($javaVersionText -split "`n")[0].Trim())
}

$totalBytes = 0
$totalBytes = DirBytes $gameDir
$log = Get-ChildItem (Join-Path $dataDir 'logs') -File -Filter 'launcher-*.log' -ErrorAction SilentlyContinue | Select-Object -First 1
$logTail = @()
if ($log) { $logTail = @(Get-Content $log.FullName -Tail 80 | ForEach-Object { [string]$_ }) }

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    mrpack = [ordered]@{ name = $manifest.name; bytes = (Get-Item $pack).Length; minecraft = $depMc; fabricLoader = $depLoader; files = $manifestFiles.Count; declaredModBytes = $declaredBytes; overridesEntries = $overrideNames.Count }
    import = [ordered]@{ elapsedSec = [int]$sw.Elapsed.TotalSeconds; response = $importResponse; operationId = $opId }
    instance = [ordered]@{ id = $instanceId; name = [string]$inst.name; gameDirectory = $instDir; minecraftVersion = [string]$inst.minecraftVersion }
    mods = [ordered]@{ ok = $modOk; missing = $modMissing.Count; bad = $modBad.Count; samplesMissing = @($modMissing | Select-Object -First 5); samplesBad = @($modBad | Select-Object -First 5) }
    originalClient = [ordered]@{ size = $expClientSize; sha1 = $expClientSha1; gotSize = $(if ($jar) { (Get-Item -LiteralPath $jar.FullName).Length } else { 0 }); gotSha1 = $(if ($jar) { Sha1File $jar.FullName } else { '' }) }
    javaExe = [string]$javaExe
    javaVersionOutput = ($javaVersionText -split "`n" | Select-Object -First 2)
    gameDirBytes = $totalBytes
    timeline = $timeline
    checks = $checks
    failures = @($checks | Where-Object { -not $_.ok })
    logTail = $logTail
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 1
$fails = @($checks | Where-Object { -not $_.ok }).Count
Write-Host ''
if ($fails -eq 0) { Write-Host "整合包导入真实验收判定: PASS（$($checks.Count) 项 0 失败，导入 $([int]$sw.Elapsed.TotalSeconds)s）" -ForegroundColor Green; exit 0 }
Write-Host "整合包导入真实验收判定: FAIL（$fails 项失败，$($checks.Count) 项）" -ForegroundColor Red
exit 1
