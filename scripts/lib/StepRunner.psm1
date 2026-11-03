#Requires -Version 5.1
<#
.SYNOPSIS
    Unicode 安全、退出码可信的「外部命令步骤」运行器（发版阻断项 R05 的修复件）。

.DESCRIPTION
    R05 的原始故障（docs/release-readiness-review-2026-10-05.md R05）：

      旧实现在 Windows 上把带中文 EvidenceDir 的整条命令写进一个
      `Set-Content -Encoding ascii` 的 .cmd：中文路径被写成 '?'，cmd 报
      「文件名、目录名或卷标语法不正确」，日志根本没产生，而多个步骤仍被记成
      exit 0 / PASS。也就是说：**验证器自己失真，会把「没跑」记成「通过」。**

    本模块把「外部命令步骤」的职责收敛为三件可断言的事：

      1. 命令文本整条经 CreateProcessW 以 Unicode 传入（Windows 用 `cmd /d /s /c "<命令>"`），
         工作目录用 ProcessStartInfo.WorkingDirectory 传入，日志用 .NET 流重定向按字节落盘，
         因此中文/空格参数与路径都不再依赖 cmd 的代码页或中间临时文件。
      2. 退出码立即由 Process.ExitCode 取得，并记录 超时 / 日志缺失 / 零输出 等真实状态，
         不以「最后一条命令的退出码」或「脚本没抛异常」代替。
      3. 判定与证据分离：Invoke-StepProcess 只采集事实，Test-StepGate 只做门槛判定，
         调用方据此区分 PASS / FAIL / 已知边界。

    安全边界：不修改调用方仓库中的任何文件；不写临时脚本；不执行 git 状态变更；不访问网络。

.PARAMETER Name
    步骤名（写入结果对象与调用方证据）。

.PARAMETER CommandLine
    要执行的命令原文（相对路径形式）。Windows 下经 `cmd /d /s /c` 执行，
    因此不应包含 `%`、`&`、`|` 等 cmd 元字符；非 Windows 下用 /bin/sh -c 执行。

.PARAMETER WorkingDirectory
    子进程工作目录。必须是绝对路径；不经 shell 文本传递，因此可含中文与空格。

.PARAMETER LogPath
    stdout 落盘路径（stderr 会追加其后）。父目录必须已存在。
    无法创建日志文件时**不抛异常**，而是把 logExists=$false 记进结果，让门槛判失败。

.PARAMETER TimeoutSec
    超时秒数。超时即杀进程树并把 timedOut=$true 记进结果（门槛判失败）。

.PARAMETER EnvironmentVars
    追加到子进程环境的变量（可选）。

.OUTPUTS
    [pscustomobject] 步骤事实对象：
      name, command, workingDirectory, logPath, logExists, logBytes, stderrBytes,
      exitCode, timedOut, durationSec, startUtc, endUtc, outputText, error
#>
Set-StrictMode -Version 2.0

function Test-StepRunnerIsWindows {
    [CmdletBinding()]
    param()
    if (Get-Variable -Name 'IsWindows' -ErrorAction SilentlyContinue) { return [bool]$IsWindows }
    return ($env:OS -eq 'Windows_NT')
}

function Set-StepProcessArguments {
    <#
      .SYNOPSIS
        统一设置子进程参数：PowerShell 7 / .NET Core 有 ArgumentList（自动引号、天然 Unicode 安全）；
        Windows PowerShell 5.1 / .NET Framework 没有该属性，退回手工引号的 Arguments 字符串。
      .DESCRIPTION
        本机实测没有 PowerShell 7（harness shell = Windows PowerShell 5.1），所以这条回退路径是必需的，
        不是可选优化。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Psi,
        [Parameter(Mandatory)][string[]] $ArgumentValues
    )
    if (($Psi.PSObject.Properties.Name) -contains 'ArgumentList') {
        foreach ($v in $ArgumentValues) { [void]$Psi.ArgumentList.Add($v) }
        return 'ArgumentList'
    }
    $quoted = @()
    foreach ($v in $ArgumentValues) {
        if ($v -match '[\s"]') { $quoted += ('"' + ($v -replace '"', '""') + '"') } else { $quoted += $v }
    }
    $Psi.Arguments = ($quoted -join ' ')
    return 'Arguments'
}

