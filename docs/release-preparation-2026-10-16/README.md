# 发布准备检查：暂不放行

日期：2026-10-16，Asia/Hong_Kong。用户要求“还有其他的问题吗，没有就可以提交改动准备打包发布了”。本轮发现发行候选的实际冷启动阻断，因此没有执行条件性提交、推送、打标签或上传 Release。`FormalPhaseApproval=NOT_GRANTED`。

## 结论

FA-01 至 FA-05 修复未发现新的前端回归；正式清单的 379 项 Node 测试全部通过。但是按发布参数生成的 v1.0.1 单文件候选在本机全新目录启动时，`HttpListener.Start()` 抛出错误 6“句柄无效”，本地 API 不可用。两次隔离冷启动均失败。不能将“构建成功、进程存活、出现窗口标题”当成发行包可用。

这将此前开发运行时问题提升为本轮发行候选的已复现阻断；尚未确定是系统、运行时、原生依赖初始化还是它们的组合导致，不能据此断言所有用户机器都会失败，亦不能称为 .NET 已确认缺陷。

## 输入与产物

- SourceRoot：`D:/松光启动器`，分支 `main`，基线 HEAD `1356efa70cfd91f6289ee6e0dc6af5160b444ab3`。
- 源码仍含上一轮未提交的 FA 修复；开始清单见 `baseline-status.txt`，没有覆盖已有修复或证据。
- `prior-evidence-hash-check.json`：上一轮的 13 个源文件/元数据/程序集哈希全部一致；这是对旧证据输入的核对，不是旧测试重新执行。
- 候选版本暂取现有 `release-notes/v1.0.1.md` 对应的 1.0.1，仅供本地验证；没有创建标签。
- 发布目录：`publish/`，只取其中 `Pineglow.Launcher.exe` 验证；未删除原生 PDB，未覆盖仓库已有 dist。
- EXE：53,089,586 bytes，SHA256 `F665C26DB60941335DEF1F5B9F6F743AAF23D148B9AE54B6A53336E4A0BF2900`。
- 自包含运行时：`Microsoft.NETCore.App 10.0.9`，来自本次 win-x64 输出的 runtimeconfig；版本和哈希见 `candidate-metadata.json`。
- ExecutionRoot：本目录 `cold-start/中文 冷启动 <guid>/`，每次只有 EXE、portable.flag 和本轮解包/数据目录；没有使用真实用户配置、账户或游戏目录。
- EvidenceRoot：本目录；`input-manifest.json` 记录 tracked 与显式白名单的去重输入及哈希。不是干净提交、远程 CI 或正式发布证明。

发布命令与工作流同类参数，仅把发布目录和 RID 锁文件转移到本轮目录，保护原 `packages.lock.json`：

```powershell
dotnet publish dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -r win-x64 --self-contained /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false /p:Version=1.0.1 /p:PublishDir=<本目录绝对路径>/publish /p:NuGetLockFilePath=<本目录绝对路径>/rid-packages.lock.json
```

没有使用 RID locked-mode，也没有把此普通发布称作锁定还原通过。仓库原锁文件没有变化。

## 本轮直接验证

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 最终生产代码差异复核、git diff --check | 未发现新的 FA 范围阻断；无空白错误 | 当前 diff；`final-workspace-status.txt` |
| 上轮输入哈希核对 | 13/13 相同 | `prior-evidence-hash-check.json` |
| UI 资源闭包 | 12 个文件、14 个本地引用、0 缺失 | `ui-resource-closure.json`、同名 log |
| 测试清单双向一致 | 28 个 Node 测试文件、1 个 C# 项目；0 缺失/未登记 | `test-inventory.json` |
| 正式清单全部 Node 测试 | 379/379，0 fail、0 skip，exit 0 | `frontend-tests.log`、`check-exits.json` |
| win-x64 自包含单文件发布 | exit 0 | `publish.log` |
| 实际 EXE 冷启动，端口 48325 | API 未就绪；原脚本报告路径另有未初始化变量异常 | `standalone-cold-start.log`、`first-launch-exception.txt` |
| 校正报告路径后，同一个 EXE 换全新目录/端口 48263 | 再次 API 未就绪，FAIL，exit 1 | `standalone-cold-start-report.json`、同名 log、`second-launch-exception.txt` |

379 是清单中 UI 和 scripts 测试的总数；上一轮 327 是 UI 测试数。不是本轮新增 52 项。未重跑全量 C#，没有将部分 C# 或历史通过记录算作全量通过。

## 问题

### RP-01 / P1：发行候选本地 API 无法启动

完整日志显示：

```text
[LocalApi] 启动失败: 句柄无效。
System.Net.HttpListenerException (6)
  at System.Net.HttpListener.SetupV2Config()
  at System.Net.HttpListener.Start()
  at Pineglow.Launcher.Services.LocalApiService.Start(...)
  at Pineglow.Launcher.App.OnFrameworkInitializationCompleted()
```

生产调用位于 `LocalApiService.cs:193`；`App.cs:33` 调用、catch 后继续启动窗口。UI 资源已经解包，进程仍存活并有“松光启动器”窗口标题，但 API 不可用，正常产品操作无法得到后端服务。本轮直接证据限于该机器及该 EXE。后续应先确认底层原因与受影响范围，再做最小修复，不能改用 mock 或仅降低版本掩盖。

### RP-02 / P2：冷启动验证失败路径无法写出报告

原 `scripts/verify-single-file-standalone.ps1:232` 仅在健康成功分支初始化 `$served`，`:306` 无条件引用；StrictMode 在健康失败时抛错，JSON 不落盘。原始失败日志已保留。

仅在本轮证据目录生成两份验证器副本：第一份只将 Start-Process 增加 `-WindowStyle Hidden`；第二份在启动前初始化空 `$served`，保留失败判定，正确写出 FAIL 报告。副本均传 `-KeepWorkDir -SkipWindowTitleCheck`，保留临时目录和日志；窗口标题只记录，不算 GUI 通过。没有修改仓库正式脚本或产品实现。第二次的 FAIL 由真实 API 故障触发，不是报告器异常。

### RP-03 / P2：发布介绍需要补齐这次修复

现有 `release-notes/v1.0.1.md` 已包含自动检查和更新介绍功能，但尚未包含 FA-01 至 FA-05。本目录 `v1.0.1-notes-draft.md` 已准备完整介绍草稿；RP-01 闭环后再同步正式文件并执行 release-notes 校验。本轮草稿不代表可发布状态。

## 尚未验证与交接

没有做真实账户登录、Minecraft 启动、在线升级/替换/回滚、全量 C#、同 SHA 远程 CI 或正式签名/上传。本轮不将这些缺证据项全部归类为新产品缺陷，但发布前需要按既定门槛补齐。

详细后续流程：`../release-blockers-remediation-workflow-2026-10-16.md`；可复制执行提示词：`../release-blockers-execution-prompt-2026-10-16.md`。本轮没有扩展实施到运行时/服务架构，也没有在发现阻断后执行条件性提交。候选 EXE 仅保留为诊断产物，不用于分发。
