# 记录 89：缺陷 1 的补丁（**第 17 条**）—— **至此没有一条缺陷是无补丁的了**

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 04:16–04:36（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。
补丁：`apply-loader-entry.ps1`（3 步）——**未应用**。

---

## 结果

```
[OK] P-LOADERENTRY 1/3 InstallLoaderAsync becomes public
[OK] P-LOADERENTRY 2/3 route POST /api/instances/{id}/loader
[OK] P-LOADERENTRY 3/3 the handler
steps applied = 3

buildExit=0   0 警告 0 错误
testExit=0    失败: 0，通过: 947
```

## 三步分别是什么

| 步 | 内容 |
| --- | --- |
| 1/3 | `InstancePackService.InstallLoaderAsync` 由 `private` 改 **`public`** —— 这是这个功能**唯一的结构性改动**，四个分支的分派逻辑本身一行没动 |
| 2/3 | 注册路由 **`POST /api/instances/{id}/loader`**（URL 形状 `/api/instances/{id}/loader` → `seg[2]` 是实例 id、`seg[3]` 是 `"loader"`） |
| 3/3 | 处理器：参数校验 → **`ModLoaderInfo.TryParse`**（字符串→枚举，OptiFine 在这里被明确拒绝）→ 解析实例与 Java → 构造 `InstancePackService` → 调分派 → **回写实例字段** → 响应 |

## 一处我自己在跑之前就抓到的错

路由条件我第一版写成 `seg.Length == 3 && seg[2] == "loader"` ——
那是**照抄 import 路由的形状**（`/api/instances/import`），但新路由是
**`/api/instances/{id}/loader`**，多一段。正确的条件是
`seg.Length == 4 && seg[3] == "loader"`，实例 id 取 `seg[2]`。

**这个错不是编译器发现的，是我在写完之后回读了一遍路由才发现的** ——
如果照原样跑，`TryParse` 会拿到字符串 `"loader"` 并报"不支持的加载器：loader"，
而**编译和测试都不会有任何反应**（`seg[2]` 传进去本来就是个字符串，类型对得上）。

## ⚠️ 这是**后端那一半**（必须说清楚）

**用户现在还到不了这个路由** —— 前端没有按钮。
也就是说：**能力已经有了，入口还没有。** UI 那一步（在实例详情或"安装管理"里加一个
"安装加载器"的选择器）**不在这个补丁里**，是下一件要做的事。

所以这个补丁的准确描述是：**"为已存在的实例安装加载器"的后端入口**，不是"用户可以用这个功能了"。

## 里程碑：**没有一条缺陷是无补丁的了**

| | 之前 | **现在** |
| --- | --- | --- |
| 已就绪的补丁 | 16 条 | **17 条** |
| **无补丁的缺陷** | 1 条（缺陷 1） | **0 条** |

（缺陷 15 的 ② 是**撤回**，不是"未修"——它本来就不是缺陷，见记录 70。）

**注意**：17 条这个数字是"已就绪"，其中 **P-LOADERENTRY 尚未并入合并验证**
（第 71 轮那次是 16 条）。按本会话惯例，下一轮应重跑一次。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
