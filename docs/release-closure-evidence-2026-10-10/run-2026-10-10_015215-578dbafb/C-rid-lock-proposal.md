# C（RID 发布锁定）诊断与已验证方案

日期：2026-10-10，Asia/Hong_Kong。运行目录：`docs/release-closure-evidence-2026-10-10/run-2026-10-10_015215-578dbafb/`。
**本文件是方案与验证记录，不是实施：产品目录 `D:\松光启动器` 未做任何改动，实验全部在 `%TEMP%` 隔离副本里完成。**

## 1. 现状与根因

- `dev/PineglowLauncher/Pineglow.Launcher.csproj:17` 开了 `RestorePackagesWithLockFile=true`，`packages.lock.json` 只覆盖 `net10.0` 一个段（8,663 字节）。
- 带 RID 的发布把包图改成两个段：`net10.0` **加上** `net10.0/win-x64`（10868 字节），新增 8 个原生资源包与 `Microsoft.NET.ILLink.Tasks`。
- 因此 `dotnet restore -r win-x64 -p:PublishSingleFile=true --locked-mode` 必然 `NU1004`（本轮再次实测：退出码 1，非 RID lock 未被改动）。
- CI 现在的取舍是「非 RID 锁定 + RID 发布不锁定」，并有**守卫**（`build.yml:58`、`release.yml:92`）禁止把 RID 形态写进那两个 `packages.lock.json`，理由是共享文件一旦被改写，非 RID 锁定还原就会失败。这套取舍本身是自洽的——问题只是**发布产物因此没有任何锁定保证**。

## 2. 被证据排除的方案（重要负面结果）

**「在 csproj 里按 RID 条件切 `NuGetLockFilePath`」不行。** 实测：

```
<NuGetLockFilePath Condition="'$(RuntimeIdentifier)' != ''">packages.$(RuntimeIdentifier).lock.json</NuGetLockFilePath>

A 非 RID 锁定还原            exit 0
B RID 锁定还原（未生成时）    exit 1
C RID --force-evaluate       exit 0   ← 非 RID lock 同时被改写成 RID 形态（FBBC9AE6… → 36DACA91…，10868 字节）
D 非 RID 锁定还原（之后）      exit 1   ← 现状守卫与 BuildLockFileTests 都会因此变红
```

原因：一次带 `-r` 的还原里，非 RID 那一趟 MSBuild 求值仍取默认锁路径，于是把 RID 形态写进了共享文件。
**这条必须写进结论：照搬「条件属性」会静默破坏现有 CI 守卫。** 证据 `c-ridlock-experiment.json`、`c-ridlock-experiment.log`。

## 3. 已验证的方案：命令行固定锁路径

不改 csproj（保持默认 `packages.lock.json` 给非 RID 路径），只在**带 RID 的命令行**上固定另一个锁文件：

```
dotnet restore dev/PineglowLauncher/Pineglow.Launcher.csproj `
  -r win-x64 -p:PublishSingleFile=true -p:NuGetLockFilePath=packages.win-x64.lock.json --locked-mode

dotnet publish dev/PineglowLauncher/Pineglow.Launcher.csproj -c Release -r win-x64 --self-contained `
  /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false `
  /p:RestoreLockedMode=true /p:NuGetLockFilePath=packages.win-x64.lock.json
