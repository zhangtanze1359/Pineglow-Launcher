#Requires -Version 5.1
<#
.SYNOPSIS
    scripts/lib/StepRunner.psm1 的自证测试（R05：验证器本身必须可信）。

.DESCRIPTION
    R05 的原始故障不是产品缺陷，而是**验证工具失真**：旧实现用
    `Set-Content -Encoding ascii` 把含中文证据目录的整条命令写成 .cmd，
    中文被写成 '?'，cmd 报「文件名、目录名或卷标语法不正确」，日志根本没产生，
    却有多个步骤被记成 exit 0 / PASS。

    因此本文件逐条断言修复后的运行器在这六类情形下都给出**可信事实**：

      SR-01 正常命令：exit 0、日志存在且有内容、门槛通过
      SR-02 非零退出：真实退出码被记录（不是 0，也不被吞掉）
      SR-03 中文+空格工作目录：命令确实在该目录下执行（日志里是该中文路径）
      SR-04 超时：timedOut=$true 且进程被杀，门槛失败
      SR-05 零输出：命令退出 0 但毫无输出时，RequireOutput 门槛必须失败
      SR-06 日志无法创建：不抛异常，logExists=$false，门槛失败
      SR-07 零计数：'pass 0' 这种「零测试」必须失败，'pass 7' 才通过
      SR-08 缺少必需证据：报告里没有 'Passed!' 时门槛失败
      SR-09 中文日志路径：日志按字节写入，中文内容可被 UTF-8 正确读回

    运行方式（本机执行策略默认不允许未签名脚本，必须显式 Bypass）：
      powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/step-runner.test.ps1

    退出码：0 = 全部通过；1 = 有失败用例；2 = 环境不满足。
