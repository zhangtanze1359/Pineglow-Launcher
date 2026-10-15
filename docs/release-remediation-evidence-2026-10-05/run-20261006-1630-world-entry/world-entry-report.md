# R06「进入世界 / 加载存档」：**不用 GUI 就验通了**（第 56 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
这一格此前一直记 NOT_RUN（理由："需要 GUI 交互"）。本轮用**产品自己的 `CustomGameArgs`** +
**官方服务端生成的存档**把它做成了**可复现的真机验收**。

## 1. 做法（全部离线可复现）

1. **备好 Java**：Adoptium **JRE 17**（43,780,109 B；`openjdk 17.0.20.1`）——本机没有系统 Java。
2. **生成一个真实存档**：官方 **1.20.1 server.jar**（47,791,053 B，**sha1 = `84194a2f286ef7c14ed7ce0090dba59902951553`，与 Mojang 官方值一致**），
   跑一次服务器（flat、peaceful、creative）：
   ```
   [07:56:09] Preparing level "QuickWorld"
   [07:56:10] Preparing start region for dimension minecraft:overworld
   [07:56:11] Preparing spawn area: 83%
   [07:56:12] Time elapsed: 1626 ms
   [07:56:12] Done (2.067s)!
   ```
   世界落盘：`level.dat`（1,406 B）+ `region/*.mca` **4 个**（共 2,186,941 B）。
3. **用产品装 1.20.1**：`POST /api/instances` 建实例 → `POST /api/versions/download {"versionId":"1.20.1"}` 安装
   （**异步任务**，实测 `assets` 落盘 **620.6 MB**）。
4. **把世界放进实例**：`<gameDir>/saves/QuickWorld`（10 个文件，`level.dat` 在）。
5. **让产品"直接进世界"**：实例配置 `customGameArgs = --quickPlaySingleplayer QuickWorld`
   （1.20+ 客户端支持该参数；`javaPath` 指向第 1 步的 JRE 17）。
6. `prelaunch-check` → `ok=true`；`POST /api/instances/{id}/launch` → `{"ok":true,"pid":29820}`。

## 2. 结果（游戏日志原文）

```
命令行含 quickPlaySingleplayer = True
[08:13:58] [Server thread/INFO]: WorldTester joined the game        ← 玩家真的进了世界
[08:13:58] [Render thread/INFO]: Loaded 2 advancements
标记: integratedServer=1 preparingSpawn=2 timeElapsed=1 playerJoined=1 lwjgl=1 atlas=12 崩溃=0 行数=50
判定: PASS：真的进了世界（集成服务器已启动、出生区已准备、玩家已加入）  退出后 totalPlaySeconds=153
```

- **`Starting integrated minecraft server` ×1**（集成服务器起来了）
- **`Preparing spawn area` ×2**、**`Time elapsed:` ×1**（出生区已准备）
- **`WorldTester joined the game` ×1**（**玩家进入世界**——这是"进世界"最直接的证据）
- `Backend library: LWJGL` ×1、**纹理图集 12 个**、**崩溃 0**、JVM 全程存活 150 秒
- 退出后产品结算 `totalPlaySeconds = 153`

## 3. 结论与影响

- **R06 的"进入世界 / 加载存档"由 NOT_RUN → PASS**（真机、真实存档、官方服务端产物、产品自己的启动链路）。
- 顺带证明：产品的 **`CustomGameArgs` 会原样进入命令行**（可复核 `world-entry-cmdline.txt`），
  且**实例级 `javaPath` 生效**（用的是我指定的 JRE 17，而不是产品另下一个）。
- **不含**"加载已有存档的交互流程"（点选存档、切世界等 GUI 操作）——那部分仍属界面交互，本轮验的是"世界能被加载并进入"。

## 4. 我自己的两个 bug（都已修，记下来）

1. **`POST /api/versions/download` 是异步的**（返回 `{"status":"queued"}`）——我没轮询到终态就停启动器，
   把排队中的下载**掐掉了**，随后启动报 `未找到版本 JSON：…\versions\1.20.1\1.20.1.json`。**产品行为正确**。
2. **`GET /api/downloads` 返回的是 `{"tasks":[...]}`（对象，不是数组）**——我按数组取 `id`，永远匹配不到，
   脚本空转到超时。修法：`@($list.body.tasks)`。
   （另有一个小坑：`$jvmPid = 0` 时 `Get-Process -Id 0` 会返回 **Idle 进程**，导致"存活=True"的假象，已改为 `pid -gt 0` 判定。）

## 5. 复现

```powershell
# ① 生成世界（JRE17 + 官方 server.jar，约 2 分钟）
powershell -File ...\run-20261006-1630-world-entry\prepare-world.ps1 -WorkRoot %TEMP%\pg-world -JsonPath <证据>\worldgen.json
#    ⚠️ 该脚本的 stdout 读取会阻塞（服务端一直有输出）；世界生成后 Ctrl-C/杀进程即可，世界文件已完整。
# ② 阶段 2+3：复用已装好 1.20.1 的冷目录，改配置 + 放世界 + 启动 + 观测
powershell -File ...\verify-world-entry-phase23.ps1 -ColdDir <冷目录> -InstanceId <id> `
  -JavaExe %TEMP%\pg-world\jre17\bin\java.exe -WorldDir %TEMP%\pg-world\server1201\QuickWorld `
  -JsonPath <证据>\world-entry.json -ObserveSeconds 150
```

产物：`world-entry.json`（判定/标记/游玩时长）、`world-entry-game.log`（游戏日志 50 行）、
`world-entry-cmdline.txt`（完整命令行，可核对 `--quickPlaySingleplayer`）、`step2.log`（逐步时间线）、
`server-latest.log`（服务端生成世界的原文）、`worldgen.json`（jar sha1 与世界文件校验）。
