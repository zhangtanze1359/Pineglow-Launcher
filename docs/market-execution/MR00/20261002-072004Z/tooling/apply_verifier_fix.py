"""MR00-5: 在 ExecutionRoot 内对 verify-source-snapshot.ps1 施加最小修复，并输出统一 diff。

修复依据（均为 2026-10-02 本轮实测证据，见 EvidenceRoot/MR00/<run-id>/logs/）：
  缺陷 1（假通过）：临时 .cmd 以 -Encoding ascii 写出，日志路径含非 ASCII 字符时
    被转成 ???，cmd 整行解析失败、命令从未执行，而 %ERRORLEVEL% 仍为 0 -> 记为 PASS。
  缺陷 2（挂死）：Start-Process -Wait 等待整棵进程树，dotnet build 派生的常驻
    VBCSCompiler.exe 使每个编译步骤空等约 10 分钟。
  护栏：日志未生成即判定命令未执行，绝不记为通过。
"""
import difflib
import io
import os
import sys

RUNNER_OLD = '''    if ($onWindows) {
        $bat = Join-Path $tempRoot ("vss-step-{0}.cmd" -f ([guid]::NewGuid().ToString('N')))
        Set-Content -LiteralPath $bat -Encoding ascii -Value (
            "@echo off`r`ncd /d `"$SnapshotDir`"`r`n$CommandLine > `"$logPath`" 2>&1`r`nexit /b %ERRORLEVEL%`r`n")
        try {
            $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $bat -WorkingDirectory $SnapshotDir -NoNewWindow -Wait -PassThru
            $code = $proc.ExitCode
        } finally { if (Test-Path -LiteralPath $bat) { Remove-Item -LiteralPath $bat -Force } }
    } else {
        $sh = Join-Path $tempRoot ("vss-step-{0}.sh" -f ([guid]::NewGuid().ToString('N')))
        Set-Content -LiteralPath $sh -Encoding utf8 -Value (
            "#!/bin/sh`ncd `"$SnapshotDir`"`n$CommandLine > `"$logPath`" 2>&1`nexit `$?`n")
        try {
            $proc = Start-Process -FilePath '/bin/sh' -ArgumentList $sh -WorkingDirectory $SnapshotDir -NoNewWindow -Wait -PassThru
            $code = $proc.ExitCode
        } finally { if (Test-Path -LiteralPath $sh) { Remove-Item -LiteralPath $sh -Force } }
    }
'''

