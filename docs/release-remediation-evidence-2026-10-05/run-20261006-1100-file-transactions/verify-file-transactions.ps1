#Requires -Version 5.1
<#
.SYNOPSIS
    文件层的"替换/回滚/备份/恢复"真实验收（R06「Mod/存档：替换与回滚、实例级备份恢复」的**文件层**）。

.DESCRIPTION
    这些端点不依赖游戏进程，只依赖磁盘与产品自己的实现，所以可以在没有真机的机器上跑实：
      · 存档：`POST /saves/{folder}/backup`、`GET .../backups`、`POST .../backups/{name}/restore`
      · Mod：`POST /mods/{file}/delete`（**可恢复删除**）、`POST /recovery/{opId}/restore`、
             `POST /mods/{file}/replace`（**事务化替换：新包先校验，再隔离旧包，失败回滚**）
    每一相都做**字节级**核对（SHA256），并用**反证**排除"假成功"。
    诚实边界：这证明的是**文件层事务与恢复行为**确实生效；"在游戏里看到效果"仍属真机项。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-FreePort {
    for ($i = 0; $i -lt 60; $i++) {
        $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop()
        if ($p -gt 1024) { return $p }
    }
    throw '找不到空闲端口'
}
function Sha256File([string]$Path) { if (-not (Test-Path -LiteralPath $Path)) { return '' }; (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLower() }
function New-JarLike([string]$Path, [string]$Marker) {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    $fs = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    try {
        $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $e = $zip.CreateEntry('META-INF/MANIFEST.MF')
            $w = New-Object IO.StreamWriter($e.Open())
            $w.WriteLine('Manifest-Version: 1.0'); $w.WriteLine('Marker: ' + $Marker); $w.Dispose()
            $e2 = $zip.CreateEntry('payload.txt')
            $w2 = New-Object IO.StreamWriter($e2.Open()); $w2.WriteLine('marker=' + $Marker); $w2.Dispose()
        } finally { $zip.Dispose() }
    } finally { $fs.Dispose() }
}
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

# ---------------------------------------------------------------- 启动候选产物
$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('松光 文件事务 ' + [guid]::NewGuid().ToString('N'))
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
function Api([string]$Method, [string]$Path, $Body) {
    $u = "http://127.0.0.1:$port$Path"
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod $u -Method $Method -Headers $hdr -ContentType 'application/json' -Body $json -TimeoutSec 120
        } else { $r = Invoke-RestMethod $u -Method $Method -Headers $hdr -TimeoutSec 120 }
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6) }
    } catch {
        $resp = $_.Exception.Response; $code = 0; $txt = $_.Exception.Message
        if ($resp) {
            $code = [int]$resp.StatusCode
            try { $rd = New-Object System.IO.StreamReader($resp.GetResponseStream()); $txt = $rd.ReadToEnd(); $rd.Dispose() } catch { }
        }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $txt }
    }
}

Write-Host ''
Write-Host '== 文件层替换/回滚/备份/恢复 真实验收 ==' -ForegroundColor Cyan
Write-Host "  产物=$exe  端口=$port"

