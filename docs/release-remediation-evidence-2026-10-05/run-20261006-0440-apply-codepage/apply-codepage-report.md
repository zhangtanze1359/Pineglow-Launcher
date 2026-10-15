# 替换脚本在"非中文 Windows"下能不能用（第 19 轮）+ 一处诚实的覆盖缺口

日期：2026-10-06（Asia/Hong_Kong）。证据：`cp437-apply-tests.log`（控制台代码页 437 下重跑 7 个用例的原始输出）。

## 1. 起因：一个此前从没被问过的问题

自更新替换脚本由 `UpdateService.BuildApplyScript` 生成，内容是 `.bat`，里面**内联了绝对路径**，
而安装目录可能含中文与空格（例如 `D:\松光启动器\中文 目录\`）。
脚本由 `File.WriteAllText` 写入 —— 即 **UTF-8 无 BOM**。
**cmd.exe 解析 .bat 用的是当时的 OEM 代码页**，本机是 936（中文）。
于是问题来了：**在一台英文 Windows（OEM 437）或英文 CI runner 上，这个脚本还能用吗？**
此前所有验证都在本机 936 下做过，这个问题**从来没有被回答过**——
而"英文系统"是极常见的用户配置，CI 的 `windows-latest` 也是英文。

## 2. 机制（读代码得到的解释）

`BuildApplyScript` 生成的 `.bat` 开头两行是：

```
@echo off
chcp 65001 >nul
```

两行都是**纯 ASCII**，因此在任何 OEM 代码页下都能被正确解析执行；
第 2 行把代码页切成 UTF-8 之后，**后面那些含中文路径的行才被按 UTF-8 读取**。
如果删掉 `chcp 65001`，中文路径行在 437 下就会解析成乱码 —— 也就是说这两行是**承重结构**。

## 3. 实验（决定性的）

把控制台代码页切成 **437**，然后重跑替换脚本的 7 个用例
（测试里 `cmd.exe` 以 `CreateNoWindow` 启动、**继承同一个控制台**，因此子进程的起始代码页就是 437）：

```
chcp 437            → Active code page: 437
dotnet test ... --filter FullyQualifiedName~UpdateApplyScriptPathTests
exit=0              7 passed / 0 failed
```

关键用例的实测状态与 936 下**完全一致**：

| 用例 | 437 下的实测 |
| --- | --- |
| 中文+空格路径：备份→替换→校验→applied | `[R06/upgrade-wait] state=applied waited=5.3s` |
| 目标只读（无权限形态一）| `[R06/upgrade-noperm] state=replace_failed target=OLD-CONTENT` |
| 显式拒绝 ACE（无权限形态二）| `[R06/upgrade-deny-ace] state=replace_failed target=OLD-CONTENT` |

**结论：在英文（437）代码页下，含中文路径的自更新替换仍然可用。**
这回答了"英文 Windows / 英文 CI runner 上自更新会不会因为编码挂掉"这个问题——
答案是不会，机制是脚本自己那句 `chcp 65001`。

（实验后已把代码页恢复为 936。）

## 4. 把机制钉住（回归位）

只靠一次实验不够：如果以后有人把那行 `chcp` 当成冗余删掉，本机 936 下**照样全绿**，
缺陷只会在英文机器上炸。已在既有用例 `ApplyScript_DoesNotDependOnConsoleOnlyCommands`
里加了一条**文本级回归位**（并写明理由与本次实验）：

```csharp
Assert.StartsWith("@echo off" + "\r\n" + "chcp 65001", script);
```

文本断言比行为断言弱，这里是**有意选的**：要在测试里真造出"另一个 OEM 代码页"，
就得在子进程里 `chcp`，而 `chcp` 改的是**共享控制台**的代码页 ——
一旦泄漏没恢复，会连带改变验证器自己的日志编码与门槛匹配。不值得为一句话引入这种风险。

## 5. 诚实的覆盖缺口：`UpdateService.ApplyUpdate` 本身没有被测

本轮顺带查清了一件此前没写明的事：**"启动器自己调用替换"这一步没有任何测试覆盖**。

- 已有覆盖：`BuildApplyScript` 的产物语义（7 个用例，真实 `cmd.exe` 执行）+ 上面这条编码回归位。
- **未覆盖**：`UpdateService.ApplyUpdate(downloadedPath, scriptExt, version)`（`UpdateService.cs:593`）——
  它把脚本写盘并 `Process.Start("cmd.exe", "/c \"脚本\"")` **分离启动**，然后进程退出。
- **为什么没测**：`targetExe = Environment.ProcessPath`（第 595 行）——它指向**当前进程自己的 exe**。
  在测试进程里调用它，生成的脚本会去"等这个正在运行的测试宿主退出"（上界 300 轮 ≈ 5 分钟），
  然后试图覆盖一个正在运行的映像 → 失败回滚 → 还会去"重启"测试宿主。既慢又危险。
  要安全地测它，需要把 target 变成可注入参数 —— **那是产品代码重构**，不在本轮授权范围内，
  也没有必要为了可测性去动将要发布的核心路径。
- 残余风险：低（脚本语义与编码都已验，`ApplyUpdate` 只多做"写盘 + 分离启动"，
  且 `WorkingDirectory`/`UseShellExecute=false`/`CreateNoWindow=true` 与实测缺陷修复一致），
  但**必须如实记为"未直接验证"**，不能因为"间接覆盖"就说它过了。

## 6. 边界

- `chcp 437` 只是近似英文机器的 OEM 代码页；英文 Windows 的其他差异（日期/数字格式、ACL 默认、
  区域设置对 .NET 的影响）不在本轮范围。
- 未验证 Unix 分支（`/bin/sh` 脚本）：本机无该环境。
- 本轮改的是**测试文件**（加一条断言），未改产品代码。
