# 第 25 轮：**打过补丁的构建**也走通了「启动到标题界面」

运行标识：`run-2026-10-10_015215-578dbafb`（第 25 轮追加）
时间：2026-10-11 17:20–17:26，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离夹具里完成。
被验产物：`642051E5F46F940E81A2F80F1B1F95B1B4DE4E7207E0F135A9ECE99EB1609C3D`（= 5 条产品修复 + C 的合并构建）。

---

## 1. 为什么这一轮是必要的

前面 20 多轮的端到端功能验证（四种加载器、断点续传、界面、更新链……）全部是在**出厂 EXE** `CA8F94C1…` 上做的。
而**补丁会改变二进制**（第 24 轮实测：只打修复 → `694A73F5…`；修复 + C → 同样的 `694A73F5…`；
本轮定稿版含日志节流 → `642051E5…`）。

也就是说：**「出厂 EXE 能装能玩」并不能自动推出「打过补丁的 EXE 能装能玩」**。
补丁里有几处恰好碰在启动/下载路径上（下载客户端加了 UA 头、安装器 JVM 参数前插了代理参数、导入日志改了委托），
所以必须单独验一次。

## 2. 做法与结果

夹具：新的 portable 目录 + **合并构建的 EXE** + 沿用已有实例的配置（游戏目录指向已装好 1.21.11 与各加载器的夹具，
所以这一轮只验**启动**，不重复下载）。

| 步骤 | 结果 |
| --- | --- |
| 枚举实例 | 7 个，加载器与版本都正确读出（Vanilla ×3、Quilt、Forge、NeoForge、**Fabric**） |
| 目标实例 | `d7-fabric 导入`（Fabric 0.19.5 + MC 1.21.11） |
| `prelaunch-check` | **ok=True，errors=0，warnings=0** |
| `launch` | **ok=True**，游戏 pid 25044 |
| **到达标题界面** | **True**，标记 `Sound engine started` |
| 收尾 | 残留 launcher **0**、残留 javaw **0** |

### 2.1 独立于探针标记的原始证据

游戏日志（`d7-fabric 导入 (1)\logs\latest.log`，已复制到 `round25-patched-launch/patched-launch-game.log`）关键行：

```
[17:21:12] [main/INFO]: Loading Minecraft 1.21.11 with Fabric Loader 0.19.5
    - fabricloader 0.19.5
[17:21:12] [main/INFO]: SpongePowered MIXIN Subsystem ... Service=Knot/Fabric Env=CLIENT
[17:21:18] [Render thread/INFO]: Setting user: d6tester
[17:21:2x] [Render thread/INFO]: Sound engine started
```

**即：确实是 Fabric（Knot）加载器在跑，并且进到了标题界面**——不是"启动器说成功"而已。

日志里仅有的两条 ERROR 仍是**离线账号**的必然结果（Mojang 用户属性 401、Realms 鉴权失败），与启动器无关。

---

## 3. 至此「两个产物」的证据各自完整

| 产物 | 是什么 | 已完成的验证 |
| --- | --- | --- |
| **出厂 EXE** `CA8F94C1…` | 当前仓库里那份（3 次流水线 PASS 的产物） | 四种加载器装成并到标题界面、断点续传、界面级（九视图）、真实更新链门槛、跨 RID 打包、镜像安装…… |
| **打过补丁的 EXE** `642051E5…` | 出厂 + 5 条产品修复（+ C，但 C 不改二进制） | **947/0 全量测试**、五条修复各自的行为 A/B、**真实导入（含 Java 自动下载与库下载）**、**启动 Fabric 到标题界面**（本轮） |

**结论**：采用那 5 条修复之后，被验的不再是"另一个没验过的产物"——它有自己的安装与启动证据。
（仍未覆盖的是"打过补丁的构建"上的**其余**场景：四种加载器全矩阵、断点续传、界面级、更新链。若要发布带补丁的版本，
建议按第 6.3 节的做法在补丁版本上重跑一遍完整流水线——那正是 `verify-source-snapshot.ps1` 一次就能覆盖的东西。）

---

## 4. 本轮没有新增工具失误

（前几轮的教训已生效：脚本自检 BOM、探针目录不放第二个顶级语句项目、不截断构建输出。）

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
