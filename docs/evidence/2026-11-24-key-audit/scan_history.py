import subprocess
import re

KEY_RE = re.compile(rb'\$2a\$10\$[A-Za-z0-9+/.]{50,}')

commits = [
    '18f89a9', 'c3fb8f8', 'f585b14', 'a4a0aae', '51bea1c', '932a879',
    '016d89f', '04b8cc3', '2a8dfe0', '2999a90', '85cc21e', '9498d99',
    '051f8ea', 'd5f00aa', '12056df', '39e03f2', '3665815',
]

print('=== 逐提交：树里含完整 Key 的文件 ===')
for c in commits:
    full = subprocess.run(['git', 'rev-parse', c], capture_output=True, text=True).stdout.strip()
    lr = subprocess.run(
        ['git', '-c', 'core.longpaths=true', 'ls-tree', '-r', '-z', '--name-only', full],
        capture_output=True)
    files = [f for f in lr.stdout.split(b'\x00') if f.strip()]
    hits = []
    for f in files:
        if not f.endswith((b'.cs', b'.json', b'.csproj', b'.props', b'.targets',
                           b'.yml', b'.yaml', b'.ps1', b'.cmd', b'.sh', b'.md', b'.txt')):
            continue
        br = subprocess.run(
            ['git', '-c', 'core.longpaths=true', 'cat-file', '-p', f'{full}:'.encode() + f],
            capture_output=True)
        if br.returncode == 0 and KEY_RE.search(br.stdout):
            hits.append(f.decode('utf-8', 'ignore'))
    print(f'{c}: {len(hits)} 个文件')
    for h in hits[:5]:
        print('    ', h)
