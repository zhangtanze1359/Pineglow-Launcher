#Requires -Version 5.1
<#
.SYNOPSIS
    MR00 只读环境核对 + 命令执行取证助手。

.DESCRIPTION
    统一封装外部命令调用，满足本次批次的证据要求：
      - 每条命令记录 command / cwd / startUtc / endUtc / exitCode / durationSec
      - stdout 与 stderr 分文件保存，避免 stderr 混入
      - 绝不用 $ErrorActionPreference='Stop' 包住外部命令（原生命令写 stderr 会致命中断）
    只读脚本：不修改任何项目文件。

.EXAMPLE
    .\evidence\Invoke-Evidence.ps1 -LogPath .\evidence\logs\x.json -Command 'git rev-parse HEAD' -Cwd 'D:\松光启动器'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $LogPath,
    [Parameter(Mandatory = $true)][string] $Command,
    [string] $Cwd = (Get-Location).Path,
    [int]    $TimeoutSec = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$stdoutPath = "$LogPath.stdout.txt"
$stderrPath = "$LogPath.stderr.txt"
$outDir = Split-Path -Parent $LogPath
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    [void](New-Item -ItemType Directory -Path $outDir -Force)
}

# 用 Start-Process -Redirect 分离 stdout/stderr，并拿到真实 ExitCode
$startUtc = (Get-Date).ToUniversalTime()
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$tmpErr = "$LogPath.err.tmp"
$tmpOut = "$LogPath.out.tmp"
Remove-Item -LiteralPath $tmpErr, $tmpOut -Force -ErrorAction SilentlyContinue

$proc = Start-Process -FilePath 'cmd.exe' `
    -ArgumentList '/c', $Command `
    -WorkingDirectory $Cwd `
    -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
$sw.Stop()

# cmd.exe 写文件时可能用 GBK；按 UTF8 优先读，失败回退默认编码
function Read-BestEffort {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    try { return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) } catch { }
    try { return [System.IO.File]::ReadAllText($Path) } catch { }
    return ''
}
$outText = Read-BestEffort $tmpOut
$errText = Read-BestEffort $tmpErr
[System.IO.File]::WriteAllText($stdoutPath, $outText, (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($stderrPath, $errText, (New-Object System.Text.UTF8Encoding($false)))
Remove-Item -LiteralPath $tmpErr, $tmpOut -Force -ErrorAction SilentlyContinue

$record = [ordered]@{
    command     = $Command
    cwd         = $Cwd
    startUtc    = $startUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
    endUtc      = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    durationSec = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    exitCode    = $proc.ExitCode
    stdoutPath  = $stdoutPath
    stderrPath  = $stderrPath
    stdoutLines = ($outText -split "`r?`n").Count
    stderrLines = ($errText -split "`r?`n").Count
}
$json = New-Object System.Text.StringBuilder
[void]$json.AppendLine('{')
$keys = @($record.Keys)
for ($i = 0; $i -lt $keys.Count; $i++) {
    $k = $keys[$i]
    $v = $record[$k]
    $vs = if ($v -is [int] -or $v -is [double]) { "$v" } else { '"' + ($v -replace '\\', '\\\\') + '"' }
    [void]$json.AppendLine("  `"$k`": $vs,")
}
[void]$json.AppendLine("  `"exitCode`": $($proc.ExitCode)")
[void]$json.AppendLine('}')
[System.IO.File]::WriteAllText($LogPath, $json.ToString(), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ("[{0}] exit={1} {2:N1}s  :: {3}" -f (Split-Path -Leaf $LogPath), $proc.ExitCode, $sw.Elapsed.TotalSeconds, $Command)
exit $proc.ExitCode