function Stop-StepProcessTree {
    <#
      .SYNOPSIS
        结束步骤进程及其子进程。
      .DESCRIPTION
        Windows 上步骤是通过 cmd.exe 起的，直接 Kill() 只杀 cmd.exe，
        node/dotnet 子进程仍然活着并**持有重定向管道**，收集线程会一直等到子进程自然退出
        （实测：5 秒超时被拖成 60 秒）。所以必须按进程树杀。
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Process)

    if (Test-StepRunnerIsWindows) {
        try {
            # 用 & 调用，把 taskkill 的输出收进本进程管道
            # （Start-Process 会直接写父控制台，把 SUCCESS 行漏进调用方的输出/日志）
            $null = & taskkill.exe /T /F /PID ([string]$Process.Id) 2>&1
            if ($LASTEXITCODE -eq 0) { return 'taskkill /T /F' }
        } catch { }
    }
    try { $Process.Kill() } catch { }
    return 'Kill()'
}

function Invoke-StepProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $CommandLine,
        [Parameter(Mandatory)][string] $WorkingDirectory,
        [Parameter(Mandatory)][string] $LogPath,
        [int] $TimeoutSec = 1800,
        [hashtable] $EnvironmentVars = @{}
    )

    $isWindows = Test-StepRunnerIsWindows
    $startUtc = (Get-Date).ToUniversalTime()
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    $logExists = $false
    $logBytes = 0
    $stderrBytes = 0
    $exitCode = $null
    $timedOut = $false
    $errorText = ''
    $outputText = ''

    $stderrPath = "$LogPath.stderr"
    $outFs = $null
    $errFs = $null

    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.WorkingDirectory = $WorkingDirectory

        if ($isWindows) {
            # 刻意不使用临时 .cmd：命令文本整条经 CreateProcessW 以 Unicode 传入，
            # 因此中文/空格参数与工作目录都不再依赖 cmd 的代码页与批处理编码。
            #
            # 这里**直接**给 Arguments 赋值，不经过通用引号助手：
            # cmd 的 /s 语义要求 /c 之后是一个「首尾带引号」的整串，
            # 若再叠一层引号转义，末尾引号会被带进命令文本
            # （实测：node 收到 `console.log(1)"` 而语法报错）。
            # 命令内部自带的双引号（--logger "console;verbosity=minimal"）实测可正确传递。
            $psi.FileName = 'cmd.exe'
            $psi.Arguments = '/d /s /c "' + $CommandLine.Trim() + '"'
        } else {
            $psi.FileName = '/bin/sh'
            [void](Set-StepProcessArguments -Psi $psi -ArgumentValues @('-c', $CommandLine))
        }

        foreach ($k in $EnvironmentVars.Keys) {
            $psi.EnvironmentVariables[[string]$k] = [string]$EnvironmentVars[$k]
        }

        try {
            $outFs = [System.IO.File]::Create($LogPath)
            $logExists = $true
        } catch {
            $errorText = "无法创建日志文件: $($_.Exception.Message)"
        }

        $proc = [System.Diagnostics.Process]::Start($psi)
        $outTask = $null
        $errTask = $null

        if ($logExists) {
            try {
                $errFs = [System.IO.File]::Create($stderrPath)
                $outTask = $proc.StandardOutput.BaseStream.CopyToAsync($outFs)
                $errTask = $proc.StandardError.BaseStream.CopyToAsync($errFs)
            } catch {
                $errorText = "重定向失败: $($_.Exception.Message)"
            }
        } else {
            # 不能落盘也必须把管道读干，否则子进程可能因管道写满而挂住
            $sink = New-Object System.IO.MemoryStream
            $sink2 = New-Object System.IO.MemoryStream
            $outTask = $proc.StandardOutput.BaseStream.CopyToAsync($sink)
            $errTask = $proc.StandardError.BaseStream.CopyToAsync($sink2)
        }

        try {
            $exited = $proc.WaitForExit([int]($TimeoutSec * 1000))
        } catch {
            $exited = $false
        }

        if (-not $exited) {
            $timedOut = $true
            [void](Stop-StepProcessTree -Process $proc)
            try { [void]$proc.WaitForExit(30000) } catch { }
        }

        if ($outTask -ne $null -and $errTask -ne $null) {
            try {
                # 有界等待：即便子进程树没杀干净，也不能让验证器无限挂住
                [void][System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($outTask, $errTask), 20000)
            } catch {
                if (-not $errorText) { $errorText = "读取子进程输出失败: $($_.Exception.Message)" }
            }
        }

        try { $exitCode = $proc.ExitCode } catch { $exitCode = $null }
        $proc.Dispose()
    } catch {
        $errorText = "启动子进程失败: $($_.Exception.Message)"
    } finally {
        if ($null -ne $outFs) { try { $outFs.Flush(); $outFs.Dispose() } catch { } }
        if ($null -ne $errFs) { try { $errFs.Flush(); $errFs.Dispose() } catch { } }
    }

    # stderr 追加到同一日志，保持「单一日志承载全部输出」的既有契约（与 2>&1 等价，顺序不保证）
    if ($logExists -and (Test-Path -LiteralPath $stderrPath)) {
        try {
            $errLen = (Get-Item -LiteralPath $stderrPath).Length
            if ($errLen -gt 0) {
                $errBytes = [System.IO.File]::ReadAllBytes($stderrPath)
                $append = [System.IO.File]::Open($LogPath, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
                try { $append.Write($errBytes, 0, $errBytes.Length) } finally { $append.Dispose() }
            }
            $stderrBytes = [int]$errLen
        } catch { } finally {
            Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
        }
    }

    if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
        $logExists = $true
        try {
            $bytes = [System.IO.File]::ReadAllBytes($LogPath)
            $logBytes = $bytes.Length
            $outputText = [System.Text.Encoding]::UTF8.GetString($bytes)
        } catch { }
    }

    $sw.Stop()
    return [pscustomobject]@{
        name             = $Name
        command          = $CommandLine
        workingDirectory = $WorkingDirectory
        logPath          = $LogPath
        logExists        = $logExists
        logBytes         = $logBytes
        stderrBytes      = $stderrBytes
        exitCode         = $exitCode
        timedOut         = $timedOut
        durationSec      = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        startUtc         = $startUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
        endUtc           = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        outputText       = $outputText
        error            = $errorText
    }
}

