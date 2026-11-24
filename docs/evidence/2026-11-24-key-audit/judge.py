import re
import subprocess
import sys

KEY_FULL = re.compile(r'\$2a\$10\$[A-Za-z0-9+/.]{50,}')
KEY_PREFIX_ONLY = re.compile(r'\$2a\$10\$')

commits = [
    ('c3fb8f8', 'docs/open-items.md'),
    ('04b8cc3', 'docs/open-items.md'),
    ('39e03f2', 'dev/PineglowLauncher/Pineglow.Launcher.Services/ModContentService.cs'),
    ('2a8dfe0', 'dev/PineglowLauncher/Pineglow.Launcher.Services/CurseForgeKeyDefaults.cs'),
]

print('=== 判定每个命中是「完整 Key」还是「截断引用」 ===')
for commit, path in commits:
    r = subprocess.run(['git', 'show', f'{commit}:{path}'], capture_output=True)
    text = r.stdout.decode('utf-8', 'ignore')
    full = KEY_FULL.findall(text)
    has_prefix = bool(KEY_PREFIX_ONLY.search(text))
    print(f'--- {commit} : {path}')
    print(f'    含 $2a$10$ 前缀: {has_prefix}')
    print(f'    含 60 字符完整 Key: {len(full)} 处')
    if full:
        for k in full:
            print(f'      >>> 完整值前12: {k[:12]}... 长度 {len(k)}')
    # 展示相关行
    for line in text.splitlines():
        if '$2a$10$' in line:
            print(f'    行: {line.strip()[:160]}')
            break
    print()
