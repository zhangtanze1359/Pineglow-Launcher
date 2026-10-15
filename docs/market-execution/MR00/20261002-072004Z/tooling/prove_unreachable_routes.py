"""决定性验证：证明 /api/instances/{id}/bisect/* 与 /mods/* 路由位于
`seg.Length == 4` 的 if 块内部，因而永远不可达（死代码）。

方法：逐字符计算大括号深度，报告每条关键 if 所在行的嵌套深度与所属外层 if。
不做任何猜测，直接给出可复算的结构事实。
"""
import io
import re
import sys

path = sys.argv[1]
lines = io.open(path, 'r', encoding='utf-8-sig').read().splitlines()

# 深度追踪（跳过字符串/字符/注释里的括号）
depth = 0
depth_at = {}
for i, ln in enumerate(lines, start=1):
    depth_at[i] = depth
    out = []
    j = 0
    n = len(ln)
    while j < n:
        c = ln[j]
        if c == '/' and j + 1 < n and ln[j + 1] == '/':
            break
        if c == '"':
            j += 1
            while j < n:
                if ln[j] == '\\':
                    j += 2
                    continue
                if ln[j] == '"':
                    break
                j += 1
        elif c == "'":
            j += 1
            while j < n:
                if ln[j] == '\\':
                    j += 2
                    continue
                if ln[j] == "'":
                    break
                j += 1
        elif c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
        j += 1

base = depth_at[415]
print('=== 关键路由分支的嵌套结构 ===')
print('L415  `if (seg.Length == 4 && seg[1]=="instances")` 起始深度 = %d' % depth_at[415])
print()
targets = [
    (511, 'bisect start/test/giveup  (要求 seg.Length == 5)'),
    (531, 'mods toggle/delete      (要求 seg.Length == 6)'),
    (419, 'instance delete         (要求 seg.Length == 4)  <- 对照组'),
    (409, 'diagnose cancel         (要求 seg.Length == 5)  <- 对照组：在 L415 之前'),
]
for ln, desc in targets:
    rel = depth_at[ln] - base
    inside = '在 L415 块【内部】' if depth_at[ln] > base else '在 L415 块【外部】(顶层)'
    print('L%-4d depth=%-3d 相对 L415 = %+d  %-8s  %s' % (ln, depth_at[ln], rel, inside, desc))

print()
print('=== 结论 ===')
b5 = depth_at[511] > base
b6 = depth_at[531] > base
if b5:
    print('L511 要求 seg.Length == 5，却被包在要求 seg.Length == 4 的 if 内。')
    print('  => 该条件恒为 false，/api/instances/{id}/bisect/{start,test,giveup} 为【不可达死代码】。')
if b6:
    print('L531 要求 seg.Length == 6，同样被包在要求 seg.Length == 4 的 if 内。')
    print('  => /api/instances/{id}/mods/{file}/{toggle,delete} 亦为【不可达死代码】。')
print()
print('对照：L409 的 diagnose/cancel 同样要求 seg.Length == 5，但它位于 L415 之前，depth=%d，可达。' % depth_at[409])
