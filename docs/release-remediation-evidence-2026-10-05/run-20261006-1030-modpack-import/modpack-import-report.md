# 真实整合包导入端到端验收：**PASS（15 项 0 失败）** —— R06 三项一次跑通（第 43 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
包：**Fabulously Optimized v3.15.0**（Modrinth 官方 CDN，`.mrpack` 1,245,888 B，`minecraft=1.18.2`、`fabric-loader=0.19.3`、
清单 **43 个文件 / 27.4 MB** + **60 条 overrides**）。
接口：候选产物自己的 `POST /api/instances/import`（base64 + `operationId`；**同步**接口，导入结束才返回）。

## 1. 结果（15 项断言，0 失败）

| # | 检查 | 结果 |
| --- | --- | --- |
| 1 | 实例已登记 | ✅ `name=Fabulously Optimized 导入`，`version=fabric-loader-0.19.3-1.18.2` |
| 2 | 实例游戏目录存在 | ✅ `<.minecraft>/instances/Fabulously Optimized 导入` |
| 3 | **清单里 43 个 mod 全部落盘且 SHA1 一致** | ✅ `ok=43 missing=0 bad=0` |
| 4 | **overrides 已应用（抽查 60 条）** | ✅ `checked=60 missing=0` |
| 5–7 | **原版 1.18.2 client.jar** | ✅ 20,259,661 B 与官方一致；SHA1 `2e9a3e3107cc…` 与官方一致 |
| 8–9 | **assets 完整性** | ✅ 索引 `1.18`，**3052/3052 个对象齐全、0 截断** |
| 10 | **加载器已安装** | ✅ `versions/` 下同时有 `1.18.2` 与 `fabric-loader-0.19.3-1.18.2` |
| 11–12 | **Java 由产品自动安装** | ✅ `runtimes/java-17-windows-x64/jdk-17.0.20.1+1-jre/bin/java.exe`，且 `openjdk version "17.0.20.1"` 可执行 |
| 13 | libraries | ✅ 52 个 jar |
| 14 | mods 目录 | ✅ 46 个 jar（43 清单 + 3 条 overrides 里的 jar）|
| 15 | 写盘停止（导入结束） | ✅ 磁盘连续 3 次采样不变，游戏目录 **556.8 MB** |

⇒ **一次导入同时验证了三件此前未验的事**：
① **整合包导入**（解析 / 下载 / 哈希 / overrides / 加载器）；
② **Java 自动安装**——这是 `JavaRuntimeService.InstallAsync` 的**唯一调用点**（第 41 轮发现），
   本轮是**产品自己下载 + 校验 SHA256 + 解压**的完整路径（第 42 轮只验证了"来源/布局/被发现"）；
③ **现代版本真实安装**（3052 个 assets 对象逐字节核对，比第 40 轮的 1.5.2 更能代表现代版本）。

## 2. 本轮的两个"操作层"教训（都已修）

1. **主脚本自己崩了，但导入没崩**：轮询磁盘时写了
   `(Get-ChildItem ... | Measure-Object Length -Sum).Sum` —— 在 `Set-StrictMode` 下**空管道没有 `Sum` 属性**，
   于是访问它直接抛错、脚本整段中断。**但导入是在启动器进程里同步跑的**，客户端脚本死掉不影响服务端，
   磁盘继续从 206 MB 涨到 556 MB。
   ⇒ 我改用**事后核对**（`verify-modpack-result.ps1`）：端口从启动日志取（`127.0.0.1:57918`），
   会话令牌从 `http://127.0.0.1:<port>/` 的 meta 标签现取，**不重启任何东西**就对已完成的导入逐项验收。
   主脚本里的三处同类表达式已统一改成 `DirBytes()` 辅助函数（空目录返回 0）。
2. 这也说明：**"脚本失败"与"被测行为失败"必须分开判断**——这次如果我把脚本崩溃当成导入失败，就会得出完全相反的结论。

## 3. 顺带查清的一个小缺陷：错误信息让用户"用一个不存在的东西"

`LocalApiService.cs:1411` 在超过 24 MiB 上传上限时报：
> 实例包超过 24 MiB 的 JSON 上传上限，**请使用流式导入**。

但核对后：**没有任何分片/流式导入路由**（`seg[...]` 里没有 `upload/chunk/stream` 分支），
界面也是**整包 base64** 上传（`ui/js/api.js:872-874`）。⇒ 用户拿一个较大的整合包只会得到一句"请用流式导入"的死胡同提示。
（小知识：`.mrpack` 通常只有清单所以很小——本包仅 1.2 MB；但 **CurseForge 的 zip 常把 overrides 打进去**，很容易超过 18 MB 的实际上限。）
严重度：低-中；修法：改文案，或真的实现分片上传（属产品决定）。

## 4. 诚实边界

- 本轮**没有**"进游戏"：整合包装好了、Java 装好了、prelaunch 前置已具备，但**启动/退出仍属真机项**。
- 本轮**没有**验证 CurseForge / MCM 两种格式的导入（只验了 Modrinth `.mrpack`）；
  另两种格式的代码路径存在（`InstancePackService.DetectModpackFormat`），但需要各自格式的真实包。
- 本轮**没有**验证导入取消（`operationId` + `/import/cancel`）——那条路径有专门的自动化测试与早前轮次的干跑。

## 5. 复现命令

```powershell
# 一次性端到端（会崩溃的那个版本已修好 DirBytes）
powershell -NoProfile -ExecutionPolicy Bypass -File `
  docs\release-remediation-evidence-2026-10-05\run-20261006-1030-modpack-import\verify-modpack-import.ps1 `
  -ExePath %TEMP%\pineglow-keep\candidate-5009cf73d0801e83.exe -MrpackPath <Fabulously.Optimized-*.mrpack> `
  -WorkRoot %TEMP%\pg-modpack -JsonPath <证据>\modpack-import.json

# 事后核对（用于"服务端还在跑、客户端脚本已死"的情况）
powershell -NoProfile -ExecutionPolicy Bypass -File `
  ...\verify-modpack-result.ps1 -Port <端口> -ColdDir <冷目录> -MrpackPath <同一个 .mrpack> -JsonPath <证据>\modpack-result.json
```

产物：`modpack-result.json`（15 项断言 + 实例/Java/assets/时间线）、`modpack-result.log`、
`modpack-import.log`（主脚本中断的原始现场）。
