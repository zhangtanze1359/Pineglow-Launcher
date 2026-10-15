# 补丁构建的回归：**Vanilla 与 Quilt 都正常启动**（第 76 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动工作区一个字节**。
目的：证明 B1 的"`arguments` 合并"修复**没有弄坏另外两条路径**（第 75 轮已证 Fabric 从"必崩"变为"进渲染管线"）。

## 1. 结果（都在**打过补丁的构建**上）

| 路径 | 命令行 | 标记 | 判定 |
| --- | --- | --- | --- |
| **Vanilla 1.18.2** | 7,412 字符，**含 `-Djava.library.path`**（指向 `…\versions\1.18.2\natives`）**且含 `-Dminecraft.launcher.brand`** | `lwjgl=1 sound=1 atlas=10 崩溃=0 行数=32`，JVM 存活整个观察期 | **PASS（6/6）** ✅ |
| **Quilt**（真实包 `ukupvp`，quilt-loader 0.21.0）| 18,352 字符，**含 `-Djava.library.path`** | `loader=3 lwjgl=1 sound=1 atlas=12 崩溃=0 行数=709`，JVM 存活 | **PASS** ✅ |
| （对照，第 75 轮）**Fabric** | 10,561 字符，含库路径 | `fabric=1 lwjgl=1 sound=1 atlas=10 崩溃=0 行数=1335` | **PASS** ✅ |

⇒ **三种启动路径（Vanilla / Fabric / Quilt）在打过补丁的构建上全部正常**，而候选上 Fabric 必崩。
合并修复的效果与第 53 轮的逻辑推演一致：**Fabric 被修好、Quilt/Vanilla 不受影响**。

## 2. 一个**我自己造成的**假失败（已更正，产品行为正确）

第一次跑 Vanilla 时得到：`prelaunch ok=False errors=未找到可用的 Java 运行时，请先在设置中指定 Java 路径`，launch 被拒。
原因：我在第 75 轮之后**删掉了补丁副本的 `data/`**，连带删掉了"整合包导入时自动装好的 Java 17"。
⇒ **产品的拦截与提示完全正确**；复用同一份补丁构建（`data/runtimes/` 里已有 Java）重跑即 PASS。

## 3. 顺带记一条产品行为（不是缺陷，但影响测试写法）

**装完版本不会自动把"默认游戏"实例绑到该版本**（默认实例的 `minecraftVersion` 仍是空串）：
第一次跑时我按 `minecraftVersion == 1.18.2` 找实例，找不到 ⇒ 得**显式 `POST /api/instances {name, version}`**（与第 45 轮候选验收一致）。
界面里应当是"给实例选版本"的交互；写测试时要显式建带版本的实例。

## 4. 9 步清单进度（全部在**补丁构建**上）

| 项 | 状态 |
| --- | --- |
| 全量 903 测试（+新增回归测试）| ✅ **904/904**（第 74 轮）|
| B0 / B1 / B2 代码级复现 | ✅ 4/4（第 74 轮）|
| **Fabric 启动** | ✅ 端到端（第 75 轮）|
| **Vanilla 启动 / Quilt 启动** | ✅ **本轮** |
| 仍待做（批准后在工作区正式执行）| 打过补丁的 Node 304、**Forge 包导入不再 404**、全量验证链 + 新 sha256 |

## 5. 复现

```powershell
# Vanilla（复用第 74 轮的补丁构建；其 data/runtimes 里已有 Java 17）
powershell -File ...\run-20261006-2700-patched-regressions\verify-patched-vanilla-only.ps1 `
  -PatchedColdDir %TEMP%\pg-patched\dev\PineglowLauncher\bin\Release\net10.0 -JsonPath <证据>\patched-vanilla.json
# Vanilla + Quilt 一起（首次跑时注意：若 data/ 是空的，会因"未找到 Java"而失败——那是环境，不是回归）
powershell -File ...\verify-patched-regressions.ps1 -PatchedColdDir <同上> -QuiltPack <ukupvp.mrpack> -JsonPath <证据>\patched-regressions.json
```

产物：`patched-vanilla.json` + `vanilla-only-step.log` + `vanilla-cmdline.txt` + `vanilla-game.log`（Vanilla）；
`patched-regressions.json` + `regress-step.log` + `quilt-cmdline.txt` + `quilt-game.log`（Quilt）。
