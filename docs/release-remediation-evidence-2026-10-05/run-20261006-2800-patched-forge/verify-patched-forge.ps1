#Requires -Version 5.1
<#
.SYNOPSIS
    **补丁构建上跑真实 Forge 包**（第 77 轮）：验证 B2（安装器 URL 修复 ⇒ 不再 404）以及 B1 是否也让 Forge 能启动。
.DESCRIPTION
    背景：第 51/52/61 轮里，**候选**在"下载 Forge 安装器"这一步必然 404（URL 多一段），因此 Forge 连装都装不上。
    本脚本在**打过补丁的构建**上导入同一个真实包（`optimized-fps`，1.20.1 + forge 47.4.3）：
      ① 导入是否成功（候选在 ~330 秒后失败）；
      ② 落盘核对：实例 loaderType/version、安装器生成的版本 JSON、`libraries/` 里的 Forge 库；
      ③ 启动并抓命令行：应含 `-Djava.library.path`，主类应为 `cpw.mods.bootstraplauncher.BootstrapLauncher`；
      ④ 观察日志：LWJGL/声音/图集、0 崩溃。
#>
param(
    [Parameter(Mandatory)][string] $PatchedColdDir,
    [Parameter(Mandatory)][string] $MrpackPath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $UserName = 'PatchedForgeTester',
    [int] $StartupTimeoutSec = 120,
    [int] $ImportTimeoutSec = 3000,
    [int] $ObserveSeconds = 150
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'forge-step.log'
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
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path
Log ("补丁构建=" + $cold)
Log ("真实 Forge 包=" + (Split-Path $pack -Leaf) + "  " + (Get-Item $pack).Length + " B")

$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'fg-stdout.log') -RedirectStandardError (Join-Path $cold 'fg-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 补丁构建没起来'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)
$null = Api $port $token 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')

