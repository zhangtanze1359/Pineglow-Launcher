#Requires -Version 5.1
<#
.SYNOPSIS
    MR00 正式验证步骤执行器（修正版）。

.DESCRIPTION
    修正了 verify-source-snapshot.ps1 的两个已证实缺陷，使本轮取到的退出码可信：

    缺陷 1（假通过）：原脚本用 -Encoding ascii 写出临时 .cmd，
        当日志路径含非 ASCII 字符（如 C:\...\松光启动器\...）时会被写成 ???，
        cmd.exe 整行解析失败并报 "The filename, directory name, or volume label syntax
        is incorrect."，命令从未执行；但 %ERRORLEVEL% 仍为 0，脚本遂记为 exit=0 PASS。
        → 本实现把日志重定向到 %TEMP% 下的纯 ASCII 路径（cmd 不再接触非 ASCII 路径），
          并显式断言日志文件已生成；未生成即按失败处理（exitCode 9001），绝不记 PASS。

    缺陷 2（挂死）：原脚本用 Start-Process -Wait。
        PowerShell 的 -Wait 会等待整棵进程树，而 dotnet build 会派生常驻的
        VBCSCompiler.exe（Roslyn 编译服务器，idle 约 10 分钟），父进程退出后它仍存活，
        导致每个编译步骤都可能空等约 10 分钟，表现为脚本挂死。
        → 本实现不使用 -Wait，改为对返回的 Process 对象调用 WaitForExit()，
          该方法只等待 cmd.exe 自身，不等待 VBCSCompiler 等后代。

    只读性：本脚本不修改任何被测项目文件，仅写证据文件。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $LogDir,
    [Parameter(Mandatory = $true)][string] $Name,
    [Parameter(Mandatory = $true)][string] $Command,
    [Parameter(Mandatory = $true)][string] $Cwd,
    [string[]] $Expect = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not (Test-Path -LiteralPath $LogDir)) { [void](New-Item -ItemType Directory -Path $LogDir -Force) }
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())

$bat    = Join-Path $tempRoot ("mr00-step-{0}.cmd" -f ([guid]::NewGuid().ToString('N')))
$tmpLog = Join-Path $tempRoot ("mr00-log-{0}.txt"  -f ([guid]::NewGuid().ToString('N')))
$stdoutPath = Join-Path $LogDir "$Name.stdout.txt"
$stderrPath = Join-Path $LogDir "$Name.stderr.txt"
$metaPath   = Join-Path $LogDir "$Name.json"

# 预检：cmd 只会接触 $tempRoot 下的纯 ASCII 路径
foreach ($p in @($bat, $tmpLog)) {
    $round = [System.Text.Encoding]::ASCII.GetString([System.Text.Encoding]::ASCII.GetBytes($p))
    if ($round -ne $p) { throw "内部错误：临时路径非 ASCII，会重现缺陷 1: $p" }
}

