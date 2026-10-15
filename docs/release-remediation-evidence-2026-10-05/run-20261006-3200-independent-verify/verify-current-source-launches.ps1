#Requires -Version 5.1
<#
.SYNOPSIS
    **当前源码（已应用 4 处修复）的真实启动回归**：Vanilla 1.18.2 / Fabric 真实包 / Quilt 真实包（第 83 轮独立复验）。
.DESCRIPTION
    用 verify-source-snapshot 生成的**当前源码单文件 EXE**（GUID 隔离快照里的发布产物）在**全新便携目录**里跑三个真实启动：
      ① Vanilla 1.18.2：装版本 → 建实例 → 启动 → 命令行须含 `-Djava.library.path`，日志 LWJGL/声音/图集、0 崩溃；
      ② Fabric 真实包（Fabulously Optimized 1.18.2）：导入 → 启动 → 同样断言（**修复前必崩**）；
      ③ Quilt 真实包（ukupvp）：导入 → 启动 → 同样断言（回归）。
    每个用例都记录原始命令行、日志标记与判定。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $FabricPack,
    [Parameter(Mandatory)][string] $QuiltPack,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [string] $VanillaVersion = '1.18.2',
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 1800,
    [int] $ImportTimeoutSec = 2400,
    [int] $ObserveSeconds = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'launch-regress-step.log'
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

function RunCase([int]$port, [string]$token, [string]$tag, [string]$instId, [string]$instDir) {
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
    $libVal = ''; if ($hasLib -and $cmd -match '-Djava\.library\.path=([^ ]+)') { $libVal = $Matches[1] }
    [IO.File]::WriteAllText((Join-Path $outDir ($tag + '-cmdline.txt')), $cmd, (New-Object Text.UTF8Encoding($false)))
    Log ("[$tag] 命令行长度=" + $cmd.Length + " 含库路径=" + $hasLib)
    Log ("[$tag]   -Djava.library.path=" + $libVal)
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
        loader = @($lines | Where-Object { $_ -match 'with Fabric Loader|Quilt Loader|Loading Minecraft' }).Count
        lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
        sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
        atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
        crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError' }).Count
        logLines = $lines.Count
    }
    $aliveNow = JavaAlive $instDir
    Log ("[$tag] 标记: loader=" + $m.loader + " lwjgl=" + $m.lwjgl + " sound=" + $m.sound + " atlas=" + $m.atlas + " 崩溃=" + $m.crash + " 行数=" + $m.logLines + " java存活=" + $aliveNow)
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 5
    return [pscustomobject]@{ prelaunch = $pre.text; launch = $launch.text; cmdLength = $cmd.Length; hasLib = $hasLib; libVal = $libVal; markers = $m; javaAliveAtEnd = $aliveNow }
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('launchreg-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'
[void](New-Item -ItemType Directory -Path $cold -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
Log ("被测 EXE=" + $exe + "  " + (Get-Item $exe).Length + " B  sha256=" + (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower())
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 启动器未就绪'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)
$null = Api $port $token 'POST' '/api/accounts/offline' '{"userName":"CurrentSrcTester"}'

$res = [ordered]@{}

# ---------- ① Vanilla
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
$state = Api $port $token 'GET' '/api/state' $null 60
$vInst = @(Prop $state.body 'instances') | Where-Object { (Prop $_ 'id') -eq $vId } | Select-Object -First 1
$vDir = ''; if ($vInst) { $vDir = [string](Prop $vInst 'gameDirectory') }
Log ("[vanilla] 实例 id=" + $vId + " dir=" + $vDir)
$res['vanilla'] = RunCase $port $token 'vanilla' $vId $vDir

# ---------- ② Fabric（真实包）
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $FabricPack).Path))
$sw2 = [Diagnostics.Stopwatch]::StartNew()
$impF = Api $port $token 'POST' '/api/instances/import' (@{ fileName = (Split-Path $FabricPack -Leaf); fileBase64 = $b64 }) $ImportTimeoutSec
$sw2.Stop()
$fId = ''; $fDir = ''; $fLoader = ''
if ($impF.ok) {
    $fId = [string](Prop $impF.body 'instanceId')
    $st2 = Api $port $token 'GET' '/api/state' $null 60
    $fi = @(Prop $st2.body 'instances') | Where-Object { (Prop $_ 'id') -eq $fId } | Select-Object -First 1
    if ($fi) { $fDir = [string](Prop $fi 'gameDirectory'); $fLoader = [string](Prop $fi 'loaderType') }
}
Log ("[fabric] 导入 ok=" + $impF.ok + " 用时=" + [int]$sw2.Elapsed.TotalSeconds + "s loader=" + $fLoader)
$res['fabric'] = [ordered]@{ importOk = $impF.ok; importSeconds = [int]$sw2.Elapsed.TotalSeconds; loaderType = $fLoader; importResponse = $impF.text }
if ($fId -and $fDir) { $res['fabric']['run'] = RunCase $port $token 'fabric' $fId $fDir }

