# 原版 Minecraft **真的启动了**（并正常退出）：R06「启动/退出」在真机上证完（第 45 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
工具：`verify-vanilla-launch.ps1`（本目录，可复跑；本轮它在观察循环里崩过一次，原因与修法见 §5）。
结论：**启动链路端到端成立**——JVM 起来、OpenGL/声音/资源图集全部就绪、窗口标题 `Minecraft 1.18.2`，
退出后启动器正确结算游玩时间。

## 1. 这一轮的前提（都已在前面几轮单独验过）

| 前置 | 出处 |
| --- | --- |
| 版本清单 917 条真实版本 | 第 36 轮 |
| 原版下载 + 完整性（client.jar SHA1、assets 逐对象）| 第 40 轮 |
| Java 运行时的来源/校验和/布局/被产品发现 | 第 42 轮 |
| 整合包导入会**自动装好 Java** | 第 43 轮 |
| 文件层替换/回滚/备份恢复 | 第 44 轮 |

本轮把最后一段补上：**用 Java 真的把游戏拉起来，再让它退出**。

## 2. 实测时间线（本机有真实 GPU：RTX 4060 Laptop + Radeon 780M，15.2 GB 内存，交互式桌面）

| 时刻 | 事件 |
| --- | --- |
| 05:34:04 | 建实例（`launch-acceptance`，1.18.2）→ `POST /api/versions/download` |
| 05:38:20 | **版本安装完成**：`status=completed`，256 秒（3071 个资源文件）|
| 05:38:50 | Java 17 就绪：**43,780,109 B / 29 秒 / SHA256 与 Adoptium 声明一致** |
| 05:38:55 | 离线账号创建成功；**`prelaunch-check` → `ok=True`，errors 为空** |
| 05:38:56 | **`POST /api/instances/{id}/launch` → 200 `{"ok":true,"pid":5924}`**（363 ms）|
| 05:39:01–05:39:12 | 游戏日志：`Setting user: AcceptanceTester` → `Backend library: LWJGL version 3.2.2` → `Narrator library … loaded` → `Reloading ResourceManager` → `OpenAL initialized` → `Sound engine started` → **10 个纹理图集 `Created: …-atlas`** |
| 05:43:xx | 进程实测：`pid=5924`、**工作集 2,685 MB**、**窗口标题 `Minecraft 1.18.2`** |
| 05:44:01 | 主动终止 JVM（产品**没有停止接口**，只能外部 kill）；`totalPlaySeconds` 由 0 → **305** |

## 3. 启动器组装的命令行（实测，节选）

```
"<…>\runtimes\java-17-windows-x64\jdk-17.0.20.1+1-jre\bin\java.exe" -Xmx4096M -Dfile.encoding=UTF-8
  -XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump
  -Djava.library.path=<…>\versions\1.18.2\natives
  -Dminecraft.launcher.brand=PineglowLauncher -Dminecraft.launcher.version=1.0.0
  -cp <…>\libraries\…;…（完整 classpath）
```

⇒ JRE 取自 `runtimes/`、内存参数来自实例配置、natives 路径与 classpath 正确、品牌/版本属性注入正确。

## 4. 判定：**PASS**（启动 + 退出）

- **启动**：`{"ok":true,"pid":5924}`；JVM 存活且**窗口标题 `Minecraft 1.18.2`**；LWJGL/OpenGL、OpenAL 声音、
  10 个纹理图集、资源重载、讲述人库**全部成功**——这不是"进程起来了但马上就崩"，而是真的进入了渲染与资源管线。
- **退出**：终止后启动器把该实例的 `totalPlaySeconds` 从 0 结算为 **305**（≈5 分 5 秒），`playCount=1`
  ⇒ 进程退出的检测与游玩统计**正确**。
- 日志里唯一的错误是**离线账号必然出现的**：`authlib InvalidCredentialsException: Status: 401`（拿不到会话属性）
  与 Realms 的 `SignedJWT: 0`——两者都**没有阻止游戏进入主菜单**。

## 5. 本轮我自己的一个 bug（已修，且是老毛病重犯）

观察循环里写了 `$mine = $javas | Where-Object { ... }` —— **没有 `@()` 包裹**，
于是在 `Set-StrictMode` 下当只有 1 个匹配对象时访问 `$mine.Count` 直接抛
`property 'Count' cannot be found`，脚本当场中断（游戏进程还在跑，我随后手工核对并收尾）。
⇒ 脚本里所有管道赋值已统一加 `@(...)`；这条与第 43/44 轮的 `Measure-Object` 空管道、字段名假设是**同一类**问题：
**StrictMode 下任何"想当然的标量/属性"都会静默把脚本打断**。

## 6. 诚实边界

- 本轮用的是**离线账号**（`AcceptanceTester`），所以它证明的是"**离线账号 + 正版文件**能启动"；
  **真实微软账号的登录后链路**（令牌→XboxLive/XSTS→档案/皮肤披风、续期/切换/取消）仍是 NOT_RUN。
- 只启动到**主菜单/资源就绪**阶段，未验证"进入世界、存档加载、Mod 生效、多实例并发"等玩法层行为。
- 产品**没有 API 停止接口**（`/api/instances/{id}/stop` 不存在），本轮退出是外部 kill；
  这不代表 UI 不能关（用户关窗口是正常路径），但"程序化停止/强制结束"这条路在接口层缺失——可作为可选加固项记录。
- 本轮**没有**测加载器（Fabric/Forge/NeoForge）实际启动；只测了 Vanilla。

## 7. 复现命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-1130-vanilla-launch\verify-vanilla-launch.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe -Version 1.18.2 `
  -WorkRoot %TEMP%\pg-launch -JsonPath <证据>\vanilla-launch.json -ObserveSeconds 180
# 期望：版本安装 completed（约 4 分钟）→ JRE SHA256 一致 → prelaunch ok=True
#       → launch 200 带 pid → 观察期内 JVM 存活、latest.log 出现 LWJGL/Sound engine/atlas
```

产物：`step.log`（逐段时间线）、`game-latest.log`（游戏原始日志 32 行）、`launcher.log`、
`launch-facts.json`（中断后补记的机器可读事实：进程/窗口标题/日志标记/前后状态）。
