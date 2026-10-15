#Requires -Version 5.1
<#
.SYNOPSIS
    当前源码（已应用 4 处修复）的分阶段真实验收（第 84 轮）：Quilt / Vanilla / Forge 三个真实用例，可分别重跑。
.DESCRIPTION
    用法：
      -Phase quilt    导入真实 Quilt 包（带 15 秒一次的阶段轮询，便于定位"卡在哪一步"）→ 启动 → 断言
      -Phase vanilla  装 1.18.2 → 建实例 → 启动 → 断言（需要该冷目录里已有 Java，通常由 quilt/fabric 导入装好）
      -Phase forge    导入真实 Forge 包（带轮询）→ 若导入成功则启动 → 断言
    冷目录由 -ColdDir 指定（不存在则新建并放 portable.flag + 复制 EXE），便于**复用同一个已装好 Java 的目录**。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][ValidateSet('quilt', 'vanilla', 'forge')][string] $Phase,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $PackPath,
    [string] $VanillaVersion = '1.18.2',
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 1200,
    [int] $ImportTimeoutSec = 900,
    [int] $ObserveSeconds = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir ("phase-$Phase-step.log")
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }
function Api([int]$port, [string]$token, [string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 60) {
    $hdr = @{ 'X-Pineglow-Token' = $token }
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec
        } else { $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $TimeoutSec }
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail }
    }
}
function Prop($obj, [string]$name) { if ($null -eq $obj) { return $null }; foreach ($p in $obj.PSObject.Properties) { if ($p.Name -ieq $name) { return $p.Value } }; return $null }
function JavaAlive([string]$instDir) { return @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not (Test-Path $ColdDir)) { [void](New-Item -ItemType Directory -Path $ColdDir -Force) }
$exeInCold = Join-Path $ColdDir 'Pineglow.Launcher.exe'
if (-not (Test-Path $exeInCold)) { Copy-Item -LiteralPath $exe -Destination $exeInCold -Force }
if (-not (Test-Path (Join-Path $ColdDir 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $ColdDir 'portable.flag') -Force) }
Log ("Phase=$Phase  EXE=" + $exeInCold + "  " + (Get-Item $exeInCold).Length + " B")
Log ("  EXE sha256=" + (Get-FileHash $exeInCold -Algorithm SHA256).Hash.ToLower())

$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath $exeInCold -WorkingDirectory $ColdDir -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $ColdDir ("p-$Phase-stdout.log")) -RedirectStandardError (Join-Path $ColdDir ("p-$Phase-stderr.log"))
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 启动器未就绪'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)
$null = Api $port $token 'POST' '/api/accounts/offline' '{"userName":"PhaseTester"}'

$result = [ordered]@{ phase = $Phase; coldDir = $ColdDir; exeSha256 = (Get-FileHash $exeInCold -Algorithm SHA256).Hash.ToLower() }

