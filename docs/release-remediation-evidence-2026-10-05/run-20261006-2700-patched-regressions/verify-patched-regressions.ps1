#Requires -Version 5.1
<#
.SYNOPSIS
    **补丁构建的回归**：Vanilla 1.18.2 与 Quilt 实例都要能正常启动（第 76 轮）——证明 B1 的合并修复没弄坏另外两条路径。
.DESCRIPTION
    在**打过补丁的构建**（apphost，原地跑，便携目录=同目录 `data/`）上：
      ① 装 Vanilla 1.18.2 → 启动 → 断言命令行含 `-Djava.library.path`、日志有 LWJGL/声音/图集、0 崩溃；
      ② 导入真实 Quilt 包（`ukupvp`，quilt-loader 0.21.0）→ 启动 → 同样断言。
    Quilt 的 profile **没有 `jvm` 数组**，合并后它改为走 `arguments.jvm` 分支（库路径来自父版本的 `${natives_directory}` 模板）
    —— 这条正是需要端到端确认的。
#>
param(
    [Parameter(Mandatory)][string] $PatchedColdDir,
    [Parameter(Mandatory)][string] $QuiltPack,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $Version = '1.18.2',
    [string] $UserName = 'PatchedRegressTester',
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 1800,
    [int] $ImportTimeoutSec = 2400,
    [int] $ObserveSeconds = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'regress-step.log'
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
function StartL([string]$cold, [int]$port) {
    $env:PINEGLOW_API_PORT = "$port"
    $pr = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $cold 'rg-stdout.log') -RedirectStandardError (Join-Path $cold 'rg-stderr.log')
    $tk = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $tk = $Matches[1] }; break } } catch { }
    }
    return [pscustomobject]@{ proc = $pr; token = $tk }
}
function LaunchAndObserve([int]$port, [string]$token, [string]$instId, [string]$instDir, [string]$tag) {
    $launch = Api $port $token 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
    Log ("[$tag] launch -> " + $launch.text)
    $cmd = ''
    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
        $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
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
        $alive = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
        $tail = ''
        if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
        Log ("  [$tag] {0,4}s java={1}  {2}" -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(100, $tail.Length)))
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
    $aliveNow = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
    Log ("[$tag] 标记: loader=" + $m.loader + " lwjgl=" + $m.lwjgl + " sound=" + $m.sound + " atlas=" + $m.atlas + " 崩溃=" + $m.crash + " 行数=" + $m.logLines + " java存活=" + $aliveNow)
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 5
    return [pscustomobject]@{ launch = $launch.text; cmdLength = $cmd.Length; hasLib = $hasLib; libVal = $libVal; markers = $m; javaAliveAtEnd = $aliveNow }
}

$cold = (Resolve-Path -LiteralPath $PatchedColdDir).Path
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
$quilt = (Resolve-Path -LiteralPath $QuiltPack).Path
Log ("补丁构建=" + $cold)

$port = Get-FreePort
$l = StartL $cold $port
if (-not $l.token) { Log '❌ 补丁构建没起来'; exit 2 }
Log ("启动器 pid=" + $l.proc.Id + " apiPort=" + $port)
$null = Api $port $l.token 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')

