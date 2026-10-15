#Requires -Version 5.1
<#
.SYNOPSIS
    **受控实验：只补一个参数，加载器实例能不能起来？**（第 48 轮）
    A) 让产品按原样启动 Fabric 实例 → 记录"崩"（基线）；
    B) 抓下产品生成的完整命令行，**只插入 `-Djava.library.path=<natives>`**，其余一字不改，自己跑一次 → 看是否进主菜单。
    这样在**不改任何源码**的前提下，验证"第 47 轮定位到的根因"与"修法是否有效"。

.DESCRIPTION
    复用真实 Modrinth 包（Fabulously Optimized 1.18.2 + fabric-loader 0.19.3）。每步落盘 step.log；
    管道结果一律 @()；错误正文读 $_.ErrorDetails.Message。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $MrpackPath,
    [string] $UserName = 'FixSimTester',
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120,
    [int] $ImportTimeoutSec = 2400,
    [int] $BaselineCrashWaitSec = 60,
    [int] $FixedRunWaitSec = 120,
    [int] $CallTimeoutSec = 60
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$outDir = if ($JsonPath) { Split-Path -Parent $JsonPath } else { $env:TEMP }
if (-not (Test-Path $outDir)) { [void](New-Item -ItemType Directory -Path $outDir -Force) }
$logPath = Join-Path $outDir 'step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('fixsim-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'
$dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru `
    -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
$token = $null
$deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $token }
Log "启动器 pid=$($proc.Id) port=$port runDir=$runDir"
function Api([string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 0) {
    if ($TimeoutSec -le 0) { $TimeoutSec = $CallTimeoutSec }
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

# ---------------------------------------------------------------- 导入真实 Fabric 包
Log '== 受控实验：只补 -Djava.library.path =='
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
$swI = [Diagnostics.Stopwatch]::StartNew()
$imp = Api 'POST' '/api/instances/import' (@{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64 }) $ImportTimeoutSec
$swI.Stop()
Log ("  导入 ok={0} 用时={1}s {2}" -f $imp.ok, [int]$swI.Elapsed.TotalSeconds, $imp.text.Substring(0, [Math]::Min(160, $imp.text.Length)))
if (-not $imp.ok) { Log '导入失败，退出'; exit 1 }
$instId = [string]$imp.body.instanceId
$state = Api 'GET' '/api/state' $null
$inst = @($state.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$instDir = [string]$inst.gameDirectory
Log ("  实例 {0} loader={1} version={2} gameDir={3}" -f $inst.name, $inst.loaderType, $inst.minecraftVersion, $instDir)
$nativesDir = Join-Path $instDir ("versions\" + [string]$inst.minecraftVersion + "\natives")
Log ("  natives 目录={0} 存在={1}（文件 {2} 个，lwjgl.dll={3}）" -f $nativesDir, (Test-Path $nativesDir), @(Get-ChildItem $nativesDir -File -ErrorAction SilentlyContinue).Count, (Test-Path (Join-Path $nativesDir 'lwjgl.dll')))

$null = Api 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
$pre = Api 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
Log ("  prelaunch ok={0} errors={1}" -f $pre.body.ok, (@($pre.body.errors) -join '|'))

# ---------------------------------------------------------------- A) 基线：产品原样启动 → 崩
Log '-- A 基线（产品原样启动）--'
$launch = Api 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
Log ("  launch -> " + $launch.text)
$brokenCmd = ''
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
    if ($mine.Count -gt 0) { $brokenCmd = [string]$mine[0].CommandLine; break }
}
[IO.File]::WriteAllText((Join-Path $outDir 'baseline-cmdline.txt'), $brokenCmd, (New-Object Text.UTF8Encoding($false)))
$baselineHasLib = ($brokenCmd -match '-Djava\.library\.path')
Log ("  基线命令行长度={0}  含 java.library.path={1}" -f $brokenCmd.Length, $baselineHasLib)
Start-Sleep -Seconds $BaselineCrashWaitSec
$gameLog = Join-Path $instDir 'logs\latest.log'
$baseLog = @(); if (Test-Path $gameLog) { $baseLog = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
$baseCrash = @($baseLog | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError' }).Count
Log ("  基线：日志行数={0} 崩溃标记行={1}" -f $baseLog.Count, $baseCrash)
@($baseLog) | Set-Content (Join-Path $outDir 'baseline-game.log') -Encoding utf8
# 清掉基线的 java 进程
$javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
$mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
foreach ($p in $mine) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 3
# 基线日志改名，避免与修复组混在一起
if (Test-Path $gameLog) { Move-Item $gameLog (Join-Path $instDir 'logs\latest.baseline.log') -Force }

# ---------------------------------------------------------------- B) 修复组：只插一个参数
Log '-- B 修复组（同一条命令行 + 只加 -Djava.library.path）--'
$javaExe = ''
if ($brokenCmd -match '^"([^"]+)"') { $javaExe = $Matches[1] } elseif ($brokenCmd -match '^(\S+\.exe)') { $javaExe = $Matches[1] }
Log ("  java.exe = " + $javaExe)
$libFlag = '-Djava.library.path=' + $nativesDir
# 插入点：-Dfile.encoding=UTF-8 之后（保持其它参数一字不改）
$fixedCmd = $brokenCmd -replace '(-Dfile\.encoding=UTF-8)', ('$1 "' + $libFlag + '"')
$fixedCmd = $fixedCmd.TrimStart('"')
if ($fixedCmd.StartsWith($javaExe)) { $fixedCmd = $fixedCmd.Substring($javaExe.Length).TrimStart() }
[IO.File]::WriteAllText((Join-Path $outDir 'fixed-cmdline-args.txt'), $fixedCmd, (New-Object Text.UTF8Encoding($false)))
Log ("  修复组参数长度={0}  新增标志={1}" -f $fixedCmd.Length, $libFlag)
$fixedOut = Join-Path $outDir 'fixed-jvm-stdout.log'
$fixedErr = Join-Path $outDir 'fixed-jvm-stderr.log'
$fixedProc = Start-Process -FilePath $javaExe -ArgumentList $fixedCmd -WorkingDirectory $instDir -PassThru -RedirectStandardOutput $fixedOut -RedirectStandardError $fixedErr
Log ("  修复组 JVM pid={0}" -f $fixedProc.Id)
# 核对实际命令行确实带上了标志
$chk = Get-CimInstance Win32_Process -Filter "ProcessId=$($fixedProc.Id)" -ErrorAction SilentlyContinue
$fixedHasLib = ($chk -and [string]$chk.CommandLine -match '-Djava\.library\.path')
Log ("  修复组实际命令行含 java.library.path={0}" -f $fixedHasLib)
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $FixedRunWaitSec) {
    Start-Sleep -Seconds 10
    if (-not (Get-Process -Id $fixedProc.Id -ErrorAction SilentlyContinue)) { Log ("  {0}s 时 JVM 已退出" -f [int]$sw.Elapsed.TotalSeconds); break }
    $tail = ''
    if (Test-Path $gameLog) { $t2 = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t2.Count -gt 0) { $tail = [string]$t2[0] } }
    Log ("    {0,4}s  {1}" -f [int]$sw.Elapsed.TotalSeconds, $tail.Substring(0, [Math]::Min(110, $tail.Length)))
}
$fixedAlive = [bool](Get-Process -Id $fixedProc.Id -ErrorAction SilentlyContinue)
$fixLog = @(); if (Test-Path $gameLog) { $fixLog = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
@($fixLog) | Set-Content (Join-Path $outDir 'fixed-game.log') -Encoding utf8
$markers = [ordered]@{
    fabricLoader = @($fixLog | Where-Object { $_ -match 'with Fabric Loader' }).Count
    mods = @($fixLog | Where-Object { $_ -match 'Loading \d+ mods' }).Count
    lwjgl = @($fixLog | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($fixLog | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($fixLog | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crashed = @($fixLog | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError' }).Count
}
Log ("  修复组标记: fabric={0} mods={1} lwjgl={2} sound={3} atlas={4} 崩溃={5}  JVM 存活={6}" -f $markers.fabricLoader, $markers.mods, $markers.lwjgl, $markers.sound, $markers.atlas, $markers.crashed, $fixedAlive)
if ($fixedAlive) { Stop-Process -Id $fixedProc.Id -Force -ErrorAction SilentlyContinue }

$verdict = if (($markers.lwjgl -gt 0) -and ($markers.crashed -eq 0)) { 'PASS：补上该参数后不再崩溃（进入渲染管线）' } elseif ($markers.crashed -gt 0) { 'FAIL：补参数后仍然崩溃' } else { 'UNKNOWN：既没崩也没到渲染阶段' }
Log ("判定: " + $verdict)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    mrpack = (Split-Path $pack -Leaf)
    instance = [ordered]@{ id = $instId; name = [string]$inst.name; loaderType = [string]$inst.loaderType; version = [string]$inst.minecraftVersion; gameDirectory = $instDir }
    nativesDir = $nativesDir
    baseline = [ordered]@{ hasLibraryPath = $baselineHasLib; cmdLength = $brokenCmd.Length; crashMarkerLines = $baseCrash; logLines = $baseLog.Count }
    fixed = [ordered]@{ insertedFlag = $libFlag; cmdLength = $fixedCmd.Length; actuallyHasFlag = $fixedHasLib; markers = $markers; jvmStillAliveAfterWait = $fixedAlive; waitSeconds = $FixedRunWaitSec }
    verdict = $verdict
}
if ($JsonPath) { $res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8 }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
