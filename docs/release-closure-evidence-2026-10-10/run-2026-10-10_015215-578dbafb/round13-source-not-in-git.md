# 第 13 轮：交付阻断的量化 —— 恢复出来的源码**根本不在 git 里**

运行标识：`run-2026-10-10_015215-578dbafb`（第 13 轮；含第 13B 轮更正）
时间：2026-10-11 14:30–14:45，Asia/Hong_Kong（+08:00）
本轮性质：**只做验证与取证，产品目录零改动**（0 staged；未提交、未推送、未改 `.gitignore`）。

> **本文件经过一次自我更正。** 第 13 轮初稿写了两条「CI 会在某步失败」的推断，随后我**在隔离的原生 HEAD 检出里真的把那些步骤跑了一遍**，发现推断是错的，已按实测重写（见第 4 节）。保留这次更正记录，因为它比结论本身更能说明证据等级。

---

## 1. 一句话

**HEAD 里装的是 8 月那份产品，不是我们验证过的 10 月版本。**
工作树 298 个文件（`dev`+`ui`+`scripts`+`.github`，排除生成物）里，**163 个 git 里根本没有**（dev 104 / ui 28 / scripts 30 / 根 `global.json` 1），另有 41 个已跟踪文件被修改。
所以要发布「我们验证过的那个产品」，必须先把这 **204 个文件变更**提交进去——这不是走过场，而是**唯一能把被验对象搬到版本库里的动作**。

---

## 2. 硬数字（全部实测）

| 项 | 值 |
| --- | --- |
| 原生 HEAD 检出（`git archive HEAD`）文件总数 | **139** |
| 工作树文件数（`dev`+`ui`+`scripts`+`.github`，排除 `bin`/`obj`/`TestResults`/`.vs`/`node_modules`） | **298** |
| **工作树里有、git 里没有的** | **162**，另加根目录 `global.json` = **163** |
| 其中 `dev/` / `ui/` / `scripts/` / `.github/` | **104 / 28 / 30 / 0** |
| 已跟踪但被修改的文件 | **41** |
| 上述 163 个文件里被 `.gitignore` 挡住的 | **0**（逐个 `git check-ignore` 验证；已用已知被忽略路径做过工具自检） |
| `dev/` 下 `.cs`：HEAD vs 工作树 | **119 vs 212 → 缺 93 个 C# 源文件** |
| 需要的一次性提交规模 | **新增 163 + 修改 41 = 204 个文件变更** |

---

## 3. git 里缺的是什么

### 3.1 产品与测试源码 —— `dev/` 缺 104 个

- 服务层：`UiBundle.cs`、`SecretStoreBackend.cs`、`ModBisectService.cs`、`AiDiagnoseService.cs`、`MinecraftNewsService.cs`、`FileRecoveryService.cs`、`RunningInstanceRegistry.cs`、`PathSafetyGuard.cs`、`SkinImageProxyService.cs` …
- 模型层：`AiProviderProfile.cs`、`DiagnosticReport.cs`、`FileRecoveryRecord.cs`
- 测试层：**约 70 个 `*.cs` 测试文件**（C# 那 941 个用例的绝大部分）
- `dev/PineglowLauncher/VerifyHost/`（CI 有一步专门构建它）
- `dev/PineglowLauncher.Tests/TestAssets/`
- **两份 `packages.lock.json`**（同时也在离线快照白名单里——这正是「离线流水线能 PASS、远程 CI 一次没跑过」的原因）

### 3.2 `ui/` 缺 28 个

| 缺的文件 | 后果 |
| --- | --- |
| `ui/vendor/three.min.js`、`ui/vendor/THREE-LICENSE.txt` | 3D 皮肤预览的运行依赖；`R02` UI 资源闭包检查会报缺失 |
| `ui/css/pineglow.css`、`ui/js/skin3d.js`、`ui/js/skin3d-core.js` | 界面样式与皮肤脚本 |
| `ui/tests/**`（21 个 `*.test.cjs`） | **Node 305 个用例的绝大部分** |
| `ui/assets/pine-forest.svg` | 运行时封面素材（`.gitignore` 专门为它留了 `!ui/assets/pine-forest.svg`） |

### 3.3 `scripts/` 缺 30 个 —— 连 CI 要调用的脚本本身都不在

