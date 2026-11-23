# 第 6 次复核报告 · 执行者勘误

本目录的 `HANDOFF-2026-11-23-open04-review-r6-codex.md` 是复核方原文，**未作任何修改**。第 7 轮复核发现该报告中有一处**事实错误**，按「保留原件 + 另附勘误」处理，不篡改原文。

## 勘误内容

原文 `:45–49` 的轮次表把**第 1 轮与第 3 轮对调**了：

| 原文写的 | 实际（执行者与第 7 轮复核方均已独立核实） |
| --- | --- |
| 第 1 轮：**19 份** TRX，4567 通过、0 失败、**3 轮全量**、16 条目标状态 | 那是**第 4 轮**的数字。第 1 轮实为 **23 份** TRX、**6071 通过 / 1 失败**、**4 轮无过滤全量**、10 条目标状态 |
| 第 3 轮：**23 份** TRX，6071 通过、**1 失败**、4 轮全量、17 条目标状态 | 那是**第 1 轮**的数字。第 3 轮实为 **18 份** TRX、**3065 通过 / 0 失败**、**2 轮无过滤全量**、10 条目标状态 |

## 核实依据

**第 1 轮**（工作树 `t3-0fc59f52`，`docs/evidence/2026-11-23-open04-review/runs.csv`）：

```text
full-01        passed=1501 failed=1  NO-FILTER(full)
full-02        passed=1502 failed=0  NO-FILTER(full)
full-stress-01 passed=1502 failed=0  NO-FILTER(full)
diag-full-03   passed=1502 failed=0  NO-FILTER(full)
```

→ 4 轮无过滤全量，`full-01` 带 1 失败（`full-01.trx:1945`，`ConfigPreservationTests` 的 `.tmp` 断言）。

**第 3 轮**（工作树 `t3-399f2fe4`，`docs/evidence/2026-11-23-open04-review4/`）：

```text
full-01-default.trx  total=1502 passed=1502 failed=0
full-02-loaded.trx   total=1502 passed=1502 failed=0
```

→ 仅 2 轮无过滤全量，18 份 TRX 全部 0 失败。

## 衍生影响

1. 执行者据此改正了台账与执行交接：第 1 轮「4 轮全量、失败后另有 3 轮通过」；第 3 轮「2 轮全量、0 失败」。
2. 第 7 轮复核独立确认了上述核实（其报告第 3 节：「执行者说的 2 轮正确」）。
3. **原文其余部分（尤其第 6 轮判 FAIL 的三条实质问题）不受此勘误影响**——那些指正经核实均成立。
