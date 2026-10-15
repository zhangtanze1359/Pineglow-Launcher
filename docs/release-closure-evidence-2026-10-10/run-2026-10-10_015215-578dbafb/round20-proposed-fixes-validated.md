# 第 20 轮：把三条缺陷变成**已验证的补丁**（未应用），并发现 C 的方案清单漏了一个文件

运行标识：`run-2026-10-10_015215-578dbafb`（第 20 轮追加）
时间：2026-10-11 15:07–15:17，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。所有改动只发生在 `%TEMP%` 的隔离副本里；
`proposed-fixes.patch` 是**补丁文本**，不是已生效的修改。

---

## 1. 为什么要做

前 19 轮把「距离发布」量化得很清楚，但那份清单的成本是**估算**（「一行到几行的改动」）。
本轮把其中三条**落成可以评审、可以一键应用的补丁**，并用完整测试套件证明它们不破坏任何东西——
这样等你决定「修哪几条」时，剩下的是**应用 + 验收**，而不是「先写代码」。

三条（都是第 16 轮对码与前几轮根因分析的直接产物）：

| 编号 | 对应缺陷 | 内容 |
| --- | --- | --- |
| **P-UA** | 缺陷 6（镜像库下载 403） | 给 `CreateApiHttp` 造出的客户端加显式非浏览器 `User-Agent`（复用产品已有约定 `PineglowLauncher/<版本>`） |
| **P-FABRICMETA** | 缺陷 5（Fabric 元数据镜像前缀） | `meta.fabricmc.net` → `<base>/fabric-meta` + 原路径（原来少了 `/fabric-meta`，实测 404） |
| **P-PROXYJVM** | 缺陷 2（代理没传给安装器 JVM） | 把代理设置透传到 Forge/NeoForge 安装器进程的命令行 |

---

## 2. 验证方法与结果

隔离副本 = `dev/` + `ui/` + `.github/` + `global.json` 的完整拷贝（**这一版我把 `.github/` 也拷了**，原因见第 4 节）。

| 步骤 | 结果 |
| --- | --- |
| **基线**（未打补丁的完整副本）`dotnet test -c Release` | **941 通过 / 0 失败**，exit 0（1 分 8 秒） |
| 应用 15 处补丁（见 `apply-proposed-fixes.ps1`） | 全部命中，**每处都断言「恰好匹配 1 次」**，无静默失配 |
| `dotnet build -c Release` | **exit 0，0 个警告、0 个错误** |
| **打补丁后** `dotnet test -c Release` | **943 通过 / 0 失败**，exit 0（1 分 7 秒） |

**943 = 941（原有全部用例，一个没坏）+ 2（本轮新增的 Fabric 元数据映射回归用例）。**

新增的回归用例不是凑数：**全仓库原本对 `MirrorHelper` 一个测试都没有**——
这正是「`meta.fabricmc.net` 映射少了 `/fabric-meta`」能长期存在的问题的原因。两个用例断言：

```
Rewrite("https://meta.fabricmc.net/v2/versions/loader") == "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader"
Rewrite("https://meta.quiltmc.org/v3/versions")        == "https://bmclapi2.bangbang93.com/quilt-meta/v3/versions"
```

---

## 3. 补丁本体

`round20-proposed-fixes/proposed-fixes.patch`（14,799 字节，14 个 hunk）：

| 文件 | 增/删 |
| --- | --- |
| `LocalApiService.cs` | +26 / −3 |
| `MirrorHelper.cs` | +4 / −1 |
| `LoaderInstallHelper.cs` | +12 / −1 |
| `InstallerLoaderService.cs` | +2 / −2 |
| `ForgeService.cs` | +3 / −2 |
| `NeoForgeService.cs` | +3 / −2 |
| `InstancePackService.cs` | +7 / −3 |

**补丁里没有任何附带改动**——我专门核对过：`bomNoiseLines=0`。
（第一次生成时我用的写入编码给 7 个文件都加了 BOM，而它们原本**没有** BOM；这会在补丁里制造 7 行无关噪声。
已改为「按原文件是否带 BOM 决定写入编码」，重新生成后噪声为 0。这类"顺手改了编码"是最容易夹带进提交的东西。）

`P-PROXYJVM` 的形状（保持向后兼容，所有旧调用点与测试都不用改）：

