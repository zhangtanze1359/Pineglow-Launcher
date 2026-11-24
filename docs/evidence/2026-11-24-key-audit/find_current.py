"""在当前 HEAD 工作树里精确定位所有含**完整 60 字符** CurseForge Key 的文件。

与粗略 grep 的区别：只认 50+ 合法字符的完整值，不把 `$2a$10$G3lIsq…WjOCrG` 这类截断引用算进去。
"""
import os
import re

KEY_FULL = re.compile(rb'\$2a\$10\$[A-Za-z0-9+/.]{50,}')

SKIP_DIRS = {'.git', 'node_modules', 'bin', 'obj', '_archive'}
BINARY_EXT = {b'.exe', b'.dll', b'.png', b'.jpg', b'.ico', b'.zip', b'.trx', b'.pdb'}

hits = []
for dp, dn, fn in os.walk('.'):
    dn[:] = [d for d in dn if d not in SKIP_DIRS]
    for f in fn:
        p = os.path.join(dp, f)
        try:
            if os.path.getsize(p) > 50 * 1024 * 1024:
                continue
            b = open(p, 'rb').read()
        except Exception:
            continue
        if KEY_FULL.search(b):
            hits.append(p)

print(f'=== 当前工作树含【完整 Key】的文件: {len(hits)} ===')
for h in sorted(hits):
    print('  ', h)

print()
print('=== 只含截断引用（$2a$10$ 但不完整）的文件 ===')
prefix_only = []
for dp, dn, fn in os.walk('.'):
    dn[:] = [d for d in dn if d not in SKIP_DIRS]
    for f in fn:
        p = os.path.join(dp, f)
        try:
            if os.path.getsize(p) > 50 * 1024 * 1024:
                continue
            s = open(p, 'rb').read()
        except Exception:
            continue
        if b'$2a$10$' in s and not KEY_FULL.search(s):
            prefix_only.append(p)
for h in sorted(prefix_only):
    print('  ', h)
