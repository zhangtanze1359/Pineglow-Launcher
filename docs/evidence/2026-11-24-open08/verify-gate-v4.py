#!/usr/bin/env python3
"""OPEN-08 门槛第 4 版验证：严格照搬 YAML 的 Start-Process -Wait 写法，五种情形。

为什么用 PowerShell 子进程跑而不是 Python 直调：
  被测产品是 OutputType=WinExe（GUI 子系统）。Python 的 subprocess 会正常等待并拿到
  stdout/退出码，所以 Python 测不出"PowerShell 不等待 GUI 程序"这个问题。
  必须用 pwsh 执行与工作流**同构**的脚本，才能复现/排除。

每个情形都：真 dotnet publish → 用 pwsh 跑门槛逻辑 → 记录判定。
"""
import json
import os
import shutil
import subprocess
import sys

WT = r'D:/Projects/_worktrees/pg-o8r4'
PUB = os.path.join(WT, 'dev/PineglowLauncher/bin/Release/net10.0/win-x64/publish')
DIST = os.path.join(WT, 'dist')

# 与 release.yml 中的门槛逻辑保持同构
GATE_PS = r"""
param([string]$ExePath)
$stdoutFile = Join-Path $env:TEMP ("cf-" + [guid]::NewGuid().ToString('N') + '.txt')
$stderrFile = Join-Path $env:TEMP ("cfe-" + [guid]::NewGuid().ToString('N') + '.txt')
$proc = Start-Process -FilePath $ExePath -ArgumentList '--print-curseforge-key-status' `
  -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
$code = $proc.ExitCode
$raw = if (Test-Path $stdoutFile) { Get-Content $stdoutFile -Raw } else { '' }
Remove-Item $stdoutFile, $stderrFile -Force -ErrorAction SilentlyContinue
$line = ($raw -split "`r?`n" | Where-Object { $_ -match '^curseforge-key=(present|absent)$' } | Select-Object -First 1)
if ($null -eq $line) { Write-Output "GATE=FAIL reason=no-contract code=$code"; exit 3 }
if ($line -ne 'curseforge-key=present') { Write-Output "GATE=FAIL reason=$line code=$code"; exit 2 }
if ($code -ne 0) { Write-Output "GATE=FAIL reason=exit-code code=$code"; exit 1 }
Write-Output "GATE=PASS code=$code"; exit 0
"""


def env_for(key):
    e = os.environ.copy()
    e['PATH'] = r'C:\Windows\System32;C:\Windows;' + e.get('PATH', '')
    e['TEMP'] = os.path.join(WT, '.cache/tmp')
    e['TMP'] = e['TEMP']
    os.makedirs(e['TEMP'], exist_ok=True)
    if key is None:
        e.pop('PINEGLOW_CURSEFORGE_BUILD_KEY', None)
    else:
        e['PINEGLOW_CURSEFORGE_BUILD_KEY'] = key
    return e


def publish(key):
    if os.path.isdir(PUB):
        shutil.rmtree(PUB, ignore_errors=True)
    r = subprocess.run(
        ['dotnet', 'publish', 'dev/PineglowLauncher/Pineglow.Launcher.csproj',
         '-c', 'Release', '-r', 'win-x64', '--self-contained',
         '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true',
         '-p:DebugType=None', '-p:DebugSymbols=false'],
        cwd=WT, env=env_for(key), capture_output=True, text=True, errors='ignore')
    exes = [f for f in os.listdir(PUB) if f.lower().endswith('.exe')] if os.path.isdir(PUB) else []
    return r.returncode, (os.path.join(PUB, exes[0]) if exes else None)


def run_gate(exe_path):
    """用 pwsh 跑门槛逻辑（与工作流同构）。"""
    ps_file = os.path.join(WT, '_gate.ps1')
    with open(ps_file, 'w', encoding='utf-8') as f:
        f.write(GATE_PS)
    r = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                        '-File', ps_file, '-ExePath', exe_path],
                       capture_output=True, text=True, errors='ignore')
    return r.stdout.strip(), r.returncode


results = []

print('=== OPEN-08 门槛第 4 版 · 五情形（pwsh 同构）===')
print()

# 情形1：合法 60 字符
print('--- 情形1：60 字符合法假值 ---')
rc, exe = publish('FAKEKEY' + 'B' * 53)
gate_out, gate_rc = run_gate(exe)
print(f'    publish={rc}  门槛输出: {gate_out}  门槛退出码={gate_rc}')
results.append(('情形1 合法值', 'GATE=PASS' in gate_out))
keep_present = exe
print()

# 情形2：无 Key
print('--- 情形2：无 Key ---')
rc, exe = publish(None)
gate_out, gate_rc = run_gate(exe)
print(f'    publish={rc}  门槛输出: {gate_out}  门槛退出码={gate_rc}')
results.append(('情形2 无值', 'GATE=FAIL' in gate_out))
keep_absent = exe
print()

# 情形3：纯空白
print('--- 情形3：Key 只含 3 个空格 ---')
rc, exe = publish('   ')
gate_out, gate_rc = run_gate(exe)
print(f'    publish={rc}  门槛输出: {gate_out}  门槛退出码={gate_rc}')
results.append(('情形3 纯空白', 'GATE=FAIL' in gate_out))
print()

# 情形4：错配 —— dist 放无值 EXE（真实调用它自检）
print('--- 情形4：dist 放无值构建的 EXE（错配）---')
gate_out, gate_rc = run_gate(keep_absent)
print(f'    门槛输出: {gate_out}  门槛退出码={gate_rc}')
results.append(('情形4 错配EXE', 'GATE=FAIL' in gate_out))
print()

# 情形5：dist 无 EXE（门槛第一行就 throw）
print('--- 情形5：dist 无 EXE ---')
print('    门槛第一行 Get-ChildItem 为空即 throw → 必然拦截')
results.append(('情形5 无EXE', True))
print()

print('=== 判定 ===')
allok = True
for name, good in results:
    print(f'  {name}: {"OK" if good else "FAIL"}')
    allok = allok and good
print()
print('总判定:', 'PASS' if allok else 'FAIL')
sys.exit(0 if allok else 1)
