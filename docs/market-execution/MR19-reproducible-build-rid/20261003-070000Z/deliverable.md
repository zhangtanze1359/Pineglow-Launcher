# MR19 · 可复现构建、RID 边界与 CI · 交付报告

- **run-id**：`20261003-070000Z`
- **执行范围**：RID 发布锁定的**冻结决策**（含实验证据）、lock 文件不变量守卫、CI 守卫步骤、托管程序集可复现性实测
- **依据**：`docs/market-task-plan-2026-10-03.md` §7.2/§8/§10、`market-research-task-workflow-2026-10-02.md` §11
- **最终状态**：**证据就绪**（RID 锁定按"接受不锁定"冻结并给出理由；真实 GitHub Actions 运行未验证，见 §六）
- **未执行**：不提交、不推送、不发布

---

## 一、结论先行

| 项 | 结果 |
|---|---|
| 复现「RID 锁定不可用」 | ✅ `dotnet publish -r win-x64 -p:RestoreLockedMode=true` → **NU1004**，并点名 `Microsoft.NET.ILLink.Tasks` |
| 复现「publish 就地改写 lock 文件」 | ✅ 8663 → **10868 字节**，新增 `net10.0/win-x64` 段（8 个包，含 ILLink.Tasks） |
| **新发现：两条路径互斥** | ✅ 展开后的 lock 文件能让 **RID** 锁定还原通过（exit 0），但会让**非 RID** 锁定还原失败（NU1004）。**"提交 RID 形态"不是修复，而是会静默破坏 CI 里的锁定校验** |
| RID 锁定决策 | ✅ **冻结为"RID 路径不做锁定还原"**，理由与实验证据见 §三 —— 这正是当前 CI 取舍正确的原因 |
| 不变量守卫（测试） | ✅ 新增 3 个用例，钉死"提交的 lock 文件必须是非 RID 形态" |
| 不变量守卫（CI） | ✅ `build.yml` 与 `release.yml` 各加一步，**并用 actionlint 1.7.7 校验通过** |
| 守卫双向实跑 | ✅ 提交态通过；把展开版放到同路径 → **被拦**并给出 NU1004 原因 |
| 可复现构建（实测） | ✅ 托管程序集**跨强制全量重建逐字节一致**（`52C17031…`，3580928 bytes） |
| C# 测试 | ✅ 新增 3 个用例 → **3 通过 / 0 失败** |
| C# 全量回归 | ✅ **728 通过 / 0 失败 / 0 跳过**（725 + 3） |
| Node 全量回归 | ✅ **216 通过 / 0 失败**（16 个文件，未变） |

---

## 二、实验记录（可复跑）

### 2.1 复现两个已知事实

```
dotnet restore dev/PineglowLauncher/Pineglow.Launcher.csproj --locked-mode        → exit 0
dotnet publish  dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -r win-x64 \
                --self-contained /p:PublishSingleFile=true /p:RestoreLockedMode=true
  → error NU1004: 项目的运行时标识符已更改。项目的运行时标识符: win-x64，锁定文件的运行时标识符:
  → error NU1004: net10.0 的包引用已更改。…项目的包引用: …, Microsoft.NET.ILLink.Tasks >= 10.0.9
  → exit 1

# 去掉 RestoreLockedMode 后（CI 现状）
  → exit 0，用时 7s；packages.lock.json 8663 → 10868 字节（+2205）
  → 新增段：net10.0/win-x64（8 个包，含 Microsoft.NET.ILLink.Tasks）
```

### 2.2 关键实验：展开后的 lock 文件能让 RID 锁定还原通过吗？

**能**，但代价是另一条路径坏掉：

| 实验 | 结果 |
|---|---|
| ① RID 发布 + `RestoreLockedMode=true`（用展开后的 lock） | **exit 0** ✅ |
| ② 非 RID `--locked-mode`（用展开后的 lock） | **exit 1**，NU1004：`net10.0` 段多出 `Microsoft.NET.ILLink.Tasks:[10.0.9, )` ❌ |
| ③ 测试项目 `--locked-mode`（用展开后的 lock） | **exit 1** ❌ —— 因为它引用主项目，还原会走到主项目的 lock |

**结论**：RID 形态与非 RID 锁定还原**互斥**，因为 RID 发布会把 `Microsoft.NET.ILLink.Tasks`
注入**共享的 `net10.0` 段**，而非 RID 路径不引用它。
因此**当前 CI 的取舍（非 RID 锁定校验 + RID 不锁定）是正确设计**，
计划里的 `KNOWN-UNLOCKED` 应当**冻结为接受项**，而不是"待修复"。