```csharp
// LoaderInstallHelper：新增可选参数，并把它们插在 -jar 之前
public LoaderInstallHelper(HttpClient http, string gameDirectory, MinecraftDownloader downloader,
                           Action<string> log, IReadOnlyList<string>? extraJvmArgs = null)
...
foreach (string jvmArg in _extraJvmArgs) { processStartInfo.ArgumentList.Add(jvmArg); }
processStartInfo.ArgumentList.Add("-jar");            // JVM 选项必须在 -jar 之前

// LocalApiService：与游戏启动用的是同一组值（那里早就这么做了）
private static IReadOnlyList<string> BuildLoaderInstallerJvmArgs(AppConfig config) { ... }
```

---

## 4. 附带发现：**C 的方案清单漏了一个文件，而且漏的那个会让 CI 变红**

我第一次跑测试时用的是只含 `dev/`+`ui/` 的副本，结果 `WorkflowInvariantTests` **12 项全红**。
当时的正确判断是「**是我副本不完整**」——这些用例要读 `.github/workflows/*.yml`——于是补齐 `.github/` 重跑，基线 941 全绿。

但顺着这条线读下去，发现了一个**对 C 有实质影响**的事实：
`dev/PineglowLauncher.Tests/WorkflowInvariantTests.cs` 把「当前这套取舍」**写成了硬不变量**：

| 用例 | 断言 |
| --- | --- |
| `RidPublish_MustNotUseRestoreLockedMode` | **RID 发布的那一行不得出现 `RestoreLockedMode`**（`:148`），理由写在消息里：「实测必然 NU1004（与 --locked-mode 互斥）」 |
| `Workflows_DoNotReferenceRidSpecificLockFiles` | **工作流不得引用 `packages.<rid>.lock.json`**（`:168`），理由：「本项目只有一份 lock」 |

而 C 的方案**恰恰要求这两件事**。我做了实测验证——在隔离副本里把 C 的改动加到 `release.yml` 的发布行上
（追加 `/p:RestoreLockedMode=true /p:NuGetLockFilePath=packages.win-x64.lock.json`），然后只跑这组不变量：

```
失败: 2，通过: 11，总计: 13   exit=1
  失败 Workflows_DoNotReferenceRidSpecificLockFiles(release.yml)
       release.yml 不得引用 RID 专用 lock 文件（本项目只有一份 lock）：packages.win-x64.lock.json
  失败 RidPublish_MustNotUseRestoreLockedMode(release.yml)
       release.yml 的 RID 发布不得使用 RestoreLockedMode —— 实测必然 NU1004（与 --locked-mode 互斥）
```

即：

> **`C-rid-lock-proposal.md` 的「落地时需要改的确切位置」表是不完整的。** 它列了
> `packages.win-x64.lock.json`（新增）、`release.yml:115`、`build.yml:141`、两处守卫、`BuildLockFileTests.cs`，
> **但没有列 `WorkflowInvariantTests.cs`**。而按方案实施，这个文件里的 **2 条不变量会立刻变红**。

这不是「测试碍事」，而是**设计变更必须显式处理的部分**：那两条不变量当初就是用来**捍卫旧取舍**的
（「只有一份 lock」「RID 发布不锁定」）。采用 C 意味着**公开推翻这个取舍**，因此必须：
要么改写这两条不变量的含义（从「不得」改成「必须与 RID lock 并锁」），要么明确记录这次取舍变更的理由。
否则实施者会在 CI 上撞到两条自相矛盾的断言，而不知道为什么。

**对第 15 轮结论的修正**：我在第 15 轮写「C 的三个选项都已验证」——准确的说法是
**「锁定机制本身（restore/publish 命令）三个 RID 都已验证；但工作流改动的配套影响此前未验证，现已发现它需要额外改 `WorkflowInvariantTests.cs`」**。
选项 1/2 仍然可行，只是工作量要把这一个文件算进去。

---

## 5. 状态

- 三条补丁：**已写、已编译、已跑完整测试套件、未应用**。
- 产品目录 `D:\松光启动器`：**零改动**（0 staged；`LocalApiService.cs`、`MirrorHelper.cs`、`LoaderInstallHelper.cs`
  `InstallerLoaderService.cs`、`ForgeService.cs`、`NeoForgeService.cs`、`InstancePackService.cs` 七个文件的哈希与开工时一致）。
- C：**方案仍需补一项**（`WorkflowInvariantTests.cs`），范围决定仍在你手上。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
