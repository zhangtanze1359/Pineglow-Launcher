#Requires -Version 5.1
<#
.SYNOPSIS
    文件层"替换/回滚/备份/恢复"真实验收（v2：**逐步落盘日志 + 短超时**，专治"卡住看不到位置"）。

.DESCRIPTION
    v1 在一次运行中卡死且没有分步输出（stdout 只在进程结束后才拿到）。v2 改成：
      · 每一步（含每次 API 调用前后）都 **立即追加**到 <JsonPath 同目录>\step.log；
      · 每个请求 30 秒超时，超时**记为失败并继续**，不再无限等待；
      · 请求体显式用 UTF-8 字节（PS 5.1 直接传字符串会把中文变成 `??????`，v1 踩过）；
      · 实例名用 ASCII，避免无关编码问题干扰判定。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120,
    [int] $CallTimeoutSec = 30
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$logPath = if ($JsonPath) { Join-Path (Split-Path -Parent $JsonPath) 'step.log' } else { Join-Path $env:TEMP 'filetx-step.log' }
$logDir = Split-Path -Parent $logPath
if (-not (Test-Path $logDir)) { [void](New-Item -ItemType Directory -Path $logDir -Force) }
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$msg) {
    $line = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $msg
    [IO.File]::AppendAllText($logPath, $line + "`r`n", (New-Object Text.UTF8Encoding($false)))
    Write-Host $line
}

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
            foreach ($n in 'META-INF/MANIFEST.MF', 'payload.txt') {
                $e = $zip.CreateEntry($n); $w = New-Object IO.StreamWriter($e.Open()); $w.WriteLine('marker=' + $Marker); $w.Dispose()
            }
        } finally { $zip.Dispose() }
    } finally { $fs.Dispose() }
}

$checks = New-Object System.Collections.Generic.List[object]
function Add-C($name, $ok, $detail) {
    $checks.Add([pscustomobject]@{ name = $name; ok = [bool]$ok; detail = $detail }) | Out-Null
    Log (("{0} {1} :: {2}" -f $(if ($ok) { '[OK]  ' } else { '[FAIL]' }), $name, $detail))
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('filetx-' + [guid]::NewGuid().ToString('N'))
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
Log "启动器已拉起 pid=$($proc.Id) port=$port runDir=$runDir"
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
if (-not $token) { Log '启动器未就绪，退出'; exit 2 }
Log '会话令牌已取得'
$hdr = @{ 'X-Pineglow-Token' = $token }

function Api([string]$Method, [string]$Path, $Body) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Log ("  -> {0} {1}" -f $Method, $Path)
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $bytes = [Text.Encoding]::UTF8.GetBytes($json)
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body $bytes -TimeoutSec $CallTimeoutSec
        } else {
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $CallTimeoutSec
        }
        $sw.Stop()
        Log ("  <- 200 {0} {1}ms" -f $Path, [int]$sw.Elapsed.TotalMilliseconds)
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6) }
    } catch {
        $sw.Stop()
        $resp = $_.Exception.Response; $code = 0; $txt = $_.Exception.Message
        if ($resp) { $code = [int]$resp.StatusCode; try { $rd = New-Object System.IO.StreamReader($resp.GetResponseStream()); $txt = $rd.ReadToEnd(); $rd.Dispose() } catch { } }
        Log ("  <- status={0} {1}ms {2}" -f $code, [int]$sw.Elapsed.TotalMilliseconds, $txt.Substring(0, [Math]::Min(170, $txt.Length)))
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $txt }
    }
}