Set-Content -LiteralPath $bat -Encoding ascii -Value (
    "@echo off`r`ncd /d `"$Cwd`"`r`n$Command > `"$tmpLog`" 2>&1`r`nexit /b %ERRORLEVEL%`r`n")

$startUtc = (Get-Date).ToUniversalTime()
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName        = 'cmd.exe'
$psi.Arguments       = '/c ' + $bat
$psi.WorkingDirectory = $Cwd
$psi.UseShellExecute  = $false
$psi.CreateNoWindow    = $true
$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
[void]$proc.Start()
$proc.WaitForExit()          # 只等待 cmd.exe 自身，不等待 VBCSCompiler.exe 等后代
$code = $proc.ExitCode
$sw.Stop()

# 缺陷 1 的护栏：日志未生成 == 命令极可能根本没跑，不能记为通过
$logProduced = Test-Path -LiteralPath $tmpLog
$verifierDefect = $false
if (-not $logProduced) {
    $code = 9001
    $verifierDefect = $true
}

$text = ''
$decodeMode = 'none'
if ($logProduced) {
    # 解码策略：先试严格 UTF-8（node / MSBuild 的现代输出），失败再回退控制台代码页 936。
    # 不能一律用 GBK：node 的 spec 报告是 UTF-8，用 GBK 解会把 '#'、'✔' 变成乱码，
    # 导致 '^# fail 0' 这类计数正则全部失配（实测已发生）。
    $bytes = [System.IO.File]::ReadAllBytes($tmpLog)
    $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
    try {
        $text = $strictUtf8.GetString($bytes)
        $decodeMode = 'utf-8'
    } catch {
        $text = [System.Text.Encoding]::GetEncoding(936).GetString($bytes)
        $decodeMode = 'gbk-936'
    }
}
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($stdoutPath, $text, $utf8NoBom)
[System.IO.File]::WriteAllText($stderrPath, '', $utf8NoBom)
# 保留未经转码的原始字节，供独立复验方按正确代码页自行解码
if ($logProduced) { Copy-Item -LiteralPath $tmpLog -Destination (Join-Path $LogDir "$Name.raw.log") -Force }

# 测试数量提取。
# 注意：node v24 spec 报告的汇总行前缀是 U+2139(ℹ) 而非 '#'，且 cmd 重定向会按控制台
# 代码页转码使该前缀变成乱码。因此正则必须「不锚定前缀、只匹配 标签+数字+行尾」，
# 否则计数全部失配（2026-10-02 实测踩过：锚定 '^# tests' 时 184/184 全被漏掉）。
$nodeTests = $null; $nodePass = $null; $nodeFail = $null; $nodeSkip = $null
if ($text -match '(?m)^\S*\s*tests\s+(\d+)\s*$')    { $nodeTests = $Matches[1] }
if ($text -match '(?m)^\S*\s*pass\s+(\d+)\s*$')     { $nodePass  = $Matches[1] }
if ($text -match '(?m)^\S*\s*fail\s+(\d+)\s*$')     { $nodeFail  = $Matches[1] }
if ($text -match '(?m)^\S*\s*skipped\s+(\d+)\s*$')  { $nodeSkip  = $Matches[1] }
$csPassed = $null; $csFailed = $null; $csSkipped = $null; $csTotal = $null
# dotnet test 在 zh-CN 控制台下输出中文汇总：「已通过! - 失败: 0，通过: 612，已跳过: 0，总计: 612」
if ($text -match '(?m)通过:\s*(\d+)')   { $csPassed  = $Matches[1] }
if ($text -match '(?m)失败:\s*(\d+)')   { $csFailed  = $Matches[1] }
if ($text -match '(?m)已跳过:\s*(\d+)') { $csSkipped = $Matches[1] }
if ($text -match '(?m)总计:\s*(\d+)')   { $csTotal   = $Matches[1] }
# 英文回退
if ($null -eq $csTotal) {
    if ($text -match '(?m)Passed:\s*(\d+)')   { $csPassed  = $Matches[1] }
    if ($text -match '(?m)Failed:\s*(\d+)')    { $csFailed  = $Matches[1] }
    if ($text -match '(?m)Skipped:\s*(\d+)')   { $csSkipped = $Matches[1] }
    if ($text -match '(?m)Total:\s*(\d+)')     { $csTotal   = $Matches[1] }
}
$buildErrors = $null
if ($text -match '(?m)^\s*(\d+)\s+Error\(s\)')      { $buildErrors = $Matches[1] }
if (($null -eq $buildErrors) -and ($text -match '(?m)个错误')) { $buildErrors = 'zh-present' }

# 期望串命中情况
$expectHits = @()
foreach ($e in $Expect) {
    if ($text -match $e) { $expectHits += "$e=YES" } else { $expectHits += "$e=NO" }
}

$status = if ($code -eq 0) { 'PASS' } else { 'FAIL' }
$meta = [ordered]@{
    name           = $Name
    command        = $Command
    cwd            = $Cwd
    startUtc       = $startUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
    endUtc         = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    durationSec    = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    exitCode       = $code
    status         = $status
    logProduced    = $logProduced
    verifierDefect = $verifierDefect
    logBytes       = $(if ($logProduced) { (Get-Item -LiteralPath $tmpLog).Length } else { 0 })
    decodeMode     = $decodeMode
    stdoutPath     = $stdoutPath
    nodeTests      = $nodeTests
    nodePass       = $nodePass
    nodeFail       = $nodeFail
    nodeSkipped    = $nodeSkip
    csPassed       = $csPassed
    csFailed       = $csFailed
    csSkipped      = $csSkipped
    csTotal        = $csTotal
    buildErrors    = $buildErrors
    expectHits     = $expectHits
}
($meta | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $metaPath -Encoding UTF8

$extra = @()
if ($nodeTests) { $extra += "node tests=$nodeTests pass=$nodePass fail=$nodeFail skip=$nodeSkip" }
if ($csTotal)   { $extra += "cs total=$csTotal passed=$csPassed failed=$csFailed skipped=$csSkipped" }
if ($buildErrors -ne $null) { $extra += "buildErrors=$buildErrors" }
$extra += ($expectHits -join ' ')
Write-Host ("[{0}] exit={1} {2,6:N1}s log={3}B  {4}" -f $Name, $code, $sw.Elapsed.TotalSeconds, $meta.logBytes, ($extra -join '  '))
if ($verifierDefect) { Write-Host "     !! 验证器护栏触发：命令未产生日志，已按失败处理（9001）" -ForegroundColor Red }
exit $code
