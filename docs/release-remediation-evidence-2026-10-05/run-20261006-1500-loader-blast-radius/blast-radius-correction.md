# 加载器影响面**更正**：Quilt 不受影响；受影响的是 Fabric/Forge/NeoForge（第 53 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
本轮做的是**推翻我自己第 50 轮的推断**：那时我写"Quilt 同代码路径 + 官方 profile 同构 ⇒ 极可能同病"。
把四个加载器的 **profile 原文**取回来、再按产品现有逻辑推演一遍后，结论要改。

## 1. 四个加载器 profile 的真实形态（逐个取回）

| 加载器 | profile 来源 | `inheritsFrom` | 自己的 `arguments` | 其中 `jvm` 数组 | 自带 `-Djava.library.path` |
| --- | --- | --- | --- | --- | --- |
| **Fabric** | `meta.fabricmc.net/v2/versions/loader/{mc}/{loader}/profile/json` | `1.18.2` | `jvm` + `game` | **1 条**（`-DFabricMcEmu= …`）| ❌ |
| **Quilt** | `meta.quiltmc.org/v3/versions/loader/{mc}/{loader}/profile/json` | `1.20.1` | **只有 `game`** | **没有 `jvm`** | ——（无该数组）|
| **Forge** | 官方 installer 生成 `version.json` | `1.20.1` | `jvm` + `game` | **16 条** | ❌ |
| **NeoForge** | 官方 installer 生成 `version.json`（本轮补测 `21.1.254`）| `1.21.1` | `jvm` + `game` | **15 条** | ❌ |
| （对照）Vanilla | `piston-meta` 的版本 JSON | 无 | `jvm` + `game` | 多条，**含 `-Djava.library.path=${natives_directory}`** | ✅ |

## 2. 按产品**现有逻辑**推演最终命令行（`fallback-check.cs`，逻辑抄自 `LauncherService.ResolveJvmArguments` 179–197）

```
fabric    走 jvm 分支=True  命令行含 -Djava.library.path=False ⇒ 受影响（会崩）
quilt     走 jvm 分支=False 命令行含 -Djava.library.path=True  ⇒ 不受影响
forge     走 jvm 分支=True  命令行含 -Djava.library.path=False ⇒ 受影响（会崩）
neoforge  走 jvm 分支=True  命令行含 -Djava.library.path=False ⇒ 受影响（会崩）
vanilla   走 jvm 分支=True  命令行含 -Djava.library.path=True  ⇒ 不受影响
FALLBACK-CHECK RESULT=PASS  受影响加载器数=3（期望 3：fabric/forge/neoforge）
```

**机制**：
- 只要 profile 里**存在** `arguments.jvm` 数组，代码就只用它、**不再补** `-Djava.library.path`；
- **Fabric/Forge/NeoForge** 的 `jvm` 里都没有库路径 ⇒ 命令行缺库路径 ⇒ **LWJGL 初始化崩**；
- **Quilt 的 `arguments` 里没有 `jvm`** ⇒ 走**兜底分支** ⇒ 反而拿到了 `-Djava.library.path` ⇒ **不受影响**；
- **Vanilla** 自己的 `jvm` 里就带 `${natives_directory}` ⇒ 无论如何都有库路径 ⇒ **不受影响**（这也修正了我第 49 轮"Vanilla 走的是兜底分支"的说法：
  它可能走兜底、也可能走 jvm 分支，**两种都带库路径**，所以结论不变、解释更准）。

## 3. 对修法的影响（好消息）

补丁 A（`arguments` 改为**合并**）对这两个"不受影响"的加载器**没有副作用**：

| 场景 | 合并后的效果 |
| --- | --- |
| 无 `inheritsFrom` 的版本（Vanilla）| `MergeArguments(null, child)` → **原样返回 child** ⇒ 行为不变 |
| Quilt（父有 `jvm`、子只有 `game`）| 合并后 = 父的 `jvm` + 子的 `game` ⇒ **仍有库路径**，且 `game` 参数更完整（符合 Minecraft 官方继承语义）|
| Fabric/Forge/NeoForge | 合并后父版本的 `-Djava.library.path=${natives_directory}` **回到命令行** ⇒ 崩溃被修 |

⇒ 补丁 A 是"**修 3 个、不动 2 个、顺带更正确**"。

## 4. 影响面的最终表述（可写进发布说明）

> 含 **Fabric / Forge / NeoForge** 加载器的实例在**当前版本无法启动**（命令行缺 `-Djava.library.path`，LWJGL 初始化即崩）；
> **Quilt 与 Vanilla 不受影响**。修复补丁已备好（见 `run-20261006-1310-loader-fix-patch/`），修完需回归 Vanilla + Fabric 两种启动。

## 5. 边界

- Quilt"不受影响"是**按 profile 原文 + 现有代码逻辑推演**得出的（`fallback-check.cs` 逐条复刻了分支条件），
  **没有**真的导入一个 Quilt 包跑一遍（那需要 6–10 分钟；若你要写进发布说明，我可以补跑一次把它钉死）。
- NeoForge 本轮用的是 `21.1.254`（1.21.1）的安装器；**1.20.1 那条 NeoForge 线**在该 maven 里已查不到。

## 6. 复现

```powershell
# ① 逐个取回 profile 原文（示例：Quilt）
Invoke-RestMethod 'https://meta.quiltmc.org/v3/versions/loader/1.20.1/0.20.0-beta.9/profile/json' | ConvertTo-Json -Depth 4
# ② 按现有逻辑推演（本目录）
dotnet run fallback-check.cs      # 期望：FALLBACK-CHECK RESULT=PASS（受影响=3）
```
产物：`fallback-check.cs` / `fallback-check.log`（原始输出）。