### 2.3 可复现构建实测

```
dotnet build … -c Release                  → Pineglow.Launcher.dll  SHA 52C170310B59C304…  3580928 bytes
dotnet build … -c Release --no-incremental → Pineglow.Launcher.dll  SHA 52C170310B59C304…  3580928 bytes
逐字节一致 = True
```

托管程序集跨强制全量重建**逐字节可复现**。
**未测**：单文件自包含发布的最终 exe 是否可复现（含 ILC/原生输出，需单独研究，见 §六）。

---

## 三、实际改动

| 文件 | 状态 | SHA256 |
|---|---|---|
| `dev/PineglowLauncher.Tests/BuildLockFileTests.cs` | **新增** | 见 `manifest.json` |
| `.github/workflows/build.yml` | 修改（新增守卫步骤） | 见 `manifest.json` |
| `.github/workflows/release.yml` | 修改（新增守卫步骤） | 见 `manifest.json` |
| `scripts/source-snapshot-inputs.txt` | 修改（登记新测试） | 见 `manifest.json` |

**产品源码零改动** —— 本包只碰测试、CI 与输入清单。

### 3.1 守卫的不变量

提交的 `packages.lock.json` 必须是**非 RID 形态**：

1. 不得出现 `"net10.0/` 段；
2. 不得出现 `Microsoft.NET.ILLink.Tasks`；
3. 两个项目都必须保持 `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`。

三处实现：C# 测试（`BuildLockFileTests`，3 例）、`build.yml` 一步（bash）、`release.yml` 一步（pwsh）。
CI 步骤的注释里写明了**为什么**（含实验数字），避免后人"顺手把 lock 文件提交成 RID 形态"。

### 3.2 为什么 CI 步骤要单独存在（而不是靠 NU1004 自己报错）

NU1004 的消息是"包引用已更改"，**不说明是谁改的、为什么**。守卫把它换成一句可执行的诊断。
本地也验证过：把展开版放到同路径 → 守卫**抛出** `含 RID 段：会让非 RID 的 --locked-mode 还原失败（NU1004）`。

---

## 四、工作流 YAML 校验（按 `authoring-github-workflows` 技能）

- 本仓库**没有** actionlint 门禁（技能里假设存在 `.github/workflows/actionlint.yml`，实际不存在）；
- 因此我**自己下载并校验**了 actionlint：
  - 用技能给出的固定 SHA `023070a2…0757` 校验 `actionlint_1.7.7_checksums.txt` 里的 linux 资产 → **匹配**（作为信任锚）；
  - 再用该文件里 windows 资产的 SHA `7f12f180…a731` 校验下载的 zip → **匹配**；
  - `actionlint.exe -version` → `1.7.7`；
- `actionlint -shellcheck= -pyflakes= -color .github/workflows/build.yml .github/workflows/release.yml` → **exit 0**；
- 完整检查（含 shellcheck 风格）同样 **exit 0**；
- 本次改动**没有新增任何 `${{ }}` 表达式**，因此技能里最危险的"未加引号表达式里的 `#` 被当注释"陷阱不适用。

**建议（未实施）**：给仓库加一个 actionlint CI 门禁（技能描述的那种）。这属于 CI 改进，但会新增工作流文件，
本包未做，记在 §六。

---

## 五、本轮自身发现并修正的问题

1. **测试里把项目文件名按目录名拼**：`PineglowLauncher/PineglowLauncher.csproj` 不存在（实际是
   `Pineglow.Launcher.csproj`）。已改为显式映射，避免"目录名 == 项目名"的错误假设。
2. **一个反直觉的连带失败**：lock 文件被展开后，**测试项目**的 `--locked-mode` 也会失败——
   因为它引用主项目，还原会走到主项目的 lock。这写进了 §2.2，因为它决定了"只能二选一"。

---

## 六、明确未做与未验证