function StartImportWithPoll([int]$port, [string]$token, [string]$packPath, [string]$tag) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $packPath).Path))
    $body = @{ fileName = (Split-Path $packPath -Leaf); fileBase64 = $b64 }
    $json = $body | ConvertTo-Json -Compress
    $hdr = @{ 'X-Pineglow-Token' = $token }
    # 用 BeginGetResponse 异步发起，主线程每 15 秒轮询一次状态（便于定位"卡在哪一步"）
    $req = [System.Net.HttpWebRequest]::Create("http://127.0.0.1:$port/api/instances/import")
    $req.Method = 'POST'; $req.ContentType = 'application/json; charset=utf-8'; $req.Timeout = $ImportTimeoutSec * 1000; $req.ReadWriteTimeout = $ImportTimeoutSec * 1000
    $req.Headers.Add('X-Pineglow-Token', $token)
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $req.ContentLength = $bytes.Length
    $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $async = $req.BeginGetResponse($null, $null)
    $resp = $null; $done = $false
    Log ("  [$tag] 导入已发起，开始每 15 秒轮询阶段（总上限 " + $ImportTimeoutSec + " 秒）")
    while ($sw.Elapsed.TotalSeconds -lt $ImportTimeoutSec) {
        if ($async.AsyncWaitHandle.WaitOne(15000)) { $done = $true; break }
        $st = Api $port $token 'GET' '/api/state' $null 30
        $insts = @(); $i = Prop $st.body 'instances'; if ($i) { $insts = @($i) }
        $names = ($insts | ForEach-Object { [string](Prop $_ 'name') }) -join ' | '
        $dl = Api $port $token 'GET' '/api/downloads' $null 30
        $tasks = @(); $t = Prop $dl.body 'tasks'; if ($t) { $tasks = @($t) }
        $tinfo = ($tasks | Select-Object -Last 3 | ForEach-Object { "[" + [string](Prop $_ 'status') + "]" + [string](Prop $_ 'title') }) -join ' '
        $cpu = 0; try { $cpu = [math]::Round((Get-Process -Id $proc.Id -EA SilentlyContinue).CPU, 1) } catch { }
        Log ("  [$tag] " + [int]$sw.Elapsed.TotalSeconds + "s 实例=" + $insts.Count + " 任务=" + $tasks.Count + " CPU=" + $cpu + "s  最近任务: " + $tinfo)
        Log ("  [$tag]   实例名: " + $names.Substring(0, [Math]::Min(120, $names.Length)))
    }
    $text = ''; $ok = $false
    if ($done) {
        try { $resp = $req.EndGetResponse($async); $sr = New-Object IO.StreamReader($resp.GetResponseStream()); $text = $sr.ReadToEnd(); $sr.Close(); $resp.Close(); $ok = $true }
        catch { $text = $_.Exception.Message }
    } else {
        $text = 'IMPORT_TIMEOUT（' + $ImportTimeoutSec + ' 秒内未返回响应；已保留轮询记录）'
        try { $req.Abort() } catch { }
    }
    $sw.Stop()
    Log ("  [$tag] 导入结束 ok=" + $ok + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s 响应=" + $text.Substring(0, [Math]::Min(200, $text.Length)))
    return [pscustomobject]@{ ok = $ok; seconds = [int]$sw.Elapsed.TotalSeconds; response = $text }
}

function RunLaunch([int]$port, [string]$token, [string]$tag, [string]$instId, [string]$instDir) {
    $pre = Api $port $token 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
    $launch = Api $port $token 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
    Log ("[$tag] prelaunch ok=" + [string](Prop $pre.body 'ok') + " errors=" + (@(Prop $pre.body 'errors') -join '|'))
    Log ("[$tag] launch -> " + $launch.text)
    $cmd = ''
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        $mine = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
        if ($mine.Count -gt 0) { $cmd = [string]$mine[0].CommandLine; break }
    }
    $hasLib = ($cmd -match '-Djava\.library\.path')
    [IO.File]::WriteAllText((Join-Path $outDir ($tag + '-cmdline.txt')), $cmd, (New-Object Text.UTF8Encoding($false)))
    $gameLog = Join-Path $instDir 'logs\latest.log'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
        Start-Sleep -Seconds 10
        $alive = JavaAlive $instDir
        $tail = ''
        if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
        Log ("  [$tag] {0,4}s java={1}  {2}" -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(95, $tail.Length)))
        if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
    }
    $lines = @(); if (Test-Path $gameLog) { $lines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
    @($lines) | Set-Content (Join-Path $outDir ($tag + '-game.log')) -Encoding utf8
    $m = [ordered]@{
        loader = @($lines | Where-Object { $_ -match 'with Fabric Loader|Quilt Loader|Loading Minecraft|Forge' }).Count
        lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
        sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
        atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
        crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError|has crashed' }).Count
        logLines = $lines.Count
    }
    Log ("[$tag] 命令行长度=" + $cmd.Length + " 含库路径=" + $hasLib + " ；标记: loader=" + $m.loader + " lwjgl=" + $m.lwjgl + " sound=" + $m.sound + " atlas=" + $m.atlas + " 崩溃=" + $m.crash + " 行数=" + $m.logLines)
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 5
    return [pscustomobject]@{ prelaunch = $pre.text; launch = $launch.text; cmdLength = $cmd.Length; hasLib = $hasLib; markers = $m }
}

