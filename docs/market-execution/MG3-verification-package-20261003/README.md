# MG3 独立复验包 · Windows 候选（无障碍 / 构建 / 更新 / 性能）

- **范围**：MR17 / MR19 / MR20 / MR21
- **证据根**：`C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution\`
- **源码根**：`D:\松光启动器`
- **本包状态**：**证据就绪，等待独立复验**
- **生成方式**：由 `scripts/verify-mg3.ps1` 从各包 `manifest.json` 与 `green/changed-files.sha256.txt` **实际读出**

---

## 一、一条命令跑完全部自动检查

本机只有 Windows PowerShell 5.1（没有 `pwsh`）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-mg3.ps1
```

它会：汇总四个包的 manifest（含 MR21 的 `productCodeChanged` 只读声明）→ **跨全部 run 的证据漂移检查**
→ 重跑 C# 与 Node 全量 → 打印复验边界。

本次实测输出（完整日志 `verify-mg3-full.log`）：

```
扫描到 114 条记录，覆盖 15 个 run
一致=54  本里程碑内无法解释的漂移=1  被更晚里程碑覆盖的历史记录=59  文件缺失=0
已通过! - 失败: 0，通过: 752，已跳过: 0, 总计: 752
ℹ tests 230   ℹ pass 230   ℹ fail 0
C# exit=0  Node exit=0
```

### 那条"无法解释的漂移"是什么（已解释）

| 项 | 值 |
|---|---|
| 路径 | `scripts/source-snapshot-inputs.txt` |
| 最新记录来自 | `MR17-accessibility-keyboard/20261003-100000Z`（属本里程碑） |
| 原因 | **本复验包自己造成的**：生成 MG3 包时把 `scripts/verify-mg3.ps1` 登记进白名单"类别 G"（6→7） |
| 影响 | 产品源码（类别 A/B/F）**无变化**；白名单是快照构建的输入清单，不参与编译 |

**这是"共享索引被反复编辑"的固有现象**，不是实现缺陷：每新增一个可复现性工具就要登记一次，
因此它总会与"上一个记录它的包"不一致。MG2 的复验包里有同样一条（成因相同），
已在 `MG2-verification-package-20261003/drift-notes.md` 详细记录。
本包**没有**通过"改脚本让它不报"来消除漂移。

---

## 二、MG3 风险清单 → 证据映射（计划 §八 列的五条风险）

| 计划里的风险 | 本里程碑如何防住 | 复验命令 / 位置 |
|---|---|---|
| **更新不能回退** | MR20 把"直接覆盖 exe"改成 **备份 → 替换 → 逐字节校验 → 失败回滚**，三条失败路径都仍启动旧版本；并有 **3 个真实执行替换脚本**的用例 | `dotnet test --filter "FullyQualifiedName~UpdateRecoveryTests"`；`MR20…/green/02-update-recovery-after.log` |
| **平台身份错误** | MR19 冻结 RID 边界（RID 形态与非 RID 锁定还原**互斥**，附实验）；MR20 增加**平台资产匹配**（资产名含别的平台记号 → 下载前拒绝） | `MR19…/green/…`、`MR20…/green/01…`；用例 `AssetCompatibility_RejectsForeignPlatform` |
| **构建测试失败** | MR19 的 lock 文件**不变量守卫**（C# 测试 3 例 + `build.yml`/`release.yml` 各一步，用 actionlint 1.7.7 校验）；MR19 实测托管程序集**逐字节可复现** | `powershell -File .\scripts\verify-mg3.ps1`；`MR19…/deliverable.md` §2.3 |
| **关键 UI 不可用** | MR17 修掉六处无障碍缺口（**焦点可见**、弹窗键盘契约、`role=button` 可激活、live region、粘底操作区、24×24 目标尺寸）；MR21 冻结了服务层性能基线 | `node --test ui/tests/a11y-keyboard.test.cjs`；`MR17…/deliverable.md` §二 |
| **发布信任边界未决** | MR20 明确写出**信任链声明**（SHA256 只能发现损坏/截断，**不提供发布者身份认证**，同源可同时篡改）并回给界面；MR19 把 RID 锁定**冻结为接受项**（含理由与反例实验） | `MR20…/deliverable.md` §一；`MR19…/deliverable.md` §2.2 |

