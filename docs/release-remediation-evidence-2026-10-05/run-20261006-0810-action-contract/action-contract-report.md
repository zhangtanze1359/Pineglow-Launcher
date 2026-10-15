# action 契约校验：输入名合法、运行时存在、哪些前所未跑（第 34 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
工具：`check-action-contracts.py`（本目录，可复跑）。结论：**`ACTION-CONTRACT RESULT=PASS`**（15 个 `uses` 步骤 / 5 个 action / 硬失败 0）。

## 1. 为什么做这一步

1. **actionlint 不校验 `with:` 里的输入名** —— 第 32 轮的**反向对照**里我故意写了 `bogus-input: x`，
   actionlint 一声不吭（`runs.using`/输入 schema 都在它视野之外）。这是它已知的盲区。
2. 本轮候选**新增/改写了 CI 步骤**（证据产物上传、`needs: verify` 互锁、独立冷启动、真实更新链、镜像状态落盘），
   这些**从未在真 runner 上跑过**；一个拼错的输入名会让**首次推送直接变红**。
3. 所以用**每个 action 自己的 `action.yml`** 当权威，把"输入名对不对、必填齐不齐、运行时是否已淘汰"变成可复跑的判定。

## 2. 方法（刻意**零 `api.github.com` 调用**）

匿名 API 配额只有 **60 次/小时**，所以数据源全部避开它：

| 需要的事实 | 来源 |
| --- | --- |
| 引用 → 提交 SHA | `git ls-remote https://github.com/<owner>/<repo> <ref>`（含 `^{}` 剥离带注解的 tag）|
| action 的输入清单与运行时 | `https://raw.githubusercontent.com/<owner>/<repo>/<sha>/action.yml` |
| Node 版本是否存在 | `https://nodejs.org/dist/index.json` |
| SDK 版本是否存在 | dotnetcli `releases-index.json` → 对应 channel 的 `releases.json` |

运行用 DSH 内置 python（自带 **PyYAML 6.0.3**；系统 python 3.12 **没有** yaml，别用错解释器）。

## 3. 结果：5 个 action 全部通过

| action | 解析到的提交 | `runs.using` | 声明输入数 | 本仓库用到的 `with` | 判定 |
| --- | --- | --- | --- | --- | --- |
| `actions/checkout@v4` | `11d5960a326750d5…` | node20 | 21 | （无）| ✅ |
| `actions/setup-dotnet@v4` | `67a3573c9a986a3f…` | node20 | 8 | `global-json-file` | ✅ |
| `actions/setup-node@v4` | `49933ea5288caeca…` | node20 | 12 | `node-version` | ✅ |
| `actions/upload-artifact@v4` | `ea165f8d65b6e75b…` | node20 | 7 | `name`, `path`, `if-no-files-found` | ✅ |
| `softprops/action-gh-release@v2` | `3bb12739c298aeb8…` | node20 | 19 | `files` | ✅ |

- **没有未声明的输入**（`unknownInputs = []`）、**没有缺必填**（`missingRequired = []`）；
- 5 个 action 都是 `node20` 运行时，**没有** `node12/node16/docker` 这类已被淘汰的声明。

### 3.1 运行时版本**真实存在**（这条以前没人核过）

| 项目 | 值 | 是否存在于官方源 |
| --- | --- | --- |
| `setup-node` 的 `node-version` | `24.19.0` | ✅ 在 `nodejs.org/dist/index.json` 里 |
| `global.json` 的 SDK | `10.0.301`（`rollForward: latestPatch`）| ✅ 在 dotnet release-metadata 的 **10.0** channel 里 |

⇒ CI 会装到与本机**完全一致**的 Node 与 SDK（本机 `node -v` = v24.19.0、`dotnet --version` = 10.0.301）。
以前这里只是"顺带一致"，现在有官方索引为证。

## 4. 顺带查清的"到底哪里没跑过"（比之前的说法更准）

对比 v1.0.0 / HEAD（**已提交**）与工作区（**候选**）里各步骤的 `uses` + `with`：

| 事实 | 说明 |
| --- | --- |
| v1.0.0 真 runner 上**跑过** | `checkout@v4`、`setup-dotnet@v4`（`dotnet-version: '10.0.x'`）、`upload-artifact@v4`（`name`/`path`）、`action-gh-release@v2`（`files`）|
| 工作区**改了契约** | `setup-dotnet@v4` 的 `dotnet-version: '10.0.x'` → **`global-json-file: global.json`**（更确定：钉到 10.0.301）；`upload-artifact@v4` 新增 `if-no-files-found` 键 |
| **该仓库从未跑过** | **`actions/setup-node@v4` 整步**；以及所有新增/改写的步骤正文（证据上传、独立冷启动、真实更新链、镜像落盘、`needs: verify` 互锁）|

⇒ 之前写的"本轮新增/改写的步骤从未在真 runner 上跑过"**成立**；
但"这些 action 本身没跑过"**不成立**（四个在 v1.0.0 就用了）。这个区分现在有证据。

## 5. 一条产品决定（新）：是否升级 action 大版本 / 按 SHA 钉死

当前钉的是浮动大版本 `@v4` / `@v2`，**仍然存在且可用**（v1.0.0 就是用它发布成功的）。
但实测它们已经落后：

| action | 现钉 | 仓库已有最新大版本 |
| --- | --- | --- |
| `actions/checkout` | v4 | **v7** |
| `actions/setup-dotnet` | v4 | **v6** |
| `actions/setup-node` | v4 | **v7** |
| `actions/upload-artifact` | v4 | **v7** |
| `softprops/action-gh-release` | v2 | **v3** |

- 风险不是"现在会红"，而是**未来某天旧大版本被下线**（`upload-artifact@v3` 就是这样被硬停的），
  以及**浮动 tag 可被上游改写**（供应链面）。
- 选项：① 保持现状（首次推送时天然验证）；② 升级到最新大版本；③ **按本脚本记录的 SHA 钉死**（最稳，但每次升级要手动改）。
- **代价提示**：任何一项都会改工作流字节 ⇒ 冻结哈希失效 ⇒ 必须重跑 actionlint + 本脚本 + 全量验证链。
  所以它属于**产品决定**，我没有擅自改。

## 6. 边界（不证明什么）

1. 静态校验 ≠ runner 行为：不覆盖 `actions/*` 的真实实现、runner 镜像内容、`secrets`/`permissions`、跨步骤状态；
2. `runs.using: node20` 只是**声明值**，不代表 runner 已装好对应 Node；
3. 不校验 `uses` 的**安全性**（是否值得信任的发布者），只看契约；
4. 浮动大版本会在上游更新时**静默改变**行为。

## 7. 复现命令

```powershell
# 用 DSH 内置 python（自带 PyYAML）
& "C:\Users\Administrator\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe" `
  docs\release-remediation-evidence-2026-10-05\run-20261006-0810-action-contract\check-action-contracts.py `
  --repo D:\松光启动器 `
  --json docs\release-remediation-evidence-2026-10-05\run-20261006-0810-action-contract\action-contract.json
# 期望：ACTION-CONTRACT RESULT=PASS steps=15 actions=5 findings=0（退出码 0）
```

产物：`action-contract.json`（含解析到的 SHA、每个 action 的完整 inputs、每个步骤的输入对照）、
`action-contract.log`（原始终端输出）。
