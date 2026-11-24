# CurseForge Key 泄露审计 · 2026-11-24

## 结论摘要

**存在真实泄露，且此前的守卫给了虚假的安全感。**

| 位置 | 状态 | 说明 |
| --- | --- | --- |
| **GitHub / Gitee 公开仓库历史** | 🔴 **泄露** | 17 个提交的树里含完整 60 字符 Key，**任何人可 clone 拿到** |
| **已发布的 v1.0.4 / v1.0.5 / v1.0.6** | 🔴 **泄露** | 二进制内含旧 Key |
| **v1.0.6 源码 tag** | 🔴 **泄露** | `CurseForgeKeyDefaults.cs` 含完整 Key |
| **HEAD 之前的 `docs/market-execution/` 4 个快照** | 🔴 **泄露** | 历史证据副本内嵌完整 Key |
| **当前 HEAD 工作树** | ✅ **已清零** | 脱敏 + 构建期注入，扫描 0 命中 |
| **当前 HEAD 构建产物** | ✅ **已清零** | 扫描 0 命中 |

## 最严重的问题：守卫覆盖不全

OPEN-08 销项时我声称「源码树零字面量、守卫能防回归」。

**那个守卫只扫 `dev/PineglowLauncher/**/*.cs`。**

审计实测：即使在我「修好」的提交 `016d89f` 里，**仍有 6 个文件含完整 Key**，守卫一个都没覆盖到：

```text
dev/PineglowLauncher.Tests/SecretHygieneTests.cs
docs/evidence/2026-11-16-s9/验收.md
docs/market-execution/MR05-dependency-install-tests/.../ModContentService.cs.mr05b
docs/market-execution/MR05-modcontent-update-wiring-tests/.../ModContentService.cs.mr05a
docs/market-execution/MR05-recovery-version-display/.../ModContentService.cs.fixed
docs/market-execution/MR12-outbound-endpoint-audit/.../ModContentService.cs.before
```

> 这是本次审计最重要的发现：**不是"有泄露"，而是"我以为已经修好、其实没修全"。**

## 审计方法

| 范围 | 方法 | 结果 |
| --- | --- | --- |
| 当前工作树（全部文本文件） | 正则 `$2a$10$` + 50 个以上合法字符 | **0 命中**（脱敏后） |
| 构建产物 `bin/` `obj/` | 二进制扫描 | 0 命中 |
| Git 历史 | `git log -S` 缩小到 17 个提交，逐个精确判定 | **17 个全部含完整 Key** |
| 截断引用 vs 完整值 | 分开判定 | 文档里的 `$2a$10$G3lIsq…WjOCrG`（20 字符）**不算泄露** |

最早的引入点：**`3665815`（2026-08-10，「CurseForge 免 Key 方案落地」）**。

## 已做的修复

### 1. 脱敏 4 个历史快照

`docs/market-execution/` 下 4 个文件里的完整 Key 替换为：

```text
"$2a$10$<REDACTED-2026-11-24-SEE-docs/evidence/2026-11-24-key-audit/>"
```

**为什么可以改这些「历史证据」**：它们是 2026-10 那次 MR 审计的实现文件备份，
用途是证明「当时的代码长什么样」。一个 60 字符 API Key 常量与那份证据要证明的东西
（依赖安装 / 端点审计逻辑）无关。脱敏后文件仍完整可读，占位符指向本审计记录。

### 2. 守卫扩展到全仓库

`SecretHygieneTests` 新增 `FullKeyValue_DoesNotAppearAnywhereInRepository`：

- 扫**仓库内全部文本文件**（排除 `.git`、构建输出、`node_modules`、`_archive`、`.cindy-worktrees`、二进制扩展名、测试文件自身）
- 判定条件是**完整 60 字符值**（`$2a$10$` + 50+ 字符），**不是**前缀
- 因此文档里的截断引用不会被误判

## 验证

| 项 | 结果 |
| --- | --- |
| 守卫 7/7 通过 | ✅（新增全仓库测试，1 秒） |
| **探针反证**：往 `docs/evidence/` 放含完整 Key 的文件 | ✅ **被精确点名，退出 1** |
| 探针删除后 | ✅ 恢复通过 |
| 全量测试 | **1505 通过 / 0 失败 / 0 跳过** |
| 脱敏后扫描 | 工作树真实文件 **0 命中** |

## 仍未解决（需要编排方）

### A. Git 历史里的 Key 仍在

`git log -S` 的 17 个提交**都在公开仓库里**。已发布出去的克隆无法回收。

**清理历史（filter-branch / filter-repo）能移除，但**：
- 会改变所有 commit SHA
- 所有下游 clone 必须重来
- **已经 clone 过的人手里仍有**
- **已发布的 Release 附件仍含旧 Key**（除非删除 release）

### B. 已发布的三个版本二进制含旧 Key

v1.0.4 / v1.0.5 / v1.0.6 的 exe 里都有。**除非删除 Release，否则无法撤回。**

### C. 轮换决定已重新评估为「要轮换」

编排方 2026-11-24 决定**轮换**。原「不轮换」的前提是「未泄露」——
本次审计证明该前提不成立（Key 在公开历史里）。

轮换步骤需持有人到 CurseForge 后台操作（见下节）。

## 轮换待办（需持有人执行）

1. 到 CurseForge 后台**吊销**当前 Key。
2. 生成新 Key。
3. 更新 GitHub Secret `PINEGLOW_CURSEFORGE_BUILD_KEY`。
4. 之后任意一次发布，构建期注入的就是新 Key。
5. **旧的已发布版本仍含旧 Key**——但旧 Key 吊销后即失效，风险消除。

> 注意：**吊销后，v1.1 之前的旧版本用户若未升级，CurseForge 功能会失效**（它们内置的是被吊销的旧 Key）。
> 这是轮换的代价，需接受。

## 另一个发现：本机还有副本

`.cindy-worktrees/curious-liskov/` 是另一个 AI 的工作树（被 `.git/info/exclude` 排除，不在版本控制内），
里面有 5 个文件含完整 Key。**它不会进仓库，但在本机磁盘上。**

未处理——需要编排方决定是否清理该工作树。

## 产物

| 文件 | 内容 |
| --- | --- |
| `find_current.py` | 当前工作树精确扫描（完整 Key 判定） |
| `count_full.py` | 历史提交逐条精确判定 |
| `judge.py` | 区分「完整 Key」与「截断引用」 |
| `redact.py` | 4 个快照的脱敏脚本 |
| `scan_history.py` / `fix_escape.py` | 中间脚本（保留以复现过程） |
