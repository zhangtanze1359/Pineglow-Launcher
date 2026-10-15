#Requires -Version 5.1
<#
.SYNOPSIS
    **运行态守卫**实测（R06「running 只读」）：真的把游戏跑起来，然后看哪些操作被挡住、哪些没被挡。

.DESCRIPTION
    代码里 13 个处理器带 `RunningInstanceRegistry.IsRunning(instanceId, ...)` 守卫（另一处是 launch 的原子预约
    `TryBeginLaunch`）。本轮用**真在跑的游戏**逐个打这些端点，记录真实状态码与错误文本，并用"退出后再试一次"作对照。
    每步立即写 step.log；所有管道赋值都 @() 包裹（前几轮被 StrictMode 咬过三次）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $Version = '1.18.2',
    [string] $UserName = 'GuardTester',
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 1800,
    [int] $JvmWaitSeconds = 180,
    [int] $CallTimeoutSec = 45
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
Add-Type -AssemblyName System.IO.Compression.FileSystem

$logPath = if ($JsonPath) { Join-Path (Split-Path -Parent $JsonPath) 'step.log' } else { Join-Path $env:TEMP 'guard-step.log' }
$logDir = Split-Path -Parent $logPath
if (-not (Test-Path $logDir)) { [void](New-Item -ItemType Directory -Path $logDir -Force) }
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) {
    $line = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m
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
$checks = New-Object System.Collections.Generic.List[object]
function Add-C($name, $ok, $detail) {
    $checks.Add([pscustomobject]@{ name = $name; ok = [bool]$ok; detail = $detail }) | Out-Null
    Log (("{0} {1} :: {2}" -f $(if ($ok) { '[OK]  ' } else { '[FAIL]' }), $name, $detail))
}

# ---------------------------------------------------------------- 启动候选产物
$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('guard-' + [guid]::NewGuid().ToString('N'))
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
if (-not $token) { Log '启动器未就绪'; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $token }
Log "启动器 pid=$($proc.Id) port=$port runDir=$runDir"

function Api([string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 0) {
    if ($TimeoutSec -le 0) { $TimeoutSec = $CallTimeoutSec }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec
        } else {
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $TimeoutSec
        }
        $sw.Stop()
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6); ms = [int]$sw.Elapsed.TotalMilliseconds }
    } catch {
        $sw.Stop()
        $resp = $_.Exception.Response; $code = 0; $txt = $_.Exception.Message
        if ($resp) { $code = [int]$resp.StatusCode; try { $rd = New-Object System.IO.StreamReader($resp.GetResponseStream()); $txt = $rd.ReadToEnd(); $rd.Dispose() } catch { } }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $txt; ms = [int]$sw.Elapsed.TotalMilliseconds }
    }
}
function Probe([string]$name, [string]$method, [string]$path, $body) {
    $r = Api $method $path $body
    Log ("  {0,-46} {1,4} {2,5}ms  {3}" -f $name, $r.status, $r.ms, $r.text.Substring(0, [Math]::Min(150, $r.text.Length)))
    return $r
}

# ---------------------------------------------------------------- 准备：实例 + 版本 + Java + 账号
Log '== 运行态守卫实测 开始 =='
$create = Api 'POST' '/api/instances' ('{"name":"guard-acceptance","version":"' + $Version + '"}')
if (-not $create.ok) { Log '创建实例失败'; exit 2 }
$instId = [string]$create.body.instance.id
$state = Api 'GET' '/api/state' $null
$inst = @($state.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$instDir = [string]$inst.gameDirectory
Log "实例 id=$instId gameDir=$instDir"
[void](New-Item -ItemType Directory -Path (Join-Path $instDir 'mods') -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $instDir 'saves\GuardWorld') -Force)
[IO.File]::WriteAllBytes((Join-Path $instDir 'saves\GuardWorld\level.dat'), [Text.Encoding]::UTF8.GetBytes('GUARD-WORLD'))
[IO.File]::WriteAllBytes((Join-Path $instDir 'mods\GuardMod.jar'), [Text.Encoding]::UTF8.GetBytes('GUARD-MOD'))

$dl = Api 'POST' '/api/versions/download' ('{"versionId":"' + $Version + '"}')
$taskId = [string]$dl.body.id
$sw = [Diagnostics.Stopwatch]::StartNew(); $task = $null
while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 5
    $list = Api 'GET' '/api/downloads' $null
    $task = @($list.body.tasks) | Where-Object { $_.id -eq $taskId } | Select-Object -First 1
    if ($task -and ($task.status -eq 'completed' -or $task.status -eq 'failed')) { break }
}
Log ("  版本安装 status={0} 用时={1}s" -f $task.status, [int]$sw.Elapsed.TotalSeconds)
if ($task.status -ne 'completed') { Log '安装未完成'; exit 1 }

$meta = Invoke-RestMethod 'https://api.adoptium.net/v3/assets/latest/17/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse' -TimeoutSec 60
$pkg = $meta[0].binary.package
$jreDir = Join-Path $dataDir 'runtimes\java-17-windows-x64'
[void](New-Item -ItemType Directory -Path $jreDir -Force)
$zipPath = Join-Path $runDir ([string]$pkg.name)
Invoke-WebRequest -Uri ([string]$pkg.link) -OutFile $zipPath -UseBasicParsing -TimeoutSec 900
$shaOk = ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLower() -eq ([string]$pkg.checksum).ToLower())
Expand-Archive -LiteralPath $zipPath -DestinationPath $jreDir -Force
Log "  Java 就绪（SHA256 一致={0}）" -f $shaOk
$null = Api 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
$pre = Api 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
Add-C 'prelaunch-check 通过（可以启动）' ([bool]$pre.body.ok) ("ok=" + $pre.body.ok + " errors=" + (@($pre.body.errors) -join '|'))

