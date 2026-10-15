# MR21 补测 · 启动与环境边界（缺口登记表 A12）——**并修掉一个静默降级缺陷**

- **run-id**：`20261003-290000Z`
- **性质**：把 MR21 的"端口冲突/目录不可写未测量"变成**实测 + 守卫**；并**修掉一个真实缺陷**
- **依据**：`MR21-install-launch-perf-baseline/20261003-090000Z/deliverable.md` §六：
  "目录不可写 / 端口冲突 | 未在本包重新测量"、"WebView2 缺失时的降级文案 | 未观测"
- **最终状态**：**证据就绪**（4 个用例；1 个 RED 成立；**产品代码修复 1 处**；**另修掉一个既有 flaky 测试**）

---

## 一、实测结论（回答 MR21 的三项）

### 1. 端口被占用：**不崩，但只给通用提示** ✅/⚠️

| 观察 | 结果 |
|---|---|
| `LocalApiService.Start(busyPort)` | **抛出 `HttpListenerException`**（"另一个程序正在使用此文件，进程无法访问。"）—— 服务本身**没有**兜底 |
| 谁兜住了它 | **`App.cs:31-39`**：`try { LocalApiService.Start(); } catch (Exception ex) { LauncherLog.Error(...); Console.Error.WriteLine(...); }` → **应用不会崩** |
| 失败后的状态 | `IsRunning == false` ✅（没有半启动状态） |
| 失败后能否重启 | **能**：换空闲端口 `Start(freePort)` 成功，`/api/health → 200` ✅ |
| 用户看到什么 | ⚠️ **只有通用的 "API unavailable"**：外壳窗口探健康检查失败后走 catch，**静默加载静态 `UI/index.html`**（或 `about:blank`）。**没有"端口被占用"这类可操作提示。** |

### 2. UI 根目录缺失：**按设计回退，仍能启动** ✅

`ResolveUiRoot()` 依次尝试 `PINEGLOW_UI_DIR` → 可执行目录旁 `UI/` → 配置目录下 `UI/` → 兜底返回可执行目录旁路径。
实测：把 `PINEGLOW_UI_DIR` 指向**不存在的目录**，服务**仍正常启动** ✅（回退是设计意图）。

### 3. WebView2 缺失：**仍未验证** 🔴

本机 WebView2 已安装（`154.0.4258.48`），**无法观测缺失路径**。
代码层面可说的只有：外壳窗口的兜底 `catch` 会**吞掉一切异常**并退回静态文件/`about:blank`；
**没有**针对"WebView2 运行时缺失"的专门检测或文案。因此"缺失时用户看到什么"**仍未知**。

---

## 二、**修掉一个真实缺陷：端口被覆盖时界面会静默降级**

### 缺陷

| 事实 | 位置 |
|---|---|
| `DefaultPort = 48123`，且 `PINEGLOW_API_PORT` **可覆盖**端口 | `LocalApiService.cs:24`、`:169` |
| 外壳窗口**硬编码** `http://127.0.0.1:48123/api/health` 与 `http://127.0.0.1:48123/` | `WebShellWindow.axaml.cs:190`、`:195` |

**后果**：一旦用 `PINEGLOW_API_PORT` 覆盖端口，健康检查会**探错端口** → 抛 `InvalidOperationException("API unavailable")`
→ 走进 `catch` → **静默**加载静态 `UI/index.html`。界面看起来"能用"，但**完全没有 API**（搜索/实例/更新全部失效），
且**没有任何错误提示**。前端 `ui/js` 本身用的是相对 URL ✅，所以问题**只在这两行硬编码**。

### 修复（+ 一个缺失的 using）

```csharp
// A19 修复（MR21 边界）：端口**必须**取自 LocalApiService.Port。
int apiPort = LocalApiService.Port;
...
using var response = await client.GetAsync($"http://127.0.0.1:{apiPort}/api/health");
...
Web.Source = new Uri($"http://127.0.0.1:{apiPort}/" + cacheBuster);
```

同时补上 `using Pineglow.Launcher.Services;`（首次修复后编译报 `CS0103` —— 见 §五 教训）。

---

## 三、4 个用例

| # | 用例 | 断言 |
|---|---|---|
| ① | `Start_OnBusyPort_Throws_AndLeavesTheServiceNotRunning` | 占用端口上 `Start` **必须抛出**，且 `IsRunning == false`（不得半启动） |
| ② | `Start_AfterBusyPortFailure_SucceedsOnAFreePort` | 失败后换空闲端口**必须启动成功**，且 `/api/health` **真的返回 200**（不只是标志位为真） |
| ③ | `Start_WithNonExistentUiDir_StillStarts` | `PINEGLOW_UI_DIR` 指向不存在目录时仍能启动（并在 finally 还原环境变量） |
| ④ | `ShellWindow_MustUseTheConfiguredPort_NotAHardCodedOne` | **静态守卫**：外壳窗口不得出现 `127.0.0.1:48123`，且必须引用 `LocalApiService.Port` |

**④ 就是修复前的 RED**（见 §四）。