$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
$sw = [Diagnostics.Stopwatch]::StartNew()
$imp = Api $port $token 'POST' '/api/instances/import' (@{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64 }) $ImportTimeoutSec
$sw.Stop()
Log ("导入 ok=" + $imp.ok + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s")
Log ("  响应: " + $imp.text.Substring(0, [Math]::Min(240, $imp.text.Length)))
if (-not $imp.ok) {
    Log '导入失败（候选就是在这里 404 的）'
    [ordered]@{ generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'); importOk = $false; importSeconds = [int]$sw.Elapsed.TotalSeconds; importResponse = $imp.text; verdict = 'FAIL：补丁构建下 Forge 包导入仍然失败' } |
        ConvertTo-Json -Depth 5 | Set-Content -Path $JsonPath -Encoding utf8
    try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
    exit 1
}
$instId = [string](Prop $imp.body 'instanceId')
$state = Api $port $token 'GET' '/api/state' $null
$inst = @(Prop $state.body 'instances') | Where-Object { (Prop $_ 'id') -eq $instId } | Select-Object -First 1
$instDir = [string](Prop $inst 'gameDirectory')
$loaderType = [string](Prop $inst 'loaderType')
$mcVersion = [string](Prop $inst 'minecraftVersion')
Log ("实例 loaderType=" + $loaderType + " version=" + $mcVersion + " dir=" + $instDir)
$verJson = Join-Path $instDir ("versions\" + $mcVersion + "\" + $mcVersion + ".json")
$forgeLibs = @()
$libRoot = Join-Path $instDir 'libraries\net\minecraftforge'
if (Test-Path $libRoot) { $forgeLibs = @(Get-ChildItem $libRoot -Recurse -File -EA SilentlyContinue) }
$installerArtifacts = @(Get-ChildItem (Join-Path $instDir 'libraries\cpw\mods') -Recurse -File -EA SilentlyContinue)
Log ("版本 JSON 存在=" + (Test-Path $verJson) + " ；Forge 库文件=" + $forgeLibs.Count + " ；cpw/mods 文件=" + $installerArtifacts.Count)
$modsDir = Join-Path $instDir 'mods'
$modJars = @(Get-ChildItem $modsDir -File -Filter '*.jar' -EA SilentlyContinue)
Log ("mods/*.jar=" + $modJars.Count)

$pre = Api $port $token 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
Log ("prelaunch ok=" + [string](Prop $pre.body 'ok') + " errors=" + (@(Prop $pre.body 'errors') -join '|'))
$launch = Api $port $token 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
Log ("launch -> " + $launch.text)
$cmd = ''
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
    if ($mine.Count -gt 0) { $cmd = [string]$mine[0].CommandLine; break }
}
$hasLib = ($cmd -match '-Djava\.library\.path')
$isBootstrap = ($cmd -match 'cpw\.mods\.bootstraplauncher\.BootstrapLauncher')
$launchTarget = ''
if ($cmd -match '--launchTarget\s+(\S+)') { $launchTarget = $Matches[1] }
[IO.File]::WriteAllText((Join-Path $outDir 'forge-cmdline.txt'), $cmd, (New-Object Text.UTF8Encoding($false)))
Log ("命令行长度=" + $cmd.Length + " 含库路径=" + $hasLib + " BootstrapLauncher=" + $isBootstrap + " launchTarget=" + $launchTarget)
$gameLog = Join-Path $instDir 'logs\latest.log'
$sw2 = [Diagnostics.Stopwatch]::StartNew()
while ($sw2.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 10
    $alive = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
    $tail = ''
    if (Test-Path $gameLog) { $t = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
    Log ("  {0,4}s java={1}  {2}" -f [int]$sw2.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(100, $tail.Length)))
    if ($alive -eq 0 -and $sw2.Elapsed.TotalSeconds -gt 20) { break }
}
$lines = @(); if (Test-Path $gameLog) { $lines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
@($lines) | Set-Content (Join-Path $outDir 'forge-game.log') -Encoding utf8
$m = [ordered]@{
    forgeLines = @($lines | Where-Object { $_ -match 'Forge|ModLauncher|Found mod file|FML' }).Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError|has crashed' }).Count
    logLines = $lines.Count
}
$aliveNow = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
Log ("标记: forge=" + $m.forgeLines + " lwjgl=" + $m.lwjgl + " sound=" + $m.sound + " atlas=" + $m.atlas + " 崩溃=" + $m.crash + " 行数=" + $m.logLines + " java存活=" + $aliveNow)
$checks = [ordered]@{
    import_succeeded = $imp.ok
    loader_is_forge = ($loaderType -eq 'Forge')
    installer_profile_present = (Test-Path $verJson)
    forge_libraries_present = ($forgeLibs.Count -gt 0)
    mods_installed = ($modJars.Count -gt 0)
    cmdline_has_libpath = $hasLib
    bootstrap_launcher = $isBootstrap
    lwjgl_backend = ($m.lwjgl -gt 0)
    atlases_created = ($m.atlas -gt 0)
    no_crash = ($m.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：补丁构建下 Forge 包**导入成功**（安装器不再 404）且实例**能启动到渲染管线**'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    patchedColdDir = $cold
    mrpack = (Split-Path $pack -Leaf)
    import = [ordered]@{ ok = $imp.ok; seconds = [int]$sw.Elapsed.TotalSeconds; response = $imp.text }
    instance = [ordered]@{ id = $instId; loaderType = $loaderType; version = $mcVersion; gameDirectory = $instDir; versionJson = (Test-Path $verJson); forgeLibFiles = $forgeLibs.Count; cpwModFiles = $installerArtifacts.Count; modJars = $modJars.Count }
    prelaunch = $pre.text
    launch = [ordered]@{ response = $launch.text; cmdLength = $cmd.Length; hasLibraryPath = $hasLib; bootstrapLauncher = $isBootstrap; launchTarget = $launchTarget }
    markers = $m
    javaAliveAtEnd = $aliveNow
    checks = $checks
    verdict = $verdictText
} | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
@(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