# ---------------------------------------------------------------- 启动游戏
$launch = Probe 'launch（第一次，应成功）' 'POST' ("/api/instances/" + $instId + "/launch") '{}'
Add-C '第一次启动返回 ok + pid' ($launch.ok -and ($launch.text -match 'pid')) ($launch.text.Substring(0, [Math]::Min(140, $launch.text.Length)))

# 等 JVM 出现
$jvmPid = 0
$deadline = (Get-Date).AddSeconds($JvmWaitSeconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
    if ($mine.Count -gt 0) { $jvmPid = [int]$mine[0].ProcessId; break }
}
Add-C '游戏 JVM 正在运行' ($jvmPid -gt 0) ("pid=$jvmPid")
if ($jvmPid -le 0) { Log 'JVM 未出现，无法测运行态守卫'; exit 1 }
Start-Sleep -Seconds 20   # 让游戏进入渲染阶段（此前实测约 10 秒到此）

# ---------------------------------------------------------------- 运行中：逐个打守卫
Log '-- 运行中：守卫探测 --'
$dup = Probe 'launch（第二次，应被重复启动守卫拒绝）' 'POST' ("/api/instances/" + $instId + "/launch") '{}'
Add-C '重复启动被拒（400 + 已在运行）' ((-not $dup.ok) -and ($dup.text -match '已在运行')) ("status=" + $dup.status + " " + $dup.text.Substring(0, [Math]::Min(120, $dup.text.Length)))

$delMod = Probe 'mods/GuardMod.jar/delete（应被拒）' 'POST' ("/api/instances/" + $instId + "/mods/GuardMod.jar/delete") '{}'
Add-C '运行中禁止删除 Mod' ((-not $delMod.ok) -and ($delMod.text -match '已在运行|运行')) ("status=" + $delMod.status)
Add-C '被拒后 Mod 文件仍在' (Test-Path -LiteralPath (Join-Path $instDir 'mods\GuardMod.jar')) 'ok'

$listSaves = Probe 'saves（列表，应被拒）' 'GET' ("/api/instances/" + $instId + "/saves") $null
Add-C '运行中禁止读取存档列表' (-not $listSaves.ok) ("status=" + $listSaves.status)

$listBk = Probe 'saves/GuardWorld/backups（应被拒）' 'GET' ("/api/instances/" + $instId + "/saves/GuardWorld/backups") $null
Add-C '运行中禁止列出存档备份' (-not $listBk.ok) ("status=" + $listBk.status)

# 这一条是**观察项**：创建备份是否也被挡
$mkBk = Probe 'saves/GuardWorld/backup（观察：是否也被挡）' 'POST' ("/api/instances/" + $instId + "/saves/GuardWorld/backup") '{}'
Log ("  ⇒ 运行中创建备份：status={0}（{1}）" -f $mkBk.status, $(if ($mkBk.ok) { '允许' } else { '被挡' }))

$delInst = Probe 'DELETE /api/instances/{id}（应被拒）' 'DELETE' ("/api/instances/" + $instId) $null
Add-C '运行中禁止删除实例' (-not $delInst.ok) ("status=" + $delInst.status)

# ---------------------------------------------------------------- 退出后：守卫应解除
Log '-- 终止 JVM，验证守卫解除 --'
if ($jvmPid -gt 0) { Stop-Process -Id $jvmPid -Force -ErrorAction SilentlyContinue }
$cleared = $false
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $listSaves2 = Api 'GET' ("/api/instances/" + $instId + "/saves") $null
    if ($listSaves2.ok) { $cleared = $true; break }
}
Add-C 'JVM 退出后守卫解除（存档列表恢复可读）' $cleared ("清空等待=" + [int](90 - ($deadline - (Get-Date)).TotalSeconds) + "s")
$after = Probe 'mods/GuardMod.jar/delete（退出后应成功）' 'POST' ("/api/instances/" + $instId + "/mods/GuardMod.jar/delete") '{}'
Add-C '退出后可以正常删除 Mod（带恢复记录）' ($after.ok -and ($after.text -match 'operationId')) ("status=" + $after.status + " " + $after.text.Substring(0, [Math]::Min(120, $after.text.Length)))

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    runDir = $runDir
    instanceId = $instId
    instanceGameDir = $instDir
    version = $Version
    jvmPid = $jvmPid
    observations = [ordered]@{
        secondLaunch = [ordered]@{ status = $dup.status; text = $dup.text }
        modsDelete = [ordered]@{ status = $delMod.status; text = $delMod.text }
        savesList = [ordered]@{ status = $listSaves.status; text = $listSaves.text }
        backupsList = [ordered]@{ status = $listBk.status; text = $listBk.text }
        createBackup = [ordered]@{ status = $mkBk.status; allowed = $mkBk.ok; text = $mkBk.text }
        deleteInstance = [ordered]@{ status = $delInst.status; text = $delInst.text }
        afterExitModsDelete = [ordered]@{ status = $after.status; text = $after.text }
    }
    checks = $checks
    failures = @($checks | Where-Object { -not $_.ok })
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

# 收尾：杀掉可能残留的 JVM，关启动器
$javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
$mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
foreach ($p in $mine) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue } catch { } }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }

$fails = @($checks | Where-Object { -not $_.ok }).Count
if ($fails -eq 0) { Log "运行态守卫验收判定: PASS（$($checks.Count) 项 0 失败）"; exit 0 }
Log "运行态守卫验收判定: FAIL（$fails / $($checks.Count) 项失败）"
exit 1
