# 🔴 找到第二个 blocker：**Forge / NeoForge 的安装器 URL 多了一段 ⇒ 必然 404**（第 61 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**（只写了 gitignored 的 `bin/obj`）。
这也**更正我第 51/52 轮的判断**：那个 404 **不是环境问题**，是**产品把 URL 拼错了**。

## 1. 怎么找到的：用**产品自己的代码**跑一遍

构建启动器（`dotnet build … -c Release`，1 秒，只写 `bin/obj`），写一个探针程序**引用构建出的 `Pineglow.Launcher.dll`**，
直接调用产品自己的类型（`ForgeService` / `LoaderInstallHelper` / `MirrorHelper`），并用反射拿到私有的 `GetInstallerUrl`：

```
[3] 反射调用 GetInstallerUrl：
    GetInstallerUrl("1.20.1-47.4.3") = https://maven.minecraftforge.net/net/minecraftforge/forge/forge/1.20.1-47.4.3/forge-1.20.1-47.4.3-installer.jar
                                                                                        ^^^^^^^^^^ ← "forge" 出现两次
[4] 裸 HttpClient GET：
    200  https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.3/forge-1.20.1-47.4.3-installer.jar
[5] 产品代码路径 LoaderInstallHelper.DownloadFileAsync：
    [helper] Forge installer: forge-1.20.1-47.4.3-installer.jar
    RESULT=PASS 下载成功 6068826 B          ← 换成"正确 URL"，产品自己的下载器完全正常
```

## 2. 两两对照（HEAD 实测）

| URL | 结果 |
| --- | --- |
| Forge **正确**：`…/net/minecraftforge/forge/1.20.1-47.4.3/forge-…-installer.jar` | **200**，6,068,826 B |
| Forge **产品拼法**：`…/net/minecraftforge/**forge/forge**/1.20.1-47.4.3/…` | **404** |
| NeoForge **正确**：`…/net/neoforged/neoforge/21.1.254/neoforge-…-installer.jar` | **200**，6,972,013 B |
| NeoForge **产品拼法**：`…/net/neoforged/**neoforge/neoforge**/21.1.254/…` | **404** |

## 3. 根因：一行拼接把 artifact 段写了两遍

`InstallerLoaderService.GetInstallerUrl`（`dev/PineglowLauncher/Pineglow.Launcher.Services/InstallerLoaderService.cs:157`）：

```csharp
return $"{_artifactBaseUrl}/{_groupPath}/{_artifactId}/{mavenVersion}/{_artifactId}-{mavenVersion}-installer.jar";
```

而两个子类传参时 **`_groupPath` 里已经包含了 artifact 段**：

| 子类 | `_groupPath` | `_artifactId` | 拼出来 |
| --- | --- | --- | --- |
| `ForgeService` | `net/minecraftforge/forge` | `forge` | `…/net/minecraftforge/forge/**forge**/…` ❌ |
| `NeoForgeService` | `net/neoforged/neoforge` | `neoforge` | `…/net/neoforged/neoforge/**neoforge**/…` ❌ |

**metadata 的 URL 是单独传的**（`…/maven-metadata.xml`，拼法正确）⇒ 所以**版本列表能取到、只有安装器下不来**，
这也解释了为什么第 51 轮的现象是"导入跑到一半、在下载安装器那一步 404"。

## 4. 修法（一处、且已验证）

```csharp
// 去掉多余的 /{_artifactId}（_groupPath 已含 artifact 段）
return $"{_artifactBaseUrl}/{_groupPath}/{mavenVersion}/{_artifactId}-{mavenVersion}-installer.jar";
```

按这个拼法，Forge/NeoForge 都得到上表里的"正确 URL"（**200**），而且**产品自己的 `DownloadFileAsync` 在正确 URL 上实测成功**
（`RESULT=PASS 下载成功 6068826 B`）⇒ **修法在行为上已被验证**（补丁文件：`proposed-fix-installer-url.diff`，**未应用**）。

## 5. 影响（为什么是 blocker）

- **Forge 与 NeoForge 实例在当前版本根本无法安装**（任何机器上都一样，与网络无关）。
- 这解释了第 51/52 轮那个"未定因 404"：我每次手工探的都是**正确 URL**（200），而产品请求的是**重复段 URL**（404）——
  两边探的不是同一个地址，所以怎么查都"解释不通"。**这是我的排查盲点，已更正。**
- 与第 47/53 轮的**启动缺陷**叠加：即便把 URL 修好、装上了 Forge/NeoForge，它们**仍会因为命令行缺 `-Djava.library.path` 而起不来**。
  ⇒ 要让 Forge/NeoForge 真正可用，**两处都得修**。

## 6. 复现

```powershell
# ① 构建（只写 bin/obj）
dotnet build dev\PineglowLauncher\Pineglow.Launcher.csproj -c Release
# ② 探针（引用构建产物，调产品自己的代码 + 反射拿 GetInstallerUrl）
#    见本目录 Program.cs；把编译出的 forgeurlprobe.dll 放进启动器输出目录后 `dotnet exec forgeurlprobe.dll`
# ③ 对照 HEAD（正确 vs 产品拼法）
Invoke-WebRequest 'https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.3/forge-1.20.1-47.4.3-installer.jar' -Method Head
Invoke-WebRequest 'https://maven.minecraftforge.net/net/minecraftforge/forge/forge/1.20.1-47.4.3/forge-1.20.1-47.4.3-installer.jar' -Method Head   # 404
```

产物：`Program.cs`（探针源码）、`probe.log`（上面那段完整输出）、`proposed-fix-installer-url.diff`（未应用的补丁）。
