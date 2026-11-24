# Gitee 源码同步记录 · 2026-11-24

## 结论

Gitee 仓库的 `master` 已同步到 GitHub `main@16939f8`，**整树覆盖**。
更新主源 `update.json` 未受影响，端到端验证通过。

## 同步前发现的事实（与最初判断不同）

原本以为「Gitee master 停在 2026-08-11 的 `c8aa286f65`，只是没同步」。

**实际情况更复杂**：Gitee 是一条**独立的历史线**，不是 GitHub 的旧快照：

```text
bedc11f756  release: v1.0.6 (OPEN-07 修源)     ← 我提交 update.json 时产生
c8aa286f65  feat: 本机 Gitee 镜像脚本 mirror-gitee.mjs
f61f44f83a  ci: Gitee 镜像失败不阻断发布 + 上传快速失败
2f251f17da  ci: Gitee 附件上传改用 curl + 重试
236e4ab94a  ci: 修复 Gitee owner 硬编码
988dac5610  ci: 修复 release PublishDir 路径不一致
e4329c69db  ci: 修复 release.yml 无效工作流
```

这些 CI 修复提交在 GitHub 上**不存在**。因此不能 fast-forward，必须整树覆盖。

## 一个必须避开的坑

**`update.json` 原先只存在于 Gitee**（通过 contents API 提交），GitHub 仓库里没有。

如果直接整树覆盖，**`update.json` 会被删掉**，更新主源立刻回到 404——OPEN-07 白修。

处置：先把 Gitee 上的 `update.json` 取下来，作为 `update.json` 纳入 GitHub 仓库（提交 `16939f8`），
确认同步后它仍在。`release.yml` 的镜像步骤仍通过 Gitee contents API 单独写入，**不依赖这个本地文件**。

## 执行

| 步骤 | 结果 |
| --- | --- |
| 添加 Gitee remote（token 内联 URL） | 成功 |
| `git fetch gitee` | 成功；分叉 `gitee/master` 独有 1、本地独有 195 |
| 确认 Gitee 独有的 `update.json` 内容与本地一致 | ✅ `git diff` 为空 |
| `git push gitee HEAD:master --force` | ✅ `bedc11f...16939f8 (forced update)` |
| 删除 Gitee remote | ✅ |
| 扫描 `.git` 下 token 残留 | ✅ **0 命中** |

## 验证

| 项 | 结果 |
| --- | --- |
| Gitee `master` SHA | **`16939f8ca5`**，与本地 HEAD 一致 |
| `update.json` 仍在 | **HTTP 200** |
| 新源码到位 | `CurseForgeKeyDefaults.cs` 无 Key 字面量，含 `ReadFromAssemblyMetadata` |
| 更新链端到端 | `version=1.0.6`、`sha256=43750657…`、`url` 指向 gitee.com、notes 1540 字 —— **与同步前一致** |

## 安全处置

- Token 只内联在 `remote add` 的 URL 里，**未写入任何持久配置文件**；
  推送后 `git remote remove gitee` 清除。
- 事后 `grep -rl <token> .git/` **零命中**。
- 临时目录（含下载产物）已删除。

## 遗留（如实记录）

1. **Gitee 上的旧提交仍在 Git 历史中**（含旧的 `CurseForgeKeyDefaults.cs`，带完整 Key）。
   与本项目已记录的「Key 存在于历史快照」一致，**不是新增泄露**。
   若要使其失效，只能由持有人做服务端吊销/轮换。
2. **Gitee 原有的那串 CI 修复提交脱离当前树**（`f61f44f`…`c8aa286`）。
   它们仍在 Git 对象库里可查，但当前 `master` 不再由它们演进而来。
3. **本次是手工同步一次**。此后 GitHub 的新提交需要再次推送到 Gitee——
   **目前没有自动化**。若需要持续镜像，得另加工作流。
