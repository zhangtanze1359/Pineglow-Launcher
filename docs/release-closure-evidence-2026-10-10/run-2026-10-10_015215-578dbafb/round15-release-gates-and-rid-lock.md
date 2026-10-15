# 第 15 轮：跑通了两条此前从未在本会话执行过的发布门槛，并把 C 的三个选项全部验证完

运行标识：`run-2026-10-10_015215-578dbafb`（第 15 轮追加）
时间：2026-10-11 14:37–14:45，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（0 staged；所有实验在 `%TEMP%` 隔离副本里完成）。
被验产物：`CA8F94C1A6173F23F38F3D2A3C949AFFC16D5AB67752B662C7806831F350D93E`（53,052,722 字节，与 3 次流水线发布逐字节相同）。

---

## 1. 真实更新链路门槛：**PASS**（`release.yml` 的 "Live update-chain check"）

`release.yml:195-200` 这一步是**发布必过门槛**，之前本会话从未真正跑过（基线只做了只读 HTTP 探测）。本轮用**产品自己的脚本**对着**真实互联网**跑了一遍：

```
& .\scripts\verify-update-live-download.ps1 -ExePath <正本 EXE 的副本> -JsonPath ...\live-update-stageA.json
```

结果：**退出码 0，判定 PASS**（证据 `round15-live-update-gate/`）。

| 断言 | 结果 |
| --- | --- |
| 更新功能处于开启状态 | OK（`enabled=True`） |
| 默认地址链「要么取到清单，要么逐源说明失败原因」 | OK（`gotManifest=True`） |
| 取到清单时来源可辨认 | OK，`manifestSource` = GitHub 备用源，**`usedFallback=True`** |

产品自报的运行数据：

```json
{ "enabled": true, "currentVersion": "1.0.0", "updateAvailable": false, "latestVersion": "1.0.0",
  "manifestSource": "https://github.com/zhangtanze1359/Pineglow-Launcher/releases/latest/download/manifest.json",
  "usedFallback": true }
```

并自述：`默认主源本轮实测: primary-failed（已按设计降级到备用源…）`。

**这说明**：内置默认主源（Gitee）确实拿不到清单，而产品的**降级逻辑是按设计工作的**——如实报告主源失败、切到备用源、拿到清单、来源可辨认。这是「更新通道可用性」这一条的**实测通过**，而不是纸面设计。
（`updateAvailable=false` 是正确的：已发布的备用清单是 1.0.0，而这个 EXE 也是 1.0.0；没有降级误报。）

### 1.1 两个更新端点的直接读数（只读 HTTP，证据 `round15-update-endpoints/`）

| 源 | 位置 | 实测 |
| --- | --- | --- |
| **主源**（内置默认） | `ConfigStore.cs:17` `https://gitee.com/ztz1359/pineglow-launcher/raw/master/update.json` | **404**（`Repository or file not found`） |
| **备用源** | `ConfigStore.cs:30` GitHub `releases/latest/download/manifest.json` | **200**，`version=1.0.0`、`size=43,989,943`、`sha256=6130db19…`；其资产 HEAD 也是 **200** |

**发布流程注记**：`release.yml` 只在配置了 `GITEE_TOKEN` 时才会创建 Gitee release 并改写仓库根的 `update.json`；未配置时脚本按设计跳过、只留一条 warning。
所以**若发版时没有配 token，主源会继续保持 404**（功能上由备用源兜底）。这不是代码缺陷，是**发布环境配置**问题，建议发版前确认。

---

## 2. `build.yml` 跨 RID 发布矩阵：两个非 Windows RID 都 **exit 0**

在隔离副本里跑 `build.yml:141` 的那条命令（只是把 `${{ matrix.rid }}` 换掉）：

| RID | 结果 | publish 目录 |
| --- | --- | --- |
| `linux-x64` | **exit 0**（26 s） | 1 个文件 |
| `osx-x64` | **exit 0**（29 s） | 1 个文件 |

*证据等级*：这是**从 Windows 跨 RID 打包**，**不等价于在 ubuntu-latest / macos-latest runner 上执行**。它能证明 RID 相关的还原与打包（运行时包、本地资源、单文件捆绑）没问题；**不能**证明 Linux/macOS 上的测试宿主行为（`build.yml:100-102` 自己也写明 macOS 上测试宿主加载不了主程序集，所以测试只在 Linux 跑）。
证据：`round15-rid-matrix-and-lockhazard/`。

---

## 3. 一个提交前的**操作陷阱**（本轮实测发现）

普通 RID 发布**会就地改写** `dev/PineglowLauncher/packages.lock.json`：

