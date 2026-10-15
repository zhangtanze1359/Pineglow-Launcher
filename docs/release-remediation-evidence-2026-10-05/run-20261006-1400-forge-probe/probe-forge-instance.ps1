#Requires -Version 5.1
<#
.SYNOPSIS
    判定 **Forge 实例**是否也受"缺 -Djava.library.path"影响（第 51 轮）：导入真实 Forge 包 → 启动 →
    抓**启动器自己那条命令行**看有没有库路径，再看游戏是否崩。
.DESCRIPTION
    第 50 轮按代码路径分档：Fabric 实测崩、Quilt 同构、**Forge/NeoForge 待定**（走官方 installer 生成的 JSON）。
    本轮用 Modrinth 上的真实 Forge 包（1.20.1 + forge 2.4.10，.mrpack 仅 0.4 MB）把 Forge 定下来。
    每步落盘 step.log；管道结果一律 @()；错误正文读 $_.ErrorDetails.Message。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $MrpackPath,
    [string] $UserName = 'ForgeProbe',
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120,
    [int] $ImportTimeoutSec = 2700,
    [int] $ObserveSeconds = 150,
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
$runDir = Join-Path $WorkRoot ('forgeprobe-' + [guid]::NewGuid().ToString('N'))
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

Log '== Forge 实例启动判定 =='
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
$swI = [Diagnostics.Stopwatch]::StartNew()
$imp = Api 'POST' '/api/instances/import' (@{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64 }) $ImportTimeoutSec
$swI.Stop()
Log ("  导入 ok={0} 用时={1}s {2}" -f $imp.ok, [int]$swI.Elapsed.TotalSeconds, $imp.text.Substring(0, [Math]::Min(200, $imp.text.Length)))
if (-not $imp.ok) { Log '导入失败，退出'; exit 1 }
$instId = [string]$imp.body.instanceId
$state = Api 'GET' '/api/state' $null
$inst = @($state.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$instDir = [string]$inst.gameDirectory
Log ("  实例 {0} loaderType={1} version={2} gameDir={3}" -f $inst.name, $inst.loaderType, $inst.minecraftVersion, $instDir)

# 读实例的版本 JSON：是否有 inheritsFrom、自己的 arguments.jvm 里有没有库路径
$verId = [string]$inst.minecraftVersion
$vj = Join-Path $instDir ("versions\" + $verId + "\" + $verId + ".json")
$verJson = [ordered]@{ exists = (Test-Path $vj); inheritsFrom = ''; mainClass = ''; hasOwnLibraryPath = $false; jvmEntryCount = 0 }
if (Test-Path $vj) {
    $vd = [IO.File]::ReadAllText($vj, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $verJson.inheritsFrom = [string]$vd.inheritsFrom
    $verJson.mainClass = [string]$vd.mainClass
    if ($vd.arguments -and $vd.arguments.jvm) {
        $verJson.jvmEntryCount = @($vd.arguments.jvm).Count
        $verJson.hasOwnLibraryPath = (($vd.arguments.jvm | ForEach-Object { [string]$_ }) -join ' ') -match 'java\.library\.path'
    }
}
Log ("  版本 JSON: 存在={0} inheritsFrom='{1}' mainClass={2} 自带库路径={3} jvm 条目={4}" -f $verJson.exists, $verJson.inheritsFrom, $verJson.mainClass, $verJson.hasOwnLibraryPath, $verJson.jvmEntryCount)

$null = Api 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
$pre = Api 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
Log ("  prelaunch ok={0} errors={1}" -f $pre.body.ok, (@($pre.body.errors) -join '|'))
$launch = Api 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
Log ("  launch -> " + $launch.text)

$cmd = ''
$deadline = (Get-Date).AddSeconds(40)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
    if ($mine.Count -gt 0) { $cmd = [string]$mine[0].CommandLine; break }
}
$hasLib = ($cmd -match '-Djava\.library\.path')
$libVal = ''; if ($hasLib -and $cmd -match '-Djava\.library\.path=([^ ]+)') { $libVal = $Matches[1] }
Log ("  启动器命令行长度={0} 含库路径={1} 值={2}" -f $cmd.Length, $hasLib, $libVal)
[IO.File]::WriteAllText((Join-Path $outDir 'forge-cmdline.txt'), $cmd, (New-Object Text.UTF8Encoding($false)))

$gameLog = Join-Path $instDir 'logs\latest.log'
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 10
    $alive = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
    $tail = ''
    if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
    Log ("    {0,4}s java={1}  {2}" -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(110, $tail.Length)))
    if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
}
$lines = @(); if (Test-Path $gameLog) { $lines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
@($lines) | Set-Content (Join-Path $outDir 'forge-game.log') -Encoding utf8
$m = [ordered]@{
    forgeLines = @($lines | Where-Object { $_ -match 'Forge|ModLauncher|Found mod file' }).Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError|has crashed' }).Count
    logLines = $lines.Count
}
$aliveNow = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
Log ("标记: forge={0} lwjgl={1} sound={2} atlas={3} 崩溃={4} 行数={5} java存活={6}" -f $m.forgeLines, $m.lwjgl, $m.sound, $m.atlas, $m.crash, $m.logLines, $aliveNow)
$verdict = if ($m.crash -gt 0) { 'Forge 也崩（与 Fabric 同病）' } elseif ($m.lwjgl -gt 0 -and $m.atlas -gt 0) { 'Forge 正常进入渲染管线（不受该缺陷影响）' } else { '未判定（既没崩也没到渲染阶段）' }
Log ('判定: ' + $verdict)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    mrpack = (Split-Path $pack -Leaf)
    instance = [ordered]@{ id = $instId; name = [string]$inst.name; loaderType = [string]$inst.loaderType; version = $verId; gameDirectory = $instDir }
    versionJson = $verJson
    commandLine = [ordered]@{ length = $cmd.Length; hasLibraryPath = $hasLib; libraryPathValue = $libVal }
    markers = $m
    javaAliveAtEnd = $aliveNow
    verdict = $verdict
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
@(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
