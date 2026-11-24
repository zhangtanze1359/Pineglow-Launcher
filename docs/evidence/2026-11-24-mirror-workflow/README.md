# Gitee 自动镜像 workflow · 2026-11-24

## 做了什么

新增 `.github/workflows/mirror-gitee.yml`，在 `main` 每次 push 时把源码镜像到 Gitee `master`。

取代此前「手工同步一次」的状态（见 [`../2026-11-24-gitee-sync/README.md`](../2026-11-24-gitee-sync/README.md)）。

## 设计取舍

| 决策 | 理由 |
| --- | --- |
| **只在 `main` push / `workflow_dispatch` 触发** | Gitee 是产品镜像，不需要 PR 与开发分支的历史 |
| **`--force`** | 首次同步已覆盖 Gitee 历史，之后原则上可 fast-forward；保留 force 容忍「有人在 Gitee 上直接改了东西」。**代价**：Gitee 侧直接改动会被静默覆盖——这是镜像仓库的预期行为 |
| **`continue-on-error: true`** | 镜像失败不该让 GitHub 构建变红 |
| **失败用 `::error::` + job summary** | 这是 2026-11-24 的直接教训：旧的 Gitee 镜像链路因 `continue-on-error` **静默失效三个月**。同样的坑不能再踩 |
| **不做 shallow（`--depth=1`）** | 会让 Gitee 侧 `git log` 残缺；246 MiB pack 全量推一次约 1–3 分钟，可接受 |
| **不用 `set -e`** | 要在失败处给出具体诊断（如分叉统计）；改为逐处显式检查退出码 |

## 与 `release.yml` 的分工

| | `release.yml` 的 Mirror to Gitee | `mirror-gitee.yml` |
| --- | --- | --- |
| 触发 | tag push（`v*`） | main push |
| 做什么 | 建 Gitee Release、传 exe 附件、写 `update.json` | 只同步源码树 |
| 产物 | Release + `update.json` | Gitee `master` 分支内容 |

**两者互不替代。**

## 安全

- Token 只在 `remote add` 的 URL 里，写进 `.git/config`。
- `trap cleanup EXIT` 保证步骤结束（含异常）即 `git remote remove gitee`。
- 用 `<user>:<token>@host` 形式；GitHub Actions 对 secrets 值自动掩码。
- 不写进任何 artifact。

## 验证

### 本地干跑（推送前）

把 workflow 的脚本段提取出来，注入真实 token 执行：

```text
本地 HEAD: b07163e9...
gitee/master: b07163e9...
--- 分叉统计（左=gitee独有 右=本地独有）---
0	0
--- 推送 ---
Everything up-to-date
--- 校验 ---
远端 master: b07163e9...
本地  HEAD: b07163e9...
OK: Gitee master 已与 GitHub main 一致
```

- `bash -n` 语法检查通过
- `trap` 生效（remote 被清除）
- token 残留扫描 **0 处**

### 真实 CI（推送后）

推送 `1a7baae` 触发 workflow：

| 项 | 值 |
| --- | --- |
| Run | `45323857240` — **success**，17 秒 |
| 原因 | 与 Gitee 只差 1 个提交，增量推送 |
| CI 输出 | `本地 HEAD: 1a7baae1...` |
| **Gitee master 实际 SHA** | **`1a7baae1bbc1d9340b2b24dfe374747f544dda3c`** |

两边一致。

## 遗留（如实记录）

1. **首次全量推送的耗时未实测**。本次是增量（17 秒）。若 Gitee 被重置、或有人在 Gitee 上 force 回退，
   下次会变成全量（预估 1–3 分钟，`timeout-minutes: 20` 足够，但**未实测**）。
2. **Gitee 侧的直接改动会被静默覆盖**。这是 `--force` 的预期行为，但**没有给 Gitee 侧维护者留提示**。
   若日后有多人直接改 Gitee，需要重新考虑策略。
3. **失败不会阻塞任何东西**。镜像挂了 GitHub 构建照样绿——只有 job summary 和 `::error::` 标注。
   这比之前的「静默」好，但**仍需要人去看**。
4. 本 workflow **不验证 Gitee 上的 `update.json`**（那是 `release.yml` 的事，且只在发版时写）。
