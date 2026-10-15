# 第 22 轮：两条修复的**现场验证**（都是 A/B，都有对照组）

运行标识：`run-2026-10-10_015215-578dbafb`（第 22 轮追加）
时间：2026-10-11 16:48–17:15，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本与夹具里完成。
被验产物（补丁版）：`694A73F53635C2C4D9E69E9FBC1474FD2686141AD185E05AD373ED515AD27850`（53,052,722 字节，含全部 6 条修复）。

第 21 轮把 P-UA 的因果链闭合了，但 P-PROXYJVM 与 P-FAKEIP 还只有"编译通过 + 单测绿"。本轮把这两条也做成**带对照组的现场证据**。

---

## 1. P-FAKEIP：开关在**真实 fake-IP 网络**下确实救回来了

**做法**：用**打过补丁的 EXE**，在真实环境（本机 DNS 现在返回 `198.18.0.28` + `2001:2::1c`）下取官方新闻图片。
每次运行前**清空图片缓存**（`data\cache\news-images`），并且**每次换一张不同的图片 ID**，杜绝"从缓存里读到上一次的结果"。

| 运行 | `newsFakeIpCompat` | 结果 |
| --- | --- | --- |
| 第 1 次 | `false` | **400 `{"error":"dns_private"}`**（拒绝） |
| **第 2 次** | **`true`** | **HTTP 200，38,769 字节，magic `FF D8 FF E0` → 真实 JPEG** |
| 第 3 次 | `false` | **400 `{"error":"dns_private"}`**（拒绝） |

**对照组（未打补丁的出厂 EXE，第 18 轮，同样是开关打开）**：**400 `{"error":"dns_mixed"}`**。

→ 同一个场景（开关打开 + 双栈 fake-IP），修复前 **400**、修复后 **200 并拿到真图**；开关关掉时**仍然拒绝**（安全性质未被削弱）。

### 1.1 过程里的一次自纠

我第一次跑这个 A/B 时两种模式**都返回 200**，看起来像是"开关根本没起作用"。
没有就此下结论——去查磁盘发现 `data\cache\news-images\2a43e476….img`：**第二次是命中缓存**，根本没走 DNS 与 SSRF 校验。
加上"清缓存 + 每次换 ID"之后才得到上面这张干净的三行表。**如果当时直接写结论，就会得出完全相反的错误判断。**

---

## 2. P-PROXYJVM：安装器**子进程真的收到了代理参数**

**做法**（不联网、不下载安装器，只隔离命令行构造这一段）：
把我的一个 `fakejava.exe`（把收到的 argv 逐行打印出来）当作 `javaPath` 传给产品的 `RunInstallerAsync`，看子进程到底收到了什么。

```
=== 传入 BuildLoaderInstallerJvmArgs(config) 产生的参数（打过补丁） ===
  ARG|-Dhttp.proxyHost=127.0.0.1
  ARG|-Dhttp.proxyPort=7897
  ARG|-Dhttps.proxyHost=127.0.0.1
  ARG|-Dhttps.proxyPort=7897
  ARG|-jar
  ARG|dummy-installer.jar
  ARG|--installClient
  ARG|C:\...\pineglow-proxyjvm-...

=== 不传额外参数（修复前的行为） ===
  ARG|-jar
  ARG|dummy-installer.jar
  ARG|--installClient
  ARG|C:\...\pineglow-proxyjvm-...
```

两个要点都成立：

1. **四个代理参数确实出现在子进程的命令行里**；
2. **位置正确**——在 `-jar` **之前**（JVM 选项必须在 `-jar` 之前才会被当成 JVM 选项，写在后面就变成程序参数了）。

另外两端的接线也一并实测：

| 检查 | 打过补丁 | 未打补丁（基线） |
| --- | --- | --- |
| `BuildLoaderInstallerJvmArgs(config)`，`ProxyEnabled=true` | `-Dhttp.proxyHost=127.0.0.1 -Dhttp.proxyPort=7897 -Dhttps.proxyHost=127.0.0.1 -Dhttps.proxyPort=7897` | **方法不存在**（`GetMethod` 返回 null） |
| 同上，`ProxyEnabled=false` | `(empty)` | — |

把它和第 8 轮的 A/B 接起来，因果链闭合：
**同样的安装器、同样的代理环境——不加这四个参数 → `ExitCode=1 Downloading minecraft client failed, invalid checksum`；加上 → `ExitCode=0 Successfully installed client into launcher`。**

---

## 3. 本轮的工具失误（两条，都花了时间）

1. **探针项目里混进了第二个 `Program.cs`**：我为了排查构造函数签名，在探针目录下建了 `diag/` 子项目。SDK 式项目会把子目录里的 `.cs` 一起 glob 进来，于是出现「只有一个编译单元可具有顶级语句」的编译错误——而我把 build 输出截断到只看最后两行，**没看见错误**，于是连跑了三次都用的**旧产物**，还奇怪为什么改的代码没生效。
   教训：**截断构建输出会掩盖编译失败**；探针目录里不要再放第二个顶级语句项目。
2. **构造函数签名凭记忆写**：`MinecraftDownloader` 的第三个参数是 `Action<MinecraftDownloadProgress>`，我按 `Action<string>` 传，反射抛类型不匹配。改成按运行时拿到的参数类型构造委托才好。

---

## 4. 至此六条补丁各自的可信度

| 补丁 | 证据等级 |
| --- | --- |
| **P-UA** | 反射 A/B（未打补丁不发 UA / 打补丁发 `PineglowLauncher/1.0.0`）+ 第 12 轮实测该 UA 得 200、无 UA 得 403 |
| **P-PROXYJVM** | **本轮**：子进程命令行 A/B（参数存在且在 `-jar` 之前）+ 第 8 轮实测这四个参数让安装器从 exit 1 变 exit 0 |
| **P-FAKEIP** | **本轮**：真实 fake-IP 网络下的三行 A/B（关→400、开→200 真图、再关→400）+ 未打补丁对照 400 |
| **P-FABRICMETA** | 2 个回归用例断言重写结果；第 9/10 轮已用 HTTP 状态码证明 `<base>/fabric-meta/...` 得 200、`<base>/...` 得 404 |
| **P-IMPORTLOG** | 编码正确性由 947 用例与人工核对保证；**没有**做端到端（要跑一次完整导入才看得到日志）——如实标注 |
| 回归用例 ×6 | 隔离副本全量：基线 941 → 打补丁 **947 通过 / 0 失败**；`dotnet build` **0 警告 0 错误** |

**仍然没有应用到产品目录。** 产品目录 8 个相关文件哈希与开工时一致，0 staged。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
