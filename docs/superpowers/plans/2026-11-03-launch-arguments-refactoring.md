# 启动参数装配行为保持重构 Implementation Plan

> **For agentic workers:** 本次已获实施授权，由当前会话逐步执行并复核；不委派、不提交或发布。Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 提高 `LauncherService` 启动参数装配的可读性，保持所有现有输入对应的参数、顺序及回退行为。

**Architecture:** 仅在现有类内提取读取现代参数数组的私有方法，将 JVM / 游戏参数解析中的跳转改为结构化分支。保留现有方法签名、反射名称、公共 API、异常文案、运行平台判断和依赖。

**Tech Stack:** C# / .NET SDK 10.0.301，net10.0，xUnit 2.9.3 + VSTest。

**Spec:** 用户本次要求“提高代码可维护性，不改变任何现有行为”；适用 `csharp-refactoring` 技能及用户提供的三份执行/验收规则。

## Global Constraints

- SourceRoot / ExecutionRoot：`D:\松光启动器`；起始 HEAD：`47e939c7739baf6aed4920fb58fe2bfce3c7ec65`；起始工作区干净。
- EvidenceRoot：`D:\松光启动器\docs\csharp-refactoring-2026-11-03`。
- 分类：Extract Method / 结构化控制流 / 局部变量改名，均为行为保持操作；不升级依赖、不改变公共可空性、配置或外部名称。
- 现代数组存在但为空时，不得回退到旧参数；缺失/类型不符才回退。重复 JVM 选项、参数顺序、占位符替换、窗口参数覆写、服务器地址 Trim 均须保留。
- 不改已有测试来适配结果；差分探针输出保存在新证据目录，数据均为合成输入。

## Task 1: 参数解析局部重构及验证

**Files:**
- Modify: `dev/PineglowLauncher/Pineglow.Launcher.Services/LauncherService.cs`，只改启动参数装配相关方法。
- Read/test: `dev/PineglowLauncher.Tests/LoaderLaunchAssemblyTests.cs`。
- Create: EvidenceRoot 下基线源文件副本、验证日志、合成参数差分探针及结果报告。

**Interfaces:** 公共方法、现有私有方法名称/签名不变；提取 `private static bool TryGetArgumentArray(MinecraftVersion version, string argumentKind, out JsonElement array)`，供两个解析方法使用。

- [x] 保存精确源文件基线、HEAD、SHA256，读取测试副作用及构建约束。
- [x] 构建原版 Release/net10.0，运行 `LoaderLaunchAssemblyTests`。首次因默认 TEMP 权限在夹具初始化失败；保留日志，仅为测试进程指定工作区 TEMP/TMP 后，未修改的源码基线 10/10 通过。
- [x] 用反射调用现有两个私有解析入口，3,800 组输入 / 7,600 个结果保存基线输出；比较包含顺序和重复值。
- [x] 提取共同数组读取逻辑，以 if / else if / else 保留原先优先级，删除 goto 和标签，将相关临时变量改为含义明确的名称。使用经审阅的小范围绑定编辑，并以编译器验证。
- [x] 显式构建主项目与直接复用服务源文件的 VerifyHost，再运行聚焦测试和完全相同的差分探针：10/10 通过，输出 SHA256 一致。
- [x] 检查差异、旧方法反射可达性及参数输出一致性；上游 `VersionResolverInheritanceTests` 5/5 通过。
- [x] 保存命令、时间（Asia/Hong_Kong）、退出码、测试数量、源文件和产物哈希，交付范围明确的结论。详见 EvidenceRoot 下 README.md / checks.json / final-manifest.json。

## Commands

工作目录均为仓库根目录；运行日志捕获原生退出码。

```powershell
dotnet build dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -f net10.0 -p:RestoreLockedMode=true
dotnet test dev/PineglowLauncher.Tests/Pineglow.Launcher.Tests.csproj -c Release -f net10.0 -p:RestoreLockedMode=true --filter FullyQualifiedName~LoaderLaunchAssemblyTests --logger "console;verbosity=normal"
dotnet build dev/PineglowLauncher/VerifyHost/VerifyHost.csproj -c Release -f net10.0
```

## Stop point

达到本次局部重构验证门槛后交付；不提交、不推送、不发布。`FormalPhaseApproval=NOT_GRANTED`。技术结论只覆盖源码审阅、构建、受控参数差分和实际执行的自动化测试。
