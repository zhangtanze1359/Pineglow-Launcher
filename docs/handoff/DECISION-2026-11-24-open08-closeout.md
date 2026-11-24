# OPEN-08 销项决定 · 2026-11-24

## 决定

**OPEN-08 销项。** 依据：`docs/open-items.md` 销项条件「Key 状态已确定并落地，守卫测试能阻止再次逾期，对外说明与实际一致」，由编排方（用户）于 2026-11-24 作出签定。

## ⚠️ 必须如实记录的一点

**独立复核没有给出 PASS。**

OPEN-08 共派 **3 轮**复核，**三轮全部 FAIL**。本次销项是**编排方在 3 轮上限后直接接受**，不是「经独立复核确认」。

引用本记录时**不得表述为「经独立复核通过」**。准确说法是：

> 三轮复核累计指出 5 个缺陷，全部已修复并实测；编排方在用量上限后裁定接受。

## 三轮复核指出的 5 个缺陷与修复

| 轮次 | 缺陷 | 根因 | 修复与验证 |
| --- | --- | --- | --- |
| 1 | 发布门槛扫压缩单文件 EXE 外层明文，主程序集被压进包内 → **永远失败** | 没意识到 `EnableCompressionInSingleFile=true` 会压缩主程序集 | 改为验最终产物（最终版见第 3 轮） |
| 1 | Windows 守卫把 `obj` 的 SDK 生成物误报为源码字面量 | `path.Contains("/bin/")` 只匹配正斜杠 | 按路径段判断 |
| 2 | **错配 EXE 被放行**（带值 DLL + 无值 EXE） | 门槛读中间 DLL，不校验与 EXE 的对应关系 | 门槛改为调用最终 EXE 自检 |
| 2 | **纯空白 Key 被放行**（运行期 Trim 后为空） | 只查键名存在，不看值 | EXE 用 `ProvidedAtBuildTime`（已 Trim）判定 |
| 3 | **PowerShell 对 WinExe 不等待**，拿不到 stdout 与 `$LASTEXITCODE` | 产品是 `OutputType=WinExe`（GUI 子系统） | `Start-Process -NoNewWindow -Wait -PassThru` + 重定向到文件 |
| 3 | 守卫按任意路径段排除 `bin`/`obj`，**放过源码里的 `binary/` 等** | 排除范围过宽 | 只认「`bin`/`obj` 后紧跟 `Release`/`Debug`/`net*`」这种构建输出形状 |
| 3 | 验收项 1 的旧证据不合格（Python 直调、未绑定候选） | 验的不是产品入口 | 改为产品自身 API + 绑定 EXE SHA-256 |

> 第 3 轮列出的是 3 条（含验收证据），合计三轮 7 条。其中 5 条属技术缺陷。

## 已落地的实现

| 项 | 做法 | 文件 |
| --- | --- | --- |
| Key 来源 | 构建机环境变量 `PINEGLOW_CURSEFORGE_BUILD_KEY`（GitHub Secret 已配置） | `.github/workflows/*.yml` |
| 注入方式 | MSBuild `AssemblyMetadata`，由 SDK `GenerateAssemblyInfo` 生成到 `obj/` | `Pineglow.Launcher.csproj` |
| 读取 | `Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()` | `CurseForgeKeyDefaults.cs` |
| 解析顺序 | 调用方传入 → 环境变量 → 构建期注入值（**未变**） | `ModContentService.cs` |
| 诊断入口 | `--print-curseforge-key-status` → `present/absent`，不启动 GUI，不输出值 | `Program.cs` |
| 发布门槛 | 调用最终 EXE 自检；五情形正确 | `release.yml` |
| 守卫 | 源码树零字面量；扫 `.cs` + 构建配置输入（含 `.yml`）；构建输出按形状排除 | `SecretHygieneTests.cs` |

## 验证结果（全部可复核）

| 项 | 结果 | 证据 |
| --- | --- | --- |
| 门槛五情形（PowerShell 同构） | 合法值 PASS / 无值 FAIL / 纯空白 FAIL / 错配 FAIL / 无 EXE FAIL | [`gate-v4-five-cases.txt`](evidence/2026-11-24-open08/raw/gate-v4-five-cases.txt) |
| 守卫 · 真实 `obj` 不误报 | 6/6 通过 | — |
| 守卫 · `binary/` 探针 | **被拦住**（1 失败，退出 1） | — |
| 全量 C# 测试 | **1504 通过 / 0 失败 / 0 跳过** | [`full-tests-v4.txt`](evidence/2026-11-24-open08/raw/full-tests-v4.txt) |
| **产品入口真实下载** | 候选 EXE `SHA-256 51E1492D…DCB79`；搜索 24 条；安装落盘 jar 347,265 bytes；**config 不含 Key** | [`product-entry-curseforge.txt`](evidence/2026-11-24-open08/raw/product-entry-curseforge.txt) |
| 授权来源 | CurseForge 官方邮件，2026-07-15 批准 "Pineglow Launcher" 的 3rd Party API | [`AUTHORIZATION.md`](evidence/2026-11-24-open08/AUTHORIZATION.md) |

## 接受的剩余风险（如实记录）

1. **Key 仍存在于 Git 历史与已发布产物中。**
   - `CurseForgeKeyDefaults.cs` 的 **6 个历史版本中 5 个**含完整字面量；
   - 复核方扩大口径（全仓库历史）报告 **82 个匹配快照**；
   - 已发布的 **v1.0.6 tag 与 GitHub Release 附件**（SHA-256 `43750657…61cd`）仍含旧 Key。
   - **编排方判定「未泄露、不轮换」。** 准确含义是「**未观察到未经授权使用的证据**」，
     不是「无法被提取」。若日后要使其失效，只能由持有人到 CurseForge 后台吊销/轮换——
     **历史重写做不到这一点**。

2. **v1.0.6 的发布包未重发**，仍含旧内嵌 Key。本项修复只影响后续构建。

3. **CI 里测试步骤与发布步骤的 Key 状态不同**：`dotnet test` 不读该 secret，只有 Publish 配了 `env`。
   已验证这不影响任何测试（`SecretHygieneTests` 只断言行解析自洽，Key 为空也成立）。

4. **诊断入口依赖 PowerShell 的显式等待**：若日后有人改成 `& $exe ...` 写法，门槛会静默失效。
   已在 `release.yml` 就地写明原因，但没有自动化守卫阻止这种退化。

5. **独立复核始终未给 PASS**（见本文开头）。

## 明确未做的事

- **未轮换 Key**（编排方决定）。
- **未从 Git 历史清除 Key**（重写历史无法使已分发副本失效，且代价大）。
- **未重发 v1.0.6**。
- 本轮销项**不代表发布获准**；`FormalPhaseApproval=NOT_GRANTED`。

## 遗留

- **OPEN-07**（Gitee 主源 404）仍在清单中，与本项无关。
- 若日后决定轮换 Key，需同时更新 GitHub Secret 并重新发布。
