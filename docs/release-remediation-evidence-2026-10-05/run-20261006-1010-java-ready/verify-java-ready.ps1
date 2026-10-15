#Requires -Version 5.1
<#
.SYNOPSIS
    把真实 JRE（Adoptium，与产品同源）按产品的目录约定放进 `runtimes/`，验证：
    ① 下载包校验和可用 ② 真实 JRE 能跑 ③ 产品的 JavaLocator 能发现它 ④ prelaunch-check 在 Java 就绪后通过。

.DESCRIPTION
    背景：`JavaRuntimeService.InstallAsync`（产品自带的下载+校验+解压）**只有整合包导入一个调用点**，
    普通用户没有"安装 Java"入口（第 41 轮发现）。所以这里**按它的目录约定**
    （`<config>/runtimes/java-<major>-<os>-<arch>/…/bin/java.exe`，见 JavaRuntimeService.cs:84-125、149-166）
    用**同一来源、同一校验和**放入一份真实 JRE，再让产品自己去发现它。
    诚实边界：这**不是**在跑产品的 InstallAsync，而是验证"来源可用 + 布局约定 + 发现与检查逻辑"。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [int] $MajorVersion = 17,
    [string] $WorkRoot,
    [string] $JsonPath,
    [int] $StartupTimeoutSec = 120
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
function Sha256File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLower() }

$checks = New-Object System.Collections.Generic.List[object]
function Add-C($name, $ok, $detail) {
    $checks.Add([pscustomobject]@{ name = $name; ok = [bool]$ok; detail = $detail }) | Out-Null
    $c = if ($ok) { 'Green' } else { 'Red' }; $m = if ($ok) { '[OK]  ' } else { '[FAIL]' }
    Write-Host ("  {0} {1} :: {2}" -f $m, $name, $detail) -ForegroundColor $c
}

Write-Host ''
Write-Host "== 真实 JRE 就绪验证（Java $MajorVersion，Adoptium）==" -ForegroundColor Cyan

# ---- 1) Adoptium 元数据（与产品同一端点、同一参数形态）
$api = "https://api.adoptium.net/v3/assets/latest/$MajorVersion/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse"
$meta = Invoke-RestMethod $api -TimeoutSec 60
$pkg = $meta[0].binary.package
$link = [string]$pkg.link
$sum = ([string]$pkg.checksum).ToLower()
$name = [string]$pkg.name
Write-Host "  包: $name"
Write-Host "  链接: $link"
Write-Host "  校验和(sha256): $($sum.Substring(0,16))…"
Add-C 'Adoptium 元数据可取' ($link -match '^https://' -and $sum.Length -eq 64) "name=$name"

# ---- 2) 下载 + 校验
$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('松光 Java就绪 ' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'
$dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)

$work = Join-Path $runDir 'jre-work'
[void](New-Item -ItemType Directory -Path $work -Force)
$zip = Join-Path $work $name
$sw = [Diagnostics.Stopwatch]::StartNew()
Invoke-WebRequest -Uri $link -OutFile $zip -UseBasicParsing -TimeoutSec 900
$sw.Stop()
$got = Sha256File $zip
Add-C '下载完成且 SHA256 与 Adoptium 声明一致' ($got -eq $sum) ("bytes=" + (Get-Item $zip).Length + " elapsed=" + [int]$sw.Elapsed.TotalSeconds + "s got=" + $got.Substring(0,12) + "… want=" + $sum.Substring(0,12) + "…")

# ---- 3) 解压到产品约定的目录
$installDir = Join-Path $dataDir ("runtimes\java-$MajorVersion-windows-x64")
[void](New-Item -ItemType Directory -Path $installDir -Force)
Expand-Archive -LiteralPath $zip -DestinationPath $installDir -Force
$javaExe = Get-ChildItem $installDir -Recurse -File -Filter 'java.exe' | Select-Object -First 1
Add-C '解压后存在 java.exe（产品也是递归查找）' ($javaExe -ne $null) ([string]$javaExe.FullName)
$javaVersionText = ''
if ($javaExe) {
    $sw2 = [Diagnostics.Stopwatch]::StartNew()
    # java -version 写到 stderr：必须临时关闭 Stop，否则会被当成 NativeCommandError 终止脚本（本轮踩过）
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $javaVersionText = (& $javaExe.FullName -version 2>&1 | Out-String)
    $ErrorActionPreference = $prevEap
    $sw2.Stop()
    Add-C '真实 JRE 可执行（java -version）' ($javaVersionText -match 'version') (($javaVersionText -split "`n")[0].Trim() + "  ($([int]$sw2.Elapsed.TotalMilliseconds)ms)")
}

# ---- 4) 让产品自己去发现
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

$java = Get-Api '/api/java'
$found = @($java.installations)
Add-C '产品发现 runtimes/ 下的 JRE' ($found.Count -ge 1) ("installations=" + $found.Count + "  " + (($found | ForEach-Object { "major=" + $_.versionMajor + " path=" + $_.javaPath }) -join '; '))

$create = Post-Api '/api/instances' '{"name":"Java就绪观测","version":"1.20.1"}'
$inst = $create.instance
$pre = Get-Api ("/api/instances/" + $inst.id + "/prelaunch-check")
$errs = @($pre.errors)
Add-C 'Java 就绪后 prelaunch-check 不再报 Java 缺失' (($errs -join ' ') -notmatch 'Java') ("ok=" + $pre.ok + " errors=" + ($errs -join ' | '))

$result = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    exeSha256 = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLower()
    adoptium = [ordered]@{ major = $MajorVersion; name = $name; link = $link; sha256Declared = $sum }
    downloaded = [ordered]@{ bytes = (Get-Item $zip).Length; sha256 = $got; seconds = [int]$sw.Elapsed.TotalSeconds }
    installDir = $installDir
    javaExe = [string]$javaExe.FullName
    javaVersionOutput = ($javaVersionText -split "`n" | Select-Object -First 3)
    javaApiInstallations = $found
    prelaunchOk = $pre.ok
    prelaunchErrors = $errs
    checks = $checks
    failures = @($checks | Where-Object { -not $_.ok })
}
if ($JsonPath) { ($result | ConvertTo-Json -Depth 6) | Set-Content -Path $JsonPath -Encoding utf8 }

try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
Start-Sleep -Seconds 1
$fails = @($checks | Where-Object { -not $_.ok }).Count
Write-Host ''
if ($fails -eq 0) { Write-Host "真实 JRE 就绪验证判定: PASS（$($checks.Count) 项 0 失败）" -ForegroundColor Green; exit 0 }
Write-Host "真实 JRE 就绪验证判定: FAIL（$fails 项失败）" -ForegroundColor Red
exit 1