Log '== 文件层事务/恢复验收 v2 开始 =='
$create = Api 'POST' '/api/instances' '{"name":"filetx-acceptance","version":"1.18.2"}'
Add-C '创建实例' ($create.ok) ($create.text.Substring(0, [Math]::Min(160, $create.text.Length)))
$instId = ''; if ($create.ok) { $instId = [string]$create.body.instance.id }
$state = Api 'GET' '/api/state' $null
$inst = @($state.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$instDir = [string]$inst.gameDirectory
Add-C '实例游戏目录可用' (Test-Path -LiteralPath $instDir) $instDir
$savesDir = Join-Path $instDir 'saves'; $modsDir = Join-Path $instDir 'mods'
[void](New-Item -ItemType Directory -Path $savesDir -Force); [void](New-Item -ItemType Directory -Path $modsDir -Force)

Log '-- A 存档备份/恢复 --'
$world = 'TestWorld'; $wdir = Join-Path $savesDir $world
[void](New-Item -ItemType Directory -Path (Join-Path $wdir 'region') -Force)
$levelDat = Join-Path $wdir 'level.dat'; $region = Join-Path (Join-Path $wdir 'region') 'r.0.0.mca'
[IO.File]::WriteAllBytes($levelDat, [Text.Encoding]::UTF8.GetBytes('LEVEL-DAT-ORIGINAL-v1'))
[IO.File]::WriteAllBytes($region, (1..4096 | ForEach-Object { [byte]($_ % 251) }))
$origLevelSha = Sha256File $levelDat; $origRegionSha = Sha256File $region
$worlds = Api 'GET' '/api/worlds' $null
Add-C '存档被产品识别' ($worlds.text -match [regex]::Escape($world)) ($worlds.text.Substring(0, [Math]::Min(180, $worlds.text.Length)))
$bk = Api 'POST' "/api/instances/$instId/saves/$world/backup" '{}'
Add-C '创建存档备份' ($bk.ok) ($bk.text.Substring(0, [Math]::Min(180, $bk.text.Length)))
$bkList = Api 'GET' "/api/instances/$instId/saves/$world/backups" $null
$backupName = ''
# 注意（第 44 轮踩过）：真实字段是 `fileName`（不是 `name`）；StrictMode 下访问不存在的属性会**直接中断脚本**，
# 所以这里逐个用 PSObject.Properties 判定，而不是想当然地取 .name。
if ($bkList.ok) {
    $it = @($bkList.body.backups) | Select-Object -First 1
    if ($it) {
        foreach ($k in 'fileName', 'name', 'file') { if ($it.PSObject.Properties.Name -contains $k) { $backupName = [string]$it.$k; break } }
    }
}
Add-C '备份列表含条目' ($bkList.ok -and $backupName -ne '') ("name=$backupName raw=" + $bkList.text.Substring(0, [Math]::Min(140, $bkList.text.Length)))
[IO.File]::WriteAllBytes($levelDat, [Text.Encoding]::UTF8.GetBytes('LEVEL-DAT-CORRUPTED-v2'))
Remove-Item -LiteralPath $region -Force
[IO.File]::WriteAllBytes((Join-Path $wdir 'extra.tmp'), [Text.Encoding]::UTF8.GetBytes('extra'))
Log '  已制造改动（level.dat 改写、region 删除、新增 extra.tmp）'
if ($backupName) {
    $rs = Api 'POST' "/api/instances/$instId/saves/$world/backups/$backupName/restore" '{}'
    Add-C '恢复存档备份' ($rs.ok) ($rs.text.Substring(0, [Math]::Min(200, $rs.text.Length)))
    # 第 44 轮更正：产品的"恢复"是**非破坏式导入**（SaveBackupService.RestoreBackup → SaveService.ImportSave），
    # 目标目录已存在时会写成 `TestWorld (1)`，**不覆盖正在用的存档**。所以核对要跟着返回的 path 走，
    # 而不是断言"原地被改写"。同时对"直播存档未被覆盖"单独断言（这正是数据安全属性）。
    $restored = ''
    if ($rs.ok) { foreach ($k in 'path', 'restoredPath', 'folder') { if ($rs.body.PSObject.Properties.Name -contains $k) { $restored = [string]$rs.body.$k; break } } }
    Add-C '恢复返回了落盘路径' ($restored -ne '') $restored
    if ($restored -and (Test-Path -LiteralPath $restored)) {
        $rl = Join-Path $restored 'level.dat'
        $rr = Join-Path (Join-Path $restored 'region') 'r.0.0.mca'
        Add-C '恢复出的 level.dat 字节 = 备份前' ((Test-Path -LiteralPath $rl) -and (Sha256File $rl) -eq $origLevelSha) ("got=" + (Sha256File $rl).Substring(0, 12))
        Add-C '恢复出的 region 字节 = 备份前' ((Test-Path -LiteralPath $rr) -and (Sha256File $rr) -eq $origRegionSha) ([string]$rr)
        Add-C '非破坏式：正在使用的存档未被覆盖' ((Sha256File $levelDat) -ne $origLevelSha) ("live level.dat=" + (Sha256File $levelDat).Substring(0, 12))
    }
}
$bad = Api 'POST' "/api/instances/$instId/saves/$world/backups/__no_such__/restore" '{}'
Add-C '反证：恢复不存在的备份必须失败' (-not $bad.ok) ("status=" + $bad.status)

Log '-- B Mod 可恢复删除/恢复 --'
$modA = 'AcceptanceMod.jar'; $modPath = Join-Path $modsDir $modA
New-JarLike $modPath 'ORIGINAL-A'; $modASha = Sha256File $modPath
$del = Api 'POST' "/api/instances/$instId/mods/$modA/delete" '{}'
Add-C '删除 Mod（可恢复）' ($del.ok) ($del.text.Substring(0, [Math]::Min(240, $del.text.Length)))
Add-C '删除后原位置无该文件' (-not (Test-Path -LiteralPath $modPath)) ([string]$modPath)
$opId = ''
if ($del.ok) {
    foreach ($k in 'operationId', 'opId', 'id', 'recoveryId') { if ($del.body.PSObject.Properties.Name -contains $k) { $opId = [string]$del.body.$k; break } }
    if (-not $opId -and $del.body.record) { $opId = [string]$del.body.record.operationId }
}
Log "  恢复 id=$opId"
if ($opId) {
    $rst = Api 'POST' "/api/instances/$instId/recovery/$opId/restore" '{}'
    Add-C '执行恢复' ($rst.ok) ($rst.text.Substring(0, [Math]::Min(180, $rst.text.Length)))
    Add-C '恢复后 Mod 字节一致' ((Test-Path -LiteralPath $modPath) -and (Sha256File $modPath) -eq $modASha) ("got=" + (Sha256File $modPath).Substring(0, 12))
} else { Add-C '取到恢复操作 id' $false '删除响应里没有 operationId' }

Log '-- C Mod 事务化替换 --'
$modB = 'ReplaceMe.jar'; $modBPath = Join-Path $modsDir $modB
New-JarLike $modBPath 'ORIGINAL-B'; $modBSha = Sha256File $modBPath
# 第 44 轮更正：替换端点的"校验"是 **上传字节的 SHA256 与客户端声明一致**（完整性），不是"jar 是不是合法 zip"。
# 所以正确的回滚反例是：**字节与 expectedSha256 不匹配**（模拟传输损坏/被截断）。
$newSrc = Join-Path $runDir 'new-but-hash-mismatch.jar'
New-JarLike $newSrc 'WILL-NOT-MATCH'
$rep = Api 'POST' "/api/instances/$instId/mods/$modB/replace" (@{ newFileBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($newSrc)); expectedSha256 = ('dead' + ('0' * 60)) })
Add-C '哈希不匹配的新包被拒绝' (-not $rep.ok) ("status=" + $rep.status + " " + $rep.text.Substring(0, [Math]::Min(180, $rep.text.Length)))
Add-C '拒绝后原文件字节未变（回滚生效）' ((Test-Path -LiteralPath $modBPath) -and (Sha256File $modBPath) -eq $modBSha) ("got=" + (Sha256File $modBPath).Substring(0, 12))
Add-C '拒绝后没有留下半成品（mods 目录里没有该新包）' (-not (Test-Path -LiteralPath (Join-Path $modsDir (Split-Path $newSrc -Leaf)))) 'ok'
$goodSrc = Join-Path $runDir 'good-new.jar'
New-JarLike $goodSrc 'NEW-B'; $goodSha = Sha256File $goodSrc
$rep2 = Api 'POST' "/api/instances/$instId/mods/$modB/replace" (@{ newFileBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($goodSrc)); expectedSha256 = $goodSha })
Add-C '合法新包替换成功' ($rep2.ok) ($rep2.text.Substring(0, [Math]::Min(160, $rep2.text.Length)))
Add-C '替换后为新包字节' ((Sha256File $modBPath) -eq $goodSha) ("got=" + (Sha256File $modBPath).Substring(0, 12))

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    runDir = $runDir
    instanceId = $instId
    instanceGameDir = $instDir
    backupName = $backupName
    recoveryOperationId = $opId
    checks = $checks
    failures = @($checks | Where-Object { -not $_.ok })
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 1
$fails = @($checks | Where-Object { -not $_.ok }).Count
if ($fails -eq 0) { Log "文件层事务/恢复验收判定: PASS（$($checks.Count) 项 0 失败）"; exit 0 }
Log "文件层事务/恢复验收判定: FAIL（$fails / $($checks.Count) 项失败）"
exit 1
