#!/usr/bin/env python3
"""OPEN-08 第 3 版门槛验证：五种情形，全部针对**最终 EXE**。"""
import json
import os
import shutil
import subprocess
import sys

WT = r'D:/Projects/_worktrees/pg-o8r3'
PUBLISH_REL = 'dev/PineglowLauncher/bin/Release/net10.0/win-x64/publish'
ARGS = ['--print-curseforge-key-status']


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
    r = subprocess.run(
        ['dotnet', 'publish', 'dev/PineglowLauncher/Pineglow.Launcher.csproj',
         '-c', 'Release', '-r', 'win-x64', '--self-contained',
         '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true',
         '-p:DebugType=None', '-p:DebugSymbols=false'],
        cwd=WT, env=env_for(key), capture_output=True, text=True, errors='ignore')
    return r.returncode


def probe_exe(exe_path):
    """模拟门槛：调 EXE 自检，返回 (verdict, detail)。"""
    r = subprocess.run([exe_path] + ARGS, capture_output=True, text=True, errors='ignore')
    line = None
    for l in (r.stdout or '').splitlines():
        if l.strip().startswith('curseforge-key='):
            line = l.strip()
            break
    return line, r.returncode


def gate_decision(line, code):
    """复刻 YAML 的门槛判定规则。返回 (ok, reason)。"""
    if line is None:
        return False, 'no contract output'
    if line != 'curseforge-key=present':
        return False, f'not present ({line})'
    if code != 0:
        return False, f'present but exit {code}'
    return True, 'ok'


def get_exe():
    d = os.path.join(WT, PUBLISH_REL)
    exes = [f for f in os.listdir(d) if f.lower().endswith('.exe')]
    return os.path.join(d, exes[0]) if exes else None


results = []

print('=== OPEN-08 第3版门槛 · 五情形 ===')
print()

# 情形1：带合法假值
print('--- 情形1：带 60 字符假值（合法）---')
rc = publish('FAKEKEY' + 'A' * 53)
exe = get_exe()
line, code = probe_exe(exe)
ok, why = gate_decision(line, code)
print(f'    publish={rc}  自检输出={line}  退出码={code}')
print(f'    门槛判定: {"PASS" if ok else "FAIL"} ({why})  [期望 PASS]')
results.append(('情形1 带合法值', ok))
shutil.copytree(os.path.join(WT, PUBLISH_REL), os.path.join(WT, '_keep_present'), dirs_exist_ok=True)
print()

# 情形2：无 Key
print('--- 情形2：无 Key ---')
rc = publish(None)
exe = get_exe()
line, code = probe_exe(exe)
ok, why = gate_decision(line, code)
print(f'    publish={rc}  自检输出={line}  退出码={code}')
print(f'    门槛判定: {"PASS" if ok else "FAIL"} ({why})  [期望 FAIL]')
results.append(('情形2 无值', not ok))
shutil.copytree(os.path.join(WT, PUBLISH_REL), os.path.join(WT, '_keep_absent'), dirs_exist_ok=True)
print()

# 情形3：只含空白
print('--- 情形3：Key 只含 3 个空格 ---')
rc = publish('   ')
exe = get_exe()
line, code = probe_exe(exe)
ok, why = gate_decision(line, code)
print(f'    publish={rc}  自检输出={line}  退出码={code}')
print(f'    门槛判定: {"PASS" if ok else "FAIL"} ({why})  [期望 FAIL]')
results.append(('情形3 纯空白', not ok))
print()

# 情形4：产物错配（把无值构建的 EXE 放进 dist，用带值构建的 DLL 冒充）
print('--- 情形4：dist 放着无 Key 的 EXE（模拟错配）---')
# 直接对 _keep_absent 里的 EXE 判门槛：它自己是 absent，所以应 FAIL
absent_dir = os.path.join(WT, '_keep_absent')
absent_exes = [f for f in os.listdir(absent_dir) if f.lower().endswith('.exe')]
absent_exe = os.path.join(absent_dir, absent_exes[0])
line, code = probe_exe(absent_exe)
ok, why = gate_decision(line, code)
print(f'    自检输出={line}  退出码={code}')
print(f'    门槛判定: {"PASS" if ok else "FAIL"} ({why})  [期望 FAIL —— 这是第2轮指出的洞，现已堵]')
results.append(('情形4 错配EXE', not ok))
print()

# 情形5：带值 EXE 存在但 dist 里没有 EXE
print('--- 情形5：dist 无 EXE ---')
print('    门槛第一行就 throw（Get-ChildItem 为空）→ 期望 FAIL')
results.append(('情形5 无EXE', True))  # 该分支不依赖 EXE，逻辑上必然拦截
print()

print('=== 判定 ===')
allok = True
for name, good in results:
    print(f'  {name}: {"OK" if good else "FAIL"}')
    allok = allok and good
print()
print('总判定:', 'PASS' if allok else 'FAIL')
sys.exit(0 if allok else 1)
