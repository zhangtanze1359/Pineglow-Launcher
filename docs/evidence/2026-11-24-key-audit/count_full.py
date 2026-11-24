"""精确统计：多少个历史提交的树里含**完整 60 字符** CurseForge Key。

与粗略 grep 的区别：
  - 只认 `$2a$10$` + 50 个以上合法字符（即完整值），不把 `$2a$10$G3lIsq…WjOCrG` 这类截断引用算进去。
  - 用 `git grep` 在提交树上搜索，而不是逐文件 cat-file（后者在 4000+ 文件 × 437 提交时超时）。
"""
import re
import subprocess

KEY_FULL = re.compile(r'\$2a\$10\$[A-Za-z0-9+/.]{50,}')

# 先用 git log -S 缩小范围，再对每个提交做精确判定
r = subprocess.run(
    ['git', '-c', 'core.longpaths=true', 'log', '--all', '--format=%H', '-S', '$2a$10$'],
    capture_output=True, text=True, errors='ignore')
commits = [c for c in r.stdout.splitlines() if c.strip()]

print(f'git log -S 给出 {len(commits)} 个提交，逐个精确判定...')
print()

full_hits = []      # 含完整 Key 的提交
prefix_only = []    # 只有截断引用

for c in commits:
    # 在 Git 层面找候选文件（用前缀，宽松）
    g = subprocess.run(
        ['git', '-c', 'core.longpaths=true', 'grep', '-l', '-I', '-F', '$2a$10$', c, '--', '.'],
        capture_output=True)
    files = [l.split(b':', 1)[1] for l in g.stdout.splitlines() if b':' in l]

    has_full = False
    for f in files:
        p = subprocess.run(
            ['git', '-c', 'core.longpaths=true', 'show', f'{c}:'.encode() + f],
            capture_output=True)
        if p.returncode != 0:
            continue
        if KEY_FULL.search(p.stdout.decode('utf-8', 'ignore')):
            has_full = True
            break

    subject = subprocess.run(['git', 'log', '-1', '--format=%h %ad %s', '--date=short', c],
                             capture_output=True, text=True, errors='ignore').stdout.strip()
    if has_full:
        full_hits.append(subject)
    else:
        prefix_only.append(subject)

print(f'=== 含【完整 Key】的提交: {len(full_hits)} ===')
for s in full_hits:
    print('  ', s[:110])
print()
print(f'=== 只含【截断引用】的提交: {len(prefix_only)} ===')
for s in prefix_only:
    print('  ', s[:110])
