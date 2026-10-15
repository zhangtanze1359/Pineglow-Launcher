# 记录 99：**缺陷 1 端到端验证通过** —— 原版实例经新路由装上了 Fabric

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 07:12–07:30（+08:00）
性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 上一轮为什么超时、这一轮为什么 40 秒就完了

上一轮（420 秒超时）跑到 `3550/4591` —— 它在**下载该版本的 4,591 个资源**。
那些资源**落在了实例目录里并留了下来**（实例目录是绝对路径、跨夹具复用）。
所以本轮**只需要下 8 个 Fabric 库**：

```
[Import] 开始下载 Fabric libraries...
[Import] Fabric 库 1/8: asm-9.10.1.jar
… 8/8: fabric-loader-0.19.5.jar
[Import] Fabric libraries 下载完成
```

**耗时 40 秒。**

## 结果：整条链路走通

```
target (before) = d6-launch-1.21.11   loader=Vanilla   mc=1.21.11
*** INSTALL COMPLETED at t=40s:       loader=Fabric    mc=fabric-loader-0.19.5-1.21.11

POST 的响应：
{"ok":true,"versionId":"fabric-loader-0.19.5-1.21.11",
 "loaderType":"Fabric","loaderVersion":"0.19.5","minecraftVersion":"1.21.11"}

target (after)  = d6-launch-1.21.11   loader=Fabric    mc=fabric-loader-0.19.5-1.21.11
prelaunch ok=True errors=0
```

| 断言 | 状态 |
| --- | --- |
| 路由被走到、处理器在跑 | **✅** |
| 安装**完成** | **✅**（40 秒） |
| **响应体正确**（`versionId` / `loaderType` / `loaderVersion` / `minecraftVersion`） | **✅** |
| **实例字段被回写**（`Vanilla` → `Fabric`） | **✅** |
| **安装后实例可启动**（`prelaunch ok=True`，0 错误） | **✅** |
| 残留进程 | **0** |

## 这条验证关闭了什么

**缺陷 1 的完整链路**：此前"给已存在的实例装加载器"**根本没有入口** ——
唯一能装加载器的方式是导入一个整合包（会新建实例）。
现在：**一个原版实例，一次 POST，40 秒后它就是一个 Fabric 实例，而且自检通过。**

**唯一仍未含的是 UI 入口**（这个补丁只有后端）——
但**能力本身已经端到端证明可用**，UI 只是把它接出来。

## 当前验证版图（补丁版）

| 门槛 | 状态 |
| --- | --- |
| 十八条合并验证（build / C# 949/0 / Node 305/0 / 锁定发布 / 冷启动） | **PASS** |
| 启动到标题界面（Java 存在的夹具） | **PASS** |
| 无 Java → 自动安装 → 启动成功 | **PASS** |
| **原版实例 → 新路由装 Fabric → 字段回写 → 自检通过** | **✅ 本轮** |
| 四种加载器启动（13 条补丁时） | PASS |
| 断点续传 / 界面级 / 更新链门槛 | PASS |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