`check-ui-resource-closure.cjs`、`run-inventory-tests.cjs`、`test-inventory.txt`、`scripts/tests/`、`scripts/lib/`、
`verify-single-file-standalone.ps1`、`verify-update-live-download.ps1`（这两个是 release 作业的必过门槛）、`verify-source-snapshot.ps1`、`source-snapshot-inputs.txt` …

### 3.4 `.github/` 缺 0 —— 但工作流自身是**被修改**的

`.github/workflows/build.yml` 与 `release.yml` 都在「已修改未提交」里（合计 **+358 行**）。
HEAD 上它们是 **8 月的旧版**，没有任何锁定还原、没有 lock 形态守卫、没有 `global-json-file`、没有 Node 闭包/清单门槛；
**今天这套严格 CI 只存在于工作树里，还没进版本库。**

---

## 4. 在隔离的原生 HEAD 检出里**实跑**了 CI 的判定步骤（含更正）

在 `%TEMP%\pineglow-headcheck-4c3e82792d20455ab3cdda59b5cb44cc`（`git archive HEAD` 解出，139 个文件）逐条执行：

| 步骤 | 实测结果 | 与初稿推断是否一致 |
| --- | --- | --- |
| `dotnet restore dev/PineglowLauncher/Pineglow.Launcher.csproj --locked-mode` | **exit 0** | ❌ **初稿写错了** |
| 同上，但用「工作树的 csproj（含 `RestorePackagesWithLockFile=true`）+ 删掉两份 lock」 | **exit 0，且它自己把 `packages.lock.json` 重新生成了（8663 字节，与现有文件同大小）** | ❌ **初稿写错了** |
| lock 形态守卫（October build.yml 的 `Get-Content $f -Raw`） | **抛 `ItemNotFoundException`** | ✅ 会失败 |
| `node scripts/check-ui-resource-closure.cjs --root ui --json …` | **exit 1**（模块不存在） | ✅ 会失败 |
| `node scripts/run-inventory-tests.cjs --check` | **exit 1**（模块不存在） | ✅ 会失败 |
| `dev/PineglowLauncher/VerifyHost/VerifyHost.csproj` 是否存在 | **不存在**（构建步骤无标可建） | ✅ 会失败 |

**更正结论**：`--locked-mode` **不会**因为「lock 文件缺失」而失败——NuGet 会直接生成一份。
所以「CI 必然红」不能建立在「锁定还原失败」上；它能成立的依据是**另四条**（守卫步骤、两个 Node 门槛、VerifyHost 构建）——它们只在**10 月版工作流**里存在。

因此准确表述是：

1. **推 HEAD 本身**：CI 跑的是 8 月旧工作流，产出 8 月旧产品——**不是我们验证过的东西**。
2. **只提交 10 月工作流（或只提交已修改的跟踪文件）而不带那 163 个未跟踪文件**：CI 会在上面那四条上失败。
3. **一次性提交全部 204 个变更**：才是把「被验对象」搬进版本库的**唯一自洽做法**。

---

## 5. 这一条与前面各轮结论的关系

- 它不是新出现的产品缺陷，而是把「距离发布」的真实瓶颈讲清楚：**发布链的入口（版本库）里没有这个版本**。
- 它解释了为什么离线快照流水线（`verify-source-snapshot.ps1`）能一路 PASS，而远程 CI 一次都没跑过：
  **快照流水线的输入口径 = tracked + 白名单**，恰好把 CI 缺的文件补上了；**CI 的输入口径 = 纯 `git checkout`，只有 139 个文件**。
  → 这也让第 1 轮复验的 P1-A「候选输入闭包」缺陷有了更完整的危害描述：当时漏的是 11 个文件，而 git 里实际缺 163 个。
- 修复 A 时补的白名单只覆盖「让离线快照完整」，**不解决 git 追踪问题**（那属于提交动作，需要授权）。

---

## 6. 本轮的工具失误（记录用）

1. 第一次用 `git archive ... | tar -xf -` —— PowerShell 管道破坏二进制流，`tar` 报 `Damaged tar archive` 并解出 0 个文件。改用 `git archive --output=<文件>` 再解才有效。
2. 在统计「哪些库是 Fabric 的」时，我对**完整路径**做了 `-match 'fabric'`，而实例目录名本身就叫 `d7-fabric 导入`，于是 75 个原版库全被误判。应改按相对路径过滤。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
