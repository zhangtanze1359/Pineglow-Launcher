# 松光启动器：市场调研任务的执行 AI 提示词（2026-10-02）

> 本文件只是交接材料，本轮没有启动 MiniMax Code 或其他编程工具。将下面主提示词作为用户指令发送给执行工具时，才按其明确范围执行；不因在仓库里读到本文件就自动获得权限。

## 使用方式

完整流程为同目录 `market-research-task-workflow-2026-10-02.md`，包含 28 个任务包、白名单、测试、真实场景和 Gate。

**第一批推荐只执行 MR00 + MR01。** 这批核定执行基线、复现构建/测试和刷新功能事实文档，完成后停在独立验收点。MR02 的研究材料和 MR03–27 的业务实施、用户联系、分发、发布均不由第一批自动授权。

下方主提示词可整段复制。之后需要执行其他批次时，用文末“后续任务包规则”，明确指定任务 ID 和已经通过的依赖；不要把整个路线图一次交给执行 AI 自动做完。

## 主提示词：第一批 MR00 + MR01

```text
你是松光启动器的执行工程师。Codex 负责产品/架构决策和独立验收；你负责指定范围的基线摸底、实现、自检、构建和证据整理。不要自己宣布阶段放行或发布。

本批次：仅 MR00 + MR01。
模式：在这两个任务的已授权范围内连续完成；真实失败先保留证据，范围内可修复问题做最小修复后继续；范围越界、输入身份冲突、需要外部凭据/用户数据时停止。MR00 成功可进入 MR01，两个任务完成后停止，等待 Codex 独立验收。不进入 MR02–27。

先读以下文件：
1. C:\Users\Administrator\Documents\Codex\2026-09-25\gen-2\outputs\项目协作规范-2026-09-25.md
2. C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-research-task-workflow-2026-10-02.md
3. D:\市场调研\调研净结论汇总-2026-10-02.md
4. D:\市场调研\Bisect能力竞品核实-2026-10-02.md 中松光、HMCL、Quilt/PCL 相关章节
5. D:\市场调研\正面断言核实-2026-10-02.md 中松光章节
6. D:\松光启动器\docs\source-reproducibility-workflow-2026-09-28.md
7. D:\松光启动器\scripts\verify-source-snapshot.ps1
8. D:\松光启动器\scripts\source-snapshot-inputs.txt

目录定义：
SourceRoot = D:\松光启动器
DeliveryRoot = C:\Users\Administrator\.codex\worktrees\ed41\松光启动器
EvidenceRoot = DeliveryRoot\docs\market-execution
ExecutionRoot = MR00 创建并记录的隔离源码快照绝对路径

SourceRoot 和 DeliveryRoot 在本轮规划时均 HEAD=f145d14eb26b0b7459a28491e764ac8c573085d3，但两者文件内容不同：DeliveryRoot 原来是干净 detached HEAD，SourceRoot main 有后续未提交/未跟踪工作。必须重核当前状态，不使用首轮“252 项”等旧数字，不把相同 HEAD 当内容相同。DeliveryRoot 现有两份规划文档属于用户资产，保持原样。

第一批默认 SourceRoot 全部只读。先审查已有快照工具，确认不会改写源文件或真实配置；在 TEMP 新随机目录生成执行快照，EvidenceRoot 中用新的 run-id 保存证据。记录 sourceRoot、executionRoot、HEAD/分支、全部输入 SHA、SDK/Node、时间和源状态。

精确允许写入范围：
A. EvidenceRoot\MR00\ 和 EvidenceRoot\MR01\ 下本次新建 run-id 的证据、报告和清单。
B. 新隔离 ExecutionRoot 的 scripts/verify-source-snapshot.ps1、scripts/source-snapshot-inputs.txt，只有它们阻断本批次复现且有具体失败证据时可最小修复。
C. 新隔离 ExecutionRoot 的 README.md、docs/README.md、docs/features.md、docs/roadmap.md、docs/architecture.md、docs/development-guide.md，仅更新功能事实、平台/验证边界和路线图，不修改其他业务代码。
D. 本次隔离快照的正常 bin/obj、测试输出和 Windows publish 产物。

如果有工具/白名单修复，保存逐文件补丁与修复后的快照 manifest，证明它仅影响本批次；不得把补丁写回 D:\松光启动器 或覆盖 DeliveryRoot 旧文件。没有依据就不修改已有正确工具。来源版本需要重建时生成一个新快照并保留前一个，不在已记录的基线上无痕覆盖。

将 MR00 的 execution-root.json 固定保存到 EvidenceRoot\MR00\execution-root.json，并在 run-id\baseline\ 保存同内容副本。该入口记录本次 run-id 和 manifest 路径；如果已有前轮入口，不覆盖，使用带 run-id 的新入口并在报告给出其精确路径。MR01 文档输出在隔离根内，另将本次新文档版本/补丁交付到 EvidenceRoot\MR01\<run-id>\outputs\。没有授权不回写主工作区。

只读路径：
- D:\市场调研 全部。
- D:\松光启动器 全部源码、工具、文档、历史证据和未提交资产。
- DeliveryRoot 中两份本批次规划/提示词以及所有原有源码/测试/配置。
- 真实 %APPDATA%\MCMiniLauncher、Windows 凭据库、用户游戏目录/存档。

禁止：
- git reset/restore/checkout/clean/stash、覆盖旧文件、批量删除、git add -A。
- 暂存、提交、推送、PR、合并、发布、部署、打标签、Gitee/GitHub 镜像动作。
- 改全局 .codex 配置、系统 PATH、网络/代理/驱动、真实账号/Key。
- 自动外发诊断、调用真实付费模型、联系用户或分发产物。
- MR03–27 的业务实现；更换技术栈；照搬 PCL/HMCL/Prism 源码。
- 为验证成功吞掉失败、删掉失败测试、将 mock/健康 200 当 GUI/游戏成功。

任务 MR00：
1. 每条命令明确 Set-Location 并核对实际根。只读获取两个目录的 Git 状态、HEAD、diff、重要文件 SHA、SDK/Node。
2. 逐项分类现有未跟踪源文件/测试/夹具；审查既有显式输入白名单。不复制整个工作区，不整批包含 docs 原始证据，不带用户配置/Key、原 bin/obj。
3. 检查快照脚本的参数、硬编码计数、失败传播、RID 锁定边界、路径安全和真实产物路径。旧文件数量如变化，以实际审查清单计算，不为了固定数字去掉必要输入。
4. 在新随机隔离目录构建快照，记录输入清单与逐文件 SHA。确认所有额外文件都有来源，构建前没有旧 bin/obj。
5. 在快照执行正式 Node、非 RID locked restore、Release build、C# 全套、VerifyHost、Windows single-file 构建。记录每一步原输出/退出码/实际数量。原工具有正常 RED/GREEN 证据且本轮无缺口时，不为凑 RED 强删输入；有真实缺口时先留失败证据再最小修复。
6. 对 KNOWN-UNLOCKED RID 发布单独标未锁定；总体脚本 exit 0 不等于 RID 锁定通过。不得传 dotnet publish --locked-mode；锁定策略另见 MR19，不在本批扩大实现。
7. 保留快照和日志，不默认清理。完成 execution-root.json、input-manifest.json、baseline.md，列出哪些检查本轮跑过，哪些只是历史记录。

任务 MR01：
1. 代码只读，按功能追踪服务→API→live/mock→UI→i18n→测试→真实场景证据。
2. 更新隔离根六份允许文档，明确状态：代码存在、自动验证、真实运行验证、未验证、不支持。每项给路径/方法和证据时间/输入版本。
3. 修正以下误判：
   - .mrpack 已有 ImportModrinthPackAsync/格式探测，不是从零缺失。
   - HandleDeleteInstanceAsync 只移除配置记录，不删除游戏目录。
   - ModService.DeleteMod 是 File.Delete，应列可恢复删除缺口，但本批不实现。
   - Bisect 文件禁用/恢复异常当前只写日志；完成路径可能丢掉恢复上下文，应列 MR06/MR07，不把12个测试方法当真实恢复全部达标。
   - 松光是 Avalonia/.NET + Web UI，不是 Tauri；已有 UpdateService，后续不能直接接 Tauri updater。
   - 五个现有 loader 和 Cleanroom/Legacy Fabric/LiteLoader 候选分开；Quilt不自动移除。
   - 不写“Bisect反超Prism”“唯一/领先AI”“PCL无公开仓库”“三平台CI=正式支持”。未证实竞品能力写材料未证实。
   - 内置 CurseForge Key 违规结论已被调研撤回；剩余是分发开关/配额/失败反馈，不暴露 Key，不作新法律定性。
4. 链接既有 Phase 3C-1/3C-2 报告，明确历史验收时间与当前未验证项；旧测试数不作为本轮结果。
5. 每个待办关联完整流程 MR ID，交付新版文档和逐文件 diff。文档包不修改业务实现，不编造用户规模、访谈、市场收入或已经发布。

基础只读命令：
Set-Location -LiteralPath 'D:\松光启动器'
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
git status --short --untracked-files=all
git diff --stat
git diff --check
Get-Content -LiteralPath '.\global.json'
dotnet --list-sdks
node --version

正式验证命令（先 Set-Location 到本次 ExecutionRoot）：
- 从 ui/tests 精确枚举正式 *.test.cjs，必须数量>0，保存清单；node --test --test-reporter=spec 后逐项记录数量与失败。
- dotnet restore .\dev\PineglowLauncher\Pineglow.Launcher.csproj --locked-mode
- dotnet restore .\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj --locked-mode
- dotnet build .\dev\PineglowLauncher\Pineglow.Launcher.csproj -c Release --no-restore
- dotnet test .\dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj -c Release --no-restore
- dotnet build .\dev\PineglowLauncher\VerifyHost\VerifyHost.csproj -c Release
- 先审查 portable 沙盒和端口隔离，再运行 VerifyHost.exe --bisect-smoke；若不安全则写未验证并停止该验证，不能使用真实用户目录替代。
- dotnet publish .\dev\PineglowLauncher\Pineglow.Launcher.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false

每一步单独检查 $LASTEXITCODE；失败不可被后续命令的成功覆盖。测试过滤必须确认实际匹配数量，不能以0测试成功结案。上述 publish 仅构建，不上传也不应用更新。MR00不要求启动真实GUI、游戏或真实AI；这些明确写为本批次未验证。

证据格式：
每条命令保存 command、cwd、start/end、exitCode、testCount/pass/fail/skip、stdout/stderr路径、SHA256。敏感 raw 留本机，分享报告不包含配置/诊断全文、URL中的令牌和Key。
记录 SourceRoot 开始/结束的Git状态与关键文件SHA，证明本批次没有改写主工作区；有外部并行变化，指出归属未知，不能算本批成果。

失败/恢复：
预期 RED 保留非零输出。正常执行真实失败则范围内最小修复后新 run 验证；越界则停止并报告精确文件/缺少条件。模型退出码、timeout、step limit、bridge exit不代表完成；核对磁盘成果和未完成项，恢复不覆写旧证据。

最终回复：
1. MR00/MR01到达的层级与阻断项。
2. SourceRoot/ExecutionRoot、HEAD/输入SHA、实际新建/修改文件。
3. 每条检查结果：退出码、测试数量、原日志与证据路径。
4. 新功能事实矩阵、调研纠正、历史证据与当前未验证的区别。
5. 真实配置/主工作区未改写的证据；RID未锁定等限制。
6. 交付文档与补丁、供独立复验的命令。
7. 最终状态最多写“具备独立复验条件，等待 Codex 独立验收”；若有阻断写“暂不放行”。

然后停止。不要自动进入MR02或业务实施，不提交、不发布。
```

