# M0 阶段闭环报告

> 日期：2026-11-06 | 状态：**M0-1 / M0-2 / M0-3 / M0-5 完成；M0-4 完成改造，runner 复测待推送；M0-6 部分待确认**

## 一、M0-4 已按方案 B 完成改造

### 问题回顾

`main` 上最近三次 Build 均 failure，Windows / macOS 作业 success，只有 Ubuntu 卡在
第 11 步 `Run tests`（`dotnet test`）。根因是 5 个用例硬编码 Windows 专有命令：

| 文件 | 位置 | 原写法 |
|---|---|---|
| `InstanceRunningGuardTests.cs` | 3 处 | `Process.Start(new ProcessStartInfo("cmd.exe", "/c exit"))` |
| `InstanceLaunchGuardTests.cs` | `DeadPid()` | 同上 |
| `FileRecoveryEdgeCaseTests.cs` | `TryCreateJunction` | `cmd.exe /c mklink /J` + `Assert.True(created, ...)` 硬断言 |

Linux 上没有 `cmd.exe`，`Process.Start` 抛 `Win32Exception`（找不到文件），**不是返回非零退出码**，
所以整个作业被一个找不到的可执行文件拖红。

### 改造内容（方案 B：跨平台替身，不是加平台守卫）

新增 `dev/PineglowLauncher.Tests/CrossPlatformTestProcess.cs`：

| 平台 | 实现 |
|---|---|
| Windows | `cmd.exe /c exit`（原写法不变） |
| Unix | `/bin/sh -c "exit 0"`（`ArgumentList` 避免手工引号） |

两者都返回「真实 PID + 已退出」的进程对象，语义完全一致。调用方断言**一字未改**。

`FileRecoveryEdgeCaseTests.TryCreateJunction` → `TryCreateDirectoryLink` 按平台分流：

| 平台 | 策略 |
|---|---|
| Windows | 优先 `mklink /J`（免特权）；失败退回 `Directory.CreateSymbolicLink` |
| Unix | 直接 `Directory.CreateSymbolicLink`（普通用户即可，.NET 映射为 `FileAttributes.ReparsePoint`，与 Windows 判定口径一致） |

### 为什么不用 `if (!OperatingSystem.IsWindows()) return;`

这是关键取舍，必须写明：**xunit 2.9.3 没有动态 skip**（`Assert.Skip` 是 v3 才引入）。
静默 `return` 等于把「跳过」藏进绿色里 —— 测试显示通过，实际什么都没验。
那 5 个用例里包含「拒绝隔离重解析点」「陈旧条目自愈」这类安全语义，
把它们在开发主力平台之外永久跳过，比失败更危险。换替身能真正保住三平台覆盖。

### 本地验证

| 项 | 结果 |
|---|---|
| 编译 | 0 错误（19 个既有 xUnit analyzer warning，与本次无关） |
| 三项测试类聚焦跑 | **19 / 19 通过** |
| .NET 全量 | **967 / 967 通过**，0 跳过（1m14s） |
| 目录链接实际走的分支 | Windows 走 `mklink /J` 成功（`创建目录链接 = True ... 平台 Win32NT`） |

## 二、M0-1 遗留的两处测试夹具缺陷已修，前端恢复 380/380

之前记录的 3 个前端失败，逐个查到根因，**都不是产品缺陷，但都在掩盖问题**，已全部修掉：

### 缺陷 1：CV-08 沙箱漏注入 `clearTimeout`

`ui/tests/current-version-errors.test.cjs` 的 `launchHarness` 用 `vm.createContext(ctx)`
执行 `launcher.js` 切片，ctx 注入了 document / STATE / SG.api / console，**却漏了 `clearTimeout`**。
新增的 `setPlayLaunching` 调它，于是 `ReferenceError: clearTimeout is not defined`。

产品行为本身正确（fail-closed，`launches` 计数为 0，这正是断言想验证的）。补上
`setTimeout, clearTimeout, setInterval, clearInterval` 后 26/26 通过，断言一字未改。

### 缺陷 2：CL-05 中文路径下 `fs.cpSync` 另建乱码目录

`scripts/tests/ui-resource-closure.test.cjs` 的 `copyUi` 直接
`fs.cpSync(src, target, { recursive: true })`，让 cpSync 自己创建目标目录。

**实测证据**（本机 Node v22.22.2 + Windows 26200，做过对照实验）：

| 目标路径 | `cpSync` 返回值 | 目标目录文件数 | 实际落盘 |
|---|---|---|---|
| `.../plain/css` | 不报错 | **3**（正确） | `plain/css/` |
| `.../松光 测试/css` | 不报错 | **0** | 另建 `鏉惧厜 娴嬭瘯/css/` |