function Test-StepGate {
    <#
      .SYNOPSIS
        对 Invoke-StepProcess 的事实做门槛判定，返回 Ok 与失败原因列表。

      .DESCRIPTION
        R05 要求这些情形分别非零失败：命令非零退出、超时、日志缺失、零输出、零测试。

        判定顺序（命中即记原因，不做短路，便于一次看清所有问题）：
          - 超时
          - 启动/重定向错误
          - 日志不存在
          - RequireOutput 且日志 0 字节
          - RequireMatch 任一正则未命中
          - RequirePositiveCount 任一正则未命中或捕获组 1 非正整数
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Result,
        [switch] $RequireOutput,
        [string[]] $RequireMatch = @(),
        [string[]] $RequirePositiveCount = @()
    )

    $reasons = New-Object System.Collections.Generic.List[string]
    $text = ''
    if ($null -ne $Result.PSObject.Properties['outputText'] -and $null -ne $Result.outputText) { $text = [string]$Result.outputText }

    if ($Result.timedOut) { $reasons.Add("步骤超时（超过 $($Result.durationSec)s 未结束，已杀进程）") | Out-Null }
    if ($Result.error) { $reasons.Add([string]$Result.error) | Out-Null }
    if (-not $Result.logExists) { $reasons.Add("步骤日志未生成: $($Result.logPath)") | Out-Null }
    elseif ($RequireOutput -and [int]$Result.logBytes -le 0) { $reasons.Add('命令退出但未产生任何输出（无法证明命令真的执行了）') | Out-Null }

    foreach ($pat in $RequireMatch) {
        if (-not $pat) { continue }
        if (-not [regex]::IsMatch($text, $pat, 'IgnoreCase,Multiline')) {
            $reasons.Add("缺少必需证据（未匹配 /$pat/）") | Out-Null
        }
    }

    foreach ($pat in $RequirePositiveCount) {
        if (-not $pat) { continue }
        $m = [regex]::Match($text, $pat, 'IgnoreCase,Multiline')
        if (-not $m.Success) {
            $reasons.Add("缺少计数证据（未匹配 /$pat/）") | Out-Null
            continue
        }
        $value = $null
        if ($m.Groups.Count -gt 1) { $value = $m.Groups[1].Value }
        $n = 0
        if (-not [int]::TryParse(($value -replace '[,_]', ''), [ref]$n) -or $n -le 0) {
            $reasons.Add("计数必须为正整数，实际 '$value'（匹配 /$pat/）——零测试/零条目不得判为通过") | Out-Null
        }
    }

    return [pscustomobject]@{
        ok      = ($reasons.Count -eq 0)
        reasons = $reasons.ToArray()
    }
}

Export-ModuleMember -Function Test-StepRunnerIsWindows, Set-StepProcessArguments, Stop-StepProcessTree, Invoke-StepProcess, Test-StepGate
