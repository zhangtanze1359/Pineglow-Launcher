# MR05 补测之二 · 依赖递归安装的测试（缺口登记表 A1 的剩余部分）

- **run-id**：`20261003-210000Z`
- **性质**：关闭 A1 的**剩余部分** —— `InstallWithDependenciesAsync`（依赖递归安装）此前无覆盖
- **依据**：`MR05-modcontent-update-wiring-tests/20261003-200000Z/deliverable.md` §八："`InstallWithDependenciesAsync` **仍未覆盖**"
- **最终状态**：**证据就绪**（6 个用例；**两个针对性 RED**，其中一个暴露并修正了我自己的测试设计缺陷）

---

## 一、这条路径为什么值得测

`InstallWithDependenciesAsync` 有三处容易出错的地方，而且它同样**会写文件**（属 MG1 数据安全）：

| 机制 | 代码位置 | 风险 |
|---|---|---|
| 递归安装依赖的依赖 | `InstallRecursiveAsync` | 环状依赖会无限递归 |
| `visited` 去重（按 `source:versionId`） | 第 181 行 | 去重失效 → 重复安装 / 栈溢出 |
| 已装跳过（`installedProjects`） | 第 186 行（依赖层）、第 194 行（安装层） | 跳过失效 → 重复下载与并列副本 |

---

## 二、六个用例

| # | 用例 | 断言 |
|---|---|---|
| 1 | `InstallsRequiredDependencyRecursively` | 装 2 个（主 + 必需依赖）；`DependencyCount == 1`；`MainFilePath` 指向主 Mod |
| 2 | `SkipsOptionalDependencies` | 只装 1 个；**且不为可选依赖发任何 API 请求** |
| 3 | `SkipsAlreadyInstalledDependency` | 只装 1 个；**不发该依赖的版本查询**；磁盘仍是 2 个 |
| 4 | `DiamondGraph_InstallsSharedDependencyOnce` | main→B、main→C、B→D、C→D：共 4 个；**D 只装一次**；**D 的版本查询只发生一次** |
| 5 | `UnresolvableRequiredDependency_ThrowsClearly` | `InvalidOperationException` 且含"无法找到必需依赖" |
| 6 | `CyclicGraph_TerminatesAndInstallsEachOnce` | A→B→A：**必须终止**（20 秒上限）；A、B 各只装一次 |

全部用例**不发外网**：按 URL 路由的假处理器（`/v2/version/{id}` 回版本 JSON；下载 URL 回 jar 字节）。

---

## 三、**我第一次的 RED 没有变红 —— 这暴露了我自己的测试设计缺陷**

按纪律先构造 RED：我中性化了**安装层**的守卫（第 194 行）与 `visited`（第 181 行）。
结果 **5/5 仍然通过** —— RED 没有失败，说明测试没测到那两道守卫。

**排查后找到真实原因**：

> 菱形的去重其实由**依赖层**的守卫保证（第 186 行 `!installedProjects.Contains(item2)`）：
> D 装完后进入 `installedProjects`，C 再引用 D 时**在解析之前就被跳过**。
> 而 `visited`（第 181 行）真正防的是**环状依赖**：A→B→A 时 A **尚未安装**
> （主 Mod 在依赖之后才装），`installedProjects` 拦不住，只有 `visited` 能让递归停下。

**所以我的"菱形用例"根本没有碰到 `visited`**，而"已装跳过"用例走的是依赖层守卫而非安装层守卫。
**修正**：新增第 6 个用例（**环状依赖 A→B→A**），并把 RED 改为**分别打两道真正的守卫**。

---

## 四、两个针对性 RED（这才是"测试有牙齿"的证据）

| RED | 中性化哪一处 | 结果 |
|---|---|---|
| **A** | 第 186 行（依赖层 `!installedProjects.Contains(item2)`）→ `if (true)` | **2 失败 / 4 通过**：失败的是 `DiamondGraph_…`（断言"共享库 D 的版本查询应只发生一次"）与 `SkipsAlreadyInstalledDependency` |
| **B** | 第 181 行（`visited.Add(item)`）→ `if (true)` | **测试进程 StackOverflow 崩溃**（`通过=0 失败=0`，日志含 `Stack overflow` 与"测试运行已中止"；栈帧是 `InstallRecursiveAsync` ↔ `ResolveDependencyAsync` 无限循环） |

**RED-B 的意义**：它直接证明 `visited` 是**防无限递归**的，而第 6 个用例（环状依赖）**能检出它的缺失** ——
如果没有这个用例，`visited` 被误删不会有任何测试报警。

**RED-A 里通过的 4 个不是证据**：用例 1/2/5 与该守卫无关；用例 6 由 `visited` 保护（未被中性化）。

---

## 五、实现文件未被留下净改动

两次 RED 都改了实现文件，因此逐字节核对：

| 项 | 值 |
|---|---|
| `ModContentService.cs` 最终 SHA256 | `D39A5A2102049795940602D2071DAFFBF4C8A1F3DE3317705201ADC860FB55CC` |
| 与开工前备份一致 | **True** |
| 残留 RED 标记 | **无**（已检查 `RED-` 字样） |

---

## 六、验证

| 检查 | 结果 |
|---|---|
| 新增用例 | **6 通过 / 0 失败** |
| C# 全量 | **765 通过 / 0 失败**（755 + 4 + 6） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 实现文件 | 逐字节还原、无残留标记 |

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/ModContentDependencyInstallTests.cs` | **新增**（6 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 34→35） |
| `docs/market-execution/gap-register.md` | A1 的"剩余部分"标记为**已关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR05 行补记 |

**生产代码零净改动。**

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| 真实网络下载与真实 Modrinth 版本 JSON | 未验证（假处理器供固定 JSON） |
| CurseForge 依赖分支 | 未覆盖（本包全走 Modrinth） |
| 依赖解析的多版本挑选（`ReleaseType == release` 优先） | 未覆盖 —— 它走 `GetVersionsAsync`（项目维度查询），本包用的是 `VersionId` 直查分支 |
| 真实 GUI 中的依赖安装流程 | 未验证（需桌面会话） |

---

## 九、证据清单

```
red/02-red-a-deplevel.log      RED-A：依赖层守卫中性化 → 2 失败
red/03-red-b-visited.log       RED-B：visited 中性化 → StackOverflow 崩溃（含栈帧）
green/ModContentService.cs.mr05b  实现文件备份（两次 RED 前）
green/01-deps-first.log        首次运行 5 通过
green/02-deps-with-cycle.log   加入环状用例后 6 通过
green/03-deps-final.log        最终 6 通过
green/04-csharp-full.log       765 通过 / 0 失败
green/05-node-full.log         230 通过 / 0 失败
manifest.json
```

> 说明：`red/01-deps-before.log` 是**第一次失败的 RED 尝试**（5/5 仍通过），
> 保留它是为了给"测试设计缺陷是如何被发现的"留下证据，不隐藏这次返工。

**本报告不宣布阶段放行、不提交、不发布。**
