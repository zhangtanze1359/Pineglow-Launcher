# R01 流程步骤 6「无权限」分支：真实验收记录

日期：2026-10-06（Asia/Hong_Kong）。目的：关闭此前记为 NOT_RUN 的一项 ——
"旧版迁移与占用/校验失败/**无权限**时恢复完整可运行旧版本"里的**无权限**分支。

## 1. 做法

不推理、不 mock：让操作系统**真的拒绝写入**，然后跑产品自己生成的替换脚本（真实 `cmd.exe`），
检查脚本的落盘状态与目标文件内容。

- 新增聚焦测试：`dev/PineglowLauncher.Tests/UpdateApplyScriptPathTests.cs` →
  `ApplyScript_WhenTargetIsNotWritable_KeepsRunnableOldVersion`
- 模拟无权限的手段：把目标 exe 设为**只读属性**（`FileAttributes.ReadOnly`），
  于是 cmd 的 `copy /Y <新版本> <目标>` 会得到 `Access is denied`。
- 安装路径仍是**中文 + 空格**目录（与其余替换脚本用例一致）。

## 2. 实测输出（原始）

命令（`TEMP` 指向工作区内目录，避免本会话沙箱对 `%TEMP%` 写入的限制）：

```
dotnet test dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj -c Release --no-restore ^
  --filter "FullyQualifiedName~WhenTargetIsNotWritable" --logger "console;verbosity=detailed"
```

```
已通过 Pineglow.Launcher.Tests.UpdateApplyScriptPathTests.ApplyScript_WhenTargetIsNotWritable_KeepsRunnableOldVersion [112 ms]
[R06/upgrade-noperm] state=replace_failed target=OLD-CONTENT output=Access is denied.
Access is denied.
```

## 2b. 第二种形态：显式拒绝 ACE（同批补，`ApplyScript_WhenTargetHasExplicitDenyAce_KeepsRunnableOldVersion`）

只读属性是**文件属性**，真正意义上的"无权限"是**ACL**。两者在 Windows 上机制不同，失败路径不保证一样，
所以单列一个用例，用 `icacls` 给目标文件加显式拒绝权限后照样跑产品脚本：

```
icacls "<目标>" /deny "<用户>:(WD,AD,WA)"     # 拒绝写数据/追加数据/写属性
...
[R06/upgrade-deny-ace] state=replace_failed target=OLD-CONTENT output=Access is denied.
```

断言与第一种形态相同且全部成立：不得报 `applied`、旧版本内容原样、失败路径仍拉起旧版本。
收尾用 `icacls "<目标>" /remove:d "<用户>"` 清掉拒绝项（已实测清理后写回正常）。

**这里踩到一个值钱的坑，值得记下来（用错夹具会让"无权限"验成假象）**：

`icacls /deny "<用户>:(W)"` **不是**"只拒绝写"。`(W)` 映射到 `GENERIC_WRITE`，其中含 `SYNCHRONIZE`，
于是**连按读方式打开文件都会被拒**——.NET 的 `File.ReadAllText` 直接抛 `UnauthorizedAccessException`。
第一版用例就是这样失败的：脚本还没被判定，**测试宿主自己先读不了目标文件**（那次失败是夹具缺陷，不是产品缺陷）。
改用细粒度 `(WD,AD,WA)` 后：**读 OK、写被拒**，才是"只没有写权限"的正确模拟。实测对照：

| 拒绝项 | 读 | 写 | `cmd` 的 `copy /Y` 覆盖 |
| --- | --- | --- | --- |
| `(W)` | **被拒**（连读都不行） | 被拒 | 未测（夹具本身就不可用）|
| `(WD,AD,WA)` | OK | 被拒 | **exit=1，目标内容仍是 OLD-CONTENT**（不是"删掉再重建"）|

顺带否掉一个想当然的假设：`copy /Y` 覆盖不可写目标时**不会**先删除再重建（否则拒绝写拦不住它），
它就是失败并保持原文件——这也正是"失败即不动目标"这条性质在 OS 层面的来源。

## 3. 这两种形态共同回答了什么

断言全部成立：

| 断言 | 结果 |
| --- | --- |
| 不得报 `applied` | ✅ 实测 `state=replace_failed` |
| 旧版本内容原样（没有半截写入） | ✅ `target=OLD-CONTENT` |
| 替换前留下了备份（可恢复） | ✅ 备份文件存在 |
| 失败路径仍然启动旧版本（用户不能没有启动器） | ✅ 重启标记文件被创建 |

**新细节**：`Access is denied.` 出现了**两次** —— 第一次是替换被拒，第二次是**回滚用的
`copy /Y <备份> <目标>` 也被同一权限拒掉**。也就是说：目标不可写时**回滚动作本身也会失败**，
旧版本之所以完好，是因为"失败的 copy 没有改动目标"，而不是因为回滚成功。
这一点此前没有被验证过；它说明"无权限"场景下**不能依赖回滚**来保证用户还有可运行版本，
可靠的是"失败即不动目标 + 仍然拉起旧版本"，而这两条正是本用例断言的内容。

## 4. 边界（不得过度解读）

- 已验证**两种**形态：只读文件属性、ACL 显式拒绝 `(WD,AD,WA)`。
  **磁盘满、被安全软件拦截**仍未验证（无法在受控环境里安全模拟），不要按"无权限已全覆盖"表述。
- 只验证了 `.bat` 路径；Unix 分支用 `chmod` 模拟，本机无法验证。
- 本用例不涉及真实用户配置/凭据/存档；全部在临时中文目录内完成并清理。

## 5. 相关命令与产物

- 聚焦类全量（7 个用例，含两种"无权限"形态）：全部 `已通过`。
- 整轮验证见 `../run-20261006-0030-final/final/`（C# 用例数 901 → 902 → **903**）。