# ---------- ① Vanilla
$sw = [Diagnostics.Stopwatch]::StartNew()
$q = Api $port $l.token 'POST' '/api/versions/download' ('{"versionId":"' + $Version + '"}') 120
$taskId = [string](Prop $q.body 'id')
$task = $null
while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 3
    $list = Api $port $l.token 'GET' '/api/downloads' $null 60
    $tasks = @(); $tt = Prop $list.body 'tasks'; if ($tt) { $tasks = @($tt) }
    $task = $tasks | Where-Object { (Prop $_ 'id') -eq $taskId } | Select-Object -First 1
    if ($task -and ((Prop $task 'status') -eq 'completed' -or (Prop $task 'status') -eq 'failed')) { break }
}
$sw.Stop()
Log ("[vanilla] 安装 " + $Version + " status=" + [string](Prop $task 'status') + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s")
# 注意（第 76 轮踩过）：装完版本**不会**自动把默认实例绑到该版本（默认实例的 minecraftVersion 仍是空串），
# 所以要**显式建一个带版本的实例**（与第 45 轮候选验收一致）。
$createV = Api $port $l.token 'POST' '/api/instances' ('{"name":"regress-vanilla","version":"' + $Version + '"}') 60
$vInstId = ''
if ($createV.ok -and $createV.body -and ($createV.body.PSObject.Properties.Name -contains 'instance')) { $vInstId = [string](Prop $createV.body.instance 'id') }
$state = Api $port $l.token 'GET' '/api/state' $null 60
$inst = @(Prop $state.body 'instances') | Where-Object { (Prop $_ 'id') -eq $vInstId } | Select-Object -First 1
$vDir = ''
if ($inst) { $vDir = [string](Prop $inst 'gameDirectory') }
Log ("[vanilla] 实例 id=" + $vInstId + " dir=" + $vDir)
$pre = Api $port $l.token 'GET' ("/api/instances/" + $vInstId + "/prelaunch-check") $null
Log ("[vanilla] prelaunch ok=" + [string](Prop $pre.body 'ok') + " errors=" + (@(Prop $pre.body 'errors') -join '|'))
$resV = LaunchAndObserve $port $l.token $vInstId $vDir 'vanilla'

# ---------- ② Quilt（导入真实包）
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($quilt))
$sw2 = [Diagnostics.Stopwatch]::StartNew()
$imp = Api $port $l.token 'POST' '/api/instances/import' (@{ fileName = (Split-Path $quilt -Leaf); fileBase64 = $b64 }) $ImportTimeoutSec
$sw2.Stop()
Log ("[quilt] 导入 ok=" + $imp.ok + " 用时=" + [int]$sw2.Elapsed.TotalSeconds + "s " + $imp.text.Substring(0, [Math]::Min(160, $imp.text.Length)))
$qInstId = ''; $qDir = ''; $qLoader = ''
if ($imp.ok) {
    $qInstId = [string](Prop $imp.body 'instanceId')
    $state2 = Api $port $l.token 'GET' '/api/state' $null 60
    $inst2 = @(Prop $state2.body 'instances') | Where-Object { (Prop $_ 'id') -eq $qInstId } | Select-Object -First 1
    if ($inst2) { $qDir = [string](Prop $inst2 'gameDirectory'); $qLoader = [string](Prop $inst2 'loaderType') }
}
Log ("[quilt] 实例 loader=" + $qLoader + " dir=" + $qDir)
$resQ = $null
if ($qInstId -and $qDir) {
    $pre2 = Api $port $l.token 'GET' ("/api/instances/" + $qInstId + "/prelaunch-check") $null
    Log ("[quilt] prelaunch ok=" + [string](Prop $pre2.body 'ok') + " errors=" + (@(Prop $pre2.body 'errors') -join '|'))
    $resQ = LaunchAndObserve $port $l.token $qInstId $qDir 'quilt'
}

$checks = [ordered]@{
    vanilla_cmdline_has_libpath = ($resV.hasLib)
    vanilla_lwjgl = ($resV.markers.lwjgl -gt 0)
    vanilla_atlases = ($resV.markers.atlas -gt 0)
    vanilla_no_crash = ($resV.markers.crash -eq 0)
    quilt_imported = ($imp.ok -and $qLoader -eq 'Quilt')
    quilt_cmdline_has_libpath = ($resQ -and $resQ.hasLib)
    quilt_lwjgl = ($resQ -and $resQ.markers.lwjgl -gt 0)
    quilt_atlases = ($resQ -and $resQ.markers.atlas -gt 0)
    quilt_no_crash = ($resQ -and $resQ.markers.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：补丁构建下 Vanilla 与 Quilt 都正常启动（命令行含库路径、LWJGL/图集就绪、0 崩溃）⇒ 合并修复没弄坏另外两条路径'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    patchedColdDir = $cold
    vanilla = [ordered]@{ instanceId = $vInstId; gameDirectory = $vDir; installSeconds = [int]$sw.Elapsed.TotalSeconds; result = $resV }
    quilt = [ordered]@{ instanceId = $qInstId; gameDirectory = $qDir; loaderType = $qLoader; importSeconds = [int]$sw2.Elapsed.TotalSeconds; result = $resQ }
    checks = $checks
    verdict = $verdictText
} | ConvertTo-Json -Depth 8 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $l.proc.HasExited) { Stop-Process -Id $l.proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
