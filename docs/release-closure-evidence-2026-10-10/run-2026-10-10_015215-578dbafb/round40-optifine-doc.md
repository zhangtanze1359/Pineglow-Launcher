# 第 40 轮：README 不再把 OptiFine 说成受支持的加载器（缺陷 8）

运行标识：`run-2026-10-10_015215-578dbafb`（第 40 轮追加）
时间：2026-10-11 19:23–19:27，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。补丁在 `%TEMP%` 隔离副本里验证。
补丁：`apply-optifine-doc.ps1`（2 处）——**未应用**。

---

## 1. 事实（第 16 轮已查明，本轮复核）

README 第 11 行列了五种加载器，而产品只有四条安装路径：

| 加载器 | 有安装服务吗 |
| --- | --- |
| Fabric / Forge / NeoForge / Quilt | ✓（`FabricService` / `ForgeService` / `NeoForgeService` / `QuiltService`） |
| **OptiFine** | **✗**——没有 `OptiFineService`；`InstancePackService.InstallLoaderAsync` 的分派只有四个分支，兜底是 `throw new InvalidOperationException($"不支持的加载器：{kind}")`；CurseForge 解析器更在 `:669` **显式拒绝** OptiFine |

**代码的行为是诚实的**（遇到就明确报错），**README 不是**。

---

## 2. 改动（2 处）

| | 旧 | 新 |
| --- | --- | --- |
| 第 11 行 | `Forge / Fabric / NeoForge / Quilt / **OptiFine**` | `Forge / Fabric / NeoForge / Quilt` |
| 已知限制 | （只提了跨平台） | 新增一段：**「OptiFine 目前只能被识别（加载器枚举与版本名解析），不支持安装——安装分派只有 Fabric / Quilt / Forge / NeoForge 四条路径，遇到 OptiFine 会明确报错「不支持的加载器：OptiFine」。」** |

第 2 处是**故意保留信息**：不装作 OptiFine 完全不存在（枚举与版本名解析确实认识它），
而是把它归到"已知限制"里说清楚——这比静默删掉一行更有用。

---

## 3. 验证

| 项 | 结果 |
| --- | --- |
| 应用 2 处 | 全部命中 |
| diff | **只有这两处改动**（`@@ -10,3 +10,3 @@` 与 `@@ -36,2 +36,4 @@`），没有附带修改 |
| Node 全量（唯一可能受影响的面） | **305 通过 / 0 失败** |

**改动前已核实**：没有任何测试断言这个字符串；根 `README.md` **不在候选快照输入清单里**
（清单里只有 `ui/vendor/README.md` 与一个 TestAssets 的 README），
所以改它**不可能影响候选流水线或发布产物**。

---

## 4. 一处刻意的"不改"

README 第 10 行还有一句「**断点续传 + 可恢复任务队列**」。它**现在仍然是假的**——
但**一旦应用 P-DURABLEQUEUE 就变成真的**。

所以这一句**不能现在改**：改了反而会在补丁被应用之后变成新的错误。
它属于"取决于你是否应用补丁"的条件性文案，报告里已注明。

---

## 5. 累计状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | **12 条**：P-UA、P-FABRICMETA、P-PROXYJVM、P-FAKEIP、P-IMPORTLOG、P-DURABLEQUEUE、P-DEADLOCK、P-LOGSHARE、P-RUNSYNC、P-JAVALABEL、**P-OPTIFINE**、C（+ 3 个回归测试文件） |
| 仍未修 | 缺陷 1（独立加载器安装入口）、4（导入进度界面）、10（WebView2 首启可读性）、12 的"第二个窗口/僵尸窗口"、13 的 ②③ |
| 待你决定 | C 的范围；提交 163 个未跟踪 + 41 个已修改文件；修哪几条缺陷 |

**注**：补丁集又新增了一条，"全部一起应用"的合并验证**再次过期**（第 39 轮刚跑过 11 条）。
P-OPTIFINE 只碰根 `README.md`，与其它补丁零重叠、且不在任何流水线输入里，风险最低——
但按本会话已形成的惯例（8→9→10→11 每次都重跑），下一轮应再跑一次。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
