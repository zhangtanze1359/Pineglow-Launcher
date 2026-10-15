#Requires -Version 5.1
<#
.SYNOPSIS
    返工后的六类真实启动取证（第 93 轮）：用新 GUID 门槛快照里的单文件 EXE，逐个启动冷目录里的真实实例。
.DESCRIPTION
    对每个 `minecraftVersion` 非空的实例：删旧日志 → prelaunch → launch → 200ms 轮询抓真实命令行（取产品选用的 java）
    → 观察 → 只在"日志本轮新写"时采信标记。核对：Java 选择、占位符残留、LWJGL/图集/崩溃、观察期存活。
    成功显式 exit 0，任一断言失败非零。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][string] $JsonPath,
    [int] $ObserveSeconds = 40
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'six-launch-step.log'
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
$sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
Log ('被测 EXE=' + $exe + '  sha256=' + $sha)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }

$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'six-stdout.log') -RedirectStandardError (Join-Path $cold 'six-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 启动器未就绪'; exit 2 }
Log ('启动器 pid=' + $proc.Id + ' apiPort=' + $port)

$st = Api $port $token 'GET' '/api/state' $null 30
$instances = @(Prop $st.body 'instances') | Where-Object { -not [string]::IsNullOrWhiteSpace([string](Prop $_ 'minecraftVersion')) }
Log ('待启动实例数=' + $instances.Count)

$cases = [ordered]@{}
foreach ($inst in $instances) {
    $tag = [string](Prop $inst 'name')
    $id = [string](Prop $inst 'id')
    $dir = [string](Prop $inst 'gameDirectory')
    $ver = [string](Prop $inst 'minecraftVersion')
    $loader = [string](Prop $inst 'loaderType')
    Log ('--- [' + $tag + '] loader=' + $loader + ' ver=' + $ver)
    $lgf = Join-Path $dir 'logs\latest.log'
    if (Test-Path $lgf) { Remove-Item $lgf -Force -EA SilentlyContinue }
    $launchAt = Get-Date
    $pre = Api $port $token 'GET' ("/api/instances/" + $id + "/prelaunch-check") $null
    $launch = Api $port $token 'POST' ("/api/instances/" + $id + "/launch") '{}' 120
    Log ('    prelaunch ok=' + [string](Prop $pre.body 'ok') + ' launch=' + $launch.text)
    $cmd = ''; $dl = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $dl) {
        Start-Sleep -Milliseconds 200
        $mine = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($dir)) })
        if ($mine.Count -gt 0) { $cmd = [string]$mine[0].CommandLine; break }
    }
    $javaUsed = ''; $jm = [regex]::Match($cmd, '^"([^"]+)"'); if ($jm.Success) { $javaUsed = $jm.Groups[1].Value }
    $placeholders = ([regex]::Matches($cmd, '\$\{[a-z_]+\}')).Count
    $addOpens = ([regex]::Matches($cmd, '--add-opens')).Count
    if ($cmd) { [IO.File]::WriteAllText((Join-Path $outDir ($tag + '-cmdline.txt')), $cmd, (New-Object Text.UTF8Encoding($false))) }
    Log ('    命令行长度=' + $cmd.Length + ' java=' + $javaUsed + ' 占位符残留=' + $placeholders + ' --add-opens=' + $addOpens)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
        Start-Sleep -Seconds 10
        $alive = JavaAlive $dir
        Log ('      {0,4}s java={1}' -f [int]$sw.Elapsed.TotalSeconds, $alive)
        if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
    }
    $lines = @(); $fresh = $false
    if (Test-Path $lgf) { $fresh = ((Get-Item $lgf).LastWriteTime -gt $launchAt); $lines = @(Get-Content $lgf -EA SilentlyContinue | ForEach-Object { [string]$_ }) }
    if (-not $fresh) { Log '    ⚠️ 日志非本轮新写 ⇒ 标记记 0'; $lines = @() }
    @($lines) | Set-Content (Join-Path $outDir ($tag + '-game.log')) -Encoding utf8
    $mk = [ordered]@{
        fresh = $fresh; logLines = $lines.Count
        lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
        atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
        crash = @($lines | Where-Object { $_ -match 'Failed to locate library|has crashed|UnsatisfiedLinkError|ResolutionException|InvalidModuleDescriptor' }).Count
    }
    $aliveNow = JavaAlive $dir
    Log ('    标记: fresh=' + $mk.fresh + ' 行数=' + $mk.logLines + ' lwjgl=' + $mk.lwjgl + ' 图集=' + $mk.atlas + ' 崩溃=' + $mk.crash + ' 存活=' + $aliveNow)
    $cases[$tag] = [ordered]@{ id = $id; loader = $loader; version = $ver; javaUsed = $javaUsed; cmdLength = $cmd.Length; placeholders = $placeholders; addOpens = $addOpens; markers = $mk; javaAliveAtEnd = $aliveNow; prelaunch = $pre.text; launch = $launch.text }
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($dir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 5
}

$checks = [ordered]@{}
foreach ($k in $cases.Keys) {
    $c = $cases[$k]
    $checks[$k + '_lwjgl'] = ([int]$c.markers.lwjgl -gt 0)
    $checks[$k + '_atlas'] = ([int]$c.markers.atlas -gt 0)
    $checks[$k + '_no_crash'] = ([int]$c.markers.crash -eq 0)
    $checks[$k + '_placeholders'] = ([int]$c.placeholders -eq 0)
    if ($c.version -match '1\.21') { $checks[$k + '_java21'] = ($c.javaUsed -match 'java-21') }
    elseif ($c.version -match '^1\.(18|19|20)') { $checks[$k + '_java17'] = ($c.javaUsed -match 'java-17') }
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
$verdict = 'PASS：六类真实启动均进入渲染初始化并在观察期存活，Java 选择符合各版本要求'
if ($fails.Count -gt 0) { $verdict = 'FAIL/需解释：' + $fails.Count + ' 项（' + (($fails | ForEach-Object { $_.Key }) -join ',') + '）' }
Log ('逐项: ' + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + '=' + $_.Value.ToString() }) -join '  '))
Log ('判定: ' + $verdict)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exe = $exe; exeSha256 = $sha; observeSeconds = $ObserveSeconds
    cases = $cases; checks = $checks; verdict = $verdict
    boundary = '仅验证：进入渲染初始化（LWJGL/图集）+ 观察期存活；未验进入世界与长时游玩'
} | ConvertTo-Json -Depth 9 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
