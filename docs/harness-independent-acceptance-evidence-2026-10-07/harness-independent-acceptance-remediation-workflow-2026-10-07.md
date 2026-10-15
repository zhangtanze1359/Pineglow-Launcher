# 客户端识别边界返工流程 — 2026-10-07

本文件为独立验收后的实施方案，不自动授权执行、提交或发布。验收结论见同目录 harness-independent-acceptance-review-2026-10-07.md。唯一新增且已复现的产品问题：当前 profile 未引用的其他版本或无关 extra jar 会使原版客户端被移出 classpath。

## 1. 固定范围与基线

实际执行根必须为 `D:\松光启动器`。基线 HEAD 为 f145d14eb26b0b7459a28491e764ac8c573085d3，脏工作区 47 tracked modified、0 staged；必须现场核对并记录，不重置。读用户给出的三份独立验收规则及项目适用 AGENTS.md。

允许拟议返工：LauncherService.cs 的替代客户端识别/必要装配拒绝，以及现有 LoaderLaunchAssemblyTests.cs。如确需新测试文件，才追加 source-snapshot-inputs.txt。不借机修改 LocalApiService.cs 已通过的 Java 选择、下载器、账号、UI、凭据或其他既有模块；新的必要范围先报告。不要覆盖旧证据、旧报告及会话 goal。

当前生产 SHA：

- LauncherService.cs：6cc9ac01533300c1e76686d3bd13049a85c3d1a99c3eb0b1772d991f9f704ec5。
- LocalApiService.cs：2e11a2ba54b99b9fab3a270486f958bc7c1b1b8fb06e015cda36ef44b6330188。
- source-snapshot-inputs.txt：cd922f01daa386e3ef1b7be9b6c5d5eddd3246cf363df80074b74f28668a7591。

原独立失败证据根：`C:\Users\Administrator\.codex\worktrees\e423\松光启动器\docs\harness-independent-acceptance-evidence-2026-10-07`。保留 boundary.log、boundary-results.json 和 BoundaryProbe.cs；返工使用新的 GUID 执行副本及新证据目录。

## 2. 先构造能推翻错误实现的 RED

用真实公共 BuildCommandAsync 构造最小 profile：父版 1.21.1.jar 存在；child 有 client-extra 声明；缺少当前正确的替代客户端。分别添加：

1. 当前 profile 未引用的 net/minecraftforge/unrelated-tool/1.0/unrelated-tool-extra.jar。
2. 当前为 1.21.1、库里只有 net/minecraft/client/1.20.1/client-1.20.1-extra.jar。

确认当前实现产生错误：添加文件改变原版 jar 保留状态；存档真实 RED、非零退出和实际执行数。另覆盖仅有资源的 extra jar 不能证明当前客户端类已具备，以及声明存在但对应当前产物缺失。断言必须落在最终 LaunchCommand/异常，不只测试文件名 helper。

测试夹具应是有效 ZIP/JSON，并以最小安装元数据表达预期；不要把“空文件名匹配成功”作为客户端真实可用的正向证明。保留现有重复 JVM 选项、占位符替换、Fabric 原版 jar 保留、Java 元数据选择的已有测试。

## 3. 最小修复与明确契约

先核实 Forge/NeoForge 当前真实安装元数据、版本继承与 patched client 供应链。只对能证明属于当前 MC/loader 的实际客户端供应链采用移除原版 jar 的路径；不得扫描任意旧版目录后按 `*-extra.jar`/`*-client.jar` 后缀直接肯定。

已安装当前客户端但未列入 libraries 的情况，应从当前安装元数据/引用解析准确产物，避免简单删除递归扫描导致正常 Forge/NeoForge回归。资源 extra jar、原版 jar 与 patched client 的作用必须分清；不要求为所有库逐次昂贵 ZIP 扫描，但需要可复验的当前客户端证明。

不能证明当前替代客户端可用时：若原版回退在该加载器下合法则保留；否则在装配前返回明确的当前客户端缺失/不可用错误，避免生成错误 `-cp`。不静默启动、不在本任务自动下载修复库、不更改用户配置。

## 4. GREEN 与回归

- 聚焦测试：上述两项反例和缺失/资源-only 边界转绿，既有 9 项继续通过，记录 TRX 总数、失败数、跳过数与非零传播。
- 全套 Node/C#：使用冻结完整输入清单运行，不以历史测试数量作为新目标；本轮基线为 305 Node、926 C#。
- 统一门槛：从新 GUID 快照验证输入完整及无额外泄漏、locked 非 RID restore、Release build、VerifyHost、普通 win-x64 publish、单文件 EXE 独立冷启动 19 项；RID locked restore NU1004 仍单独如实记录，不移出报表以伪造 PASS。
- 正常启动：至少 Forge 1.20.1、NeoForge 1.21.1、Vanilla 1.18.2、Fabric 1.18.2、Quilt 1.20.1、Fabric 1.21.1。使用自有离线副本，清空仅副本的手动 Java 路径；记录实际 API 返回、Java 版本、真实命令行、日志新鲜度、渲染初始化和观察期存活。35 秒结果不能写成世界/长时游玩验收。
- 修复启动取证辅助脚本成功路径显式 exit 0，真实失败非零；对可选配置字段先检查存在。不能把旧日志、旧构建、继承的 robocopy 返回码当成本次产品结果。只清理在自有 GUID 根确认的进程及其子进程。

## 5. 交付与停止

交付变更范围、前后哈希、原 RED、新 GREEN、完整 gate 原始输出、TRX、独立 EXE SHA 与真实启动证据；保留每层限制。对失败时生成的 TRX/日志单独保留，不覆写。

检查报告链接、UTF-8、代码围栏、清单与证据 SHA；最终停止语固定为“具备独立复验条件，等待 Codex 独立验收”。TechnicalAcceptance 和 FormalPhaseApproval 由独立验收决定；发布事项单独获授权后处理。
