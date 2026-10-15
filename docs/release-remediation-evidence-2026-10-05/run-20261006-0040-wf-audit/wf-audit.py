"""工作流引用/一致性审计（actionlint 不可用时的本地替代，只覆盖与本次改动相关的子集）。

不做的事（避免自我安慰）：
  - 不校验 GitHub Actions 表达式语义、action 输入 schema、shell 语义；
  - 这些只有 actionlint / 真实 runner 能回答，本脚本**不代表** actionlint 通过。
本脚本回答的是 actionlint 也不管的那一类：**工作流引用的仓库内路径/产物是否存在、needs 是否指向真实 job**。
"""

import os
import re
import sys

try:
    import yaml
except ImportError:
    print("PyYAML 不可用")
    sys.exit(2)

FILES = [".github/workflows/build.yml", ".github/workflows/release.yml"]
problems = []

for f in FILES:
    raw = open(f, encoding="utf-8").read()
    print("===", f, "===")

    # 1) 引用的仓库内路径是否存在
    refs = set()
    for m in re.finditer(r"(?<![\w./-])((?:\./)?(?:scripts/[\w./-]+|global\.json|dev/[\w./-]+|ui/[\w./-]+))", raw):
        refs.add(m.group(1).lstrip("./"))
    missing = sorted(r for r in refs if not os.path.exists(r))
    print("  引用仓库内路径:", len(refs), "个；不存在:", missing or "无")
    if missing:
        problems.append(f + " 引用了不存在的路径: " + ", ".join(missing))

    # 2) uses: 是否都带版本（浮动分支名不算，但至少要有 @）
    acts = re.findall(r"uses:\s*(\S+)", raw)
    bad = sorted(set(a for a in acts if "@" not in a))
    print("  actions:", ", ".join(sorted(set(acts))) or "无", "| 无版本:", bad or "无")
    if bad:
        problems.append(f + " 有未固定版本的 action: " + ", ".join(bad))

    # 3) needs 指向的 job 是否存在
    doc = yaml.safe_load(raw)
    jobs = doc.get("jobs", {}) or {}
    for jn, j in jobs.items():
        nb = j.get("needs") if isinstance(j, dict) else None
        if not nb:
            continue
        nbs = [nb] if isinstance(nb, str) else list(nb)
        miss = [x for x in nbs if x not in jobs]
        print("  job", jn, "needs =", nbs, "| 缺失:", miss or "无")
        if miss:
            problems.append(f + f" job {jn} 的 needs 指向不存在的 job: {miss}")

    # 4) 关键产物：既要被生成、也要被使用（出现次数 >= 2）
    for want in [
        "ui-resource-closure.json",
        "test-inventory-report.json",
        "standalone-cold-start.json",
        "live-update-stageA.json",
        "mirror-status.json",
        "dist/manifest.json",
    ]:
        n = raw.count(want)
        print(f"  产物 {want}: 出现 {n} 次" + ("  <-- 只出现一次（生成或使用缺一半）" if n == 1 else ""))
        if n == 1:
            problems.append(f + f" 产物 {want} 只出现一次")

    # 5) 制表符缩进（YAML 不允许）与 ${{ }} 配对
    tabs = sum(1 for line in raw.splitlines() if line.startswith("\t"))
    opens = raw.count("$" + "{{")
    closes = raw.count("}}")
    print("  制表符缩进行数:", tabs, "| 表达式开/闭:", opens, "/", closes)
    if tabs:
        problems.append(f + " 含制表符缩进")
    if opens != closes:
        problems.append(f + f" 表达式不配对: {opens} vs {closes}")

print()
if problems:
    print("发现问题:")
    for p in problems:
        print("  -", p)
    sys.exit(1)
print("本地子集审计: 未发现问题（注意：这不等于 actionlint 通过，也不等于 runner 通过）")
