# 交接 · OPEN-02 旧下载源迁移执行（2026-11-22，v4）

按 [任务计划](../任务计划-2026-11-22.md) 第三节在**获授权恢复的隔离工作树**内完成。v1/v2/v3 分别被 Codex 判 FAIL（生产凭据误删、旧凭据被移出主配置、待转存标志范围过宽）；v4 已修复并获**第四轮独立复验 PASS**，按授权在本地 `main` 合入（不推送、不打 tag）。`FormalPhaseApproval=NOT_GRANTED`，发布审批另行。

## 状态

- 基线：`main@274675f0aaf686833239811adc39684d5b02743c`。
- 执行工作树：`D:/Projects/_worktrees/pineglow-open02-20261122`（HEAD 同基线，改动**未提交**，保留现场）。
- 三轮 FAIL 交接与探针原样存于 `verify-codex-fail/`、`verify-codex-fail-2/`、`verify-codex-fail-3/`；第 4 轮 PASS 原件存于 `verify-codex-pass/`。
- v4 最终候选：[`22-candidate-final-v4.patch`](../evidence/2026-11-22-open02/22-candidate-final-v4.patch)（5 个生产文件，+76/−6）。
- v4 最终测试：[`22-test-final-v4.cs`](../evidence/2026-11-22-open02/22-test-final-v4.cs)（17 个用例）。
- v4 本地结果：定向 17/17；回归 79/79；清单通过；四道门槛全绿（C# 1502/0/0，前端 508/0/0，CSS token 49，资源闭包通过）。
- **Codex 第 4 轮 PASS**：RED 2/12、GREEN 17/17、回归 79/79、清单与四道门槛 exit 0；三轮探针均通过；输入与五个生产文件哈希一致。
- OPEN-02 技术验收通过；本轮按授权在本地 `main` 合入，销项与发布审批由编排方确认。

## 三轮阻断与 v4 修复

1. **第 1 轮**：`ConfigStore.LoadAsync` 凭据读取“一个失败、全部清空”；v1 迁移走全量 `Save` → `SaveCore` 把空凭据写回 `SecretStore`（空值 = 删除），误删无关 CurseForge 凭据。
2. **第 2 轮**：旧 JSON 明文 `CurseForgeApiKey` 转存失败后，v2 的整包重写因 `[JsonIgnore]` 把旧字段移出主配置（只剩 `.bak`），无法自动重试。
3. **第 3 轮**：`SecretMigrationPending` 的置位条件覆盖整个迁移保存异常；缺少账户列表时 `EnsureAccounts` 的账户补全也进入全量保存，该保存因无关的 `AiApiKey` 删除失败后标志被误置位，下载源迁移被误挡在磁盘之外。

**v4 修复**：在 `ConfigStore.LoadAsync` 中新增局部 `legacySecretMigration`，只在三类**旧 JSON 明文凭据**进入待转存状态时置位：

- 旧 AI 配置迁移（`AiProviderUrl` / `AiModel`）；
- 从 JSON 恢复 `MicrosoftRefreshToken`；
- 从 JSON 恢复 `CurseForgeApiKey`。

只有 `legacySecretMigration` 成立且 `SaveAsync` 失败时，才设置 `AppConfig.SecretMigrationPending`；账户补全（`EnsureAccounts`/`flag2`）与 SecretStore 间的账户 token 迁移不再触发保护。`SaveConfigOnlyAsync` 的跳过逻辑保留。

新增回归：

- `ProductionStore_AccountOnlySaveFailure_DoesNotBlockMigration`：空账户列表 + 无关 AI 删除故障，断言 `SecretMigrationPending == false` 且迁移照常写盘。
- `State_AccountOnlySaveFailure_StillPersistsDownloadSource`：同一场景的真实 HTTP 入口，首次 `/api/state` 后磁盘即为 `bmclapi`。

## 证据索引

全部原始日志与补丁位于 [`docs/evidence/2026-11-22-open02/`](../evidence/2026-11-22-open02/README.md)。v4 追加：