| 项 | 说明 |
|---|---|
| 🔴 **真实 GitHub Actions 运行** | 未验证。本机没有 runner；守卫步骤用**本地等价逻辑**双向实跑 + actionlint 校验，但"在 GitHub 上真的会这样跑"未验证。 |
| **单文件自包含 exe 的可复现性** | 未测。托管程序集已实测可复现；自包含单文件包含 ILC/原生输出，需要单独研究（是否逐字节可复现、如何固定时间戳/路径）。 |
| **RID 锁定** | **按决策保持不锁定**（KNOWN-UNLOCKED 冻结为接受项）。若要真正锁定 RID，需要放弃非 RID 锁定校验，或改用每 RID 独立的 lock 文件方案（NuGet 目前不直接支持）。 |
| actionlint CI 门禁 | 未新增（建议见 §四）。 |
| linux-x64 / osx-x64 的 RID 发布 | 未在本机跑（只有 win-x64 实测）。CI 里三个 RID 都会跑，但那属真实 CI 验证。 |
| CI YAML 是否纳入源码快照白名单 | **未纳入**。理由：CI 定义不是构建输入（快照用于复现构建产物），且纳入会扩大漂移面。此决策记录在此，供复验方判断。 |

---

## 七、回退方法

```powershell
# 1) 删除新增测试
Remove-Item 'D:\松光启动器\dev\PineglowLauncher.Tests\BuildLockFileTests.cs'
# 2) 两个 workflow 删掉 "Guard lock file(s) are in non-RID form" 步骤
# 3) 白名单去掉类别 B 的 BuildLockFileTests.cs 行（计数 30→29）
```

**锁文件无需回退**：本包的实验在 `green/launcher-packages.lock.json.rid-expanded` 留有展开版副本，
提交态始终是原始形态（8663 bytes，SHA `FBBC9AE67B8E8CF2…`）。

---

## 八、证据清单

```
red/
  launcher-packages.lock.json.before           实验前提交态（8663 bytes）
  tests-packages.lock.json.before
green/
  launcher-packages.lock.json.rid-expanded     实验产出的 RID 形态（10868 bytes，仅作反例）
  01-lock-guard-first.log                      守卫测试 3 通过 / 0 失败
  02-csharp-full.log                           728 通过 / 0 失败
  03-node-full.log                             216 通过 / 0 失败
  git-status-after.txt
  changed-files.sha256.txt
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**

---

## 七、补记（第 26 轮）：单文件缺口已关

本报告 §六 把"**单文件自包含 exe 的可复现性**"标为未测。第 26 轮用
`scripts/measure-singlefile-repro.ps1` **关掉了这一项（在本机同路径下）**：

- 两次**完全独立**的发布（两次之间**整体删除** `bin/` 与 `obj/`，不是"增量 vs 全量"）；
- 单文件 exe **逐字节一致**：`89,490,941 bytes`，SHA256 `C99101C8672D7861B6FEF2F29E20F144350771DD9E92F1ABB4DD8987BBA0EC13`；
- 全部 **18 个文件**的清单与内容均一致；
- RID 发布改写 `packages.lock.json`（8663 → 10868 bytes）的副作用**已逐字节还原**，守卫复核 3/0、全量 755/0、Node 230/0。

**仍未测**：跨**路径**（单文件自包含的经典风险就是嵌入路径）、跨机器、跨天。
因此"可复现构建"这一行可以写"**托管程序集 + 单文件自包含 exe（同机同路径）**"，
但**不得**写成"全链可复现"或"跨环境可复现"。

详见 `docs/market-execution/MR19-singlefile-reproducibility/20261003-150000Z/deliverable.md`。

---

## 八、补记（第 29 轮）：**头条结论必须收窄为"同一路径"**

本报告 §2.3 的头条结论是"**托管程序集跨强制全量重建逐字节一致**"。
但它的两次构建（普通 vs `--no-incremental`）**都在同一路径** —— 因此它只证明了"同路径"。

第 29 轮做了跨路径检验（`MR19-managed-assembly-pathcheck/20261003-180000Z`）：
**三个长度完全相同（98 字符）的路径、各清 bin/obj 独立构建**：

| 路径 | 盘 | DLL SHA256 |
|---|---|---|
| `…\pgA` | C: | `E79EA742…121A` |
| `…\pgB`（与上只差一个字符） | C: | `4F369572…D428` |
| `D:\ddd…d` | **D:** | `5F4D75AC…13D5` |

**三者互不相同** → **托管程序集同样嵌入了路径内容**。

**关键对照（排除"时间"混淆）**：在同一路径上、相隔约 **2 分钟**再构建一次 →
DLL 与 exe **都与首次完全相同**。因此差异**只能归因于路径**，不是时间。

**因此本报告 §一 的这一行必须改写**：
- ❌ "可复现构建（实测）：托管程序集跨强制全量重建逐字节一致"
- ✅ "**在同一路径下**，托管程序集与单文件自包含 exe 逐字节可复现；**跨路径不可复现**"

"给定源码 → 给定哈希"的可核对发布，对**托管程序集与单文件 exe 都必须在固定路径下构建**。
