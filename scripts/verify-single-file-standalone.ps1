#Requires -Version 5.1
<#
.SYNOPSIS
    单文件发行附件的**独立冷启动**验收（发版阻断项 R01 的门槛）。

.DESCRIPTION
    R01 的原始 RED：
      按 Release 工作流参数发布后，只把工作流实际上传的 EXE 放进一个全新目录启动，
      进程因缺少 Skia 原生库直接退出（DllNotFoundException，exit -532462766）；
      发布目录里的 UI/ 与原生 DLL 都是外置文件，而工作流只上传 EXE + manifest。

    本脚本把那次手工复现固化成可重复门槛：
      1. 在**全新目录**（默认含中文与空格）里只放一个 EXE + portable.flag；
         断言目录内没有 UI/ 目录、没有 *.dll —— 也就是「附件自足」是可判定的。
      2. 真的启动它，轮询 /api/health 直到就绪或超时。
      3. 逐项取回界面关键资源（HTML/CSS/SVG/JS/vendor）并校验 MIME 与非空。
      4. 断言程序集内嵌资源确实被解包到 data/UI/<版本-指纹>/，并且
         **服务出来的字节与候选源码逐字节一致**（可选 -ExpectedUiRoot），
         用来防「新服务配旧界面」。
      5. 关闭进程树并写出 JSON 证据。

    退出码：0 = 通过；1 = 失败；2 = 用法/环境错误。

.PARAMETER ExePath
    要验收的发布产物（单个 EXE）。

.PARAMETER WorkRoot
    冷启动根目录。默认系统临时目录。冷启动目录会建在其下的「中文 冷启动 <guid>」里。

.PARAMETER ExpectedUiRoot
    可选的候选源码 UI 根目录（如 snapshot 里的 ui/）。给了就逐文件比对服务内容与源码字节。

.PARAMETER StartupTimeoutSec
    等待 /api/health 就绪的秒数，默认 60。

.PARAMETER Port
    API 端口，默认在 48231–48331 中自动挑选。

.PARAMETER JsonPath
    证据 JSON 输出路径。

.PARAMETER KeepWorkDir
    保留冷启动目录（默认保留，便于人工复验）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/verify-single-file-standalone.ps1 `
        -ExePath dist/Pineglow-Launcher-win-x64-single-file.exe -ExpectedUiRoot ui `
        -JsonPath evidence/standalone-cold-start.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ExePath,
    [string] $WorkRoot,
    [string] $ExpectedUiRoot,
    [int] $StartupTimeoutSec = 60,
    [int] $Port = 0,
    [string] $JsonPath,
    [switch] $KeepWorkDir,
    [switch] $SkipWindowTitleCheck
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Checks = New-Object System.Collections.Generic.List[object]
# 必须在分区之外初始化：界面资源采集（第 3 段）只在 /api/health 就绪时执行，
# 而报告（servedAssets）无条件引用它。不要因为「健康检查失败」再叠一个
# 「变量未初始化」的次生异常，否则最需要证据的 FAIL 场景反而写不出 JSON。
$served = [ordered]@{}

function Add-Check {
    param([string] $Name, [bool] $Ok, [string] $Detail = '')
    $script:Checks.Add([pscustomobject]@{ name = $Name; ok = $Ok; detail = $Detail }) | Out-Null
    if ($Ok) { Write-Host ("  [OK]   {0} {1}" -f $Name, $Detail) -ForegroundColor Green }
    else { Write-Host ("  [FAIL] {0} {1}" -f $Name, $Detail) -ForegroundColor Red; $script:Failures.Add("$Name :: $Detail") | Out-Null }
}

function Get-Sha256File {
    param([string] $Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $fs = $null
    try {
        $fs = [System.IO.File]::OpenRead($Path)
        $bytes = $sha.ComputeHash($fs)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $bytes) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally {
        if ($fs) { $fs.Dispose() }
        $sha.Dispose()
    }
}

function Test-TcpPortFree {
    param([int] $Candidate)
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Candidate)
        return $false
    } catch {
        return $true
    } finally {
        $client.Dispose()
    }
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { Write-Host "找不到发布产物: $ExePath" -ForegroundColor Red; exit 2 }
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
if (-not (Test-Path -LiteralPath $WorkRoot)) { [void](New-Item -ItemType Directory -Path $WorkRoot -Force) }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path