| 文件 | 内容 | 结果 |
| --- | --- | --- |
| `19-fix3-targeted.txt` | v4 定向 | 17/17，exit 0 |
| `20-fix3-regression.txt` | v4 回归 | 79/79，exit 0 |
| `21-fix3-gates.txt` | v4 四道门槛 | 全 PASS（C# 1502），exit 0 |
| `22-candidate-final-v4.patch` / `22-test-final-v4.cs` | v4 候选 | Codex 第 4 轮 PASS |
| 三轮 FAIL 原件 | `verify-codex-fail/`、`verify-codex-fail-2/`、`verify-codex-fail-3/` | FAIL |
| 第 4 轮 PASS 原件 | `verify-codex-pass/` | PASS |

## 第四轮独立复验结果

```bash
git worktree add D:/Projects/_worktrees/pineglow-open02-verify4-20261122 274675f0aaf686833239811adc39684d5b02743c
cd D:/Projects/_worktrees/pineglow-open02-verify4-20261122
git apply "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/22-candidate-final-v4.patch"
cp "D:/Projects/松光启动器/docs/evidence/2026-11-22-open02/22-test-final-v4.cs" dev/PineglowLauncher.Tests/DownloadSourceHttpMigrationTests.cs
export PATH="/c/Windows/System32:/c/Windows:$PATH"
export TEMP="D:/Projects/_worktrees/pineglow-open02-verify4-20261122/.cache/tmp"
export TMP="$TEMP"; mkdir -p "$TEMP"
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release \
  --filter "FullyQualifiedName~DownloadSourceHttpMigrationTests"
```

**结果：Codex 第 4 轮 PASS。** 它在独立工作树实测 RED 2/12、GREEN 17/17、回归 79/79、清单与四道门槛 exit 0；三轮生产探针均通过；输入与五个生产文件哈希一致。PASS 原件见 `verify-codex-pass/`。回归、清单与四道门槛命令见 [证据 README](../evidence/2026-11-22-open02/README.md#复跑命令)。

## 生产探针的第四轮口径

1. **第 1 轮探针（已入库凭据）**：四种场景保留合成 CurseForge 凭据，请求窗口内凭据后端写入/删除均为 0；文件锁场景 HTTP 200、本次生效 `bmclapi`、磁盘保留 `mcbbs`；全量 `Save` 反证删除 1 次。
2. **第 2 轮探针（旧 JSON 明文凭据）**：首次请求 `activeConfigContainsKey=True`、`keyInBackend=False`、HTTP 生效 `bmclapi`、磁盘保持 `mcbbs`；恢复后端后第二次请求 `secondKeyConfigured=True`、密钥入库、磁盘变 `bmclapi`。
3. **第 3 轮补充探针（账户补全故障）**：期望改为
   - 直接加载：`legacyCredentials=False; secretMigrationPending=False; rejectedDeletes=1; generatedAccounts=1`；
   - 首次 `/api/state`：`HTTP=200; responseSource=bmclapi; diskSource=bmclapi; unchangedBytes=False`（迁移不再被误挡）；
   - 第二次请求保持 `bmclapi`，不再触发无关保存。
4. **代码审查**：`legacySecretMigration` 是否只在这三处旧 JSON 明文凭据分支置位；`EnsureAccounts`/`flag2` 与 SecretStore 间账户 token 迁移是否不再置位；正常路径 `ConfigOnlySaveCount == 1`、`SaveCount == 0`；三条历史回归是否仍通过。可选变异：把 `legacySecretMigration` 改为无条件置位，确认第 3 轮两个新用例变红。

## 待编排方决定

1. 独立复验已完成（Codex 第 4 轮 PASS）；本轮按授权在本地 `main` 合入。
2. 是否接受 `IConfigStore.SaveConfigOnly` 接口扩展与 `AppConfig.SecretMigrationPending` 运行期标志。
3. 是否接受 `Ordinal`（大小写落盘规范化）相对发布线 `OrdinalIgnoreCase` 的语义差异。
4. OPEN-02 销项与发布审批；合入已执行，未推送、未打 tag。
5. 若计划推进 v1.0.6，再安排真实宿主、生产 `.bak`/`.prev` 与原生凭据后端验证。

RESULT: PASS
