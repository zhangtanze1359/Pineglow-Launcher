# DeepSeek Harness 验收后修复流程（2026-10-03）

状态：仅交接规划，未执行修复；本次验收请求不自动授权实施、提交或发布。

## 目标与事实基线

恢复 10 条路由的 W1 已独立技术通过，W2 的 16 项及全套 C# 628、Node 184、Bisect smoke 10 项通过。新增 15 项边界检查中 13 通过、2 失败：worldB 备份列表含 worldA、worldB 恢复请求导入 worldA 副本。根因在 handler 忽略 folderName，属既有业务缺陷。manifest 唯一不匹配项为它自身的旧记录。

源码根为 `D:\松光启动器`；证据根为 `C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution`。不得在 ed41 的旧生产文件上实现后宣称 D 盘已修复。现有 git status 1290 行只是本次基线，实施前必须重读。当前 LocalApiService SHA：`F9F662A85922C08AFF7715B1F3C0179729FD1FC46401474D2EC816108DD3FF77`。

本次证据与可复用探针：`docs/market-acceptance/20261003-W1-W3/`。保留所有旧日志，新增修复 run 目录；不要覆盖 `20261002-182304Z` 原证据。

## R0：重新封口证据（独立包）

1. 重算旧 manifest 26 项，确认其他 25 项仍匹配；若不匹配项增加，暂停修复并查明文件变动归属。
2. 在新 run 的 evidence-correction 子目录新增 corrected-manifest.json，记录旧 manifest 原 SHA、修正理由、修正 UTC、各原证据的相对位置 / SHA / 大小。不要把 corrected-manifest 自身纳入 files。
3. 写单独 corrected-manifest.sha256。先写完 manifest 再算这个 detached digest；不回填自身内容。
4. 新增逐项校验结果；文件缺失、SHA 或大小不符必须返回非零退出码。不能仅看最后一条成功。
5. 完成标准：25 个原外部证据全部匹配；修正版 manifest detached digest 验证成功；原目录所有旧文件字节保持不变。

## R1：建立并收紧存档对象契约（同 R2 修复包）

产品语义沿用已有界面：一个存档弹窗仅展示该存档备份；恢复接口的 folderName 与备份归属必须一致；恢复继续创建安全的新副本，保持原存档内容，不引入原位覆盖。

允许的最小源文件集合：

- `dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs`：只改备份列表 / 恢复两个 handler；保留 W1 路由精确匹配与令牌逻辑。
- `dev/PineglowLauncher/Pineglow.Launcher.Services/SaveBackupService.cs`：仅在需要稳定共享对象归属判定时使用；不得顺手改 ZIP 解压策略、恢复覆盖策略、备份删除或其他服务。
- `dev/PineglowLauncher.Tests/SaveBackupApiContractTests.cs`：新增真实 HTTP 契约测试，复用现有 LocalApiTestHarness。
- `scripts/source-snapshot-inputs.txt`：仅加入新增测试精确路径并同步类别计数。
- `docs/api-reference.md`、`docs/market-task-plan-2026-10-03.md`：同步契约、限制及证据入口，修复后只能标记“具备独立复验条件”。

实施步骤：

1. 读取协作规范，记录 HEAD、分支、git 状态、白名单文件 SHA；保存允许改动文件的改前副本。不得从 Git HEAD 覆盖脏文件。
2. 按 tracked 当前版本 + 明确未跟踪源码白名单制作独立快照，逐项哈希核对。先在快照中增加测试和修复，避免真实配置和真实世界落盘。
3. 把本次两个失败探针转为稳定的正式 HTTP 测试。先运行 RED，保留 worldB 返回 worldA 和跨存档恢复 200 的完整响应、退出码 1、TRX。
4. 构造备份选择的稳定归属规则：以服务产出的备份分组 / 规范相对路径判定，验证所属 instance 和 world；不能仅按备份文件名、字符串前缀或客户端 folderName 拼接任意绝对路径。
5. 列表只返回匹配的备份。恢复必须在同一个受约束集合里选择 exact backupName；错对象 / 不存在备份应给稳定的受控 4xx，且请求前后的文件树与内容 SHA 一致。
6. 不要直接增加“现存 save 必须存在”的限制而破坏已删除存档从备份恢复的合法流程；测试应覆盖原目录存在和已删除两种情况。目录名有中文、空格和大小写时与平台行为一致；若净化规则可能将两个目录合并，必须在测试中证明不会错绑对象，不能靠模糊名称匹配。
7. 正向恢复断言返回路径中 `level.dat` 的原始字节；原存档发生过修改时保持该修改；worldB 不变。保留现有新副本命名逻辑。
8. 将已验证补丁按当前源基线应用到获授权的 SourceRoot 前再次比较白名单 SHA；若另一个进程修改了文件，暂停并报告冲突，不能覆盖。实施授权如另有指定落点则遵循其明确要求。