```
publish linux-x64 / osx-x64 之前：FBBC9AE67B8E8CF2E3AA63C48242BC08AC713BE47830D337914CC39C85EE0672 (8,663 B, 非 RID 形态)
                          之后：18EDC56FF3DF64A6C97331CA5A101FC5E0342250F9394EC42B905C4AC31B6F0C
（测试项目的 packages.lock.json 不受影响）
```

这正是 `build.yml:138-139` 注释里写的既定行为，在 CI 里无害（一次性检出）。
**但在本地是陷阱**：随手跑一次 `dotnet publish -r <rid>`，就会把共享 lock 变成 RID 形态，从而让 CI 里两条 `--locked-mode` 还原失败于 NU1004。

→ **提交那 204 个变更之前，必须先复跑一次 lock 形态守卫。**
本轮已跑（等价于 `build.yml:58-72`）：

| 文件 | 含 `"net10.0/` | 含 ILLink | SHA-256 前 16 位 |
| --- | --- | --- | --- |
| `dev/PineglowLauncher/packages.lock.json` | 否 | 否 | `FBBC9AE67B8E8CF2` |
| `dev/PineglowLauncher.Tests/packages.lock.json` | 否 | 否 | `244BA2BCD1EB1F5A` |

**守卫判定 PASS**（非 RID 形态，CI 守卫会接受）。当前工作树在这条上是「可提交状态」。

---

## 4. C 的三个选项现在**全部**有实测依据（把选项 2 从「未验证」变成「已验证」）

第 10 轮只验证了 `win-x64`；本轮把同一套「命令行固定锁路径」的做法在**三个 RID** 上全套跑完（隔离副本，8 个步骤全部 **exit 0**）：

| 步骤 | 命令要点 | 退出码 |
| --- | --- | --- |
| A | 非 RID 锁定还原，`-p:NuGetLockFilePath=packages.lock.json` | **0** |
| B | 生成 `linux-x64` 锁（`-r linux-x64 -p:PublishSingleFile=true --force-evaluate`） | **0** |
| C | 生成 `osx-x64` 锁（同上） | **0** |
| D | 再跑一次非 RID 锁定还原 | **0** |
| E | `linux-x64` 锁定还原 | **0** |
| F | `osx-x64` 锁定还原 | **0** |
| G | **`publish -r linux-x64 /p:RestoreLockedMode=true`** + linux 锁 | **0** |
| H | **`publish -r osx-x64 /p:RestoreLockedMode=true`** + osx 锁 | **0** |

关键不变量（实测）：

| 文件 | 字节 | 段 | 含 ILLink |
| --- | --- | --- | --- |
| `packages.lock.json`（共享） | 8,663 | 仅 `net10.0` | 否 |
| `packages.linux-x64.lock.json` | 10,870 | `net10.0` + `net10.0/linux-x64` | 是 |
| `packages.osx-x64.lock.json` | 10,868 | `net10.0` + `net10.0/osx-x64` | 是 |

- **共享的 `packages.lock.json` 全程哈希不变**（`FBBC9AE6…`），且始终是非 RID 形态 → 现有两条守卫与 `BuildLockFileTests` 都不需要放松。
- 锁定发布产出：`linux-x64` 单文件 52,576,099 字节、`osx-x64` 单文件 54,941,474 字节，各 1 个文件。

**结论**：`C` 的三个选项现在都有实测支撑——
选项 1（只锁 win-x64）**已验证**；选项 2（三 RID 全锁）**本轮也已验证**，代价只是多提交两份 lock（+ 守卫/测试里加对应断言）；选项 3（接受未锁定）是纯决策项。

---

## 5. 本轮的工具失误（记录）

写实验脚本时把哈希函数命名为 `H`，与本机 PowerShell 的 `Get-History` 别名 `h` **冲突**（别名优先于函数），于是 `H $path` 被解释成 `Get-History -Id <路径>` 而报错；另外 PowerShell 5.1 里 `(if (...) {…} else {…})` 不能直接当表达式传给 `-f`，要用 `$(…)`。

后果：那一次运行的 `baseline/after` 哈希与文件大小没有写进日志。**但结论未受影响**——关键结果（A–H 全部 exit 0）与三个锁文件的形态/字节/哈希是我事后**直接读文件**复核的（见第 4 节表格），不是依赖那个脚本的输出。

---

## 6. 本轮新增的可断言项（汇总）

1. `release.yml` 的「真实默认更新源」门槛：**PASS（exit 0）**，降级按设计工作。
2. 更新主源 404、备用源 200（1.0.0 / 43,989,943 B），产品降级行为已被自己的门槛脚本证明。
3. `build.yml` 跨 RID 发布：`linux-x64`、`osx-x64` **都 exit 0**（跨平台打包层面）。
4. C 的三 RID 锁定方案**全部验证通过**，共享 lock 不变。
5. lock 形态守卫在**当前工作树**上 PASS → 工作树在「提交 204 个变更」这件事上已具备条件（只差授权）。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