# ---------------------------------------------------------------- 建实例
$create = Api 'POST' '/api/instances' '{"name":"文件事务验收","version":"1.18.2"}'
Add-C '创建实例' ($create.ok) ($create.text.Substring(0, [Math]::Min(160, $create.text.Length)))
$instId = ''
if ($create.ok) { $instId = [string]$create.body.instance.id }
$state = Api 'GET' '/api/state' $null
$inst = @($state.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$instDir = [string]$inst.gameDirectory
Add-C '实例游戏目录可用' (Test-Path -LiteralPath $instDir) $instDir

$savesDir = Join-Path $instDir 'saves'
$modsDir = Join-Path $instDir 'mods'
[void](New-Item -ItemType Directory -Path $savesDir -Force)
[void](New-Item -ItemType Directory -Path $modsDir -Force)

# ================================================================ A) 存档：备份 → 改动 → 恢复
Write-Host ''
Write-Host '  -- A 存档备份 / 恢复 --' -ForegroundColor Yellow
$world = 'TestWorld'
$wdir = Join-Path $savesDir $world
[void](New-Item -ItemType Directory -Path (Join-Path $wdir 'region') -Force)
$levelDat = Join-Path $wdir 'level.dat'
$region = Join-Path (Join-Path $wdir 'region') 'r.0.0.mca'
[IO.File]::WriteAllBytes($levelDat, [Text.Encoding]::UTF8.GetBytes('LEVEL-DAT-ORIGINAL-v1'))
[IO.File]::WriteAllBytes($region, (1..4096 | ForEach-Object { [byte]($_ % 251) }))
$origLevelSha = Sha256File $levelDat
$origRegionSha = Sha256File $region
$origBytes = DirBytes $wdir

$worlds = Api 'GET' '/api/worlds' $null
Add-C '存档被产品识别（/api/worlds 列出 TestWorld）' (($worlds.text -match [regex]::Escape($world))) ($worlds.text.Substring(0, [Math]::Min(200, $worlds.text.Length)))

$bk = Api 'POST' "/api/instances/$instId/saves/$world/backup" '{}'
Add-C '创建存档备份' ($bk.ok) ($bk.text.Substring(0, [Math]::Min(180, $bk.text.Length)))
$bkList = Api 'GET' "/api/instances/$instId/saves/$world/backups" $null
Add-C '备份列表可读且含条目' ($bkList.ok -and ($bkList.text -match 'backup')) ($bkList.text.Substring(0, [Math]::Min(220, $bkList.text.Length)))
$backupName = ''
if ($bkList.ok) {
    $item = @($bkList.body.backups) | Select-Object -First 1
    if ($item) { $backupName = [string]$item.name }
    if (-not $backupName) { $backupName = [string]$bkList.body.items[0].name }
}
Write-Host ("     备份名=$backupName")

# 改动：覆盖 level.dat、删掉 region、加一个多余文件
[IO.File]::WriteAllBytes($levelDat, [Text.Encoding]::UTF8.GetBytes('LEVEL-DAT-CORRUPTED-v2'))
Remove-Item -LiteralPath $region -Force
[IO.File]::WriteAllBytes((Join-Path $wdir 'extra.tmp'), [Text.Encoding]::UTF8.GetBytes('extra'))
Add-C '改动已生效（为恢复做准备）' ((Sha256File $levelDat) -ne $origLevelSha -and -not (Test-Path -LiteralPath $region)) "level.dat 已改、region 已删"

if ($backupName) {
    $rs = Api 'POST' "/api/instances/$instId/saves/$world/backups/$backupName/restore" '{}'
    Add-C '恢复存档备份' ($rs.ok) ($rs.text.Substring(0, [Math]::Min(180, $rs.text.Length)))
    Add-C '恢复后 level.dat 字节与备份前一致' ((Sha256File $levelDat) -eq $origLevelSha) ("got=" + (Sha256File $levelDat).Substring(0, 12) + " want=" + $origLevelSha.Substring(0, 12))
    Add-C '恢复后被删的 region 文件回来了且字节一致' ((Test-Path -LiteralPath $region) -and (Sha256File $region) -eq $origRegionSha) ([string]$region)
}
# 反证：恢复一个不存在的备份必须失败
$bad = Api 'POST' "/api/instances/$instId/saves/$world/backups/__no_such_backup__/restore" '{}'
Add-C '反证：恢复不存在的备份必须失败' (-not $bad.ok) ("status=" + $bad.status + " " + $bad.text.Substring(0, [Math]::Min(120, $bad.text.Length)))

# ================================================================ B) Mod 可恢复删除 → 恢复
Write-Host ''
Write-Host '  -- B Mod 可恢复删除 / 恢复 --' -ForegroundColor Yellow
$modA = 'AcceptanceMod.jar'
$modPath = Join-Path $modsDir $modA
New-JarLike $modPath 'ORIGINAL-A'
$modASha = Sha256File $modPath
$del = Api 'POST' "/api/instances/$instId/mods/$modA/delete" '{}'
Add-C '删除 Mod（可恢复）' ($del.ok) ($del.text.Substring(0, [Math]::Min(220, $del.text.Length)))
Add-C '删除后文件确实离开原位置' (-not (Test-Path -LiteralPath $modPath)) ([string]$modPath)
$opId = ''
if ($del.ok) {
    foreach ($k in 'operationId', 'opId', 'id', 'recoveryId') { if ($del.body.PSObject.Properties.Name -contains $k) { $opId = [string]$del.body.$k; break } }
    if (-not $opId -and $del.body.record) { $opId = [string]$del.body.record.operationId }
}
Write-Host ("     恢复操作 id=$opId")
if ($opId) {
    $rst = Api 'POST' "/api/instances/$instId/recovery/$opId/restore" '{}'
    Add-C '执行恢复' ($rst.ok) ($rst.text.Substring(0, [Math]::Min(180, $rst.text.Length)))
    Add-C '恢复后 Mod 字节与删除前一致' ((Test-Path -LiteralPath $modPath) -and (Sha256File $modPath) -eq $modASha) ("got=" + (Sha256File $modPath).Substring(0, 12) + " want=" + $modASha.Substring(0, 12))
} else {
    Add-C '取到恢复操作 id' $false '删除响应里没有 operationId（需要按实际字段名调整）'
}

# ================================================================ C) Mod 事务化替换：坏包必须回滚
Write-Host ''
Write-Host '  -- C Mod 事务化替换（坏包必须回滚）--' -ForegroundColor Yellow
$modB = 'ReplaceMe.jar'
$modBPath = Join-Path $modsDir $modB
New-JarLike $modBPath 'ORIGINAL-B'
$modBSha = Sha256File $modBPath
# 坏包：一个扩展名合法但内容不是 ZIP 的"jar"，且 expectedSha256 与实际一致（考验内容校验而非哈希不匹配）
$badSrc = Join-Path $runDir 'not-a-jar.jar'
[IO.File]::WriteAllBytes($badSrc, [Text.Encoding]::UTF8.GetBytes('this is definitively not a zip archive'))
$badB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($badSrc))
$badSha = Sha256File $badSrc
$rep = Api 'POST' "/api/instances/$instId/mods/$modB/replace" (@{ newFileBase64 = $badB64; expectedSha256 = $badSha } | ConvertTo-Json -Compress)
Add-C '坏包替换被拒绝' (-not $rep.ok) ("status=" + $rep.status + " " + $rep.text.Substring(0, [Math]::Min(200, $rep.text.Length)))
Add-C '被拒绝后原文件仍在且字节未变（回滚生效）' ((Test-Path -LiteralPath $modBPath) -and (Sha256File $modBPath) -eq $modBSha) ("got=" + (Sha256File $modBPath).Substring(0, 12) + " want=" + $modBSha.Substring(0, 12))
# 正例：用合法新包替换成功
$goodSrc = Join-Path $runDir 'good-new.jar'
New-JarLike $goodSrc 'NEW-B'
$goodB64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($goodSrc))
$goodSha = Sha256File $goodSrc
$rep2 = Api 'POST' "/api/instances/$instId/mods/$modB/replace" (@{ newFileBase64 = $goodB64; expectedSha256 = $goodSha } | ConvertTo-Json -Compress)
Add-C '合法新包替换成功' ($rep2.ok) ($rep2.text.Substring(0, [Math]::Min(160, $rep2.text.Length)))
Add-C '替换后文件字节=新包（且与旧包不同）' ((Sha256File $modBPath) -eq $goodSha -and $goodSha -ne $modBSha) ("got=" + (Sha256File $modBPath).Substring(0, 12) + " want=" + $goodSha.Substring(0, 12))

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    instanceId = $instId
    instanceGameDir = $instDir
    save = [ordered]@{ world = $world; originalBytes = $origBytes; levelDatSha = $origLevelSha; regionSha = $origRegionSha; backupName = $backupName; backupResponse = $bk.text; listResponse = $bkList.text }
    modRecovery = [ordered]@{ deleted = $modA; operationId = $opId; deleteResponse = $del.text }
    modReplace = [ordered]@{ badRejected = (-not $rep.ok); badResponse = $rep.text; goodOk = $rep2.ok; goodResponse = $rep2.text; oldSha = $modBSha; newSha = $goodSha }
    checks = $checks
    failures = @($checks | Where-Object { -not $_.ok })
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 1
$fails = @($checks | Where-Object { -not $_.ok }).Count
Write-Host ''
if ($fails -eq 0) { Write-Host "文件层事务/恢复验收判定: PASS（$($checks.Count) 项 0 失败）" -ForegroundColor Green; exit 0 }
Write-Host "文件层事务/恢复验收判定: FAIL（$fails / $($checks.Count) 项失败）" -ForegroundColor Red
exit 1
