# 候选集合双向核对 + 触发条件验证（第 15 轮）

日期：2026-10-06（Asia/Hong_Kong）。目的：把"提交时到底会纳入什么"从**叙述**变成**数过的事实**，
并补上此前没人验过的**触发条件**（PyYAML 把 `on:` 解析成布尔 `True`，所以结构校验根本看不到它；
actionlint 又拿不到）。

## 1. 正向：白名单 146 条是否都 add 得进去

| 检查 | 结果 |
| --- | --- |
| 条目数（去掉注释/空行）| **146** |
| 每条都在磁盘上存在 | ✅ 0 缺失 |
| 有已被 git 跟踪的（与"全部未跟踪"矛盾）| ✅ **0 个** |
| 有被 `.gitignore` 忽略的（`git add` 会被拒或静默跳过）| ✅ **0 个**（按 `git check-ignore -q` **退出码**判定）|

因此：**直接 `git add <路径>` 即可，不需要 `-f`**。

其中 `ui/assets/pine-forest.svg` 之所以没被忽略，靠的是 `.gitignore` 里的反选规则：

```
36: ui/assets/*                      ← 忽略该目录下所有历史资源
37: !ui/assets/pine-forest.svg       ← 唯独把运行时要用的这张放回来
```

**这条 `!` 规则是必需的**：删掉它，R02 修好的资源就又进不了候选（而本地构建照旧通过——
正是 R02 原始缺陷的形态）。它已在当前工作区的 `.gitignore` 改动里。

## 2. 一个我自己制造的假警报（记下来避免重复）

第一版检查里我用"`git check-ignore -v` **有没有输出**"判断是否被忽略，于是把
`ui/assets/pine-forest.svg` 报成了"被忽略"——实际上 `check-ignore -v` 对**反选规则命中**也会打印
（行首是 `!`），退出码才是权威（1 = 未被忽略）。改用退出码后结论变为 0 个被忽略。
**又一次：判据选错会把结论整体带反。**

## 3. 反向：有没有"该进候选却没进"的东西

筛选条件：未跟踪 + 未被忽略 + 不在白名单 + 不在 `docs/`（docs 按设计不入库）→ 共 **90 个文件**。

按目录归类：`scripts/` 32、`ui/` 20、`.testagent/` 18、`.test-tmp/` 10、`dev/` 6、`.trae/` 3、根 `docs/` 1。

逐个看过，**没有任何一个是构建或测试必需输入**：

| 类别 | 例子 |
| --- | --- |
| 探针/一次性脚本 | `scripts/_phase3a-*.py`、`_rework-*.py`、`_*.cs`、`_mimeprobe/`、`_assetgen/`、`_uritest/` |
| 调试脚本与备份 | `ui/tests/_probe.cjs`、`probe-*.cjs`、`*.bak`、`*.orig.bak`、`*.append.tmp`、`*.tmp2`、`*.new` |
| 工具/缓存/临时 | `.testagent/`、`.test-tmp/`、`.trae/`、`TestResults/*.trx` |
| 探针输出 | `dev/icon-tests-red.txt` |
| **空文件遗留物** | `dev/PineglowLauncher.Tests/DownloadResumeTests.cs`（**0 字节**，没有任何 `[Fact]`）|
| 被文档引用的本地自动化脚本 | `scripts/run-dify-minimax-workflow.ps1`（仅被 `docs/source-reproducibility-workflow-2026-09-28.md` 引用）|

**特别核过 `DownloadResumeTests.cs`**：它是 0 字节、0 个 `[Fact]`，既不在快照里也不在白名单里
——**不是"被漏掉的有效测试"**，因此"测试覆盖被悄悄少算"这件事不成立（C# 903 的计数没有漏掉真实用例）。
它和上面那些一起写进了候选提交计划的"不得进入候选"清单（按规则写，而不是逐个文件列举）。

## 4. 触发条件（此前从未被验证）

PyYAML 默认把 `on:` 解析成布尔 `True`（YAML 1.1），所以之前那份"结构校验通过"**压根没看触发条件**。
本轮用 YAML 1.2 语义（把 bool 解析器改成只认 true/false）重新解析：

| 工作流 | `on` | `permissions` | jobs |
| --- | --- | --- | --- |
| `release.yml` | `push: {tags: ['v*']}` | `{contents: write}` | `verify`, `release` |
| `build.yml` | `push: {branches: [main]}`、`pull_request`、`workflow_dispatch` | 未声明（用默认）| `build` |

要验的性质有两条，都成立：

1. **发布只能由 tag 触发**：`release.yml` 的 `on` 里**没有**分支推送或其他事件 ——
   普通推送不会误触发发布（tag 规则是 `v*`，`compute version` 里的 `Substring(1)` 依赖这个 `v` 前缀）。
2. `release` 作业有 `contents: write`，够用于创建 Release 与上传附件。

（这不替代 actionlint：表达式语义、action 输入 schema、runner 行为仍未被本地校验。）

## 5. 未做

- **未**执行任何 `git add`/`commit`/`push`（本文件只是核对）。
- **未**删除上述任何遗留文件（包括那个 0 字节空文件）：删文件不是本轮授权范围内的动作，
  改为把它们写进计划的排除规则。
- **未**评估"这些遗留文件是否该清理"这一产品/仓库治理问题——属用户决定。