```

实测结果（`c-ridlock-experiment2.json`、`c-ridlock-experiment2.log`）：

| 步骤 | 退出码 |
| --- | --- |
| A 非 RID 锁定还原（锁路径固定为 `packages.lock.json`） | 0 |
| B 生成 RID 锁（`-r win-x64 -p:PublishSingleFile=true … --force-evaluate`） | 0 |
| C RID 锁存在后，再跑非 RID 锁定还原 | 0 |
| D RID 锁定还原（锁路径固定） | 0 |
| E **`publish` 带 `RestoreLockedMode=true`** | **0** |

关键不变量：

- **非 RID `packages.lock.json` 在上述 RID 操作前后哈希完全不变**（`FBBC9AE6…`），且始终是非 RID 形态 —— 现有两条守卫与 `BuildLockFileTests` 不需要放松。
- 新文件 `packages.win-x64.lock.json`：10,868 字节，段为 `net10.0` + `net10.0/win-x64`，含 8 个原生资源包与 `Microsoft.NET.ILLink.Tasks`。
- **锁定发布的产物与当前已验收产物逐字节相同**：53,052,722 字节，SHA-256 `ca8f94c1a6173f23f38f3d2a3c949affc16d5ab67752b662c7806831f350d93e`（与四次流水线发布完全一致）；独立冷启动探针退出码 0，服务内容与当前源码一致（`c-ridlock-artifact2/`）。
  也就是说：采用该方案**不会改变发布产物**，只是补上「依赖图被锁定」这一保证。

## 4. 落地时需要改的确切位置

| 位置 | 改动 | 备注 |
| --- | --- | --- |
| `dev/PineglowLauncher/packages.win-x64.lock.json` | **新增**（由步骤 B 生成后提交） | 唯一的新增产物 |
| `.github/workflows/release.yml:115` | publish 命令追加 `/p:RestoreLockedMode=true /p:NuGetLockFilePath=packages.win-x64.lock.json` | Windows 正式发布 |
| `.github/workflows/build.yml:141` | 同上（矩阵里 `win-x64` 那一行） | linux/osx 行是否跟进退见第 5 节 |
| `build.yml:58` / `release.yml:92` 守卫 | 保持两个 `packages.lock.json` 的「非 RID」规则，**新增**对 `packages.win-x64.lock.json` 的反向断言：文件必须存在、必须含 `net10.0/win-x64` | 否则锁文件缺失/退回旧形态会静默 |
| `dev/PineglowLauncher.Tests/BuildLockFileTests.cs` | 增加同一断言（存在 + RID 形态 + 仍描述本项目） | 把不变量钉在测试里，而不只在 CI 脚本里 |

## 5. 约束、风险与需要你决定的范围

1. **RID 锁是「属性敏感」的**：用 `-p:PublishSingleFile=true` 生成的锁，不能用于不带该属性的 RID 还原（实测步骤 G 退出码 1）。因此 CI 上**锁定还原与 publish 必须传同一组属性**，否则会得到误导性的 NU1004。
2. **锁会过期**：依赖或发布属性变化后必须用 `--force-evaluate` 重新生成并提交，否则 CI 会红。这是「锁定」的本意，但需要在发布流程说明里写明再生成命令。
3. **矩阵范围**：`build.yml` 的矩阵有 `win-x64 / linux-x64 / osx-x64` 三个 RID，而 `release.yml` 只发布 `win-x64`。
   最小方案只锁 Windows 正式发布；若要矩阵一致，需要再生成 `packages.linux-x64.lock.json` 与 `packages.osx-x64.lock.json`（可在同一台机器上生成，RID 图不依赖宿主平台）。
   本轮**只验证了 win-x64**，另外两个 RID 未验证。
4. **另一个选项**：维持现状（RID 发布不锁定），但必须在候选风险记录里写明边界——现有注释其实已经写了，缺的是把它变成可审阅的风险条目并由你确认接受。
5. Linux/macOS 的发布本身仍属「已编译但无正式运行证明」，不在本次 Windows 范围内。

## 6. 本轮未做（需授权）

- 未把 `packages.win-x64.lock.json` 写入产品目录，未改 csproj、workflow 或测试。
- 未生成/验证 linux-x64、osx-x64 的锁文件。
- 未提交、未推送、未运行远程 CI（同 SHA CI 属 F 范围，另需授权）。

## 7. 建议的决策

- **选项 1（推荐）**：采纳第 3/4 节的最小方案，只锁 `win-x64` 正式发布；落地后本地复验两条锁定还原 + 锁定发布 + 冷启动，再交给独立验收。
- **选项 2**：扩展到三个 RID 全矩阵锁定（工作量约 1.5 倍，需要额外生成两份锁并验证）。
- **选项 3**：明确接受「RID 发布不锁定」并写入候选风险记录，不改 CI。

## 8. 本轮的一处自我纠正

第一次做的「锁定发布产物冷启动」检查报 404（无界面），我最初差点按「锁定发布导致 UI 缺失」下结论。实际原因是**我那个隔离副本只复制了 `dev/`，缺 `ui/`**，csproj 的 `..\..\ui\**\*` 嵌入 glob 匹配为空，产物自然没有内嵌界面。补齐 `ui/` 后重新发布，产物与流水线产物逐字节相同、冷启动正常。
原始失败证据保留在 `c-ridlock-artifact/`（其中 `ui-source-clean.json` 记录了 404 与 `ui=<不存在的外置 UI 路径>`），结论以后者为准。
