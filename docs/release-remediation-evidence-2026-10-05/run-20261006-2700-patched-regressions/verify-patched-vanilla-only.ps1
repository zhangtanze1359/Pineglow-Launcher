#Requires -Version 5.1
<#
.SYNOPSIS
    补丁构建的 **Vanilla 启动回归**（第 76 轮续）：上一次失败是**我的原因**——我删掉了补丁副本的 `data/`，
    连带把整合包导入时自动装的 Java 运行时也删了 ⇒ `prelaunch` 正确报"未找到可用的 Java 运行时"。
    本轮复用同一份补丁构建（`data/runtimes/` 里已有 Quilt 导入时装好的 Java 17）重跑 Vanilla。
#>
param(
    [Parameter(Mandatory)][string] $PatchedColdDir,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $Version = '1.18.2',
    [string] $UserName = 'PatchedVanillaTester',
    [int] $StartupTimeoutSec = 120,
    [int] $ObserveSeconds = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'vanilla-only-step.log'
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

$cold = (Resolve-Path -LiteralPath $PatchedColdDir).Path
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
$data = Join-Path $cold 'data'
$runtimes = Join-Path $data 'runtimes'
$rtCount = 0
if (Test-Path $runtimes) { $rtCount = @(Get-ChildItem $runtimes -Directory -EA SilentlyContinue).Count }
$jar = Join-Path $data ("\.minecraft\versions\" + $Version + "\" + $Version + ".jar")
Log ("补丁构建=" + $cold)
Log ("  已装版本 " + $Version + "=" + (Test-Path $jar) + " ；runtimes 目录数=" + $rtCount)

$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'vo-stdout.log') -RedirectStandardError (Join-Path $cold 'vo-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 启动器没起来'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)
$null = Api $port $token 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
$create = Api $port $token 'POST' '/api/instances' ('{"name":"regress-vanilla","version":"' + $Version + '"}') 60
$instId = ''
if ($create.ok -and $create.body -and ($create.body.PSObject.Properties.Name -contains 'instance')) { $instId = [string](Prop $create.body.instance 'id') }
$state = Api $port $token 'GET' '/api/state' $null 60
$inst = @(Prop $state.body 'instances') | Where-Object { (Prop $_ 'id') -eq $instId } | Select-Object -First 1
$instDir = ''
if ($inst) { $instDir = [string](Prop $inst 'gameDirectory') }
Log ("实例 id=" + $instId + " dir=" + $instDir)
$pre = Api $port $token 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
Log ("prelaunch ok=" + [string](Prop $pre.body 'ok') + " errors=" + (@(Prop $pre.body 'errors') -join '|'))
$launch = Api $port $token 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
Log ("launch -> " + $launch.text)
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
$hasBrand = ($cmd -match '-Dminecraft\.launcher\.brand')
[IO.File]::WriteAllText((Join-Path $outDir 'vanilla-cmdline.txt'), $cmd, (New-Object Text.UTF8Encoding($false)))
Log ("命令行长度=" + $cmd.Length + " 含库路径=" + $hasLib + " 含 brand=" + $hasBrand)
Log ("  -Djava.library.path=" + $libVal)
$gameLog = Join-Path $instDir 'logs\latest.log'
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 10
    $alive = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
    $tail = ''
    if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
    Log ("  {0,4}s java={1}  {2}" -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(100, $tail.Length)))
    if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
}
$lines = @(); if (Test-Path $gameLog) { $lines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
@($lines) | Set-Content (Join-Path $outDir 'vanilla-game.log') -Encoding utf8
$m = [ordered]@{
    mainClassLine = @($lines | Where-Object { $_ -match 'Loading Minecraft|Setting user' }).Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError' }).Count
    logLines = $lines.Count
}
$aliveNow = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
Log ("标记: lwjgl=" + $m.lwjgl + " sound=" + $m.sound + " atlas=" + $m.atlas + " 崩溃=" + $m.crash + " 行数=" + $m.logLines + " java存活=" + $aliveNow)
$checks = [ordered]@{
    prelaunch_ok = ([string](Prop $pre.body 'ok') -eq 'True')
    cmdline_has_libpath = $hasLib
    lwjgl_backend = ($m.lwjgl -gt 0)
    sound_engine = ($m.sound -gt 0)
    atlases_created = ($m.atlas -gt 0)
    no_crash = ($m.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：补丁构建下 Vanilla 1.18.2 正常启动（命令行含库路径、LWJGL/声音/图集就绪、0 崩溃）'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    patchedColdDir = $cold
    runtimes = $rtCount
    instance = [ordered]@{ id = $instId; gameDirectory = $instDir }
    prelaunch = $pre.text
    launch = [ordered]@{ response = $launch.text; cmdLength = $cmd.Length; hasLibraryPath = $hasLib; hasBrand = $hasBrand; libraryPathValue = $libVal }
    markers = $m
    javaAliveAtEnd = $aliveNow
    checks = $checks
    verdict = $verdictText
} | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
@(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
