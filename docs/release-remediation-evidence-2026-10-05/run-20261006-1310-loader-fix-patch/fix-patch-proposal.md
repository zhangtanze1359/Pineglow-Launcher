# 加载器崩溃 blocker：根因收敛到一行、三份可审阅补丁已备好（第 49 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**（`modified=44 / staged=0` 前后一致）。
产物：**三份 diff**（未应用）+ 根因精确化 + 回归方案。目标是把"要不要修"变成"审一眼就能拍板"。

## 1. 根因精确化：合并代码**追加了 libraries，却整体替换了 arguments**

`VersionResolver.ResolveInternalAsync`（`dev/PineglowLauncher/Pineglow.Launcher.Services/VersionResolver.cs`）：

```csharp
if (root.TryGetProperty("libraries", out var value4) && value4.ValueKind == JsonValueKind.Array)
    foreach (JsonElement item in value4.EnumerateArray())
        minecraftVersion.Libraries.Add(item.Clone());     // ← 追加：父子库都在 ✅

if (root.TryGetProperty("arguments", out var value))
    minecraftVersion.Arguments = value.Clone();           // ← **整体替换**：父版本 arguments 被覆盖 ❌
```

**命令行侧的证据**（第 47 轮那条 9,801 字符的基线命令行）：

| 检查 | 结果 | 含义 |
| --- | --- | --- |
| 含父版本库 `oshi-core` / `authlib` / `brigadier` / `logging-1.0.0` / `jna` / `text2speech` / `lwjgl-glfw` | ✅ 全部含 | **libraries 合并成功** |
| 含 Fabric 自己的库 `fabric-loader` / `sponge-mixin` / `asm-9.10.1` / `intermediary` | ✅ 全部含 | 同上 |
| 含 `-Djava.library.path` | ❌ 没有 | **arguments 被替换**，父版本那套 jvm 参数全丢 |

⇒ 缺陷**不是**"合并没做"，而是"**`arguments` 这一项合并方式错了**"。影响面因此是**所有继承型 profile**：
Fabric（**已实测崩溃**）、Quilt / Forge / NeoForge（同一条代码路径、同样写 `inheritsFrom`，**代码层推断同病，未逐一实测**）。

## 2. 三份补丁（都在本目录，**未应用**）

| 文件 | 规模 | 内容 |
| --- | --- | --- |
| `proposed-fix-versionresolver.diff` | +48 / −1 | **选项 A（治根，推荐）**：`arguments` 改为**合并**（`jvm`/`game` 数组父在前、子在后追加；其余标量以子版本为准），新增 `MergeArguments` 辅助方法 |
| `proposed-fix-B-launcherservice.diff` | +6 / −0 | **选项 B（最小兜底）**：走 `arguments.jvm` 分支时，若结果里没有 `-Djava.library.path` 就补一条 |
| `proposed-test-versionresolver.diff` | +39 / −0（新文件） | **回归测试**：构造"父版本有完整 `arguments` + 子版本只有自己那条 `jvm`"的继承链，断言合并后**父版本的 `-Djava.library.path=${natives_directory}` 仍在**、子版本条目也在、`libraries` 双全、`mainClass` 取子版本 |

**建议组合**：`A + 测试`（治根且有回归网）；若只想要最小改动、尽快发版，则 `B + 测试`（测试同样能钉住行为）。
两份补丁**互不冲突**，也可以都上（B 作为防御性兜底）。

> 生成方式：把源文件复制到 `%TEMP%`、**只在副本上**应用改动，再用 `git diff --no-index` 生成；
> 工作区**从未被修改**（`git status` 前后都是 44 个已修改 tracked 文件）。

## 3. 改完之后必须跑什么（回归清单）

1. `dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release`（全量 903 项，含新增测试）；
2. **Vanilla 启动**：`verify-vanilla-launch.ps1`（第 45 轮那套，期望进主菜单、10 个图集）；
3. **Fabric 启动**：`verify-fabric-launch.ps1` + `probe-fabric-sequence.ps1`
   （期望命令行**含** `-Djava.library.path`，且不再出现 `Failed to locate library`）；
4. 因为改了 `dev/**` 源码 ⇒ **产物字节变化** ⇒ 按本会话的不变量，必须**重跑全量验证链**
   （`verify-source-snapshot.ps1`：锁定还原/Node/C#/UI 闭包/清单双向一致/发布可复现/冷启动），
   既有证据（EXE sha256 `5009cf73…` 等）随之作废、需要重新生成；
5. 若新增测试文件：把它加进 `scripts/source-snapshot-inputs.txt`（否则不进候选快照/提交）。

## 4. 需要你拍板

- **A（治根 + 测试）**：约 50 行改动 + 1 个新测试文件；风险最低（改的是"合并语义"，与 Minecraft 官方继承语义一致）。
- **B（最小兜底 + 测试）**：约 6 行；不动合并逻辑，只在生成命令行时补参数；治标不治本（其它被覆盖的 `arguments.game` 项仍丢）。
- **不修**：则本次发版必须写明"**含加载器（Fabric/Quilt/Forge/NeoForge）的实例暂无法启动**"，
  并且不应把整合包导入当作卖点（第 43 轮"导入 PASS"只覆盖"装"，不覆盖"起"）。

无论哪条，**都已具备可执行的补丁与验证路径**；我**没有**擅自改动源码。

## 5. 复现与核对

```powershell
# 看根因（一行替换 vs 一行追加）
Select-String -Path dev\PineglowLauncher\Pineglow.Launcher.Services\VersionResolver.cs -Pattern 'arguments|libraries' -Context 0,3
# 看三份补丁
Get-Content docs\release-remediation-evidence-2026-10-05\run-20261006-1310-loader-fix-patch\*.diff
# 确认工作区干净（应仍是 44 个已修改、0 个已暂存）
git status --porcelain | Measure-Object
```
