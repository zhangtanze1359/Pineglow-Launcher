#Requires -Version 5.1
<#
.SYNOPSIS
    Quilt 实例"启动并抓命令行"（第 54 轮续）：**复用已导入好的实例**，只做启动与观测。
.DESCRIPTION
    上一版脚本在 `$vd.arguments.jvm` 上被 StrictMode 打断（Quilt 的 arguments **没有 jvm** 属性——这本身就是关键事实）。
    本版所有可能缺失的属性都用 `PSObject.Properties` 判定，避免同类中断。
#>
param(
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][int] $ApiPort,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $InstanceId,
    [int] $ObserveSeconds = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'step2.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function HasProp($obj, [string]$name) { return ($null -ne $obj) -and ($obj.PSObject.Properties.Name -contains $name) }

$cold = (Resolve-Path -LiteralPath $ColdDir).Path
$html = (Invoke-WebRequest "http://127.0.0.1:$ApiPort/" -UseBasicParsing -TimeoutSec 15).Content
if ($html -notmatch 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { Log '取不到会话令牌'; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $Matches[1] }
$state = Invoke-RestMethod "http://127.0.0.1:$ApiPort/api/state" -Headers $hdr -TimeoutSec 30
if (-not $InstanceId) { $inst = @($state.instances) | Where-Object { $_.loaderType -match 'Quilt' } | Select-Object -First 1 } else { $inst = @($state.instances) | Where-Object { $_.id -eq $InstanceId } | Select-Object -First 1 }
if (-not $inst) { Log '找不到 Quilt 实例'; exit 2 }
$instDir = [string]$inst.gameDirectory
Log ("实例 {0} loader={1} version={2}" -f $inst.name, $inst.loaderType, $inst.minecraftVersion)
Log ("gameDir=" + $instDir)

# —— 版本 JSON：安全读取（Quilt 的 arguments 没有 jvm）
$verId = [string]$inst.minecraftVersion
$vj = Join-Path $instDir ("versions\" + $verId + "\" + $verId + ".json")
$verJson = [ordered]@{ exists = (Test-Path $vj); inheritsFrom = ''; mainClass = ''; argKeys = @(); hasJvm = $false; jvmCount = 0; hasOwnLibPath = $false }
if (Test-Path $vj) {
    $vd = [IO.File]::ReadAllText($vj, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if (HasProp $vd 'inheritsFrom') { $verJson.inheritsFrom = [string]$vd.inheritsFrom }
    if (HasProp $vd 'mainClass') { $verJson.mainClass = [string]$vd.mainClass }
    if (HasProp $vd 'arguments') {
        $verJson.argKeys = @($vd.arguments.PSObject.Properties.Name)
        if (HasProp $vd.arguments 'jvm') {
            $verJson.hasJvm = $true
            $verJson.jvmCount = @($vd.arguments.jvm).Count
            $verJson.hasOwnLibPath = ((@($vd.arguments.jvm) | ForEach-Object { [string]$_ }) -join ' ') -match 'java\.library\.path'
        }
    }
}
Log ("版本 JSON: 存在={0} inheritsFrom='{1}' mainClass={2} arguments 键=[{3}] 有 jvm={4} jvm 条目={5} 自带库路径={6}" -f `
    $verJson.exists, $verJson.inheritsFrom, $verJson.mainClass, ($verJson.argKeys -join ','), $verJson.hasJvm, $verJson.jvmCount, $verJson.hasOwnLibPath)

$pre = Invoke-RestMethod "http://127.0.0.1:$ApiPort/api/instances/$($inst.id)/prelaunch-check" -Headers $hdr -TimeoutSec 60
$preErrors = @(); if (HasProp $pre 'errors') { $preErrors = @($pre.errors) }
Log ("prelaunch ok={0} errors={1}" -f $pre.ok, ($preErrors -join '|'))

$launch = Invoke-RestMethod "http://127.0.0.1:$ApiPort/api/instances/$($inst.id)/launch" -Method Post -Headers $hdr -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes('{}')) -TimeoutSec 120
Log ("launch -> " + ($launch | ConvertTo-Json -Compress))

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
Log ("启动器命令行长度={0} 含库路径={1} 值={2}" -f $cmd.Length, $hasLib, $libVal)
[IO.File]::WriteAllText((Join-Path $outDir 'quilt-cmdline.txt'), $cmd, (New-Object Text.UTF8Encoding($false)))

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
@($lines) | Set-Content (Join-Path $outDir 'quilt-game.log') -Encoding utf8
$m = [ordered]@{
    quiltLines = @($lines | Where-Object { $_ -match 'Quilt|quiltmc|quilt-loader' }).Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|Minecraft has crashed|UnsatisfiedLinkError|has crashed' }).Count
    logLines = $lines.Count
}
$aliveNow = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count
Log ("标记: quilt={0} lwjgl={1} sound={2} atlas={3} 崩溃={4} 行数={5} java存活={6}" -f $m.quiltLines, $m.lwjgl, $m.sound, $m.atlas, $m.crash, $m.logLines, $aliveNow)
$verdict = if ($m.crash -gt 0) { 'Quilt 也崩（与 Fabric 同病——推演被推翻）' }
elseif ($m.lwjgl -gt 0 -and $m.atlas -gt 0 -and $hasLib) { 'Quilt 正常进入渲染管线且命令行**含**库路径（推演成立：不受该缺陷影响）' }
elseif ($m.lwjgl -gt 0 -and $m.atlas -gt 0) { 'Quilt 正常进入渲染管线，但命令行**不含**库路径（需解释）' }
else { '未判定（既没崩也没到渲染阶段）' }
Log ('判定: ' + $verdict)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    instance = [ordered]@{ id = $inst.id; name = [string]$inst.name; loaderType = [string]$inst.loaderType; version = $verId; gameDirectory = $instDir }
    versionJson = $verJson
    commandLine = [ordered]@{ length = $cmd.Length; hasLibraryPath = $hasLib; libraryPathValue = $libVal }
    markers = $m
    javaAliveAtEnd = $aliveNow
    verdict = $verdict
} | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
@(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
exit 0
