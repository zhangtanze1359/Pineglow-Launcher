#Requires -Version 5.1
<#
.SYNOPSIS
    **打过补丁的构建**上做 Fabric 真实启动（第 75 轮）：这是 B1 修复的端到端证据。
.DESCRIPTION
    与第 47 轮（候选必崩）对照：同一个真实包（Fabulously Optimized 1.18.2 + fabric-loader 0.19.3）。
    本轮**不复制 EXE**——补丁构建是 apphost（需要同目录的 DLL），所以直接在构建输出目录里跑（该目录已放 `portable.flag`，
    便携数据目录就是同目录下的 `data/`）。
    关键断言：① 启动器命令行**含 `-Djava.library.path`**；② 游戏日志出现 `Backend library: LWJGL`、`Sound engine started`、图集 > 0；
    ③ **没有** `Failed to locate library` / `Minecraft has crashed`（候选在同样条件下必崩）。
#>
param(
    [Parameter(Mandatory)][string] $PatchedColdDir,
    [Parameter(Mandatory)][string] $MrpackPath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $UserName = 'PatchedFabricTester',
    [int] $StartupTimeoutSec = 120,
    [int] $ImportTimeoutSec = 2400,
    [int] $ObserveSeconds = 150,
    [int] $CallTimeoutSec = 60
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'patched-fabric-step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }
function Api([int]$port, [string]$token, [string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 0) {
    if ($TimeoutSec -le 0) { $TimeoutSec = $CallTimeoutSec }
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
$exe = Join-Path $cold 'Pineglow.Launcher.exe'
if (-not (Test-Path $exe)) { Log ('❌ 找不到补丁构建: ' + $exe); exit 2 }
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
Log ("补丁构建: " + $exe + "  " + (Get-Item $exe).Length + " B（apphost）")
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path
Log ("真实包: " + (Split-Path $pack -Leaf) + "  " + (Get-Item $pack).Length + " B")

$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath $exe -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'pf-stdout.log') -RedirectStandardError (Join-Path $cold 'pf-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 补丁构建没起来'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)

$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
$swI = [Diagnostics.Stopwatch]::StartNew()
$imp = Api $port $token 'POST' '/api/instances/import' (@{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64 }) $ImportTimeoutSec
$swI.Stop()
Log ("导入 ok=" + $imp.ok + " 用时=" + [int]$swI.Elapsed.TotalSeconds + "s " + $imp.text.Substring(0, [Math]::Min(200, $imp.text.Length)))
if (-not $imp.ok) { Log '导入失败'; exit 1 }
$instId = [string](Prop $imp.body 'instanceId')
$state = Api $port $token 'GET' '/api/state' $null
$inst = @(Prop $state.body 'instances') | Where-Object { (Prop $_ 'id') -eq $instId } | Select-Object -First 1
$instDir = [string](Prop $inst 'gameDirectory')
$loaderType = [string](Prop $inst 'loaderType')
$mcVersion = [string](Prop $inst 'minecraftVersion')
Log ("实例 loaderType=" + $loaderType + " version=" + $mcVersion + " gameDir=" + $instDir)
$nativesDir = Join-Path $instDir ("versions\" + $mcVersion + "\natives")
Log ("natives 目录=" + $nativesDir + " 存在=" + (Test-Path $nativesDir) + " 文件数=" + @(Get-ChildItem $nativesDir -File -EA SilentlyContinue).Count)

$null = Api $port $token 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
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
$isKnot = ($cmd -match 'net\.fabricmc\.loader\.impl\.launch\.knot\.KnotClient')
Log ("命令行长度=" + $cmd.Length + " 含库路径=" + $hasLib + " KnotClient=" + $isKnot)
Log ("  -Djava.library.path=" + $libVal)
[IO.File]::WriteAllText((Join-Path $outDir 'patched-fabric-cmdline.txt'), $cmd, (New-Object Text.UTF8Encoding($false)))

$gameLog = Join-Path $instDir 'logs\latest.log'
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 10
    $alive = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
    $tail = ''
    if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
    Log ("  {0,4}s java={1}  {2}" -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(110, $tail.Length)))
    if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
}
$lines = @(); if (Test-Path $gameLog) { $lines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
@($lines) | Set-Content (Join-Path $outDir 'patched-fabric-game.log') -Encoding utf8
$m = [ordered]@{
    fabricLoader = @($lines | Where-Object { $_ -match 'with Fabric Loader' }).Count
    mods = @($lines | Where-Object { $_ -match 'Loading \d+ mods' }).Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError' }).Count
    logLines = $lines.Count
}
$aliveNow = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
Log ("标记: fabric=" + $m.fabricLoader + " mods=" + $m.mods + " lwjgl=" + $m.lwjgl + " sound=" + $m.sound + " atlas=" + $m.atlas + " 崩溃=" + $m.crash + " 行数=" + $m.logLines + " java存活=" + $aliveNow)

$checks = [ordered]@{
    cmdline_has_library_path = $hasLib
    fabric_mainclass = $isKnot
    fabric_loader_loaded = ($m.fabricLoader -gt 0)
    lwjgl_backend = ($m.lwjgl -gt 0)
    sound_engine = ($m.sound -gt 0)
    atlases_created = ($m.atlas -gt 0)
    no_crash = ($m.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：补丁构建下 Fabric 实例启动成功（命令行含库路径、Fabric Loader 加载、LWJGL/声音/图集就绪、0 崩溃）——与候选"必崩"形成对照'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    patchedExe = $exe
    mrpack = (Split-Path $pack -Leaf)
    instance = [ordered]@{ id = $instId; loaderType = $loaderType; version = $mcVersion; gameDirectory = $instDir; nativesDir = $nativesDir; nativesFiles = @(Get-ChildItem $nativesDir -File -EA SilentlyContinue).Count }
    import = [ordered]@{ seconds = [int]$swI.Elapsed.TotalSeconds; response = $imp.text }
    launch = [ordered]@{ response = $launch.text; cmdlineLength = $cmd.Length; hasLibraryPath = $hasLib; libraryPathValue = $libVal; knotClient = $isKnot }
    markers = $m
    javaAliveAtEnd = $aliveNow
    checks = $checks
    verdict = $verdictText
} | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
@(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