RUNNER_NEW = '''    # ---- [MR00 修复] 2026-10-02，依据本轮实测证据 ----
    # 缺陷 1（假通过）：原实现把 $logPath 直接写进以 -Encoding ascii 写出的临时 .cmd。
    #   当 -EvidenceDir 含非 ASCII 字符（如 C:\\...\\松光启动器\\...）时该路径被写成 ???，
    #   cmd.exe 整行解析失败并输出 "The filename, directory name, or volume label syntax
    #   is incorrect."，命令从未执行；但 %ERRORLEVEL% 仍为 0，脚本遂把该步骤记为
    #   exit=0 / status=PASS。实测：8 个步骤全部「1.0s PASS」，而 step*.log 一个都没生成。
    #   => 改为让临时脚本只接触 %TEMP% 下的纯 ASCII 路径，再由 PowerShell 搬运到真实日志路径。
    # 缺陷 2（挂死）：原实现用 Start-Process -Wait。PowerShell 的 -Wait 会等待整棵进程树，
    #   而 dotnet build 会派生常驻的 VBCSCompiler.exe（Roslyn 编译服务器，idle 约 10 分钟），
    #   其父进程退出后它仍存活，导致每个编译步骤都可能空等约 10 分钟，表现为脚本挂死。
    #   实测：build 步骤日志已完整写出，脚本仍阻塞在 -Wait 上，step7 日志始终不出现。
    #   => 去掉 -Wait，改为对返回的 Process 对象调用 WaitForExit()，它只等待 cmd/sh 自身。
    # 护栏：日志文件未生成 == 命令极可能根本没跑，绝不能记为通过，一律按失败处理。
    $logProduced = $false
    if ($onWindows) {
        $bat    = Join-Path $tempRoot ("vss-step-{0}.cmd" -f ([guid]::NewGuid().ToString('N')))
        $tmpLog = Join-Path $tempRoot ("vss-log-{0}.txt"  -f ([guid]::NewGuid().ToString('N')))
        Set-Content -LiteralPath $bat -Encoding ascii -Value (
            "@echo off`r`ncd /d `"$SnapshotDir`"`r`n$CommandLine > `"$tmpLog`" 2>&1`r`nexit /b %ERRORLEVEL%`r`n")
        try {
            $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $bat -WorkingDirectory $SnapshotDir -NoNewWindow -PassThru
            # 必须先取引用句柄再等待，否则 .NET 在进程结束后读不到 ExitCode（返回 $null）。
            $null = $proc.Handle
            $proc.WaitForExit()
            $code = $proc.ExitCode
            if (Test-Path -LiteralPath $tmpLog) {
                $logProduced = $true
                Move-Item -LiteralPath $tmpLog -Destination $logPath -Force
            }
        } finally {
            if (Test-Path -LiteralPath $bat)    { Remove-Item -LiteralPath $bat -Force }
            if (Test-Path -LiteralPath $tmpLog) { Remove-Item -LiteralPath $tmpLog -Force }
        }
    } else {
        $sh = Join-Path $tempRoot ("vss-step-{0}.sh" -f ([guid]::NewGuid().ToString('N')))
        Set-Content -LiteralPath $sh -Encoding utf8 -Value (
            "#!/bin/sh`ncd `"$SnapshotDir`"`n$CommandLine > `"$logPath`" 2>&1`nexit `$?`n")
        try {
            $proc = Start-Process -FilePath '/bin/sh' -ArgumentList $sh -WorkingDirectory $SnapshotDir -NoNewWindow -PassThru
            $null = $proc.Handle
            $proc.WaitForExit()
            $code = $proc.ExitCode
            $logProduced = Test-Path -LiteralPath $logPath
        } finally { if (Test-Path -LiteralPath $sh) { Remove-Item -LiteralPath $sh -Force } }
    }
    if (-not $logProduced) {
        $script:Notes.Add("[验证器护栏] 步骤 '$Name' 未产生日志文件，命令疑似未执行；已按失败处理（原 ExitCode=$code）")
        $code = 9001
    }
    elseif ($null -eq $code) {
        $script:Notes.Add("[验证器护栏] 步骤 '$Name' 的 ExitCode 不可读；已按失败处理（不得默认为通过）")
        $code = 9002
    }
'''


def main():
    src_root, exec_root, patch_out = sys.argv[1], sys.argv[2], sys.argv[3]
    rel = 'scripts/verify-source-snapshot.ps1'
    original = os.path.join(src_root, rel.replace('/', os.sep))
    target = os.path.join(exec_root, rel.replace('/', os.sep))
    if not os.path.isfile(target):
        raise SystemExit('ExecutionRoot 内找不到 %s' % target)

    with io.open(target, 'r', encoding='utf-8-sig', newline='') as fh:
        before = fh.read()

    if RUNNER_OLD not in before:
        raise SystemExit('未找到预期的原始代码块，补丁中止（不盲改）')
    after = before.replace(RUNNER_OLD, RUNNER_NEW, 1)

    with io.open(original, 'r', encoding='utf-8-sig', newline='') as fh:
        orig_text = fh.read()

    diff = list(difflib.unified_diff(
        orig_text.splitlines(keepends=True),
        after.splitlines(keepends=True),
        fromfile='a/' + rel + '  (SourceRoot 原件，本批只读，未修改)',
        tofile='b/' + rel + '  (ExecutionRoot 副本，MR00 最小修复)',
        n=3))
    with io.open(patch_out, 'w', encoding='utf-8', newline='') as fh:
        fh.write('MR00 最小修复补丁：verify-source-snapshot.ps1 的步骤执行器\n')
        fh.write('仅应用于新的隔离 ExecutionRoot 副本；SourceRoot 原件未被改写。\n')
        fh.write('依据：本轮实测的假通过（8 步全 PASS 但 0 个 step 日志）与挂死（build 完成后仍阻塞）证据。\n\n')
        fh.writelines(diff)

    # 只写入 ExecutionRoot 内的副本（保留 BOM，PS 5.1 需要）
    with io.open(target, 'w', encoding='utf-8-sig', newline='') as fh:
        fh.write(after)

    print('patch written : %s (%d bytes, %d diff lines)' % (patch_out, os.path.getsize(patch_out), len(diff)))
    print('target patched: %s' % target)
    print('orig unchanged: %s (sha256 %s)' % (original, __import__('hashlib').sha256(open(original, 'rb').read()).hexdigest()))


if __name__ == '__main__':
    main()
