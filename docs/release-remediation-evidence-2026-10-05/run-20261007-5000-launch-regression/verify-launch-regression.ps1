#Requires -Version 5.1
<#
.SYNOPSIS
    修复后源码的真实启动**回归 + 1.21+ Fabric 的 Java 选择**（第 92 轮）。
.DESCRIPTION
    在同一个冷目录里依次做四件事（都用"修复后源码"构建出的单文件 EXE）：
      ① Vanilla 1.18.2：装版本 → 建实例 → 启动（期望 java-17）
      ② Fabric 1.18.2（真实整合包）：导入 → 启动（期望 java-17）
      ③ Quilt 1.20.1（真实整合包）：导入 → 启动（期望 java-17）
      ④ **Fabric 1.21.1（真实整合包）**：导入 → 启动（期望 **java-21**，走版本元数据路径）
    每个用例都：删旧日志 → 抓命令行（200ms 轮询，取产品实际选用的 java）→ 观察 → 只在"日志本轮新写"时采信标记。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkDir,
    [int] $ObserveSeconds = 90,
    [int] $InstallTimeoutSec = 1800,
    [int] $ImportTimeoutSec = 1800
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
function JavaAlive([string]$dir) { return @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($dir)) }).Count }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$cold = (Resolve-Path -LiteralPath $ColdDir).Path
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP ('pg-regress-' + [guid]::NewGuid().ToString('N')) }
[void](New-Item -ItemType Directory -Path $WorkDir -Force)
Log ('被测 EXE=' + $exe + '  sha256=' + (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower())
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'rg-stdout.log') -RedirectStandardError (Join-Path $cold 'rg-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 启动器未就绪'; exit 2 }
Log ('启动器 pid=' + $proc.Id + ' apiPort=' + $port)

function RunCase([string]$tag, [string]$instId, [string]$dir) {
    $lgf = Join-Path $dir 'logs\latest.log'
    if (Test-Path $lgf) { Remove-Item $lgf -Force -EA SilentlyContinue }
    $launchAt = Get-Date
    $pre = Api $port $token 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
    $launch = Api $port $token 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
    Log ('  [' + $tag + '] prelaunch ok=' + [string](Prop $pre.body 'ok') + ' launch=' + $launch.text)
    $cmd = ''; $dl = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $dl) {
        Start-Sleep -Milliseconds 200
        $mine = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($dir)) })
        if ($mine.Count -gt 0) { $cmd = [string]$mine[0].CommandLine; break }
    }
    $javaUsed = ''; $jm = [regex]::Match($cmd, '^"([^"]+)"'); if ($jm.Success) { $javaUsed = $jm.Groups[1].Value }
    $placeholders = ([regex]::Matches($cmd, '\$\{[a-z_]+\}')).Count
    if ($cmd) { [IO.File]::WriteAllText((Join-Path $outDir ($tag + '-cmdline.txt')), $cmd, (New-Object Text.UTF8Encoding($false))) }
    Log ('  [' + $tag + '] 命令行长度=' + $cmd.Length + ' java=' + $javaUsed + ' 占位符残留=' + $placeholders)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
        Start-Sleep -Seconds 10
        $alive = JavaAlive $dir
        $tail = ''
        if (Test-Path $lgf) { $t = @(Get-Content $lgf -Tail 1 -EA SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
        Log ('    [' + $tag + '] {0,4}s java={1}  {2}' -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(85, $tail.Length)))
        if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
    }
    $lines = @(); $fresh = $false
    if (Test-Path $lgf) { $fresh = ((Get-Item $lgf).LastWriteTime -gt $launchAt); $lines = @(Get-Content $lgf -EA SilentlyContinue | ForEach-Object { [string]$_ }) }
    if (-not $fresh) { Log ('  [' + $tag + '] ⚠️ 日志非本轮新写 ⇒ 标记记 0'); $lines = @() }
    @($lines) | Set-Content (Join-Path $outDir ($tag + '-game.log')) -Encoding utf8
    $mk = [ordered]@{
        fresh = $fresh; logLines = $lines.Count
        lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
        sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
        atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
        crash = @($lines | Where-Object { $_ -match 'Failed to locate library|has crashed|UnsatisfiedLinkError|ResolutionException|InvalidModuleDescriptor' }).Count
    }
    $aliveNow = JavaAlive $dir
    Log ('  [' + $tag + '] 标记: fresh=' + $mk.fresh + ' 行数=' + $mk.logLines + ' lwjgl=' + $mk.lwjgl + ' 声音=' + $mk.sound + ' 图集=' + $mk.atlas + ' 崩溃=' + $mk.crash + ' java存活=' + $aliveNow)
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($dir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 5
    return [pscustomobject]@{ javaUsed = $javaUsed; cmdLength = $cmd.Length; placeholders = $placeholders; markers = $mk; javaAliveAtEnd = $aliveNow; prelaunch = $pre.text; launch = $launch.text }
}

function ImportPack([string]$tag, [string]$packPath) {
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $packPath).Path))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $imp = Api $port $token 'POST' '/api/instances/import' (@{ fileName = (Split-Path $packPath -Leaf); fileBase64 = $b64 }) $ImportTimeoutSec
    $sw.Stop()
    $id = ''; $dir = ''; $loader = ''
    if ($imp.ok) {
        $id = [string](Prop $imp.body 'instanceId')
        $st = Api $port $token 'GET' '/api/state' $null 30
        $inst = @(Prop $st.body 'instances') | Where-Object { (Prop $_ 'id') -eq $id } | Select-Object -First 1
        if ($inst) { $dir = [string](Prop $inst 'gameDirectory'); $loader = [string](Prop $inst 'loaderType') }
    }
    Log ('  [' + $tag + '] 导入 ok=' + $imp.ok + ' 用时=' + [int]$sw.Elapsed.TotalSeconds + 's loader=' + $loader)
    return [pscustomobject]@{ ok = $imp.ok; seconds = [int]$sw.Elapsed.TotalSeconds; id = $id; dir = $dir; loader = $loader; response = $imp.text }
}

