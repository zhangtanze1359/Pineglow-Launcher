#Requires -Version 5.1
<#
.SYNOPSIS
    **加载器实例真的启动**（R06「承诺的加载器样例」）：导入真实 Fabric 整合包 → 启动它 →
    从游戏日志确认 **Fabric Loader 与 43 个 Mod 真的被加载**，而不是"实例建好了但没跑过"。

.DESCRIPTION
    代码事实：加载器**只在整合包导入时安装**（`InstancePackService.InstallLoaderAsync`，无独立 API），
    所以这条路径只能"导入真实包 → 启动它"来验。本轮用 Modrinth 官方 Fabulously Optimized 1.18.2
    （`minecraft=1.18.2` + `fabric-loader=0.19.3`，43 个 mod）。
    每步立即写 step.log；管道结果一律 @()；错误正文读 $_.ErrorDetails.Message（PS 5.1 会消费响应流）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $MrpackPath,
    [string] $UserName = 'LoaderTester',
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120,
    [int] $ImportTimeoutSec = 2400,
    [int] $ObserveSeconds = 150,
    [int] $CallTimeoutSec = 60
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$logPath = if ($JsonPath) { Join-Path (Split-Path -Parent $JsonPath) 'step.log' } else { Join-Path $env:TEMP 'loader-step.log' }
$logDir = Split-Path -Parent $logPath
if (-not (Test-Path $logDir)) { [void](New-Item -ItemType Directory -Path $logDir -Force) }
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) {
    $line = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m
    [IO.File]::AppendAllText($logPath, $line + "`r`n", (New-Object Text.UTF8Encoding($false)))
    Write-Host $line
}
function Get-FreePort {
    for ($i = 0; $i -lt 60; $i++) {
        $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop()
        if ($p -gt 1024) { return $p }
    }
    throw '找不到空闲端口'
}
function DirBytes([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $items = @(Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue)
    if ($items.Count -eq 0) { return 0 }
    return [int64](($items | Measure-Object -Property Length -Sum).Sum)
}
$checks = New-Object System.Collections.Generic.List[object]
function Add-C($name, $ok, $detail) {
    $checks.Add([pscustomobject]@{ name = $name; ok = [bool]$ok; detail = $detail }) | Out-Null
    Log (("{0} {1} :: {2}" -f $(if ($ok) { '[OK]  ' } else { '[FAIL]' }), $name, $detail))
}

# ---------------------------------------------------------------- 启动候选产物
$exe = (Resolve-Path -LiteralPath $ExePath).Path
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('loader-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'
$dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$port = Get-FreePort
$extract = Join-Path $runDir 'bundle-extract'
[void](New-Item -ItemType Directory -Path $extract -Force)
$oldPort = $env:PINEGLOW_API_PORT; $oldBase = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
$env:PINEGLOW_API_PORT = "$port"; $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extract
try {
    $proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru `
        -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
} finally {
    $env:PINEGLOW_API_PORT = $oldPort; $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $oldBase
}
$token = $null
$deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    if ((Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) -eq $null) { break }
    try {
        $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) {
            $html = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content
            if ($html -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }
            break
        }
    } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $token }
Log "启动器 pid=$($proc.Id) port=$port runDir=$runDir"
function Api([string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 0) {
    if ($TimeoutSec -le 0) { $TimeoutSec = $CallTimeoutSec }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec
        } else {
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $TimeoutSec
        }
        $sw.Stop()
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6); ms = [int]$sw.Elapsed.TotalMilliseconds }
    } catch {
        $sw.Stop()
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail; ms = [int]$sw.Elapsed.TotalMilliseconds }
    }
}

# ---------------------------------------------------------------- 导入真实 Fabric 包
Log '== 加载器实例启动实测 开始 =='
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
Log ("  包 {0}（{1} B，base64 {2} B）" -f (Split-Path $pack -Leaf), (Get-Item $pack).Length, $b64.Length)
$swI = [Diagnostics.Stopwatch]::StartNew()
$imp = Api 'POST' '/api/instances/import' (@{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64 }) $ImportTimeoutSec
$swI.Stop()
Log ("  导入 用时={0}s ok={1} {2}" -f [int]$swI.Elapsed.TotalSeconds, $imp.ok, $imp.text.Substring(0, [Math]::Min(200, $imp.text.Length)))
Add-C '整合包导入成功' $imp.ok ("用时=" + [int]$swI.Elapsed.TotalSeconds + "s")
$instId = ''
if ($imp.ok) { $instId = [string]$imp.body.instanceId }
$state = Api 'GET' '/api/state' $null
$inst = @($state.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
if (-not $inst) { $inst = @($state.body.instances) | Select-Object -Last 1; $instId = [string]$inst.id }
$instDir = [string]$inst.gameDirectory
Log ("  实例 id={0} name={1} version={2} loaderType={3} gameDir={4}" -f $instId, $inst.name, $inst.minecraftVersion, $inst.loaderType, $instDir)
Add-C '实例带加载器信息' ($inst.loaderType -match 'Fabric') ("loaderType=" + $inst.loaderType + " version=" + $inst.minecraftVersion)
$loaderDirs = @(Get-ChildItem (Join-Path $instDir 'versions') -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
Add-C '加载器版本目录已生成' (($loaderDirs -join ' ') -match 'fabric') ("versions: " + ($loaderDirs -join ', '))

# ---------------------------------------------------------------- 账号 + 预检 + 启动
$null = Api 'POST' '/api/accounts/offline' ('{"userName":"' + $UserName + '"}')
$pre = Api 'GET' ("/api/instances/" + $instId + "/prelaunch-check") $null
Add-C 'prelaunch-check 通过' ([bool]$pre.body.ok) ("ok=" + $pre.body.ok + " errors=" + (@($pre.body.errors) -join '|'))
Log '  -> POST launch'
$launch = Api 'POST' ("/api/instances/" + $instId + "/launch") '{}' 120
Add-C '启动返回 pid' ($launch.ok -and ($launch.text -match 'pid')) ($launch.text.Substring(0, [Math]::Min(160, $launch.text.Length)))

# 等 JVM
$jvmPid = 0
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
    if ($mine.Count -gt 0) { $jvmPid = [int]$mine[0].ProcessId; break }
}
Add-C '游戏 JVM 正在运行' ($jvmPid -gt 0) ("pid=$jvmPid")

# ---------------------------------------------------------------- 观察日志
$gameLog = Join-Path $instDir 'logs\latest.log'
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 10
    $last = ''
    if (Test-Path $gameLog) { $tl = @(Get-Content $gameLog -Tail 1 -ErrorAction SilentlyContinue); if ($tl.Count -gt 0) { $last = [string]$tl[0] } }
    Log ("     {0,4}s  {1}" -f [int]$sw.Elapsed.TotalSeconds, $last.Substring(0, [Math]::Min(120, $last.Length)))
    if (-not (Get-Process -Id $jvmPid -ErrorAction SilentlyContinue)) { Log '  JVM 已退出'; break }
}
$logLines = @()
if (Test-Path $gameLog) { $logLines = @(Get-Content $gameLog -ErrorAction SilentlyContinue | ForEach-Object { [string]$_ }) }
$fabric = @($logLines | Where-Object { $_ -match 'Loading Minecraft .* with Fabric Loader' })
$modsLine = @($logLines | Where-Object { $_ -match 'Loading \d+ mods' })
$modNames = @($logLines | Where-Object { $_ -match 'Loading .* mod .* from mods' })
$mixins = @($logLines | Where-Object { $_ -match 'Mixin|SpongePowered' }).Count
$errors = @($logLines | Where-Object { $_ -match 'FATAL|Mixin apply failed|Exception in thread|Failed to start' }).Count
$atlases = @($logLines | Where-Object { $_ -match 'Created: .*atlas' }).Count
$sound = @($logLines | Where-Object { $_ -match 'Sound engine started' }).Count
Add-C '日志出现 Fabric Loader 版本行' ($fabric.Count -gt 0) (($fabric | Select-Object -First 1) -join '')
Add-C '日志出现"加载 N 个 Mod"' ($modsLine.Count -gt 0) (($modsLine | Select-Object -First 1) -join '')
Add-C '资源图集已创建（渲染就绪）' ($atlases -gt 0) ("atlas 行数=" + $atlases)
Add-C '声音引擎已启动' ($sound -gt 0) ("Sound engine started 行数=" + $sound)
Add-C '没有致命/Mixin 应用失败' ($errors -eq 0) ("致命行数=" + $errors + " Mixin 相关行=" + $mixins)

# ---------------------------------------------------------------- 收尾
$javaAlive = [bool](Get-Process -Id $jvmPid -ErrorAction SilentlyContinue)
Log ("  收尾：JVM 仍存活={0}" -f $javaAlive)
if ($javaAlive) { Stop-Process -Id $jvmPid -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 4
$stateAfter = Api 'GET' '/api/state' $null
$instAfter = @($stateAfter.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
$playSec = if ($instAfter.PSObject.Properties.Name -contains 'totalPlaySeconds') { [int]$instAfter.totalPlaySeconds } else { -1 }
Add-C '退出后结算了游玩时间' ($playSec -gt 0) ("totalPlaySeconds=" + $playSec)

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    mrpack = [ordered]@{ file = (Split-Path $pack -Leaf); bytes = (Get-Item $pack).Length }
    runDir = $runDir
    instance = [ordered]@{ id = $instId; name = [string]$inst.name; loaderType = [string]$inst.loaderType; minecraftVersion = [string]$inst.minecraftVersion; gameDirectory = $instDir }
    import = [ordered]@{ elapsedSec = [int]$swI.Elapsed.TotalSeconds; response = $imp.text }
    launch = [ordered]@{ response = $launch.text; jvmPid = $jvmPid }
    gameLog = [ordered]@{
        file = $gameLog
        fabricLoaderLine = @($fabric | Select-Object -First 3)
        modsLine = @($modsLine | Select-Object -First 3)
        modLoadingCount = $modNames.Count
        mixinLines = $mixins
        fatalLines = $errors
        atlasLines = $atlases
        soundLines = $sound
        tail = @($logLines | Select-Object -Last 40)
    }
    playSecondsAfterExit = $playSec
    checks = $checks
    failures = @($checks | Where-Object { -not $_.ok })
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 7) | Set-Content -Path $JsonPath -Encoding utf8 }
@($logLines) | Set-Content -Path (Join-Path (Split-Path -Parent $JsonPath) 'game-latest.log') -Encoding utf8

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
$fails = @($checks | Where-Object { -not $_.ok }).Count
if ($fails -eq 0) { Log "加载器实例启动验收判定: PASS（$($checks.Count) 项 0 失败）"; exit 0 }
Log "加载器实例启动验收判定: FAIL（$fails / $($checks.Count) 项失败）"
exit 1
