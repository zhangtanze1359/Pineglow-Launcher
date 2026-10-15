#Requires -Version 5.1
<#
.SYNOPSIS
    在**打过两份补丁的构建**上验证缺陷 ④（Java 版本选择）与缺陷 ①②③（Forge 启动装配）（第 88 轮）。
.DESCRIPTION
    ① 复制仓库 → 应用两份补丁（`proposed-fix-forge-launch-assembly.diff` + `proposed-fix-java-version-selection.diff`）→ 构建；
    ② 清掉冷目录里被我污染的 `config.javaPath`（之前为抓参数把 javaPath 指向了 java 包装器）；
    ③ 用打过补丁的构建启动 **NeoForge 1.21.1** 实例：期望它**自己挑到 Java 21**（冷目录 `runtimes` 里已有 java-17 与 java-21）并真正启动；
    ④ 顺带再跑一次 **Forge 1.20.1** 实例（回归）。
#>
param(
    [Parameter(Mandatory)][string] $PatchDir,
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $DestRoot,
    [int] $ObserveSeconds = 150
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'javafix-step.log'
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

$repo = 'D:\松光启动器'
if (-not $DestRoot) { $DestRoot = Join-Path $env:TEMP 'pg-javafix' }
if (Test-Path $DestRoot) { Remove-Item -LiteralPath $DestRoot -Recurse -Force -EA SilentlyContinue }
[void](New-Item -ItemType Directory -Path $DestRoot -Force)
Log ('复制仓库 → ' + $DestRoot)
$rc = Start-Process -FilePath 'robocopy' -ArgumentList @($repo, $DestRoot, '/E', '/XD', '.git', 'bin', 'obj', 'node_modules', 'docs', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:1', '/W:1') -Wait -PassThru -WindowStyle Hidden
Log ('  robocopy exit=' + $rc.ExitCode)
if ($rc.ExitCode -ge 8) { Log '❌ 复制失败'; exit 2 }
Push-Location $DestRoot
& git init -q 2>&1 | Out-Null
foreach ($pf in (Get-ChildItem $PatchDir -Filter 'proposed-fix-*.diff' | Where-Object { $_.Name -match 'forge-launch-assembly|java-version-selection' })) {
    $chk = & git apply --check $pf.FullName 2>&1
    Log ('  check ' + $pf.Name + ' exit=' + $LASTEXITCODE)
    if ($LASTEXITCODE -eq 0) { & git apply $pf.FullName 2>&1 | Out-Null; Log ('  applied ' + $pf.Name) } else { Log ('  ❌ ' + ($chk -join ' ')) }
}
$b = & dotnet build 'dev\PineglowLauncher\Pineglow.Launcher.csproj' -c Release --nologo 2>&1
$bcode = $LASTEXITCODE
Log ('  build exit=' + $bcode)
if ($bcode -ne 0) { ($b | Select-Object -Last 8) | ForEach-Object { Log ('    ' + $_) }; Pop-Location; exit 1 }
$out = Join-Path $DestRoot 'dev\PineglowLauncher\bin\Release\net10.0'
Pop-Location
Log ('  已确认补丁在源码里: requiredMajor=' + @(Select-String -Path (Join-Path $DestRoot 'dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs') -Pattern 'requiredMajor').Count + ' 处; SuppliesOwnClient=' + @(Select-String -Path (Join-Path $DestRoot 'dev\PineglowLauncher\Pineglow.Launcher.Services\LauncherService.cs') -Pattern 'SuppliesOwnClient').Count + ' 处')

# ---- 清掉冷目录里被污染的 javaPath
$cold = (Resolve-Path -LiteralPath $ColdDir).Path
$cfg = Join-Path $cold 'data\config.json'
if (Test-Path $cfg) {
    $o = [IO.File]::ReadAllText($cfg, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $old = $o.javaPath
    $o.javaPath = ''
    $o | ConvertTo-Json -Depth 20 | Set-Content $cfg -Encoding utf8
    Log ('  已清空 config.javaPath（原值: ' + $old + '）')
}
Copy-Item (Join-Path $out '*') $cold -Force -Recurse -EA SilentlyContinue
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
Log ('  冷目录 EXE=' + (Get-Item (Join-Path $cold 'Pineglow.Launcher.exe')).Length + ' B（apphost，打过两份补丁）')

$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'jf-stdout.log') -RedirectStandardError (Join-Path $cold 'jf-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 启动器未就绪'; exit 2 }
Log ('启动器 pid=' + $proc.Id + ' apiPort=' + $port)
$javaApi = Api $port $token 'GET' '/api/java' $null 60
Log ('  /api/java -> ' + $javaApi.text.Substring(0, [Math]::Min(200, $javaApi.text.Length)))

function RunInst([string]$loaderFilter, [string]$tag) {
    $st = Api $port $token 'GET' '/api/state' $null 30
    $inst = @(Prop $st.body 'instances') | Where-Object { (Prop $_ 'loaderType') -eq $loaderFilter } | Select-Object -First 1
    if (-not $inst) { Log ('  [' + $tag + '] ❌ 找不到实例'); return $null }
    $id = [string](Prop $inst 'id'); $dir = [string](Prop $inst 'gameDirectory')
    Log ('  [' + $tag + '] 实例 id=' + $id + ' ver=' + (Prop $inst 'minecraftVersion'))
    $pre = Api $port $token 'GET' ("/api/instances/" + $id + "/prelaunch-check") $null
    Log ('  [' + $tag + '] prelaunch ok=' + [string](Prop $pre.body 'ok') + ' errors=' + (@(Prop $pre.body 'errors') -join '|'))
    # 关键：先删掉旧的 latest.log，并在启动前记录时间，避免把"上一轮的日志"当成这一轮的结果（第 88 轮踩过）。
    $lgf0 = Join-Path $dir 'logs\latest.log'
    if (Test-Path $lgf0) { Remove-Item $lgf0 -Force -EA SilentlyContinue }
    $launchAt = Get-Date
    $launch = Api $port $token 'POST' ("/api/instances/" + $id + "/launch") '{}' 120
    Log ('  [' + $tag + '] launch -> ' + $launch.text)
    $cmd = ''; $dl = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $dl) {
        Start-Sleep -Milliseconds 200
        $mine = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($dir)) })
        if ($mine.Count -gt 0) { $cmd = [string]$mine[0].CommandLine; break }
    }
    $javaUsed = ''
    $jm = [regex]::Match($cmd, '^"([^"]+)"'); if ($jm.Success) { $javaUsed = $jm.Groups[1].Value }
    Log ('  [' + $tag + '] 命令行长度=' + $cmd.Length + ' 使用的 java=' + $javaUsed)
    if ($cmd) { [IO.File]::WriteAllText((Join-Path $outDir ($tag + '-cmdline.txt')), $cmd, (New-Object Text.UTF8Encoding($false))) }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
        Start-Sleep -Seconds 10
        $alive = JavaAlive $dir
        $lg = Join-Path $dir 'logs\latest.log'
        $tail = ''
        if (Test-Path $lg) { $t = @(Get-Content $lg -Tail 1 -EA SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
        Log ('    [' + $tag + '] {0,4}s java={1}  {2}' -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(90, $tail.Length)))
        if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
    }
    $lines = @(); $lgf = Join-Path $dir 'logs\latest.log'
    $fresh = $false
    if (Test-Path $lgf) { $fresh = ((Get-Item $lgf).LastWriteTime -gt $launchAt); $lines = @(Get-Content $lgf -EA SilentlyContinue | ForEach-Object { [string]$_ }) }
    if (-not $fresh) { Log ("  [" + $tag + "] ⚠️ 游戏日志不是本轮新写的（或未生成）⇒ 标记一律记为 0，避免把旧 PASS 当新结果"); $lines = @() }
    @($lines) | Set-Content (Join-Path $outDir ($tag + '-game.log')) -Encoding utf8
    $mk = [ordered]@{
        logLines = $lines.Count
        lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
        sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
        atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
        crash = @($lines | Where-Object { $_ -match 'Failed to locate library|has crashed|UnsatisfiedLinkError' }).Count
    }
    $aliveNow = JavaAlive $dir
    Log ('  [' + $tag + '] 标记: 行数=' + $mk.logLines + ' lwjgl=' + $mk.lwjgl + ' 声音=' + $mk.sound + ' 图集=' + $mk.atlas + ' 崩溃=' + $mk.crash + ' java存活=' + $aliveNow)
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($dir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 5
    return [pscustomobject]@{ id = $id; javaUsed = $javaUsed; cmdLength = $cmd.Length; markers = $mk; javaAliveAtEnd = $aliveNow; prelaunch = $pre.text; launch = $launch.text }
}

