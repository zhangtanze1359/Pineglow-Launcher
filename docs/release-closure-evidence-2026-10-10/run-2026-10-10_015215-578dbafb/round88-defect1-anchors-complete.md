# 记录 88：缺陷 1 的接线锚点**全部取到** —— 下一轮可直接写补丁

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 04:04–04:16（+08:00）
性质：**只读审计，产品目录零改动**（0 staged）。

---

## 上一轮缺的那个未知：分派方法的签名与类型

```csharp
// InstancePackService.cs:475
private async Task<InstalledLoaderProfile> InstallLoaderAsync(
    ModLoaderKind kind, GameInstance instance, string mcVersion,
    string loaderVersion, string javaPath, Action<string> progress)
```

| 需要的东西 | 取到的 |
| --- | --- |
| 分派方法 | `:475`，**`private`** → 需改 `public`（唯一的结构性改动） |
| 返回值 | `InstalledLoaderProfile { VersionId, MinecraftVersion, LoaderKind, LoaderVersion }`（`InstalledLoaderProfile.cs`） |
| **字符串 → 枚举** | **`ModLoaderInfo.TryParse(loaderKind, out var kind)`** —— `:326` 已经在用，**正是路由需要的入口** |
| 四个分支 | `ModLoaderKind.Fabric / Quilt / Forge / NeoForge`，各自 new 对应服务（`FabricService` / `QuiltService` / `ForgeService` / `NeoForgeService`） |
| Java 解析 | `LocalApiService.ResolveJavaPath(config, instance, mcVersion)`（启动路径就用它） |
| 服务构造 | `new InstancePackService(ApiHttp, defaultGameDir, CreateImportLogger(), BuildLoaderInstallerJvmArgs(config))` —— **四个参数都是现成的**（P-IMPORTLOG / P-PROXYJVM 备好的） |

## 因此路由处理器长这样（下一轮照抄）

```csharp
private static async Task HandleInstallLoaderAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
{
    JsonDocument body = await ReadBodyAsync(request);
    string loaderKind    = JsonUtil.GetString(body.RootElement, "loaderType")?.Trim() ?? string.Empty;
    string loaderVersion = JsonUtil.GetString(body.RootElement, "loaderVersion")?.Trim() ?? string.Empty;

    if (string.IsNullOrWhiteSpace(loaderKind))    throw new InvalidOperationException("loaderType 不能为空");
    if (string.IsNullOrWhiteSpace(loaderVersion)) throw new InvalidOperationException("loaderVersion 不能为空");
    if (!ModLoaderInfo.TryParse(loaderKind, out ModLoaderKind kind))
        throw new InvalidOperationException("不支持的加载器：" + loaderKind);   // OptiFine 会走到这里

    AppConfig config = LoadConfig();
    GameInstance instance = RequireInstance(config, instanceId);
    string mcVersion = string.IsNullOrWhiteSpace(instance.BaseMinecraftVersion)
        ? instance.MinecraftVersion : instance.BaseMinecraftVersion;
    string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory)
        ? PlatformPaths.GetDefaultMinecraftDirectory() : config.GameDirectory;
    string javaPath = ResolveJavaPath(config, instance, mcVersion);

    InstancePackService pack = new InstancePackService(
        ApiHttp, defaultGameDir, CreateImportLogger(), BuildLoaderInstallerJvmArgs(config));
    InstalledLoaderProfile profile = await pack.InstallLoaderAsync(
        kind, instance, mcVersion, loaderVersion, javaPath, _ => { });

    instance.MinecraftVersion = profile.VersionId;
    instance.LoaderType        = kind.ToString();
    instance.LoaderVersion     = loaderVersion;
    instance.BaseMinecraftVersion = mcVersion;
    AppConfig latest = LoadConfig();
    GameInstance target = RequireInstance(latest, instanceId);
    target.MinecraftVersion = profile.VersionId;
    target.LoaderType = kind.ToString();
    target.LoaderVersion = loaderVersion;
    target.BaseMinecraftVersion = mcVersion;
    SaveConfig(latest);

    await WriteJsonAsync(response, 200, new { ok = true, versionId = profile.VersionId, loaderType = kind.ToString(), loaderVersion });
}
```

配套两处：**① `InstallLoaderAsync` 改 `public`**；**② 路由注册**（`POST /api/instances/{id}/loader`，
放在 `:466` 那条 `import` 路由旁边，`seg.Length == 3`）。

## 本轮没做什么（如实）

**没有写补丁。** 锚点齐了，但"改可见性 + 加处理器 + 注册路由 + 构建 + 测试 + 行为验证"
不是本轮剩余预算能走完的。**缺陷 1 仍然无补丁。**

**下一轮可以直接照抄上面的代码** —— 这是我这一轮唯一想留下、也确实留下了的东西。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪且十六条合并验证通过的补丁 | 16 条 |
| **仍无补丁** | **1 条**：缺陷 1（本轮锚点齐备，下一轮可直接写） |
| 待你决定 | ① 提交（(a) 2609 / (a′) 2180 / (b) 204）；② C 的范围；③ 是否要做缺陷 1 |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