---

## 三、四个包各自声称什么

| 包 | 聚焦测试 | 全量（当时） | 关键声明 |
|---|---|---|---|
| MR17 无障碍 | 9 通过 / 0 失败（RED：9 失败 / 0 通过） | C# 752、Node 230（18 文件） | **真实键盘/屏幕阅读器/缩放未验证** |
| MR19 构建与 RID | 3 通过 / 0 失败 | C# 728、Node 216（16 文件） | RID 锁定冻结为"接受不锁定"；托管程序集逐字节可复现；**真实 CI 运行未验证** |
| MR20 自更新恢复 | 19 通过 / 0 失败（RED：6 失败 / 13 通过） | C# 747、Node 221（17 文件） | 含 **3 个真实执行替换脚本**；**真实安装目录替换与真实重启未做** |
| MR21 性能基线 | 测量夹具（无性能断言） | C# 752、Node 221（17 文件） | **`productCodeChanged = False`**；**没有任何优化**；只测服务层热路径 |

---

## 四、复验方必须独立判断的事

1. **实现是否真的满足验收**：脚本不读代码，请按 `verification-checklist.md` 逐条抽查。
2. **RED 是否真实**：MG3 各包的 RED 都是"逐块还原本轮改动"构造的（不删文件，否则只是编译/语法错误）。
   每个包 `deliverable.md` 写明了构造方式。
3. **真实环境行为**（本仓库环境**无法**覆盖，务必逐条确认"未验证"是否可接受）：
   - MR17：真实键盘操作、屏幕阅读器宣读、125%/150%/200% 缩放、800×600、文字裁切、**焦点色对比度**；
   - MR19：真实 GitHub Actions 运行、单文件自包含 exe 的逐字节可复现性、linux/osx RID 发布；
   - MR20：真实安装目录替换、真实重启、Unix `.sh` 分支；
   - MR21：真实 GUI 首屏、WebView2 渲染、真实账号流程、**WebView2 缺失路径**（本机已安装）。
4. **MR21 的结论边界**：它**只测量**。基线数字**不可跨机器比较**；"没有做任何优化"是刻意的
   （卡片要求瓶颈修复走独立子包授权）。候选修复的收益被量化（p50 −80%）但**未进产品代码**。

---

## 五、"通过"怎么记录

计划 §八：**"通过"必须分别记录 自动验证结果、独立复验结果、用户是否批准**。

- 自动验证：见 §一；
- 独立复验：**尚未进行**（本包是它的输入）；
- 用户批准：**尚未给出**。

执行者最多能写"证据就绪"；**历史放行不覆盖新 diff**。本包不宣布阶段放行、不提交、不发布。

---

## 六、本包文件

| 文件 | 用途 |
|---|---|
| `README.md` | 本文件 |
| `verification-checklist.md` | 风险映射 + 逐包验收项 → 命令 → 期望 → 证据 |
| `verify-mg3-drift-check.log` | 只做漂移检查的日志 |
| `verify-mg3-full.log` | 含两套全量测试的完整日志 |
| `manifest.json` | 机器可读索引 |
| `scripts/verify-mg3.ps1` | 复验脚本（UTF-8 BOM，PowerShell 5.1 可直接跑） |

> **证据布局说明**：本包生成时发现 MR17/MR21 的 `changed-files.sha256.txt` 写在了 run 根目录，
> 与 MR03–MR20 的 `green/` 约定不一致；已**按既有约定归位**（MR21 的 run 原本没有 `green/`，已新建）。
> 归位后漂移检查才看得到它们的记录 —— 这正是"证据布局本身也要被检查"的一个实例。