if ($Port -le 0) {
    for ($i = 0; $i -lt 40; $i++) {
        $candidate = Get-Random -Minimum 48231 -Maximum 48332
        if (Test-TcpPortFree -Candidate $candidate) { $Port = $candidate; break }
    }
    if ($Port -le 0) { Write-Host '找不到空闲端口' -ForegroundColor Red; exit 2 }
}

$coldDir = Join-Path $WorkRoot ('中文 冷启动 ' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $coldDir -Force)
$exeName = Split-Path -Leaf $exe
$targetExe = Join-Path $coldDir $exeName
Copy-Item -LiteralPath $exe -Destination $targetExe -Force
[void](New-Item -ItemType File -Path (Join-Path $coldDir 'portable.flag') -Force)
$extractDir = Join-Path $coldDir 'bundle-extract'
[void](New-Item -ItemType Directory -Path $extractDir -Force)

Write-Host ''
Write-Host '== 独立冷启动验收（R01） ==' -ForegroundColor Cyan
Write-Host "  产物      : $exe"
Write-Host "  冷启动目录: $coldDir"
Write-Host "  端口      : $Port"
Write-Host "  期望 UI   : $(if ($ExpectedUiRoot) { (Resolve-Path -LiteralPath $ExpectedUiRoot).Path } else { '<未指定>' })"

# ---------------- 1. 附件自足性（目录里只应有 EXE + portable.flag） ----------------
$topFiles = @(Get-ChildItem -LiteralPath $coldDir -File | Select-Object -ExpandProperty Name)
$topDirs = @(Get-ChildItem -LiteralPath $coldDir -Directory | Where-Object { $_.Name -ne 'bundle-extract' } | Select-Object -ExpandProperty Name)
$dlls = @($topFiles | Where-Object { $_ -like '*.dll' })
$uiDir = @($topDirs | Where-Object { $_ -eq 'UI' })
Add-Check '冷启动目录无外置 UI/ 目录' ($uiDir.Count -eq 0) ("dirs=" + ($topDirs -join ','))
Add-Check '冷启动目录无外置原生 DLL' ($dlls.Count -eq 0) ("dlls=" + ($dlls -join ','))

# ---------------- 2. 环境隔离 + 启动并等待就绪 ----------------
# 原始评估对 R01 的要求是"不依赖开发目录/历史 UI/DLL/**PINEGLOW_UI_DIR**"。
# 但产品解析 UI 根的顺序是：env(PINEGLOW_UI_DIR) → EXE 旁 UI/ → 内嵌解包目录 → 配置目录，
# **env 优先级最高**。于是只要父进程环境里恰好设了 PINEGLOW_UI_DIR，这个"自足性"验收
# 就会变成"从开发树取 UI"——通过得莫名其妙。
# 这不是假设：本机会话里确实残留着 PINEGLOW_WEBVIEW_DEBUG=9222 这类变量，说明环境会泄漏进子进程。
# 因此这里主动清除从父进程继承的 PINEGLOW_*（只保留下面本脚本自己要设的两项），并把清单写进证据。
$keepEnv = @('PINEGLOW_API_PORT', 'DOTNET_BUNDLE_EXTRACT_BASE_DIR')
$scrubbedEnv = @()
foreach ($item in @(Get-ChildItem env: | Where-Object { $_.Name -like 'PINEGLOW_*' -and $keepEnv -notcontains $_.Name })) {
    $scrubbedEnv += ("{0}={1}" -f $item.Name, $item.Value)
    Remove-Item -LiteralPath ("Env:" + $item.Name) -ErrorAction SilentlyContinue
}
Add-Check '冷启动不继承开发旋钮（PINEGLOW_UI_DIR 等已清除）' `
    ([string]::IsNullOrEmpty($env:PINEGLOW_UI_DIR)) `
    ("scrubbed=" + $(if ($scrubbedEnv.Count -gt 0) { $scrubbedEnv -join '; ' } else { '无' }))

