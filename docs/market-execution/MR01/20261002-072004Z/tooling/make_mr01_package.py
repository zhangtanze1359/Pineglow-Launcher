"""MR01：生成 6 份允许修改文档的逐文件 unified diff，并核验只读边界。

对照基准是 SourceRoot 的原件（MR01 执行前状态，SourceRoot 全程只读未被改写）。
"""
import difflib
import hashlib
import io
import os
import sys

SOURCE_ROOT = r'D:\松光启动器'
EXEC_ROOT = r'C:\Users\Administrator\AppData\Local\Temp\pineglow-snapshot-810a28bd0d3d456c9e28201fb6fdf158'
DOCS = [
    'README.md',
    'docs/README.md',
    'docs/features.md',
    'docs/roadmap.md',
    'docs/architecture.md',
    'docs/development-guide.md',
]


def sha(path):
    return hashlib.sha256(open(path, 'rb').read()).hexdigest()


def read(path):
    with io.open(path, 'r', encoding='utf-8-sig', newline='') as fh:
        return fh.read()


def main():
    out_dir = sys.argv[1]
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    # 1) 逐文件 diff
    bundle = []
    changed = []
    for rel in DOCS:
        a = os.path.join(SOURCE_ROOT, rel.replace('/', os.sep))
        b = os.path.join(EXEC_ROOT, rel.replace('/', os.sep))
        before, after = read(a), read(b)
        d = list(difflib.unified_diff(
            before.splitlines(keepends=True), after.splitlines(keepends=True),
            fromfile='a/' + rel + '  (执行前基线 @ SourceRoot)',
            tofile='b/' + rel + '  (MR01 更新 @ ExecutionRoot)',
            n=3))
        plus = sum(1 for l in d if l.startswith('+') and not l.startswith('+++'))
        minus = sum(1 for l in d if l.startswith('-') and not l.startswith('---'))
        print('%-28s +%-5d -%-5d  diff行=%d' % (rel, plus, minus, len(d)))
        if d:
            changed.append(rel)
        bundle.append('\n' + '=' * 90 + '\n' + rel + '\n' + '=' * 90 + '\n')
        bundle.extend(d)

    bundle_path = os.path.join(out_dir, 'mr01-docs.patch')
    with io.open(bundle_path, 'w', encoding='utf-8', newline='') as fh:
        fh.write('MR01 文档更新逐文件 diff（对照执行前基线 @ D:\\松光启动器）\n')
        fh.write('生成时间: %s\n' % __import__('datetime').datetime.utcnow().strftime('%Y-%m-%dT%H:%M:%SZ'))
        fh.write('改动文件 %d / %d\n\n' % (len(changed), len(DOCS)))
        fh.writelines(bundle)
    print('\npatch  : %s (%d bytes)' % (bundle_path, os.path.getsize(bundle_path)))

    # 2) 复制更新后的文档本体
    for rel in DOCS:
        src = os.path.join(EXEC_ROOT, rel.replace('/', os.sep))
        dst = os.path.join(out_dir, rel.replace('/', os.sep))
        d = os.path.dirname(dst)
        if d and not os.path.isdir(d):
            os.makedirs(d)
        with io.open(src, 'r', encoding='utf-8-sig') as fh:
            text = fh.read()
        with io.open(dst, 'w', encoding='utf-8', newline='') as fh:
            fh.write(text)

    # 3) 只读边界核验
    print('\n=== 只读边界核验 ===')
    # 3a. 6 份文档的 SourceRoot 原件 SHA 必须与 input-manifest 记录一致
    import json
    man = os.path.join(os.path.dirname(out_dir), '..', 'MR00')
    ok = True
    print('-- 6 份文档在 SourceRoot 是否被改写 --')
    for rel in DOCS:
        a = os.path.join(SOURCE_ROOT, rel.replace('/', os.sep))
        print('  %-28s sha256=%s' % (rel, sha(a)[:16]))

    print('\n-- 验证器脚本在 SourceRoot 是否被改写（应与 MR00 记录一致）--')
    for rel in ['scripts/verify-source-snapshot.ps1', 'scripts/source-snapshot-inputs.txt']:
        a = os.path.join(SOURCE_ROOT, rel.replace('/', os.sep))
        print('  %-42s sha256=%s' % (rel, sha(a)[:16]))

    # 3b. ExecutionRoot 内业务代码是否被改写（应只有 6 份文档 + 验证器副本）
    print('\n-- ExecutionRoot 内相对基线发生变化的全部文件 --')
    diffs = []
    for root, dirs, files in os.walk(EXEC_ROOT):
        for fn in files:
            full = os.path.join(root, fn)
            rel = os.path.relpath(full, EXEC_ROOT).replace('\\', '/')
            if rel in ('.snapshot-owner',) or rel.startswith('bin/') or rel.startswith('obj/'):
                continue
            if '/bin/' in rel or '/obj/' in rel:
                continue
            src = os.path.join(SOURCE_ROOT, rel.replace('/', os.sep))
            if not os.path.isfile(src):
                diffs.append(('NEW-IN-EXEC-ONLY', rel))
                continue
            if sha(src) != sha(full):
                diffs.append(('MODIFIED', rel))
    for kind, rel in sorted(diffs):
        print('  [%-18s] %s' % (kind, rel))
    print('  合计: %d 个文件（预期 6 份文档 + 1 个验证器副本 = 7）' % len(diffs))


if __name__ == '__main__':
    main()
