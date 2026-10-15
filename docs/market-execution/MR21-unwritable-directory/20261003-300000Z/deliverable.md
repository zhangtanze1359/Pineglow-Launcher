# MR21 补测之二 · 目录不可写与路径无效（缺口登记表 A12 的剩余项）

- **run-id**：`20261003-300000Z`
- **性质**：关闭 A12 里"**目录不可写**"这一半（端口那一半见上一包）
- **依据**：`MR21-install-launch-perf-baseline/20261003-090000Z/deliverable.md` §六：
  "目录不可写 / 端口冲突 | 未在本包重新测量"
- **最终状态**：**证据就绪**（3 个用例；3 个 RED 成立；**生产代码零净改动**）

---

## 一、实测结论：**失败会响亮报错，且不碰任何数据** ✅

| 场景 | 实测 | 评价 |
|---|---|---|
| **`mods` 是一个文件**（本该是目录）→ 列 Mod | **500** + `error="Cannot create '…\mods' because a file or directory with the same name already exists."` | ✅ 可读、不崩、**没有假装成功** |
| **游戏目录位于文件之下**（`<file>\sub`）→ 注册实例 | **400** + `error="目录不存在，无法挂回：…\mods\sub"` | ✅ 可读、可操作 |
| **两次失败之后**，整棵合成目录 | **3 个文件逐字节不变** | ✅ 零数据损失 |

**这两条都走顶层异常映射**（`LocalApiService.cs:291-296`：`catch (Exception ex) → 500 { error = ex.Message }`），
因此"写不了"不会变成空响应或崩溃 —— **这一点是好的**。

---

## 二、做法：**确定性且不动 ACL**

把"本该是目录"的路径做成**文件**：

- `mods` 是文件 → 列 Mod 时的 `Directory.CreateDirectory` 必然失败；
- 游戏目录位于**文件之下**（`<file>\sub`）→ `Directory.Exists` 为 false → 注册被拒。

为什么不用 `icacls /deny` 造真正的 ACL 拒绝：那会改动**权限**，且若移除失败会在临时目录里留下
一个自己都写不进去的目录。本包选择**等价但零副作用**的注入方式，并在 §六 如实标注"ACL 拒绝未测"。

---

## 三、3 个用例

| # | 用例 | 断言 |
|---|---|---|
| ① | `ListMods_WhenModsPathIsAFile_ReturnsReadableError_AndLeavesThatFileUntouched` | 状态**不是 200**；`error` 非空；那个文件**仍存在且内容逐字节不变** |
| ② | `Register_WhenGameDirectoryIsUnderAFile_IsRejectedWithReadableError` | **400**；`error` 非空且含"目录不存在"或"无效" |
| ③ | `AfterBothWriteFailures_TheWholeSyntheticTreeIsByteIdentical` | 两次失败后，整棵目录树（3 个文件）**数量与内容都不变** |

---

## 四、三个 RED（含一次我构造错、一次更贴题的补充）

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 去掉注册里的 `Directory.Exists` 校验 | **1 失败**：用例 ② ✓ |
| **2** | 让 `ModService.GetModsAsync` 的 `Directory.CreateDirectory` **静默吞掉**失败 | **0 失败** ✗ —— 见下 |
| **3** | 让**路由**在读取失败时**静默返回空列表**（`catch { mods = new List<ModFile>(); }` → 200） | **1 失败**：用例 ① 报"mods 路径是文件时**不得假装成功返回 200**" ✓ |

**RED-2 为什么没变红（这次"没变红"本身是有信息的）**：
我只吞掉了 `CreateDirectory`，但紧接着的 `Directory.EnumerateFiles(mods)` 仍会抛
→ 错误只是**换了来源**，路由依然响亮失败。所以 RED-2 **没能**证明"测试能抓静默成功"。

**于是补了 RED-3**：把**路由层**改成"读不到就返回 0 个 Mod（200）"—— 这才是真正的"假装成功"，
用例 ① 立刻抓住 ✓。**这条 RED 才是"测试有牙齿"的证据。**

三次 RED 后 `LocalApiService.cs` 与 `ModService.cs` **逐字节还原**（无 `RED-` 标记残留）。

---

## 五、本轮自找并修掉的一个前置断言错误

用例 ③ 第一次运行失败，原因**不是产品**：我写了 `Assert.True(before.Count >= 4, "夹具应至少有 4 个文件")`，
而夹具实际只有 **3** 个文件（`mods`、`options.txt`、`saves/world1/level.dat`）。
改为 `== 3` 并打印实际清单后通过。

> 这是本会话第 N 次"**自己的断言/口径先错**"：与第 38 轮的出口计数、第 39 轮的引号问题同类。
> 规律很清楚：**新增断言时先把事实数一遍**，不要凭印象写边界值。

---

## 六、验证

| 检查 | 结果 |
|---|---|
| 本组用例 | **3 通过 / 0 失败** |
| C# 全量 | **804 通过 / 0 失败 / 0 跳过**（801 + 3） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 生产文件 | `LocalApiService.cs` / `ModService.cs` 逐字节还原 |

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/UnwritableDirectoryTests.cs` | **新增**（3 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 41→42） |
| `docs/market-execution/gap-register.md` | A12 标记为**已关闭**（端口 + 目录不可写两部分都关） |
| `docs/market-task-plan-2026-10-03.md` | MR21 行补记 |

**生产代码零净改动。**

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| **真正的 ACL 拒绝**（`icacls /deny`） | **未测**（原因见 §二）。本包用的是"路径被文件占住"这一等价形态 |
| **只读介质 / 磁盘满** | 未测 |
| **WebView2 缺失路径** | 未观测（本机已安装），见上一包 |
| 真实桌面渲染 | 未验证 |
| **"端口被占用"的可操作提示** | 仍未做（产品交互决策） |

---

## 九、证据清单

```
green/01-unwritable-first.log        首次运行 2 通过 / 1 失败（我自己的前置断言错）
green/02-unwritable-final.log        修正后 3 通过
green/05-unwritable-final.log        三个 RED 还原后 3 通过
green/06-csharp-full-final.log       804 通过 / 0 失败
green/07-node-full.log               230 通过 / 0 失败
red/LocalApiService.cs.before            RED 前备份
red/ModService.cs.before                 RED 前备份
red/01-red-no-dir-check.log          RED-1：去掉目录校验 → 1 失败
red/02-red-swallow-mods-error.log    RED-2：只吞 CreateDirectory → **0 失败**（说明它不构成"静默成功"）
red/03-red-silent-success.log        RED-3：路由静默返回空列表 → 1 失败（真正的"假装成功"被抓住）
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