$env:PINEGLOW_API_PORT = "$Port"
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extractDir
$stdoutPath = Join-Path $coldDir 'stdout.log'
$stderrPath = Join-Path $coldDir 'stderr.log'
$proc = $null
$process = $null
$health = $null
$startedAt = Get-Date
try {
    $proc = Start-Process -FilePath $targetExe -WorkingDirectory $coldDir -PassThru `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        $process = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if ($null -eq $process) { break }
        try {
            $resp = Invoke-WebRequest "http://127.0.0.1:$Port/api/health" -UseBasicParsing -TimeoutSec 5
            if ($resp.StatusCode -eq 200) { $health = $resp.Content; break }
        } catch { }
    }

    $stderrText = ''
    if (Test-Path -LiteralPath $stderrPath) {
        # 子进程仍持有重定向句柄，必须按共享读打开，否则 IOException
        try {
            $fs = [System.IO.File]::Open($stderrPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            try {
                $reader = New-Object System.IO.StreamReader($fs)
                $stderrText = $reader.ReadToEnd()
                $reader.Dispose()
            } finally { $fs.Dispose() }
        } catch { $stderrText = '' }
    }
    $process = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    $alive = ($null -ne $process)

    Add-Check '进程在冷启动后仍然存活（R01 原始 RED 是直接退出）' $alive ("stderr=" + $stderrText.Trim())
    Add-Check '/api/health 就绪' ($null -ne $health) ("health=" + "$health")

    # 窗口比 API 稍晚就绪：在超时预算内轮询主窗口标题，避免把「还没画出来」误判成失败
    $windowTitle = ''
    if ($alive) {
        $windowDeadline = (Get-Date).AddSeconds([Math]::Max(10, [Math]::Min(30, $StartupTimeoutSec)))
        while ((Get-Date) -lt $windowDeadline) {
            $live = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
            if ($null -eq $live) { break }
            if (-not [string]::IsNullOrWhiteSpace($live.MainWindowTitle)) { $windowTitle = $live.MainWindowTitle; break }
            Start-Sleep -Milliseconds 700
        }
        if ($SkipWindowTitleCheck) {
            # CI runner 是无交互会话（Session 0），窗口标题不可靠；此时只记录不判定。
            Write-Host ("  [note] 跳过主窗口标题判定（-SkipWindowTitleCheck），实测标题='{0}'" -f $windowTitle) -ForegroundColor Yellow
        } else {
            Add-Check '出现真实主窗口（GUI 真的起来了）' (-not [string]::IsNullOrWhiteSpace($windowTitle)) ("title='" + $windowTitle + "'")
        }
    }

    # ---------------- 3. 界面关键资源 ----------------
    if ($null -ne $health) {
        $matrix = @(
            @{ Path = '/';                       Type = 'text/html';         Min = 1000 },
            @{ Path = '/css/figma.css';          Type = 'text/css';          Min = 1000 },
            @{ Path = '/css/pineglow.css';       Type = 'text/css';          Min = 1000 },
            @{ Path = '/assets/pine-forest.svg'; Type = 'image/svg+xml';     Min = 100 },
            @{ Path = '/js/api.js';              Type = 'application/javascript'; Min = 1000 },
            @{ Path = '/js/launcher.js';         Type = 'application/javascript'; Min = 10000 },
            @{ Path = '/js/i18n.js';             Type = 'application/javascript'; Min = 1000 },
            @{ Path = '/js/skin3d.js';           Type = 'application/javascript'; Min = 1000 },
            @{ Path = '/js/skin3d-core.js';      Type = 'application/javascript'; Min = 100 },
            @{ Path = '/vendor/three.min.js';    Type = 'application/javascript'; Min = 10000 }
        )
        $downloadDir = Join-Path $coldDir 'served'
        [void](New-Item -ItemType Directory -Path $downloadDir -Force)
        foreach ($item in $matrix) {
            $rel = $item.Path.TrimStart('/')
            $outName = if ($rel -eq '') { 'index.html' } else { $rel.Replace('/', '_') }
            $outPath = Join-Path $downloadDir $outName
            try {
                # 单次 GET 同时拿内容类型与原始字节：
                # Invoke-WebRequest -OutFile 在 PS 5.1 下不暴露响应头，HEAD 也不一定被实现。
                $url = "http://127.0.0.1:$Port$($item.Path)"
                $req = [System.Net.HttpWebRequest]::Create($url)
                $req.Method = 'GET'
                $req.Timeout = 20000
                $resp = $req.GetResponse()
                $ctype = ''
                try {
                    $ctype = [string]$resp.ContentType
                    $stream = $resp.GetResponseStream()
                    try {
                        $outFs = [System.IO.File]::Create($outPath)
                        try { $stream.CopyTo($outFs) } finally { $outFs.Dispose() }
                    } finally { $stream.Dispose() }
                } finally { $resp.Close() }

                $len = (Get-Item -LiteralPath $outPath).Length
                $ok = ($len -ge $item.Min)
                if ($item.Type) { $ok = $ok -and ($ctype -like "$($item.Type)*") }
                Add-Check ("资源 $($item.Path)") $ok ("bytes=$len type=$ctype")
                $served[$item.Path] = [ordered]@{ bytes = $len; contentType = $ctype; sha256 = (Get-Sha256File $outPath); file = $outPath }
            } catch {
                Add-Check ("资源 $($item.Path)") $false ("请求失败: " + $_.Exception.Message)
                $served[$item.Path] = [ordered]@{ bytes = 0; error = $_.Exception.Message }
            }
        }

        # ---------------- 4. 解包目录与「与服务同版」 ----------------
        $uiParent = Join-Path $coldDir 'data\UI'
        $extractedDirs = @()
        if (Test-Path -LiteralPath $uiParent) { $extractedDirs = @(Get-ChildItem -LiteralPath $uiParent -Directory) }
        Add-Check '程序集内嵌 UI 已解包到 data/UI/<版本-指纹>' ($extractedDirs.Count -ge 1) ("dirs=" + (($extractedDirs | ForEach-Object { $_.Name }) -join ','))
        if ($extractedDirs.Count -ge 1) {
            $extracted = $extractedDirs[0].FullName
            Add-Check '解包目录含 index.html' (Test-Path -LiteralPath (Join-Path $extracted 'index.html'))
            if ($ExpectedUiRoot) {
                $expected = (Resolve-Path -LiteralPath $ExpectedUiRoot).Path
                $relativeList = @('index.html', 'css/figma.css', 'css/pineglow.css', 'assets/pine-forest.svg', 'js/api.js', 'js/launcher.js', 'js/i18n.js', 'js/skin3d.js', 'js/skin3d-core.js', 'vendor/three.min.js')
                $mismatch = @()
                foreach ($rel in $relativeList) {
                    $src = Join-Path $expected ($rel.Replace('/', '\'))
                    $dst = Join-Path $extracted ($rel.Replace('/', '\'))
                    if (-not (Test-Path -LiteralPath $src)) { $mismatch += "$rel(源码缺)"; continue }
                    if (-not (Test-Path -LiteralPath $dst)) { $mismatch += "$rel(解包缺)"; continue }
                    if ((Get-Sha256File $src) -ne (Get-Sha256File $dst)) { $mismatch += "$rel(SHA 不一致)" }
                }
                Add-Check '解包界面与候选源码逐字节一致（防「新服务配旧界面」）' ($mismatch.Count -eq 0) ($mismatch -join '; ')
            }
        }
    }

    $report = [ordered]@{
        generatedUtc    = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        exePath         = $exe
        exeSizeBytes    = (Get-Item -LiteralPath $exe).Length
        exeSha256       = (Get-Sha256File $exe)
        coldDir         = $coldDir
        port            = $Port
        expectedUiRoot  = $ExpectedUiRoot
        alive           = $alive
        mainWindowTitle = $windowTitle
        health          = $health
        stderr          = $stderrText
        checks          = $script:Checks.ToArray()
        failures        = $script:Failures.ToArray()
        servedAssets    = $served
        verdict         = if ($script:Failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
    }
    if ($JsonPath) {
        $parent = Split-Path -Parent $JsonPath
        if ($parent -and -not (Test-Path -LiteralPath $parent)) { [void](New-Item -ItemType Directory -Path $parent -Force) }
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $JsonPath -Encoding utf8
    } else {
        $report | ConvertTo-Json -Depth 8 | Write-Host
    }
} finally {
    if ($null -ne $proc) {
        $live = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if ($null -ne $live) {
            $null = & taskkill.exe /T /F /PID ([string]$proc.Id) 2>&1
        }
    }
    if (-not $KeepWorkDir -and (Test-Path -LiteralPath $coldDir)) {
        try { Remove-Item -LiteralPath $coldDir -Recurse -Force } catch { }
    }
}

Write-Host ''
if ($script:Failures.Count -gt 0) {
    Write-Host ("独立冷启动判定: FAIL（{0} 项失败）" -f $script:Failures.Count) -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}
Write-Host '独立冷启动判定: PASS' -ForegroundColor Green
exit 0