$results = [ordered]@{}

# ① Vanilla 1.18.2
$swi = [Diagnostics.Stopwatch]::StartNew()
$q = Api $port $token 'POST' '/api/versions/download' '{"versionId":"1.18.2"}' 120
$taskId = [string](Prop $q.body 'id'); $task = $null
while ($swi.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 3
    $list = Api $port $token 'GET' '/api/downloads' $null 60
    $tasks = @(); $tt = Prop $list.body 'tasks'; if ($tt) { $tasks = @($tt) }
    $task = $tasks | Where-Object { (Prop $_ 'id') -eq $taskId } | Select-Object -First 1
    if ($task -and ((Prop $task 'status') -eq 'completed' -or (Prop $task 'status') -eq 'failed')) { break }
}
$swi.Stop()
$cr = Api $port $token 'POST' '/api/instances' '{"name":"rg-vanilla","version":"1.18.2"}' 60
$vId = ''; if ($cr.ok -and $cr.body -and ($cr.body.PSObject.Properties.Name -contains 'instance')) { $vId = [string](Prop $cr.body.instance 'id') }
$st = Api $port $token 'GET' '/api/state' $null 30
$vi = @(Prop $st.body 'instances') | Where-Object { (Prop $_ 'id') -eq $vId } | Select-Object -First 1
$vDir = ''; if ($vi) { $vDir = [string](Prop $vi 'gameDirectory') }
Log ('[vanilla] 安装 status=' + [string](Prop $task 'status') + ' 用时=' + [int]$swi.Elapsed.TotalSeconds + 's 实例=' + $vId)
if ($vId -and $vDir) { $results['vanilla'] = RunCase 'vanilla' $vId $vDir }

# ②③④ 三个真实包
$h = @{ 'User-Agent' = 'Pineglow-Verify/1.0' }
$packs = @(
    @{ tag = 'fabric-1.18.2'; slug = 'fabulously-optimized'; gv = '1.18.2'; loader = 'fabric'; want = 'java-17' },
    @{ tag = 'quilt-1.20.1'; slug = 'ukupvp'; gv = '1.20.1'; loader = 'quilt'; want = 'java-17' },
    @{ tag = 'fabric-1.21.1'; slug = 'fabulously-optimized'; gv = '1.21.1'; loader = 'fabric'; want = 'java-21' }
)
foreach ($p in $packs) {
    $file = Join-Path $WorkDir ($p.tag + '.mrpack')
    if (-not (Test-Path $file)) {
        try {
            $vs = Invoke-RestMethod ('https://api.modrinth.com/v2/project/' + $p.slug + '/version?game_versions=%5B%22' + $p.gv + '%22%5D&loaders=%5B%22' + $p.loader + '%22%5D') -TimeoutSec 60 -Headers $h
            $v = @($vs)[0]
            $f = @($v.files | Where-Object { $_.filename -like '*.mrpack' })[0]
            Invoke-WebRequest $f.url -OutFile $file -UseBasicParsing -TimeoutSec 600
            Log ('  [' + $p.tag + '] 下载 ' + $f.filename + ' ' + (Get-Item $file).Length + ' B')
        } catch { Log ('  [' + $p.tag + '] ❌ 下载失败: ' + $_.Exception.Message); $results[$p.tag] = [ordered]@{ downloadFailed = $true }; continue }
    }
    $imp = ImportPack $p.tag $file
    if (-not $imp.ok) { $results[$p.tag] = [ordered]@{ importOk = $false; response = $imp.response }; continue }
    $run = RunCase $p.tag $imp.id $imp.dir
    $results[$p.tag] = [ordered]@{ importOk = $true; importSeconds = $imp.seconds; loaderType = $imp.loader; want = $p.want; run = $run }
}

$checks = [ordered]@{}
function AddCase([string]$tag, [string]$want) {
    if (-not $results.Contains($tag) -or -not $results[$tag].Contains('run')) { $checks[$tag + '_ok'] = $false; return }
    $r = $results[$tag].run
    $checks[$tag + '_lwjgl'] = ([int]$r.markers.lwjgl -gt 0)
    $checks[$tag + '_atlases'] = ([int]$r.markers.atlas -gt 0)
    $checks[$tag + '_no_crash'] = ([int]$r.markers.crash -eq 0)
    $checks[$tag + '_placeholders_substituted'] = ([int]$r.placeholders -eq 0)
    if ($want) { $checks[$tag + '_uses_' + $want] = ($r.javaUsed -match $want) }
}
AddCase 'vanilla' 'java-17'
AddCase 'fabric-1.18.2' 'java-17'
AddCase 'quilt-1.20.1' 'java-17'
AddCase 'fabric-1.21.1' 'java-21'
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
$verdict = 'PASS：Vanilla/Fabric/Quilt 回归 + 1.21+ Fabric 的 Java 选择全部符合预期'
if ($fails.Count -gt 0) { $verdict = 'FAIL/需解释：' + $fails.Count + ' 项（' + (($fails | ForEach-Object { $_.Key }) -join ',') + '）' }
Log ('逐项: ' + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + '=' + $_.Value.ToString() }) -join '  '))
Log ('判定: ' + $verdict)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exe = $exe; exeSha256 = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
    results = $results; checks = $checks; verdict = $verdict
} | ConvertTo-Json -Depth 9 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
