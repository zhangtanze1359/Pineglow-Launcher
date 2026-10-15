# 记录 79：P-IMPORTTASK 的**行为验证** —— 本地导入现在真的出现在进度界面里

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 01:50–02:10（+08:00）
性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 做法

自己**造一个最小 `.mrpack`**（一个 zip，里面只有 `modrinth.index.json`，`files` 为空，
`dependencies.minecraft = 1.21.11`），用 base64 走 **`POST /api/instances/import`**（本地文件导入路径），
在导入前后各读一次 **`/api/content/tasks`**。

## 结果：**前后对照，决定性**

```
导入前： {"tasks":[]}

导入后： {"tasks":[{"id":"ct1791726461063",
                     "category":"modpack",
                     "projectId":"probe.mrpack",
                     "title":"probe.mrpack",
                     "status":"running",
                     "progress":0,
                     "message":"资源进度 4400/4591",     ← 实时进度
                     "resultInstanceId":"",
                     "startedAt":"2026-10-11T21:47:41+08:00"}]}
```

**导入前是空的，导入后有一条任务，而且带着实时进度 `资源进度 4400/4591`。**

产品日志同时印证（P-IMPORTLOG 的落盘）：

```
[Import] 资源进度 4150/4591
[Import] 资源进度 4200/4591
... 4400/4591
```

## 逐条判定

| 断言 | 状态 |
| --- | --- |
| 本地导入会**登记**一条任务（此前 `{"tasks":[]}`） | **✅ 已验证** |
| 进度会**流进 UI 数据源**（`UpdateContentTask` 接线生效） | **✅ 已验证**（`message` 随导入推进） |
| **终态**（completed / failed） | **⏳ 未观测到** |

**为什么终态没验到**：这次导入要装 4,591 个资源，我在它跑到 **4400/4591** 时超时并终止了进程
（`import THREW HTTP=0` 是我的 300 秒超时，不是产品报错——日志显示它在继续推进）。
所以 `completed` 那条路径**没被走到**。

**终态的机制与已验部分相同**（都走 `UpdateContentTask` + `ContentGate`），
而且兜底写在 `finally` 里（`finally` 在成功、取消、异常三条路径上都会执行）。
**但"机制相同"是推理，不是观测** —— 所以如实记为**未验证**。

## 这次验证的边界与代价

- 造包 → 239 字节；base64 → 320 字符；**不依赖任何外部资源包**（`files` 为空），
  所以这个验证**不需要网络就能复现**（虽然导入过程本身要下 MC 资源）。
- 导入耗时以分钟计，所以**只观察到中途**。

## 结论

**P-IMPORTTASK 的主承诺已经行为验证**：
本地文件导入此前在界面上**什么都没有**（第 24 轮实测 536 秒无输出），
现在**会出现在内容中心的进度列表里，并带实时进度**。

**唯一未观测的是终态。** 下一轮若要闭合它，只需让同一次导入**跑完**（或用一个更小的版本依赖让它更快结束），
再读一次 `/api/content/tasks` 看 `status` 是否变成 `completed`。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
