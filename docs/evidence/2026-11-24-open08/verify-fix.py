#!/usr/bin/env python3
"""OPEN-08 修复验证：发布门槛 + 守卫（Python 实现，避免 PowerShell 扫二进制误报）。"""
import os
import subprocess
import sys

WT = r'D:/Projects/_worktrees/pg-o8fix'
DLL_REL = 'dev/PineglowLauncher/bin/Release/net10.0/Pineglow.Launcher.dll'
DLL = os.path.join(WT, DLL_REL)
KEYNAME = b'PineglowCurseForgeApiKey'
FAKEKEY = b'FAKEKEY0123456789ABCDEF'


def run_build(key):
    env = os.environ.copy()
    env['PATH'] = r'C:\Windows\System32;C:\Windows;' + env.get('PATH', '')
    env['TEMP'] = os.path.join(WT, '.cache/tmp')
    env['TMP'] = env['TEMP']
    os.makedirs(env['TEMP'], exist_ok=True)
    if key is None:
        env.pop('PINEGLOW_CURSEFORGE_BUILD_KEY', None)
    else:
        env['PINEGLOW_CURSEFORGE_BUILD_KEY'] = key
    r = subprocess.run(['dotnet', 'build', 'dev/PineglowLauncher/Pineglow.Launcher.csproj',
                        '-c', 'Release', '--no-incremental'],
                       cwd=WT, env=env, capture_output=True, text=True, errors='ignore')
    return r.returncode


def run_tests(filter_expr):
    env = os.environ.copy()
    env['PATH'] = r'C:\Windows\System32;C:\Windows;' + env.get('PATH', '')
    env['TEMP'] = os.path.join(WT, '.cache/tmp')
    env['TMP'] = env['TEMP']
    env.pop('PINEGLOW_CURSEFORGE_BUILD_KEY', None)
    r = subprocess.run(['dotnet', 'test', 'dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj',
                        '-c', 'Release', '--filter', filter_expr,
                        '--logger', 'console;verbosity=minimal'],
                       cwd=WT, env=env, capture_output=True, text=True, errors='ignore')
    tail = [l.strip() for l in r.stdout.splitlines() if '已通过' in l or '失败' in l]
    return r.returncode, (tail[-1] if tail else '(no summary)')


def gate_dll_has_key(needle=KEYNAME):
    if not os.path.exists(DLL):
        return None
    return needle in open(DLL, 'rb').read()


checks = []

print('=== OPEN-08 修复验证 ===')
print()

# --- 门槛：带 Key ---
print('--- 门槛 A：构建机带正确形态假值 ---')
rc = run_build('FAKEKEY0123456789ABCDEF')
has_key = gate_dll_has_key()
print(f'    构建退出码: {rc}')
print(f'    中间 DLL 含元数据键: {has_key}   (门槛应判 PASS)')
checks.append(('门槛带值', rc == 0 and has_key is True))
print()

# --- 门槛：无 Key ---
print('--- 门槛 B：构建机无 Key ---')
rc = run_build(None)
has_key = gate_dll_has_key()
print(f'    构建退出码: {rc}')
print(f'    中间 DLL 含元数据键: {has_key}   (门槛应判 FAIL)')
checks.append(('门槛无值', rc == 0 and has_key is False))
print()

# --- 守卫：Windows 生成物不应误报 ---
print('--- 守卫 A：带 Key 构建产出的 obj 生成物不应被误报 ---')
# 先用假值构建，让 obj 里出现含 Key 的 AssemblyInfo.cs
rc = run_build('FAKEKEY0123456789ABCDEF')
print(f'    构建退出码: {rc}')
code, summary = run_tests('FullyQualifiedName~SecretHygieneTests')
print(f'    守卫测试退出码: {code}')
print(f'    摘要: {summary}')
checks.append(('守卫不误报生成物', code == 0))
print()

# --- 守卫：真探针必须变红 ---
print('--- 守卫 B：源码树探针必须变红 ---')
probe = os.path.join(WT, 'dev/PineglowLauncher/Pineglow.Launcher.Services/GuardProbe.cs')
with open(probe, 'w', encoding='utf-8') as f:
    f.write('// 临时探针\nnamespace Pineglow.Launcher.Services;\ninternal static class GuardProbe\n{\n'
            '\tinternal const string Fake = "$2a$10$PROBEONLY";\n}\n')
try:
    code, summary = run_tests('FullyQualifiedName~SecretHygieneTests')
    print(f'    守卫测试退出码: {code}  (期望非 0)')
    print(f'    摘要: {summary}')
    checks.append(('守卫拦探针', code != 0))
finally:
    os.remove(probe)
print()

print('=== 判定 ===')
ok = True
for name, good in checks:
    print(f'  {name}: {"OK" if good else "FAIL"}')
    ok = ok and good
print()
print('总判定:', 'PASS' if ok else 'FAIL')
sys.exit(0 if ok else 1)
