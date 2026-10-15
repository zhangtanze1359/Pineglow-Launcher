# R06「多实例并发」：**PASS**（两个实例同时跑、同实例重复启动被挡、退出后守卫解除）（第 57 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
这一格此前记 NOT_RUN（"多实例并发仍未验"）。本轮用真机跑通。

## 1. 代码事实（先看再测）

`RunningInstanceRegistry` 是**按 `instanceId` 键**的 `ConcurrentDictionary<string, Entry>`
⇒ 语义上**不同实例可以同时运行**，守卫只针对**同一个实例**；`TryBeginLaunch` 是原子守卫（源码注释里写明是为消除
"先 IsRunning 再 MarkRunning"的竞态窗口而加的）。

## 2. 实测（两个实例、共享 gameDir、同一个 JRE 17）

| 步骤 | 结果 |
| --- | --- |
| 安装 1.18.2（异步任务，轮询到终态）| `status=completed`，用时 **250 s** |
| 启动 `conc-a` | `{"ok":true,"pid":31644}` |
| 启动 `conc-b` | `{"ok":true,"pid":24084}` |
| 与 gameDir 匹配的 java 进程 | **2 个：31644、24084**（两个不同实例**真的同时在跑**）|
| 观察 60 秒 | **2/2 全程存活** |
| **重复启动 `conc-a`** | **HTTP 400**：`{"error":"该实例已在运行（进程 31644），请勿重复启动。"}` |
| 杀掉两个 JVM 后再启动 `conc-a` | `{"ok":true,"pid":15280}` ⇒ **守卫已解除** |

**判定：`PASS：两个不同实例可并发运行；同一实例重复启动被 400 挡住；退出后守卫解除`**

## 3. 意义

- R06 的"多实例并发"由 NOT_RUN → **PASS**，且**守卫语义是"按实例"而不是"全局单例"**——
  这解释了为什么第 46 轮"运行中守卫"只挡同实例的重复启动，而不会误伤另一个实例。
- 顺带复核了两条**我自己**在第 56 轮踩过的坑（本轮脚本已按正确方式写）：
  `POST /api/versions/download` 必须**轮询 `/api/downloads` 到终态**（异步排队）；
  该端点返回 **`{"tasks":[…]}`（对象）**，按数组取 `id` 会空转到超时。

## 4. 边界

- 验的是"**两个**实例并发"；**没有**压测更多实例，也**没有**验"两个实例写同一份共享 `saves/`/`mods/` 的并发一致性"
  （产品默认 `sharedGameDirectory=true`，多个实例共享同一 `.minecraft`；同文件并发写的语义本轮未涉及）。
- 内存占用未做上限测试（本次两实例各 4096 MB，本机 15.2 GB 可用）。

## 5. 复现

```powershell
powershell -File ...\run-20261006-1700-multi-instance\verify-multi-instance.ps1 `
  -ExePath <候选 EXE> -JavaExe <JRE17>\bin\java.exe -JsonPath <证据>\multi-instance.json `
  -WorkRoot %TEMP%\pg-conc -Version 1.18.2
```

产物：`multi-instance.json`（判定/两个 pid/重复启动的 400 原文）、`step.log`（逐步时间线，含安装进度百分比）。
