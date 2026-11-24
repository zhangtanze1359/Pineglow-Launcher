# v1.0.7 候选准备 · 2026-11-24

## 目的

**本版的主要目的是更换内置 CurseForge Key**，不是功能更新。

背景：旧 Key 以明文形式存在于公开仓库的 17 个历史提交与已发布的 v1.0.4/5/6 二进制中
（见 [`../2026-11-24-key-audit/README.md`](../2026-11-24-key-audit/README.md)）。
GitHub Secret `PINEGLOW_CURSEFORGE_BUILD_KEY` 已更新为新 Key（`2026-11-24T05:09:31Z`）。

## 版本信息

| 项 | 值 |
| --- | --- |
| 版本 | **1.0.7** |
| 基线 | v1.0.6 |
| 对比提交 | `7582701` |
| 发布说明 | `release-notes/v1.0.7.md` |

## 相对 v1.0.6 的用户可见变化

**只有两项**，都不是功能更新：

1. **内置 CurseForge Key 更换** —— 改用构建期注入，新 Key。
   用户无感：CurseForge 照常可用，不需要填 Key。
2. **新增诊断参数 `--print-curseforge-key-status`** —— 供发布流程自检，
   只输出 `present`/`absent`，不启动界面、不输出 Key。普通用户不会用到。

> 其余 16 个提交全是文档、证据、CI 配置变更，用户不可见。

## 验证

在隔离工作树 `D:/Projects/_worktrees/pg-v107` 用**新 Key**构建并验证：

| 项 | 结果 |
| --- | --- |
| 构建期注入 | `[curseforge-key] 已注入（长度 60）` |
| 单文件嵌入 UI 资源 | **26 项** |
| 产物 | `Pineglow.Launcher.exe`（51,688,754 bytes） |
| **诊断参数** | 退出码 **0**，输出 `curseforge-key=present`（用 `Start-Process -Wait`，与发布门槛一致） |
| **程序集版本** | **1.0.7**，与 tag 预期一致 |
| 四道门 | **全 PASS**（C# 86.1s / inventory 38.1s / css-tokens / closure） |
| 全量 C# | **1505 通过 / 0 失败 / 0 跳过** |
| 前端 | **508 通过** |
| 基线 | 已刷新（`node scripts/gates.cjs --baseline`），`docs/baseline.md` 指向 `7582701` |
| 一致性检查 | `csproj=1.0.7 · 说明=v1.0.7.md · tag=v1.0.6` → **OK**（tag 待打，属正常） |

## 新 Key 的产品入口验证（在 pg-rot 环境完成）

| 步骤 | 结果 |
| --- | --- |
| 搜索 | `GET /api/content/search?source=curseforge` → 24 条 |
| 安装 | `POST /api/content/install` → `{"ok":true,"status":"queued"}` |
| 真实落盘 | `mezz_config-26.3-neoforge-0.7.1-standalone.jar`（347,265 bytes） |
| 零配置 | `config.json` **不含** `CurseForgeApiKey` |

> 该次验证的 EXE 是 `3DF0C127…A326`（v1.0.7 前的构建）。
> OPEN-08 的原始记录（EXE `51E1492D…DCB79`，旧 Key 时代）**已保留未改**。

## 发布前还需做的事

1. **打 tag `v1.0.7`** —— 触发 `release.yml` 自动发布（需编排方授权）
2. 发布后核对：
   - `dist/mirror-status.json` 的 `attachmentUploaded` / `updateJsonUpdated` 是否为 `true`
   - Gitee release 是否创建成功
   - `update.json` 是否更新到 1.0.7

## 已知遗留

1. **旧 Key 在 CurseForge 侧仍然有效**（三轮间隔测试均 200）。
   `Regenerate` 在 CurseForge 是「新增」不是「替换」，旧 Key 需单独删除——
   编排方正在核查后台是否有删除入口。
2. **尚未用新 Key 发布任何版本**。本版是第一次。
3. **旧版本用户不受影响**：v1.0.4/5/6 内置旧 Key，只要它仍有效就能继续用。
   但这意味着**旧 Key 的暴露面不会因为发版而消失**。
