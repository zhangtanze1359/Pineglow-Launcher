# 加载器 blocker：影响面收敛 + 合并算法独立验证（第 50 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**（`modified=44 / staged=0` 前后一致）。
两件事：① 把"哪些加载器受影响"从"推测"收敛到**按代码路径分档**；② 把提案补丁 A 的**合并算法单独跑通验证**。

## 1. 影响面：按"profile 从哪来"分档（代码级证据）

| 加载器 | profile 怎么来 | 是否带父版本 `arguments.jvm` | 结论 |
| --- | --- | --- | --- |
| **Fabric** | `FabricService` 从 `meta.fabricmc.net/v2/versions/loader/{loader}/{mc}/profile/json` 取回后**原样写盘**（`LoaderInstallHelper.WriteVersionJsonAsync`）| ❌ 官方 profile 只有自己那一条 `-DFabricMcEmu= …` | **🔴 已实测崩溃**（第 47 轮）|
| **Quilt** | `QuiltService` 同一套路：`meta.quiltmc.org/v3/versions/loader/{loader}/{mc}/profile/json` → 原样写盘 | ❌ 同一形态（官方 profile 同构）| **🔴 代码层确定同病**（未实测）|
| **Forge** | `ForgeService`（`InstallerLoaderService`）**下载官方 installer 并执行**（`RunInstallerAsync`），再读**安装器生成**的版本 JSON | ⚠️ 由官方安装器决定；Forge 生成的 JSON 通常自带完整 `arguments` | **❓ 待定**（要装一个才知道）|
| **NeoForge** | `NeoForgeService` 同上（官方 installer）| ⚠️ 同上 | **❓ 待定** |

⇒ 精确说法：**Fabric 实测必崩；Quilt 同代码路径、官方 profile 同构 ⇒ 极可能同病；Forge/NeoForge 由官方安装器生成 JSON，形态可能不同 ⇒ 未定**。
（这也意味着"要不要把 blocker 写成'所有加载器都不行'"是不准确的——**不能夸大**。）

**判定 Forge/NeoForge 的代价**：一次真实导入（含官方安装器执行 + 原版下载）约 8–10 分钟；
若你希望发布说明写得更准，我可以在下一轮跑一个 Forge 包把它定下来。

## 2. 合并算法独立验证：`MERGE-CHECK RESULT=PASS`

把提案补丁 A 里的 `MergeArguments` **原样**抄到一个 .NET 10 file-based app（`merge-check.cs`，本目录），
喂进**真实的两种 JSON 形态**（父：1.18.2 的完整 `arguments`；子：Fabric profile 只有一条 jvm），跑出：

```
merged.jvm  = ["-Djava.library.path=${natives_directory}","-cp","${class_path}","-DFabricMcEmu= net.minecraft.client.main.Main "]
merged.game = ["--width","${resolution_width}","--height","${resolution_height}"]
断言 父版本库路径保留=True 子版本条目在=True 顺序(父在前)=True game 参数保留=True
MERGE-CHECK RESULT=PASS
```

⇒ 补丁 A 的**算法本身**是对的（父在前、子在后追加；`game` 参数不再被清空）。
与另外两条证据合起来，补丁 A 现在有三重支撑：
① 根因是"arguments 被整体替换"（第 49 轮，含命令行侧印证）；
② **只补 `-Djava.library.path` 就能让 Fabric 实例进渲染管线**（第 48 轮受控实验：10 图集 / 0 崩溃 / 存活 150 秒）；
③ **合并算法本身验证通过**（本轮）。

## 3. 边界（不要越界解读）

- 本轮验证的是**算法**，不是"打完补丁后的产品行为"；真正的确认仍需改源码 + 跑回归（全量 903 测试 + Vanilla/Fabric 启动 + 全量验证链）。
- Quilt 的"同病"是**代码层推断**（同一 `WriteVersionJsonAsync` 路径 + 官方 profile 同构），**未实测**。
- Forge/NeoForge **未定**，本轮没有下任何结论。
- `merge-check.cs` 用 `dotnet run` 跑过，构建产物（`obj/`、`bin/`）已删除，避免污染 docs 树。

## 4. 复现

```powershell
cd docs\release-remediation-evidence-2026-10-05\run-20261006-1330-merge-algo-check
dotnet run merge-check.cs     # 期望：MERGE-CHECK RESULT=PASS（退出码 0）
# 影响面的代码依据
Select-String -Path dev\PineglowLauncher\Pineglow.Launcher.Services\FabricService.cs,QuiltService.cs,InstallerLoaderService.cs `
  -Pattern 'profile/json|WriteVersionJsonAsync|RunInstallerAsync'
```

产物：`merge-check.cs`（算法副本 + 断言）、`merge-check.log`（原始输出）。