即：祖先目录含非 ASCII 时，cpSync 用 GBK 解码后的乱码名另起一个目录，**返回值不报错**，
目标目录里 0 个文件 → CL-05 的「资源闭合完整」断言必然失败，失败原因完全掩盖在 `code !== 0` 里。

已在**干净基线 `30f7b56e`** 上复现，确认与本次改动无关。改为先 `mkdirSync` 再逐文件递归复制。
**检查器本身未动** —— `check-ui-resource-closure.cjs` 单独在中文路径下 exit=0，是清白的。

### 效果

| 项 | 修前 | 修后 |
|---|---|---|
| 前端清单测试 | 374 项：371 通过 / **3 失败** | **380 项：380 通过 / 0 失败** |
| 数量变化原因 | 崩溃的 `ui-resource-closure.test.cjs` 没跑出其 7 个子测试 | 现已全部计入 |

## 三、M0 全部事项状态

| 事项 | 状态 | 结论 |
|---|---|---|
| M0-1 资产保全 | ✅ 完成 | 备份 4.467GB / 7931 文件哈希全一致；隔离分支 5 组提交；**发现 10 个改动里 7 个早已随 v1.0.3 入库** |
| M0-2 文档安全对齐 | ✅ 完成 | 11 项安全机制逐条映射到源码；视图 9→10、服务 67→85、模型 49→52；MR01 分叉合回并降级 |
| M0-3 发布阻塞闭环 | ✅ 完成 | Java 扫描 124990.9ms → 321.3ms（约 389 倍）；.NET 967/967 |
| M0-4 Ubuntu 失败 | ⚠️ 改造完成，**runner 复测待推送** | 5 个用例改跨平台替身；本地 967/967 + 380/380，Linux 无法本地验证 |
| M0-5 版本口径 | ✅ 完成 | 远端 main=`30f7b56e`，Release 仅 v1.0.3 / v1.0.0；csproj `<Version>` 是唯一来源 |
| M0-6 计划基线 | ⚠️ 部分 | 排期已确认「按建议窗口」；**资料库上传授权、客户可见版审定人仍待确认** |

## 四、分支与提交

### `remediation/v1.1.0`（`D:/pineglow-v110-remediation`，基线 `30f7b56e`，7 个提交）

```
7212793 test: 修掉测试夹具的两处缺陷，前端恢复 380/380（M0-1 遗留）
98f2a26 test: 用跨平台替身进程替换硬编码的执行环境依赖（M0-4）
4505c2b docs: 项目深度解读与整改发布计划（2026-11-05）
d4abd7c docs: 迁入 v1.1.0 计划、启动参数重构方案与发布阻断诊断
e72f4cf docs(ui): 动效预览页 preview-motion.html
8ecd563 feat(ui): Play 与下载按钮的形变加载动效
d4e18d2 feat(ui): 导航图标重绘为圆角描边风格
```

跟踪文件 338（基线 331 + 7），`git status` 干净，无二进制误入。

### `docs/remediation-20261106`（`D:/pineglow-docs-remediation`，基线 `47e939c`，3 个提交）

```
cd51192 docs: 发布阻塞根因闭环与本地复测（M0-3）
a60d89f docs: Ubuntu 测试失败定位（M0-4），纠正一处过时结论
d7f67d6 docs: 安全描述与代码对齐，计数按实测口径重算（M0-2）
```

## 五、未做与阻塞

| 项 | 状态 | 原因 |
|---|---|---|
| 推送两个分支到远端 | **未做** | 网络层 `schannel: server closed abruptly`，且推送属对外动作，需明确授权 |
| M0-4 runner 复测 | **未做** | 依赖推送；且 Ubuntu 日志需仓库 admin 权限（403） |
| Linux 本地验证 | **不可能** | 本机 WSL 被安全策略禁用（Program Blacklist）、无 Docker |
| 资料库上传 | **未做** | 属项目 Drive 写入，按规则需先展示内容并取得明确确认 |
| 客户可见版 | **未出** | 需先确认审定人 |
| v1.1.0 版本号写入 csproj | **未做** | 属发布动作，需明确授权 |

## 六、一条本地环境陷阱（与产品无关，但会误导后来人）

Git Bash 下 `PATH` 含 `/usr/bin` 时，`find` 会解析到 **GNU find** 而非
`C:\Windows\System32\find.exe`。替换脚本用 `tasklist /FI ... | find /I ...`，
于是 `tasklist` 的参数被 GNU find 当路径、返回非零，`goto ready` 提前跳出等待循环，
进程仍占用目标文件 → `UpdateApplyScriptPathTests.ApplyScript_WaitsForRunningTargetThenReplaces`
报 `replace_failed`、waited=0.5s。

**解法**：`export PATH="/c/Windows/System32:/c/Windows:$PATH"` 后再 `dotnet test`，
967/967 全通过。GitHub Actions 的 Windows runner 默认 shell 不受影响。