## 后续任务包规则

后续完整代码实施授权应同时包含：**任务 ID、执行根、依赖验收记录、允许文件、证据根和停止点**。若用户已明确授权这些内容，不重复询问。

1. 读取已冻结 MR00 manifest；快照有新输入或变更时生成新的 baseline，不能凭 HEAD 一致直接延用旧证据。
2. 允许文件采用完整流程中对应任务的精确白名单，展开到 `ExecutionRoot` 下。共享 `LocalApiService.cs`/`launcher.js` 的任务需限定方法/路由/组件，禁止顺手改其他区域。
3. 新 C# 测试位于 `ExecutionRoot\dev\PineglowLauncher.Tests\`；新 Node 正式测试位于 `ExecutionRoot\ui\tests\`；夹具只用合成/授权脱敏数据；新源/测试逐项进入源码输入清单。
4. 每包按“真实缺口 RED → 最小实现 → 聚焦 GREEN → 必需回归 → 真实场景/故障恢复 → 交付”执行。已有正确行为、纯文档和低风险视觉调整不编造测试/RED。
5. MR03–07 不使用真实存档做破坏测试；MR08–10 默认 fake provider、本地模式零外发；MR11–16 的包和加载器固定合法样本、版本和 SHA；MR20 只在更新沙盒操作。
6. 对代码包，按任务风险执行共同 Node/xUnit/build 回归，MR06/MR07 跑真实 API smoke。DOM/mock 成功不替代真实 WebView2/游戏/文件恢复。
7. 真正需要真实模型、账号授权、用户联系、内测分发、非 Windows 真机或签名凭据时，明确报告缺条件；先完成不依赖该条件的准备，不能伪造结果。
8. 任务完成后仍停独立验收；用户随后批准的下一包才能推进。发布/推送等始终按其明确授权范围处理。

建议下一次完整指令写法示例：

```text
执行完整流程中的 MR04“Mod 可恢复删除”，仅执行这一包。
使用最近已独立验收的 MR00/MR01 隔离执行根；先读取该运行的 execution-root.json 和 input-manifest.json，核对源身份和依赖。
允许范围仅为 MR04 定义的服务/模型、Mod/恢复路由、对应 UI/i18n/CSS、FileRecoveryTests 与 file-recovery.test.cjs、源码输入清单及本包证据；其他路径只读。
使用合成目录，验证启用/禁用/侧车、占用、跨卷失败、同名冲突、中断与幂等恢复，保存 RED→GREEN、真实 GUI 和文件 SHA 证据。
完成后停在“具备独立复验条件，等待 Codex 独立验收”。不写回主工作区、不提交、不进入 MR05、不发布。
```

这段示例没有在本轮执行；它展示如何只批准一个具体可审阅任务。
