# DeepSeek Harness 独立验收 — 2026-10-07

**结论：限定技术验收不通过；暂不可作为新版本发布。** 正常启动修复和主要自动化门槛获得独立证据，但替代客户端识别存在已复现的版本隔离缺陷，不能关闭本轮启动装配任务。

TechnicalAcceptance=NOT_PASSED；FormalPhaseApproval=NOT_GRANTED；ReleaseApproval=NOT_GRANTED。

## 验收对象与权限

- 用户要求：deepseek harness执行完毕，请验收。
- 实际产品源码：`D:\松光启动器`；HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3`。
- 验收证据：`C:\Users\Administrator\.codex\worktrees\e423\松光启动器\docs\harness-independent-acceptance-evidence-2026-10-07`。
- 最新执行会话：`session-466887a4-24c0-4da2-b92e-a6d4801f1be2`；所查记录 lastTurn 89，goal 为 null；最新交付 `docs/FINAL-ACCEPTANCE-2026-10-07.md` 已复制为 `executor-report.md`。
- 输入冻结：312 文件，161 tracked + 151 白名单；全新 GUID 快照内独立执行。验收后 312 项 SHA-256 无漂移，HEAD 不变。
- 相对上一轮独立 310 输入快照，已有输入仅 LauncherService.cs、LocalApiService.cs、source-snapshot-inputs.txt 改变，新增 LoaderLaunchAssemblyTests.cs 和 JavaSelectionMetadataTests.cs。完整记录见 scope-diff.json/source-inputs.json。
- 本轮没有修复产品、改会话 goal、改用户原配置、提交、推送、打 tag、发布或向其他会话发送消息；只生成隔离验证与审计文档。

## 已通过的独立复验

| 项目 | 当前独立结果 | 证据/限制 |
|---|---|---|
| 快照来源及输入 | 312/312；额外输入 0，初始 bin/obj 0；事后哈希漂移 0 | source-inputs.json、source-integrity-after.json |
| 新增聚焦测试 | 9/9，通过且无跳过 | focused/focused.trx、focused.log |
| Node 全套 | 305/305 | gate/step1-node-tests.log |
| C# 全套 | 926/926，无跳过 | gate/step7-dotnet-test-release.log |
| UI 闭包/清单、非 RID locked restore、Release build、VerifyHost、普通 win-x64 publish | PASS | gate 内各步骤原始日志 |
| 仅单文件 EXE 独立冷启动 | 19/19 | gate/standalone-cold-start.json |
| 独立发布 EXE | 53,052,722 bytes；SHA 与执行方交付一致 | rebuilt-exe.json |
| 客户端边界反例 | **2/2 失败，probe exit 1** | boundary-results.json、boundary.log、BoundaryProbe.cs |

独立构建 EXE SHA-256：`3859e1390ab8b24ec1e0779439f163cb559653f449a566f015b0339ee4e5005d`。

统一 gate 总退出 0，但 gate 显式容纳了一项已知未通过边界：RID locked restore 实测 NU1004。不能把 gate PASS 写成所有发布还原门槛均通过。

## 六项真实产品启动取证

独立复制已有离线实例到新 GUID 目录，复制的配置清空实例/全局手动 Java 路径，运行上述本轮独立构建 EXE，通过真实产品 API 启动。原缓存与原用户配置保持原状。

| 实例 | 自动选用 Java | 新日志行数 | LWJGL/atlas | 35 秒存活/崩溃标记/未替换占位符 |
|---|---|---:|---|---|
| Forge 1.20.1 | 17 | 193 | 1/12 | 存活 / 0 / 0 |
| NeoForge 1.21.1 | 21 | 327 | 1/15 | 存活 / 0 / 0 |
| Vanilla 1.18.2 | 17 | 32 | 1/10 | 存活 / 0 / 0 |
| Fabric 1.18.2 | 17 | 1,227 | 1/10 | 存活 / 0 / 0 |
| Quilt 1.20.1 | 17 | 670 | 1/12 | 存活 / 0 / 0 |
| Fabric 1.21.1 | 21 | 382 | 1/14 | 存活 / 0 / 0 |

这些结果证明上述已有离线实例在当前构建进入渲染初始化并持续运行到观察期。未视觉验收游戏主界面、未进入世界、未长时游玩，也未验证全新在线安装或 Quilt 1.21+。

证据限制：原启动取证外层命令 **exit 1**，存在辅助命令对不存在 ProxyPassword 属性赋值的非终止异常，且成功路径未显式返回 0。六个分项结果均有独立的新日志及真实命令行，另用 verify-retained-launch-results.ps1 对留存数据校验，**exit 0**。不能将其表述为原外层启动脚本 exit 0。详情保存在 launch-wrapper-limitations.md。原始取证脚本和日志保留。确认并清理本次隔离目录中的遗留子进程后，process-cleanup-final.json 记录自有进程剩余 0。

## P2：无关/旧版 jar 导致当前客户端错误移出 classpath

定位：`dev/PineglowLauncher/Pineglow.Launcher.Services/LauncherService.cs`，SuppliesOwnClient 的第 455–459 行，以及第 466–468 行 IsClientJarName。第 53 行依据这个判定将原版客户端 jar 置为 null。

当前版本只要在 JVM 参数声明 `-DignoreList=client-extra,...`，就可能递归扫描整个 `libraries/net/minecraft/client`、`net/neoforged`、`net/minecraftforge`，将任何以 `-extra.jar` 或 `-client.jar` 结尾的文件作为替代客户端。扫描结果没有绑定当前 MC 版本/加载器或 profile 引用；`extra.jar` 文件名本身也不证明包含客户端类。

两个独立反例直接调用本轮冻结构建中的真实公共 BuildCommandAsync，不重写其实现，不改产品源码。隔离 profile 为 NeoForge 风格 1.21.1：父版本客户端 jar 存在、child 声明 client-extra、当前替代客户端不在库清单。添加以下不被 profile 引用的文件前，原版 jar 保留；添加后均被移出：

1. `libraries/net/minecraftforge/unrelated-tool/1.0/unrelated-tool-extra.jar`。
2. `libraries/net/minecraft/client/1.20.1/client-1.20.1-extra.jar`，与当前 1.21.1 不同。

实际输出 retainedBefore=true、retainedAfter=false，两个 pass=false，probe exit 1。隔离最小 profile 的空 classpath 值被 FlattenArguments 移除，导致 `-cp` 后紧接主类文本。此处是启动命令装配反例，未把这两个人工 profile 当作真实游戏启动实测失败；也不推断上表正常六个实例失败。

另对真实 Forge 1.20.1 隔离实例的 client-extra.jar 读取 ZIP：13,517 entries、`.class` 数量 0（extra-jar-content.json）。这证明至少该实际文件仅凭“存在且文件名匹配”不能证明替代客户端类可用。单独这份 ZIP 检查不等于新的独立启动失败。

影响：混合版本库缓存、遗留文件或当前替代客户端缺失时，无关文件改变当前实例 classpath；仍然无法满足“当前 profile 确实提供替代客户端才去掉原版 jar”的限定修复契约。正常六项通过和现有 9 项测试无法覆盖这个失败边界。因此列为本轮技术验收阻断。

## 下一步与发布边界

按同日单独返工流程执行：先保留此轮失败证据，再以真实公共 BuildCommandAsync 添加上述负例，约束替代客户端识别到当前 profile/安装元数据及可用的类供应链。当前替代客户端缺失时应保留合法回退，或在装配前明确拒绝；不能因无关文件静默去掉原版 jar。

重跑聚焦、全套、快照 gate、六类正常启动，交付仍停在“具备独立复验条件，等待 Codex 独立验收”。不得把此文档当作实现或发布授权。

发布仍需单独关闭/决定：RID locked restore NU1004；进入世界与长时运行；Quilt 1.21+；微软账号链路、AI 实际第三方调用、Gitee update.json/token 相关证据及正式发布授权。这些项目没有在本轮获得 PASS；不得用离线进程启动结果替代外部服务或发布验证。