---

## 四、RED → GREEN

| | ① | ② | ③ | ④ 静态守卫 |
|---|---|---|---|---|
| **修复前（RED）** | ✅ 通过 | ✅ 通过 | ✅ 通过 | ❌ **失败**（列出两行硬编码） |
| **修复后（GREEN）** | ✅ | ✅ | ✅ | ✅ |

RED 输出（`red/01-startup-before-fix.log`）：
`外壳窗口**不得硬编码端口**（应为 LocalApiService.Port）：…GetAsync("http://127.0.0.1:48123/api/health")…`

---

## 五、本轮自找并修掉的三个问题

### 5.1 我自己的修复第一次**编译不过**（`CS0103: LocalApiService` 不存在）

`WebShellWindow.axaml.cs` 里没有 `using Pineglow.Launcher.Services;`。**补 using 后**启动器项目才构建通过。
（**这次我先检查了 `dotnet build` 的退出码**，所以立刻发现；否则会拿旧二进制继续跑。）

### 5.2 一个**既有 flaky 测试**被全量抓出（**不是**我引入的）

全量第一次运行报 **1 失败**：`NewsLinkHandlerTests.NewsOpen_ValidToken_TamperedId_ReturnsBusinessLevelNotFound`
—— 断言"篡改 linkId 必须返回业务 400/404"，却拿到 `status=200 body={"ok":true}`。

**它落在安全相关的断言上，所以我先定性再动手**：
- 单独跑该类：**16/16 通过**；全量重跑：**801/0 通过** → 间歇性。
- 读用例发现构造方式是 `tampered = last + 中间 + first`（**交换首尾字符**）：
  **当 `first == last` 时，篡改后的 id 与真实 id 完全相同** → 请求合法成功 → 假失败。
- linkId 是注入的生成器（生产为 32 位 hex Guid）→ 首尾相同的概率约 **1/16 ≈ 6%**，与"偶发一次"吻合。

**结论：这是测试自身的 bug，不是产品/安全缺陷。** 已修正为"保证篡改一定产生不同的 id"，并加 `Assert.NotEqual(realId, tampered)` 兜住这一点。
修正后该类**连跑 5 次全过**。

### 5.3 教训汇总（本会话重复出现的三类）

1. **改完必须显式 `dotnet build` 并检查退出码**（本轮靠它发现了 5.1；第 36 轮因吞掉输出而得到过无效证据）。
2. **中文文案里的直引号在 C#/PowerShell 里都是语法字符**（本轮 C# 又踩一次，改用「」）。
3. **"篡改/变异"类测试必须断言"变异确实生效"**（5.2 就是缺了这条）。

---

## 六、验证

| 检查 | 结果 |
|---|---|
| 本组用例 | **4 通过 / 0 失败** |
| `NewsLinkHandlerTests` 连跑 5 次 | **5/5 通过**（修正 flaky 后） |
| C# 全量 | **801 通过 / 0 失败 / 0 跳过**（797 + 4） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| **启动器项目单独构建** | **exit 0**（修复所在项目） |

---

## 七、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher/Pineglow.Launcher.Views/Shell/WebShellWindow.axaml.cs` | **修复**：端口改用 `LocalApiService.Port`；补 `using Pineglow.Launcher.Services;` |
| `dev/PineglowLauncher.Tests/NewsLinkHandlerTests.cs` | **修复 flaky**：篡改构造保证产生不同 id + `Assert.NotEqual` |
| `dev/PineglowLauncher.Tests/LauncherStartupBoundaryTests.cs` | **新增**（4 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 40→41） |
| `docs/market-execution/gap-register.md` | A12 标记为**已关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR21 行补记 |

---

## 八、仍未覆盖

| 项 | 说明 |
|---|---|
| **WebView2 缺失路径** | 本机已安装，**未观测**（见 §一.3） |
| **真实桌面会话下的渲染** | 未验证；本轮只做**服务层 + 静态**检查 |
| **目录不可写（游戏目录只读）** | 本轮**没有**测这一项（MR21 与 A12 都列了；本轮聚焦端口与 UI 根目录） |
| **"端口被占用"的可操作提示** | **未做**：目前仍是通用的 "API unavailable" —— 属产品交互决策，未擅自改 |
| 真实启动/登录/游戏 | 未验证 |

---

## 九、证据清单

```
green/01-startup-after-fix.log      修复后首次运行（此时构建其实失败 → 见 §5.1）
green/02-startup-final.log          真正构建后 4 通过 / 0 失败
green/03-csharp-full.log            第一次全量（1 失败 = 既有 flaky 测试）
green/04-node-full.log              230 通过 / 0 失败
green/05-news-alone.log             NewsLinkHandlerTests 单独跑 16/16（证明是间歇性）
green/06-csharp-full-retry.log      全量重跑 801/0（未复现）
green/07-csharp-full-final.log      修正 flaky 后 801 通过 / 0 失败
red/WebShellWindow.axaml.cs.before      修复前备份
red/01-startup-before-fix.log       RED：静态守卫失败（两行硬编码端口）
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