$neo = RunInst 'NeoForge' 'neoforge'
$forge = RunInst 'Forge' 'forge'

$neoJavaIs21 = ($neo -and $neo.javaUsed -match 'java-21')
$checks = [ordered]@{
    neoforge_uses_java21 = $neoJavaIs21
    neoforge_lwjgl = ($neo -and $neo.markers.lwjgl -gt 0)
    neoforge_atlases = ($neo -and $neo.markers.atlas -gt 0)
    neoforge_no_crash = ($neo -and $neo.markers.crash -eq 0)
    forge_regression_lwjgl = ($forge -and $forge.markers.lwjgl -gt 0)
    forge_regression_atlases = ($forge -and $forge.markers.atlas -gt 0)
    forge_regression_no_crash = ($forge -and $forge.markers.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
$verdict = 'PASS：打过补丁的产品自己挑对 Java（NeoForge→Java 21）并启动到渲染管线，Forge 回归正常'
if ($fails.Count -gt 0) { $verdict = 'FAIL/需解释：' + $fails.Count + ' 项（' + (($fails | ForEach-Object { $_.Key }) -join ',') + '）' }
Log ('逐项: ' + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + '=' + $_.Value.ToString() }) -join '  '))
Log ('判定: ' + $verdict)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    patchedRoot = $DestRoot; coldDir = $cold
    neoforge = $neo; forge = $forge
    checks = $checks; verdict = $verdict
} | ConvertTo-Json -Depth 8 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
