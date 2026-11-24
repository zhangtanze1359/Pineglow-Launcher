"""把 docs/market-execution/ 下 4 个历史快照里的完整 CurseForge Key 替换成占位符。

为什么可以改这些「历史证据副本」：
  它们是 2026-10 那次 MR 审计的实现文件备份，用途是「证明当时的代码长什么样」。
  一个 60 字符的 API Key 常量与那份证据要证明的东西（依赖安装/端点审计逻辑）无关。
  把它换成占位符既消除了公开泄露，又保留文件的可读性与上下文。

替换方式：保留前缀形态与长度感，写成一个明确的占位符，避免后人误以为这是真 Key。
"""
import os
import re

KEY_FULL = re.compile(r'(\$2a\$10\$)[A-Za-z0-9+/.]{50,}')

TARGETS = [
    'docs/market-execution/MR05-dependency-install-tests/20261003-210000Z/green/ModContentService.cs.mr05b',
    'docs/market-execution/MR05-modcontent-update-wiring-tests/20261003-200000Z/green/ModContentService.cs.mr05a',
    'docs/market-execution/MR05-recovery-version-display/20261003-430000Z/green/ModContentService.cs.fixed',
    'docs/market-execution/MR12-outbound-endpoint-audit/20261003-270000Z/red/ModContentService.cs.before',
]

REPLACEMENT = '\\1<REDACTED-2026-11-24-SEE-docs/evidence/2026-11-24-key-audit/>'

for t in TARGETS:
    if not os.path.exists(t):
        print(f'SKIP(不存在): {t}')
        continue
    raw = open(t, 'rb').read()
    text = raw.decode('utf-8')
    new, n = KEY_FULL.subn(REPLACEMENT, text)
    if n == 0:
        print(f'SKIP(无命中): {t}')
        continue
    open(t, 'w', encoding='utf-8', newline='\n').write(new)
    print(f'已脱敏 {n} 处: {t}')
