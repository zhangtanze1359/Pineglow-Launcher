#Requires -Version 5.1
<#
.SYNOPSIS
    MR00 正式验证最后一步：以真实 VerifyHost.exe 运行 --bisect-smoke。

.DESCRIPTION
    不用 cmd 包装（避免 --bisect-smoke 被命令行再解析成参数），
    直接以 ProcessStartInfo 启动 exe 并对 Process 对象 WaitForExit()（只等自身）。
    运行前后各记录一次沙盒与端口状态，证明隔离与清理。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ExePath,
    [Parameter(Mandatory = $true)][string] $LogDir,
    [string] $Name = '09-verifyhost-bisect-smoke',
    [int]    $Port = 48130
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
if (-not (Test-Path -LiteralPath $LogDir)) { [void](New-Item -ItemType Directory -Path $LogDir -Force) }

$baseDir = Split-Path -Parent $ExePath
$flagPath = Join-Path $baseDir 'portable.flag'
$dataDir  = Join-Path $baseDir 'data'

function Get-PortListening { param([int] $P)
    try { return (@(Get-NetTCPConnection -LocalPort $P -State Listen -ErrorAction SilentlyContinue).Count -gt 0) } catch { return $false }
}

$before = [ordered]@{
    portableFlagExists = (Test-Path -LiteralPath $flagPath)
    dataDirExists      = (Test-Path -LiteralPath $dataDir)
    portListening      = (Get-PortListening -P $Port)
}

$stdoutPath = Join-Path $LogDir "$Name.stdout.txt"
$stderrPath = Join-Path $LogDir "$Name.stderr.txt"
$metaPath   = Join-Path $LogDir "$Name.json"
$tmpOut = [System.IO.Path]::GetTempFileName()
$tmpErr = [System.IO.Path]::GetTempFileName()

$startUtc = (Get-Date).ToUniversalTime()
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName         = $ExePath
$psi.Arguments        = "--bisect-smoke --port $Port"
$psi.WorkingDirectory = $baseDir
$psi.UseShellExecute  = $false
$psi.CreateNoWindow    = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
[void]$proc.Start()
# 异步读取两条流，避免管道缓冲死锁
$oTask = $proc.StandardOutput.ReadToEndAsync()
$eTask = $proc.StandardError.ReadToEndAsync()
$proc.WaitForExit()
$sw.Stop()
$outText = $oTask.Result
$errText = $eTask.Result
$code = $proc.ExitCode

$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($stdoutPath, $outText, $utf8)
[System.IO.File]::WriteAllText($stderrPath, $errText, $utf8)

$after = [ordered]@{
    portableFlagExists = (Test-Path -LiteralPath $flagPath)
    dataDirExists      = (Test-Path -LiteralPath $dataDir)
    portListening      = (Get-PortListening -P $Port)
}

$passChecks = ([regex]::Matches($outText, '(?m)^\s*\[PASS\]')).Count
$failChecks = ([regex]::Matches($outText, '(?m)^\s*\[FAIL\]')).Count
$sawPass    = $outText -match '\[BisectSmoke\] PASS'
$sawFail    = $outText -match '\[BisectSmoke\] FAIL'

$meta = [ordered]@{
    name          = $Name
    command       = "$ExePath --bisect-smoke --port $Port"
    cwd           = $baseDir
    startUtc      = $startUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
    endUtc        = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    durationSec   = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    exitCode      = $code
    status        = $(if ($code -eq 0) { 'PASS' } else { 'FAIL' })
    stdoutPath    = $stdoutPath
    smokePassChecks = $passChecks
    smokeFailChecks = $failChecks
    smokeSaysPass = $sawPass
    smokeSaysFail = $sawFail
    sandboxBefore = $before
    sandboxAfter  = $after
    sandboxCleanedUp = (($before.portableFlagExists -eq $after.portableFlagExists) -and ($before.dataDirExists -eq $after.dataDirExists))
    portReleased  = (-not $after.portListening)
}
($meta | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $metaPath -Encoding UTF8

Write-Host ("[{0}] exit={1} {2:N1}s  smoke PASS={3} FAIL={4}" -f $Name, $code, $sw.Elapsed.TotalSeconds, $passChecks, $failChecks)
Write-Host ("     沙箱清理={0}  端口已释放={1}" -f $meta.sandboxCleanedUp, $meta.portReleased)
Write-Host '----- VerifyHost 输出 -----'
Write-Host $outText
if ($errText) { Write-Host '----- stderr -----'; Write-Host $errText }
exit $code
