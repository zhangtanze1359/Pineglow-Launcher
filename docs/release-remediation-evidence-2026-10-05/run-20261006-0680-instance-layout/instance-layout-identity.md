# 升级就位的"用户数据布局"核对（第 28 轮）：实例目录与模型与 v1.0.0 完全一致

日期：2026-10-06（Asia/Hong_Kong）。方法：与本地 tag `v1.0.0` 逐文件比对 + 精确 API 检索。
本轮**未改动任何 tracked 文件**。

## 1. 为什么查这个

前四轮把"配置文件/凭据/更新源/清单协议/替换脚本/界面优先级"都过了一遍，还剩一块**用户最在意的东西**：
**实例（instances）**。如果新版换了实例目录约定或实例模型，升级后用户的实例会"从界面上消失"——
即使文件还在磁盘上，也是不能接受的迁移缺陷。

## 2. 结果：三个文件与 v1.0.0 **逐字节一致**

```
git diff --stat v1.0.0 -- dev/PineglowLauncher/Pineglow.Launcher.Services/InstanceIsolationService.cs \
                          dev/PineglowLauncher/Pineglow.Launcher.Services/InstanceService.cs \
                          dev/PineglowLauncher/Pineglow.Launcher.Models/GameInstance.cs
（无输出 ⇒ 三者相对已发布版本没有任何改动）
```

关键约定也逐行相同：

| 约定 | v1.0.0 | 工作区 |
| --- | --- | --- |
| 实例根 | `Path.Combine(GetMinecraftRoot(minecraftRoot), "instances")` | **同一句** |
| 目录名 | `Path.Combine(root, name)`；重名时 `"{name} ({n})"` | **同一句** |
| 默认游戏目录 | `PlatformPaths.GetDefaultMinecraftDirectory()` | 同（且第 27 轮已核对 `.minecraft` 解析逐行相同）|
| 实例模型 `GameInstance` | — | **无差异**（`git diff` 为空）|

⇒ **老用户的实例记录（在 `config.json` 里，第 21 轮已验证加法式保留）与实例文件（在 `<游戏目录>/instances/<名字>`）
在新版里都能被原样找到**，不会消失。

## 3. 顺带补齐"持久化足迹"清单

**注册表：两版都不用**。精确检索 `Microsoft.Win32.Registry|RegistryKey|Registry.CurrentUser|CreateSubKey`
在两版都是**空**；v1.0.0 里出现的 `CredReadW/CredWriteW` 是 **Windows 凭据管理器** API，不是注册表。
（第一次检索时我把 `Run"` 也塞进模式里，结果命中的是工作流里的步骤名——已经用精确模式重做，见上。）

**应用实际会写的目录**（工作区源码里 `GetConfigDirectory()` 下的子目录）：

```
<配置目录>/{ bisect, cache, diagnostics, logs, recovery, runtimes, UI, updates }
<游戏目录>/instances/<实例名>            ← 默认 %APPDATA%\.minecraft
```

其中**只有两处是版本敏感的**，且都已在前几轮查清：

| 目录 | 版本敏感点 | 处置 |
| --- | --- | --- |
| `<配置目录>/UI/` 及其**兄弟目录** `UI/` | 新版解析顺序里"EXE 旁 `UI/`"优先于内嵌包 | 第 25 轮：发布说明要求删除旧 `UI/` |
| `<配置目录>/updates/apply-state.json` | 陈旧失败记录会被继承显示 | 第 26 轮：可选产品取舍 |

其余（`bisect`/`cache`/`diagnostics`/`logs`/`recovery`/`runtimes`）都是按功能分目录、按 id 命名的
普通缓存与记录，跨版本共存无冲突。

## 4. 结论

**用户数据布局在 v1.0.0 → 候选之间没有变化**：实例模型/目录约定逐字节相同，默认游戏目录相同，
注册表零足迹，凭据与配置位置相同。升级不会让实例消失、也不会留下需要清理的注册表项。
至此，"≤1.0.0 能否安全升级"这条线在**数据层面**已经查全（见复评 §3.1 汇总表）。

## 5. 边界

- 仍是**源码级**比对（未跑真实 v1.0.0 二进制；理由见第 27 轮 §3：约定要求凭据只读）。
- 未覆盖：**打包成安装器/目录包**分发的场景（本项目没有安装器，故不适用）；
  以及用户在配置里自定义过 `GameDirectory` 的情形——那种情况下路径由用户输入决定，
  新版对它的处理与 v1.0.0 相同（`ResolveGameDirectory` 未被本轮改成不同语义，但**未单独逐行比对**）。
