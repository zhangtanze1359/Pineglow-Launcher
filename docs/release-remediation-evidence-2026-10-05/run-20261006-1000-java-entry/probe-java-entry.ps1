#Requires -Version 5.1
<#
.SYNOPSIS
    "没有 Java 的普通用户会看到什么" —— 捕获界面/接口层面对缺 Java 的真实反馈（第 41 轮）。

.DESCRIPTION
    代码事实（本轮核对）：
      · `JavaRuntimeService.InstallAsync` 只有**一个调用点**：`InstancePackService`（整合包导入自动安装）；
      · 可自动下载的大版本只有 **17 / 21 / 25**（`JavaRuntimeService.cs:37-39`，其它抛"暂不支持"）；
      · 界面里与 Java 有关的控件只有 `#btnJrePath`（显示/选择已安装的 Java 路径），**没有"安装 Java"入口**；
      · i18n 却写着"使用内置 JRE（默认）"，而单文件产物（53MB）里**没有内置 JRE**。
    本脚本用候选产物实测"缺 Java 时用户看到什么"：列出安装、建实例、跑 prelaunch-check、读报错。
    只读+写临时配置，不启动游戏、不下载 Java。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $Version = '1.20.1',
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 90
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Get-FreePort {
    for ($i = 0; $i -lt 60; $i++) {
        $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
        $l.Start(); $p = ([System.Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop()
        if ($p -gt 1024) { return $p }
    }
    throw '找不到空闲端口'
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('松光 无Java观测 ' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'
[void](New-Item -ItemType Directory -Path $cold -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
[void](New-Item -ItemType Directory -Path (Join-Path $cold 'data') -Force)
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
if (-not $token) { Write-Host '启动器未就绪' -ForegroundColor Red; exit 2 }
$hdr = @{ 'X-Pineglow-Token' = $token }

function Get-Api([string]$Path) { Invoke-RestMethod "http://127.0.0.1:$port$Path" -Headers $hdr -TimeoutSec 60 }
function Post-Api([string]$Path, [string]$Body) { Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method Post -Headers $hdr -ContentType 'application/json' -Body $Body -TimeoutSec 60 }

Write-Host ''
Write-Host '== 缺 Java 时的用户可观测反馈 ==' -ForegroundColor Cyan

$java = Get-Api '/api/java'
Write-Host ("  /api/java  installations={0}  javaPath='{1}'" -f @($java.installations).Count, $java.javaPath)

$state = Get-Api '/api/state'
$stateJava = @($state.java)
Write-Host ("  /api/state java={0}  config.javaPath='{1}'" -f $stateJava.Count, $state.config.javaPath)

$create = Post-Api '/api/instances' ('{"name":"无Java观测","version":"' + $Version + '"}')
$inst = $create.instance
Write-Host ("  创建实例 id={0} version={1} gameDir={2}" -f $inst.id, $inst.minecraftVersion, $inst.gameDirectory)

$pre = Get-Api ("/api/instances/" + $inst.id + "/prelaunch-check")
Write-Host ("  prelaunch-check ok={0}" -f $pre.ok) -ForegroundColor Yellow
foreach ($e in @($pre.errors)) { Write-Host ("    错误: " + $e) -ForegroundColor DarkYellow }
Write-Host ("  prelaunch-check 其它字段: " + (($pre.PSObject.Properties.Name | Where-Object { $_ -notin @('ok', 'errors') }) -join ','))

# 尝试启动（不真的下 Java；看它如何拒绝）
$launch = $null
try { $launch = Post-Api ("/api/instances/" + $inst.id + "/launch") '{}' } catch {
    $resp = $_.Exception.Response
    $txt = ''
    if ($resp) { try { $rd = New-Object System.IO.StreamReader($resp.GetResponseStream()); $txt = $rd.ReadToEnd(); $rd.Dispose() } catch { } }
    $launch = [pscustomobject]@{ httpStatus = [int]$resp.StatusCode; body = $txt }
}
Write-Host ("  launch 响应: " + ($launch | ConvertTo-Json -Depth 4 -Compress))

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    requestedVersion = $Version
    javaApiInstallations = @($java.installations).Count
    configJavaPath = $state.config.javaPath
    instanceId = $inst.id
    instanceGameDir = $inst.gameDirectory
    prelaunchOk = $pre.ok
    prelaunchErrors = @($pre.errors)
    launchResponse = $launch
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 5) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 1
Remove-Item -LiteralPath $runDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host ("JAVA-ENTRY OBSERVED prelaunchOk=$($pre.ok) errors=$(@($pre.errors).Count)") -ForegroundColor Cyan
exit 0
