# R06 验收发现：更新替换脚本在无控制台环境下永久卡死（已修）

日期：2026-10-05，Asia/Hong_Kong。来源线程：`codex://threads/01a10b15-0f70-7b21-9ec9-c6526072940f`。

## 结论

`UpdateService.BuildApplyScript` 生成的 Windows 替换脚本用 `timeout /t 1 /nobreak >nul` 作为
"等待目标进程退出"的循环间隔。**`timeout` 依赖控制台输入**，而该脚本是由
**无控制台的 GUI 启动器**拉起的（`Process.Start(cmd.exe /c script)`，`UseShellExecute=false` +
`CreateNoWindow=true`）。当子进程的 stdin 是一个不会关闭的管道时（实测：xUnit/vstest 测试宿主，
以及任何以重定向 stdin 方式启动启动器的父进程），`timeout` 不返回，循环再也走不到备份/替换/校验，
更新流程**停在"正在应用更新"**：既没有替换，也没有回滚，也没有任何失败记录。

这不是理论推理：本轮新增的 `UpdateApplyScriptPathTests.ApplyScript_WaitsForRunningTargetThenReplaces`
第一次运行时**真的挂住**，45 秒后进程快照显示：

```
cmd.exe   /c "…\松光 更新 中文 路径 …\apply-update.bat"      （脚本进程，仍在跑）
timeout.exe  /t 1 /nobreak                                  （阻塞在这里，不返回）
（同名的目标进程 ping.exe→Pineglow.Launcher.exe 已经退出，循环本应立即结束）
```

因为 `timeout` 占着循环，`%errorlevel%` 永远停在上一轮的值，`goto wait` 无限重复——
即使用户已经关掉启动器，更新也不会完成。

## 影响面

- 触发条件：替换脚本进程的 stdin 不是控制台（管道/重定向/无控制台宿主）。
- 后果：更新永久卡住，用户既拿不到新版本，也看不到失败提示；重启启动器后仍是旧版本（数据未被破坏）。
- 不属于数据损坏类问题，但属于"更新链路不可用"，直接命中发版就绪评估里 R06 的"更新"一行。

## 修复

`dev/PineglowLauncher/Pineglow.Launcher.Services/UpdateService.cs`：

1. 循环间隔改为不依赖控制台的 1 秒等待：`ping -n 2 127.0.0.1 >nul`。
2. 等待循环加上界 `MaxRunningProcessWaitIterations = 300`（约 5 分钟）：目标进程若始终不退，
   到点即进入备份 → 替换 → `fc /b` 校验；替换必然失败 → 走回滚并记录 `replace_failed`，
   仍然启动旧版本。这样"异常时仍有可运行旧版本"这条既有承诺在等待环节也成立，
   而不是无限等待。
3. 脚本仍是纯 ASCII（既有结构断言 `ApplyScript_ContainsBackupVerifyAndRollbackSteps` 继续通过）。

## 验证

- 新增 `dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs`（5 项，真实执行 cmd.exe）：
  - `ApplyScript_WaitsForRunningTargetThenReplaces`：用一个真实长跑进程占用目标镜像名，
    断言脚本**等它退出**（耗时 ≥2s）后才替换并记录 `applied`（修复前此用例挂死）；
  - `ApplyScript_InChineseAndSpacePath_AppliesReplacesAndRecordsApplied`：中文+空格路径下
    备份 → 替换 → 校验 → `applied`，且重启那一行真的执行（标记文件存在）；
  - `ApplyScript_InChineseAndSpacePath_ReplaceFailureRollsBackOldVersion`：替换失败 → 旧内容原样 + `replace_failed`；
  - `ApplyScript_InChineseAndSpacePath_BackupFailureLeavesTargetUntouched`：备份失败 → 不动目标 + `backup_failed`；
  - `ApplyScript_DoesNotDependOnConsoleOnlyCommands`：**回归位**，禁止再出现 `timeout /t`，
    并要求等待有上界。
- 既有 `UpdateRecoveryTests` 的替换脚本用例全部继续通过。

## 证据

- 逐条用例输出：本轮快照验证的 `step7-dotnet-test-release.log`，以及 R06 证据目录中的点名日志。
- 修复前后差异：`UpdateService.cs` 中 `BuildApplyScript` 的 `:wait` 段。

## 尚未验证（不得当成已通过）

- **HTTPS 下载阶段到替换的完整链路**：`DownloadUpdateAsync` 强制要求清单里的资产地址是 HTTPS
  （MR20 安全不变量），本机没有可信的 HTTPS 夹具，也不会为了测试去改本机信任库或放宽该检查。
  因此"检查 → 下载 → 校验 sha256/size → 退出 → 替换 → 重启 → 同版一致"这条**端到端**链路仍未跑通，
  本轮只覆盖到"替换脚本本身"和"清单降级"两段。
- 旧版 → 新版的真实替换需要两个**已发布**的正式附件，本轮不具备。
