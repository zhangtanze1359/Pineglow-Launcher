# 三个 blocker 的补丁：**在真实打过补丁的构建里全部复现验证通过**（第 74 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动工作区一个字节**（`modified=44 / staged=0` 前后一致）。
做法：把仓库**复制到 `%TEMP%`**（排除 `.git/bin/obj/node_modules/docs`）→ 在副本里 `git init` → `git apply` 四个补丁 →
`dotnet build -c Release` → 用这个**打过补丁的构建**跑复现测试。**工作区全程未被触碰**（补丁只落在副本里）。

## 1. 补丁应用与构建：干净

```
robocopy exit=1（成功）
已应用 proposed-fix-versionresolver.diff（ok）
已应用 proposed-test-versionresolver.diff（ok）
已应用 proposed-fix-installer-url.diff（ok）
已应用 proposed-fix-configdata-default.diff（ok）   ← 实际文件名 proposed-fix-configstore-default.diff
build exit=0  用时=5s   0 个警告  0 个错误
```

## 2. 复现验证：**4/4 全部成立**

| 项 | 修复前（候选）| 打过补丁的构建 |
| --- | --- | --- |
| **B0 默认实例 id** | 连续三次 `/api/state` 得到**三个不同** id | **三次完全相同**（`3793e1d006f2437089c7406af2df6e70`）✅ |
| **B0 对默认实例操作** | `/launch` → **400 `实例不存在：…`** | `/launch` → **400 `实例未关联游戏版本，请先在设置中安装版本`** ⇒ **id 已被正确解析**，只剩业务门槛 ✅ |
| **B1 加载器 `arguments` 合并** | 合并后只剩 Fabric 自己那条 ⇒ 丢库路径 | `["-Djava.library.path=${natives_directory}","-cp","${class_path}","-DFabricMcEmu= net.minecraft.client.main.Main "]` ⇒ **父版本库路径保留 + 子版本条目追加** ✅（`mainClass=…KnotClient`、`libraries=2` 也对）|
| **B2 安装器 URL** | `…/net/minecraftforge/**forge/forge**/1.20.1-47.4.3/…` ⇒ 404 | `…/net/minecraftforge/forge/1.20.1-47.4.3/forge-1.20.1-47.4.3-installer.jar` ⇒ **单段**、**GET 200 / 6,068,826 B** ✅ |

**判定：`PASS：打过补丁的构建里，B0（id 稳定 + 不再报实例不存在）、B1（合并保留库路径）、B2（URL 单段且 200）全部成立`**

## 3. 更进一步：**打过补丁的副本跑 C# 全量测试 = 904/904 全绿**

```
已通过! - 失败: 0，通过: 904，已跳过: 0，总计: 904（71 秒）
```
即 **903 个原有测试 + 1 个新增回归测试**（`VersionResolverInheritanceTests`）全部通过 ✅
⇒ 补丁**能编译、不破坏现有测试、且新回归测试真的在跑**。

## 4. 还没做（下一步清单，属 9 步验证计划的剩余项）

- 打过补丁的构建上跑 **Node 304 项**（代码未动，预期不变，但按清单应跑）；
- 打过补丁的构建做 **Vanilla / Fabric / Quilt 三种真实启动**（其中 **Fabric 启动**是最强的端到端证据：候选必崩、补丁后应进渲染管线）；
- 打过补丁的构建跑**全量验证链**（`verify-source-snapshot.ps1`）并出**新的 sha256**；
- 这些都要在**你批准修**之后、在**工作区**里正式做（本轮只是在副本里证明"补丁有效"）。

## 5. 产物与复现

```powershell
# ① 复制仓库 + 应用补丁 + 构建（全程在 %TEMP%，不动工作区）
powershell -File ...\run-20261006-2500-patched-build\apply-patches-and-build.ps1 `
  -RepoRoot D:\松光启动器 -DestRoot %TEMP%\pg-patched `
  -PatchDir docs\release-remediation-evidence-2026-10-05\run-20261006-2130-patch-set -JsonPath <证据>\patched-build.json
# ② 用打过补丁的构建跑复现（B0 走 HTTP，B1/B2 走反射探针）
powershell -File ...\verify-patched-build.ps1 -PatchedRoot %TEMP%\pg-patched -ProbeSource ...\Program.cs -JsonPath <证据>\patched-verify.json
# ③ 打过补丁副本的全量 C# 测试
dotnet test dev\PineglowLauncher.Tests\Pineglow.Launcher.Tests.csproj -c Release   # 在 %TEMP%\pg-patched 里
```

产物：`patchbuild-step.log`（复制/打补丁/构建）、`patched-build.json`、`verify-patched-step.log` + `patched-verify.json`（复现 4 项）、
`probe-output.txt`（探针原始输出）、`patched-dotnet-test.log`（904/904）。
> `%TEMP%\pg-patched` **本轮保留**，供下一轮做"打过补丁的 Fabric 真实启动"。
