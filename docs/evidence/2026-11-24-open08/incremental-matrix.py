#!/usr/bin/env python3
"""OPEN-08 增量构建矩阵测试（用 Python 扫描，避免 PowerShell 逐字节比较的误报）。"""
import os
import subprocess
import sys

WT = r'D:/Projects/_worktrees/pg-open08-chk'
DLL = os.path.join(WT, 'dev/PineglowLauncher/bin/Release/net10.0/Pineglow.Launcher.dll')


def build(env_key):
    env = os.environ.copy()
    env['PATH'] = r'C:\Windows\System32;C:\Windows;' + env.get('PATH', '')
    env['TEMP'] = os.path.join(WT, '.cache/tmp')
    env['TMP'] = env['TEMP']
    os.makedirs(env['TEMP'], exist_ok=True)
    if env_key is None:
        env.pop('PINEGLOW_CURSEFORGE_BUILD_KEY', None)
    else:
        env['PINEGLOW_CURSEFORGE_BUILD_KEY'] = env_key
    r = subprocess.run(
        ['dotnet', 'build', 'dev/PineglowLauncher/Pineglow.Launcher.csproj', '-c', 'Release'],
        cwd=WT, env=env, capture_output=True, text=True, errors='ignore')
    msg = [l.strip() for l in r.stdout.splitlines() if 'curseforge-key' in l]
    return r.returncode, (msg[0] if msg else '(no msg)')


def dll_has(needle):
    if not os.path.exists(DLL):
        return False
    data = open(DLL, 'rb').read()
    return needle.encode() in data


results = []

print('=== OPEN-08 增量构建矩阵 ===')
print()

steps = [
    ('首次构建，带 KEY-A', 'KEYAAAAAAAAAAAAAAAAAAAA'),
    ('增量构建，仍带 KEY-A', 'KEYAAAAAAAAAAAAAAAAAAAA'),
    ('增量构建，换成 KEY-B', 'KEYBBBBBBBBBBBBBBBBBBBB'),
    ('增量构建，移除 Key', None),
]

for label, key in steps:
    code, msg = build(key)
    a = dll_has('KEYAAAAAAAAAAAAAAAAAAAA')
    b = dll_has('KEYBBBBBBBBBBBBBBBBBBBB')
    print(f'--- {label}')
    print(f'    构建退出码: {code}')
    print(f'    日志: {msg}')
    print(f'    DLL 含 KEY-A: {a}   含 KEY-B: {b}')
    results.append((label, key, code, a, b))
    print()

print('=== 期望 ===')
print('  步骤1: A=True  B=False')
print('  步骤2: A=True  B=False  (增量不应丢失)')
print('  步骤3: A=False B=True   (换 Key 必须生效)')
print('  步骤4: A=False B=False  (移除 Key 必须不残留)')
print()
ok = True
expected = [(True, False), (True, False), (False, True), (False, False)]
for (label, key, code, a, b), (ea, eb) in zip(results, expected):
    good = (a == ea) and (b == eb) and code == 0
    print(f'  {label}: {"OK" if good else "FAIL"}')
    ok = ok and good
print()
print('总判定:', 'PASS' if ok else 'FAIL')
sys.exit(0 if ok else 1)