## R2：GREEN、回归与交付

每一条命令单独记录 cwd、开始 / 结束 UTC、完整命令、退出码与测试计数，日志写新 run，避免 ASCII 写中文命令造成假通过。使用 PowerShell 参数数组或原生命令，不用吞异常继续执行。

在隔离快照根分别执行：

```powershell
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter FullyQualifiedName~SaveBackupApiContractTests
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release --filter FullyQualifiedName~RouteReachabilityTests
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release
dotnet build dev/PineglowLauncher/VerifyHost/VerifyHost.csproj -c Release
dotnet dev/PineglowLauncher/VerifyHost/bin/Release/net10.0/VerifyHost.dll --bisect-smoke --port 49138
```

Node 先枚举 `ui/tests` 下正式 11 个 `.test.cjs` 文件成参数数组，再执行 `node --test`，保存枚举名单。不得用未匹配 glob 得到零测试然后报告通过。

GREEN 完成标准：

- 两个原失败场景转绿，原位存档和其他存档字节保持。
- 新增测试覆盖：两世界隔离、跨对象恢复拒绝、不存在备份、被删除存档正常恢复、恢复字节、中文空格目录、无效令牌不写盘。测试总数按实际发现计数，不预填一个绿色数字。
- 原 RouteReachabilityTests 16 项仍全绿；C# 全套在旧 628 项基础上保留原用例并通过新增用例，失败 / 跳过 0；Node 184 / 184；Bisect smoke 10 / 10。
- 真实 config / bak / prev 的前后哈希由独立护栏记录；新文件都加入源码快照白名单，冷快照可复跑。
- 新 manifest 排除自身；保存 detached SHA、最终源码 SHA、精确 diff、旧证据链接、已知限制、逐命令 JSON。
- 本次验收的 15 个独立探针复跑时两项失败已修复，13 项原绿色没有退化。

到此只宣称“具备独立复验条件，等待 Codex 独立验收”。执行 AI 无权自我宣布正式放行。

## R3：单独的目录打开行为澄清与验收（后续包）

当前源码向 Explorer 传 saves 总目录，却响应单个存档 path。先记录实际界面按钮文案及产品预期，确定是打开单个目录还是总目录；不要静默扩入 R1/R2。

若后续授权修复：另立白名单与真实 Explorer 沙盒验收。使用合成目录真实点击，确认打开的地址、响应路径、关窗与端口退出；不能仅凭返回 200 或源码调用推断实际窗口目标。该包不访问用户已有存档。

## 禁止事项与停止条件

- 禁止 reset / restore / checkout / clean / stash / add-A，禁止丢弃脏工作；禁止提交、推送、发布、部署或进入 MR03–MR07/MG1。
- 不改 Mod 删除、Bisect 事务、鉴权、下载、AI、UI、新依赖、RID 锁文件；不跑 publish、不调用付费模型、不发送外部消息。
- 不覆盖旧 RED、旧 manifest 或旧报告以抹去失败；修复证据另开 run。
- 任何真实目录写入、超出白名单、基线漂移、测试被跳过、测试退出非零、无法保证对象归属时停止并报告。RED 阶段预期非零须明确记为有效反例，不能当工具故障吞掉。

Codex 独立复验须重新制作当前源码快照、复跑对象契约和反例、核对 raw response 与最终字节，之后再判断技术验收。阶段 / 发布授权仍由用户单独决定。
