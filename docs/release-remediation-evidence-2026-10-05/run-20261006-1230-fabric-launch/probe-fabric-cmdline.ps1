#Requires -Version 5.1
<#
.SYNOPSIS
    复现并定位 Fabric 实例启动崩溃：**抓活动进程的完整 java 命令行**，看 `-Djava.library.path` 到底指向哪。
.DESCRIPTION
    现象（第 47 轮）：Fabric 实例启动后约 10 秒崩溃，日志为
    `UnsatisfiedLinkError: Failed to locate library: lwjgl.dll`。
    但 `<instance>/versions/fabric-loader-0.19.3-1.18.2/natives/lwjgl.dll` 存在且 SHA1 与 sidecar 一致。
    ⇒ 怀疑"解压到 loader 目录，但 JVM 的 library.path 指向 vanilla 目录（该目录不存在）"。
    本脚本复用上一次导入留下的数据目录（不重新下载），只启动并抓命令行。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $ColdDir,
    [string] $InstanceId,
    [string] $JsonPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; Write-Host $l; if ($JsonPath) { [IO.File]::AppendAllText((Join-Path (Split-Path -Parent $JsonPath) 'cmdline-step.log'), $l + "`r`n", (New-Object Text.UTF8Encoding($false))) } }
function Get-FreePort {
    for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }
    throw 'no port'
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$cold = (Resolve-Path -LiteralPath $ColdDir).Path
if (-not (Test-Path (Join-Path $cold 'Pineglow.Launcher.exe'))) { Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force }
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru `
    -RedirectStandardOutput (Join-Path $cold 'stdout2.log') -RedirectStandardError (Join-Path $cold 'stderr2.log')
Log "启动器 pid=$($proc.Id) port=$port cold=$cold"
$token = $null
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
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
$state = Invoke-RestMethod "http://127.0.0.1:$port/api/state" -Headers $hdr -TimeoutSec 30
if (-not $InstanceId) {
    $inst = @($state.instances) | Where-Object { $_.loaderType -match 'Fabric' } | Select-Object -First 1
    if (-not $inst) { $inst = @($state.instances) | Select-Object -Last 1 }
    $InstanceId = [string]$inst.id
}
$inst = @($state.instances) | Where-Object { $_.id -eq $InstanceId } | Select-Object -First 1
$instDir = [string]$inst.gameDirectory
Log ("实例 id={0} name={1} version={2} loader={3}" -f $InstanceId, $inst.name, $inst.minecraftVersion, $inst.loaderType)

$launch = Invoke-RestMethod "http://127.0.0.1:$port/api/instances/$InstanceId/launch" -Method Post -Headers $hdr -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes('{}')) -TimeoutSec 120
Log ("launch 响应: " + ($launch | ConvertTo-Json -Compress))

# 立刻抓命令行（崩溃约 10 秒后发生，窗口足够）
$cmd = ''
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
    $mine = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
    if ($mine.Count -gt 0) { $cmd = [string]$mine[0].CommandLine; break }
}
if (-not $cmd) { Log '没有抓到 java 命令行（可能已崩溃）' } else { Log ("命令行长度=" + $cmd.Length); [IO.File]::WriteAllText((Join-Path (Split-Path -Parent $JsonPath) 'fabric-cmdline.txt'), $cmd, (New-Object Text.UTF8Encoding($false))); Log ('完整命令行已写入 fabric-cmdline.txt') }

$libPath = ''
if ($cmd -match '-Djava\.library\.path=("[^"]+"|\S+)') { $libPath = $Matches[1].Trim('"') }
$natLoader = Join-Path $instDir 'versions\fabric-loader-0.19.3-1.18.2\natives'
$natVanilla = Join-Path $instDir 'versions\1.18.2\natives'
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    instanceId = $InstanceId
    instanceGameDir = $instDir
    launchResponse = ($launch | ConvertTo-Json -Compress)
    javaLibraryPath = $libPath
    pathExists = $(if ($libPath) { Test-Path -LiteralPath $libPath } else { $false })
    lwjglInThatPath = $(if ($libPath -and (Test-Path -LiteralPath $libPath)) { Test-Path -LiteralPath (Join-Path $libPath 'lwjgl.dll') } else { $false })
    commandHasLibraryPath = ($cmd -match '-Djava\.library\.path')
    loaderNativesDir = $natLoader
    loaderNativesExists = (Test-Path -LiteralPath $natLoader)
    loaderNativesFileCount = $(if (Test-Path -LiteralPath $natLoader) { @(Get-ChildItem $natLoader -File -ErrorAction SilentlyContinue).Count } else { 0 })
    vanillaNativesExists = (Test-Path -LiteralPath $natVanilla)
    commandHead = $cmd.Substring(0, [Math]::Min(600, $cmd.Length))
    commandLength = $cmd.Length
}
$jsonOut = ($res | ConvertTo-Json -Depth 5)
Log ("java.library.path = " + $libPath)
Log ("  该路径存在=" + $res.pathExists + "  其中 lwjgl.dll=" + $res.lwjglInThatPath)
Log ("  loader natives 目录存在=" + $res.loaderNativesExists + "（" + $res.loaderNativesFileCount + " 个文件）  vanilla natives 存在=" + $res.vanillaNativesExists)
if ($JsonPath) { $jsonOut | Set-Content -Path $JsonPath -Encoding utf8 }

Start-Sleep -Seconds 12
$javas = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue)
$still = @($javas | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) })
Log ("12 秒后 JVM 仍在=" + ($still.Count -gt 0))
foreach ($p in $still) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit 0
