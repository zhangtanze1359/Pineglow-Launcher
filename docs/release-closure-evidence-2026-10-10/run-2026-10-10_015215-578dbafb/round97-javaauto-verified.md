# 记录 97：**Java 自动安装的端到端验证通过** —— 缺陷 13 ② 的验证边界完全关闭

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 06:34–06:56（+08:00）
性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 这次测的是**此前一直标为"未观测"**的那件事

夹具：十八条补丁的构建，**所有 `JavaPath` 清空、没有 `runtimes` 目录**，本机也没有系统 Java。

## 结果：整条链路走通了

```
/api/java installations = 0                    ← 前提成立：一个 Java 都没有
POST /api/instances/{id}/launch  →  launch ok=True  pid=22352   ← 启动**成功**

产品日志：
  [Java] 未找到 Java 21，正在自动下载…
  [Java] 正在获取 Java 21 下载信息...
  [Java] 正在下载 Java 21：3.1 KB / 46.7 MB
  …（一路到）46.7 MB / 46.7 MB
  [Java] 正在解压 Java 21...
  [Java] Java 21 自动安装完成：…\data\runtimes\java-21-windows-x64\jdk-21.0.12.1+1-jre
  [Java] 自动安装完成: …\data\runtimes\…
```

配套读数：

| 检查 | 结果 |
| --- | --- |
| `/api/java` 起点 | **0 个** |
| **`launch` 返回值** | **`ok=True`** —— 自动装完之后**启动成功了** |
| `data\runtimes` 目录 | **出现了** |
| 里面的 `javaw.exe` | `…\runtimes\java-21-windows-x64\jdk-21.0.12.1+1-jre\bin\javaw.exe` |
| 残留进程 | **0** |

**下载 46.7 MB 只花了约 30 秒** —— 这台机器的网络比早期轮次好得多。

## 这条验证关闭了什么

| 断言 | 此前 | **现在** |
| --- | --- | --- |
| 路由/校验（P-JAVAAUTO 的入口） | 已验证（第 81 轮，那是 P-LOADERENTRY 的） | — |
| 补丁能应用/编译/测试通过 | ✅ | ✅ |
| **无 Java 的机器上点开始游戏会自动装 Java 并启动成功** | **⏳ 未观测** | **✅ 端到端观测到了** |

**README 承诺的「Java：自动检测与自动安装」，此前只挂在整合包导入上；现在启动路径上也成立了。**

## 顺带说明：这是**第一次**在启动路径上观测到它

记录 96 我给的是"间接物证"（`%TEMP%` 下 5 个由导入路径装出来的 JRE）。
本轮是**直接观测**：从"一个都没有"到"装好并启动成功"，全在同一次运行里发生。

## 当前验证版图（补丁版）

| 门槛 | 状态 |
| --- | --- |
| 十八条合并验证（build / C# 949/0 / Node 305/0 / 锁定发布 / 冷启动） | **PASS** |
| 启动到标题界面（Java 存在的夹具） | **PASS**（第 86 轮） |
| **无 Java → 自动安装 → 启动成功** | **✅ 本轮** |
| 四种加载器启动（13 条补丁时） | PASS |
| 断点续传 / 界面级 / 更新链门槛 | PASS |
| P-LOADERENTRY 的真实安装 | 未观测（路由与校验已验证） |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