#>
[CmdletBinding()]
param(
    [string] $EvidenceDir
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:Total = 0
$script:Failed = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

function Assert-That {
    param([string] $Name, [bool] $Condition, [string] $Detail = '')
    $script:Total++
    if ($Condition) {
        Write-Host ("ok   - {0}" -f $Name) -ForegroundColor Green
    } else {
        $script:Failed++
        $script:Failures.Add("$Name :: $Detail") | Out-Null
        Write-Host ("FAIL - {0} :: {1}" -f $Name, $Detail) -ForegroundColor Red
    }
}

$modulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'lib/StepRunner.psm1'
if (-not (Test-Path -LiteralPath $modulePath)) { Write-Host "找不到模块: $modulePath" -ForegroundColor Red; exit 2 }
Import-Module $modulePath -Force -DisableNameChecking

$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('pineglow-steprunner-tests-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $workRoot)

function New-CaseDir {
    param([string] $Name)
    $d = Join-Path $workRoot $Name
    [void](New-Item -ItemType Directory -Path $d -Force)
    return $d
}

try {
    $nodeExe = (Get-Command node -ErrorAction SilentlyContinue)
    if ($null -eq $nodeExe) { Write-Host '本机没有 node，无法运行聚焦测试' -ForegroundColor Red; exit 2 }

    # ---------------- SR-01 正常命令 ----------------
    $d1 = New-CaseDir 'sr01'
    $log1 = Join-Path $d1 'step.log'
    $r1 = Invoke-StepProcess -Name 'SR-01' -CommandLine 'node -e "console.log(1+1)"' -WorkingDirectory $d1 -LogPath $log1 -TimeoutSec 120
    $g1 = Test-StepGate -Result $r1 -RequireOutput -RequirePositiveCount '^\s*(\d+)\s*$'
    Assert-That 'SR-01 正常命令 exit=0' ($r1.exitCode -eq 0) "exitCode=$($r1.exitCode) error=$($r1.error)"
    Assert-That 'SR-01 日志存在且有内容' ($r1.logExists -and $r1.logBytes -gt 0) "logExists=$($r1.logExists) logBytes=$($r1.logBytes)"
    Assert-That 'SR-01 门槛通过' ($g1.ok) ("reasons=" + ($g1.reasons -join '; '))

    # ---------------- SR-02 非零退出 ----------------
    $d2 = New-CaseDir 'sr02'
    $log2 = Join-Path $d2 'step.log'
    $r2 = Invoke-StepProcess -Name 'SR-02' -CommandLine 'node -e "process.exit(3)"' -WorkingDirectory $d2 -LogPath $log2 -TimeoutSec 120
    Assert-That 'SR-02 非零退出码被如实记录' ($r2.exitCode -eq 3) "exitCode=$($r2.exitCode)"

    # ---------------- SR-03 中文+空格工作目录 ----------------
    $d3 = New-CaseDir '松光 步骤运行器 中文空格'
    $probe = Join-Path $d3 'probe.cjs'
    [System.IO.File]::WriteAllText($probe, "console.log('CWD=' + process.cwd());", (New-Object System.Text.UTF8Encoding($false)))
    $log3 = Join-Path $d3 'step.log'
    $r3 = Invoke-StepProcess -Name 'SR-03' -CommandLine 'node probe.cjs' -WorkingDirectory $d3 -LogPath $log3 -TimeoutSec 120
    $expectCwd = (Resolve-Path -LiteralPath $d3).Path
    Assert-That 'SR-03 中文目录下 exit=0' ($r3.exitCode -eq 0) "exitCode=$($r3.exitCode) error=$($r3.error)"
    Assert-That 'SR-03 命令确实在中文工作目录下执行' ($r3.outputText -like "*CWD=$expectCwd*") "output=[$($r3.outputText.Trim())] 期望 CWD=$expectCwd"

    # ---------------- SR-04 超时 ----------------
    $d4 = New-CaseDir 'sr04'
    $log4 = Join-Path $d4 'step.log'
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r4 = Invoke-StepProcess -Name 'SR-04' -CommandLine 'node -e "setTimeout(function(){},60000)"' -WorkingDirectory $d4 -LogPath $log4 -TimeoutSec 5
    $sw.Stop()
    $g4 = Test-StepGate -Result $r4
    Assert-That 'SR-04 超时被识别' ($r4.timedOut) "timedOut=$($r4.timedOut) exitCode=$($r4.exitCode)"
    Assert-That 'SR-04 超时后确实提前结束（未等满 60s）' ($sw.Elapsed.TotalSeconds -lt 45) ("elapsed=" + [math]::Round($sw.Elapsed.TotalSeconds, 1))
    Assert-That 'SR-04 超时门槛失败' (-not $g4.ok) ("reasons=" + ($g4.reasons -join '; '))

    # ---------------- SR-05 零输出 ----------------
    $d5 = New-CaseDir 'sr05'
    $log5 = Join-Path $d5 'step.log'
    $r5 = Invoke-StepProcess -Name 'SR-05' -CommandLine 'rem 这一行不产生任何输出' -WorkingDirectory $d5 -LogPath $log5 -TimeoutSec 120
    $g5 = Test-StepGate -Result $r5 -RequireOutput
    Assert-That 'SR-05 零输出被如实记录' ($r5.logBytes -eq 0) "logBytes=$($r5.logBytes)"
    Assert-That 'SR-05 零输出不得判为通过' (-not $g5.ok) ("reasons=" + ($g5.reasons -join '; '))

    # ---------------- SR-06 日志无法创建 ----------------
    $d6 = New-CaseDir 'sr06'
    $log6 = Join-Path $d6 '不存在的子目录/step.log'
    $r6 = Invoke-StepProcess -Name 'SR-06' -CommandLine 'node -e "console.log(1)"' -WorkingDirectory $d6 -LogPath $log6 -TimeoutSec 120
    $g6 = Test-StepGate -Result $r6
    Assert-That 'SR-06 日志缺失不抛异常但被记录' ((-not $r6.logExists) -and $r6.error) "logExists=$($r6.logExists) error=$($r6.error)"
    Assert-That 'SR-06 日志缺失门槛失败' (-not $g6.ok) ("reasons=" + ($g6.reasons -join '; '))

    # ---------------- SR-07 零计数 / 正计数 ----------------
    $d7 = New-CaseDir 'sr07'
    $log7a = Join-Path $d7 'zero.log'
    $r7a = Invoke-StepProcess -Name 'SR-07-zero' -CommandLine 'node -e "console.log(''pass 0'')"' -WorkingDirectory $d7 -LogPath $log7a -TimeoutSec 120
    $g7a = Test-StepGate -Result $r7a -RequirePositiveCount 'pass (\d+)'
    Assert-That 'SR-07 零测试必须失败' (-not $g7a.ok) ("reasons=" + ($g7a.reasons -join '; '))
    $log7b = Join-Path $d7 'seven.log'
    $r7b = Invoke-StepProcess -Name 'SR-07-seven' -CommandLine 'node -e "console.log(''pass 7'')"' -WorkingDirectory $d7 -LogPath $log7b -TimeoutSec 120
    $g7b = Test-StepGate -Result $r7b -RequirePositiveCount 'pass (\d+)'
    Assert-That 'SR-07 正计数通过' ($g7b.ok) ("reasons=" + ($g7b.reasons -join '; '))

    # ---------------- SR-08 缺少必需证据 ----------------
    $d8 = New-CaseDir 'sr08'
    $log8 = Join-Path $d8 'step.log'
    $r8 = Invoke-StepProcess -Name 'SR-08' -CommandLine 'node -e "console.log(''hello'')"' -WorkingDirectory $d8 -LogPath $log8 -TimeoutSec 120
    $g8 = Test-StepGate -Result $r8 -RequireMatch 'Passed!'
    Assert-That 'SR-08 缺少必需证据时门槛失败' (-not $g8.ok) ("reasons=" + ($g8.reasons -join '; '))

    # ---------------- SR-09 中文日志路径 ----------------
    $d9 = New-CaseDir '松光 证据 目录'
    $log9 = Join-Path $d9 '步骤输出.log'
    $r9 = Invoke-StepProcess -Name 'SR-09' -CommandLine 'node -e "console.log(''中文日志写入成功'')"' -WorkingDirectory $d9 -LogPath $log9 -TimeoutSec 120
    $back = ''
    if (Test-Path -LiteralPath $log9) { $back = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($log9)) }
    Assert-That 'SR-09 中文日志路径可写可读' ($r9.exitCode -eq 0 -and $back -like '*中文日志写入成功*') "exitCode=$($r9.exitCode) back=[$back]"

    # ---------------- 结果 ----------------
    Write-Host ''
    if ($EvidenceDir) {
        [void](New-Item -ItemType Directory -Path $EvidenceDir -Force)
        $summary = [ordered]@{
            generatedUtc   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            powerShell     = $PSVersionTable.PSVersion.ToString()
            edition        = $PSVersionTable.PSEdition
            total          = $script:Total
            failed         = $script:Failed
            failures       = $script:Failures.ToArray()
            module         = $modulePath
        }
        $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDir 'step-runner-summary.json') -Encoding utf8
    }
    # 机器可判定的 ASCII 汇总行：调用方（快照验证脚本 / CI）按它做门槛，
    # 不依赖宿主控制台的代码页或 Write-Host 的编码。
    Write-Host ("TOTAL={0} FAILED={1}" -f $script:Total, $script:Failed) -ForegroundColor Cyan
    Write-Host ("共计 {0} 项，失败 {1} 项" -f $script:Total, $script:Failed) -ForegroundColor Cyan
} finally {
    if (-not $EvidenceDir -and (Test-Path -LiteralPath $workRoot)) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($script:Failed -gt 0) { exit 1 }
exit 0