if ($Phase -eq 'quilt' -or $Phase -eq 'forge') {
    if (-not $PackPath) { Log '❌ 该阶段需要 -PackPath'; exit 2 }
    $imp = StartImportWithPoll $port $token $PackPath $Phase
    $result['import'] = [ordered]@{ ok = $imp.ok; seconds = $imp.seconds; response = $imp.response }
    $instId = ''; $instDir = ''; $loader = ''
    if ($imp.ok) {
        try { $j = $imp.response | ConvertFrom-Json; $instId = [string](Prop $j 'instanceId') } catch { }
        if ($instId) {
            $st = Api $port $token 'GET' '/api/state' $null 60
            $inst = @(Prop $st.body 'instances') | Where-Object { (Prop $_ 'id') -eq $instId } | Select-Object -First 1
            if ($inst) { $instDir = [string](Prop $inst 'gameDirectory'); $loader = [string](Prop $inst 'loaderType') }
        }
    }
    Log ("[$Phase] 实例 id=" + $instId + " loader=" + $loader + " dir=" + $instDir)
    $result['instance'] = [ordered]@{ id = $instId; loaderType = $loader; gameDirectory = $instDir }
    if ($instId -and $instDir) { $result['run'] = RunLaunch $port $token $Phase $instId $instDir }
}
elseif ($Phase -eq 'vanilla') {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $q = Api $port $token 'POST' '/api/versions/download' ('{"versionId":"' + $VanillaVersion + '"}') 120
    $taskId = [string](Prop $q.body 'id'); $task = $null
    while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
        Start-Sleep -Seconds 3
        $list = Api $port $token 'GET' '/api/downloads' $null 60
        $tasks = @(); $tt = Prop $list.body 'tasks'; if ($tt) { $tasks = @($tt) }
        $task = $tasks | Where-Object { (Prop $_ 'id') -eq $taskId } | Select-Object -First 1
        if ($task -and ((Prop $task 'status') -eq 'completed' -or (Prop $task 'status') -eq 'failed')) { break }
    }
    $sw.Stop()
    Log ("[vanilla] 安装 " + $VanillaVersion + " status=" + [string](Prop $task 'status') + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s")
    $create = Api $port $token 'POST' '/api/instances' ('{"name":"cur-vanilla","version":"' + $VanillaVersion + '"}') 60
    $vId = ''; if ($create.ok -and $create.body -and ($create.body.PSObject.Properties.Name -contains 'instance')) { $vId = [string](Prop $create.body.instance 'id') }
    $st = Api $port $token 'GET' '/api/state' $null 60
    $inst = @(Prop $st.body 'instances') | Where-Object { (Prop $_ 'id') -eq $vId } | Select-Object -First 1
    $vDir = ''; if ($inst) { $vDir = [string](Prop $inst 'gameDirectory') }
    $rt = @(Get-ChildItem (Join-Path $ColdDir 'data\runtimes') -Directory -EA SilentlyContinue).Count
    Log ("[vanilla] 实例 id=" + $vId + " dir=" + $vDir + " runtimes=" + $rt)
    $result['instance'] = [ordered]@{ id = $vId; gameDirectory = $vDir; runtimes = $rt; installSeconds = [int]$sw.Elapsed.TotalSeconds }
    if ($vId -and $vDir) { $result['run'] = RunLaunch $port $token 'vanilla' $vId $vDir }
}

$checks = [ordered]@{}
if ($result.Contains('import')) { $checks['import_ok'] = [bool]$result['import'].ok }
if ($result.Contains('run')) {
    $checks['cmdline_has_libpath'] = [bool]$result['run'].hasLib
    $checks['lwjgl_backend'] = ([int]$result['run'].markers.lwjgl -gt 0)
    $checks['atlases_created'] = ([int]$result['run'].markers.atlas -gt 0)
    $checks['no_crash'] = ([int]$result['run'].markers.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
$verdict = "PASS：$Phase 阶段全部断言成立"
if ($fails.Count -gt 0) { $verdict = "FAIL/需解释：$Phase 阶段 " + $fails.Count + " 项（" + (($fails | ForEach-Object { $_.Key }) -join ',') + "）" }
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
Log ('判定: ' + $verdict)
$result['checks'] = $checks; $result['verdict'] = $verdict
$result | ConvertTo-Json -Depth 8 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