# ---------- ③ Quilt（真实包）
$b64q = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $QuiltPack).Path))
$sw3 = [Diagnostics.Stopwatch]::StartNew()
$impQ = Api $port $token 'POST' '/api/instances/import' (@{ fileName = (Split-Path $QuiltPack -Leaf); fileBase64 = $b64q }) $ImportTimeoutSec
$sw3.Stop()
$qId = ''; $qDir = ''; $qLoader = ''
if ($impQ.ok) {
    $qId = [string](Prop $impQ.body 'instanceId')
    $st3 = Api $port $token 'GET' '/api/state' $null 60
    $qi = @(Prop $st3.body 'instances') | Where-Object { (Prop $qi 'id') -eq $qId } | Select-Object -First 1
    if (-not $qi) { $qi = @(Prop $st3.body 'instances') | Where-Object { (Prop $_ 'id') -eq $qId } | Select-Object -First 1 }
    if ($qi) { $qDir = [string](Prop $qi 'gameDirectory'); $qLoader = [string](Prop $qi 'loaderType') }
}
Log ("[quilt] 导入 ok=" + $impQ.ok + " 用时=" + [int]$sw3.Elapsed.TotalSeconds + "s loader=" + $qLoader)
$res['quilt'] = [ordered]@{ importOk = $impQ.ok; importSeconds = [int]$sw3.Elapsed.TotalSeconds; loaderType = $qLoader; importResponse = $impQ.text }
if ($qId -and $qDir) { $res['quilt']['run'] = RunCase $port $token 'quilt' $qId $qDir }

$checks = [ordered]@{
    vanilla_cmdline_has_libpath = [bool]$res['vanilla'].hasLib
    vanilla_lwjgl = ([int]$res['vanilla'].markers.lwjgl -gt 0)
    vanilla_atlases = ([int]$res['vanilla'].markers.atlas -gt 0)
    vanilla_no_crash = ([int]$res['vanilla'].markers.crash -eq 0)
    fabric_imported = [bool]$res['fabric'].importOk
    fabric_loader_is_fabric = ($res['fabric'].loaderType -eq 'Fabric')
    fabric_cmdline_has_libpath = [bool]$res['fabric'].run.hasLib
    fabric_lwjgl = ([int]$res['fabric'].run.markers.lwjgl -gt 0)
    fabric_atlases = ([int]$res['fabric'].run.markers.atlas -gt 0)
    fabric_no_crash = ([int]$res['fabric'].run.markers.crash -eq 0)
    quilt_imported = [bool]$res['quilt'].importOk
    quilt_cmdline_has_libpath = [bool]$res['quilt'].run.hasLib
    quilt_lwjgl = ([int]$res['quilt'].run.markers.lwjgl -gt 0)
    quilt_atlases = ([int]$res['quilt'].run.markers.atlas -gt 0)
    quilt_no_crash = ([int]$res['quilt'].run.markers.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：当前源码（含 4 处修复）下 Vanilla / Fabric / Quilt 三种真实启动都进渲染管线（命令行含库路径、0 崩溃）'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exe = $exe
    exeSha256 = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
    coldDir = $cold
    cases = $res
    checks = $checks
    verdict = $verdictText
} | ConvertTo-Json -Depth 8 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
