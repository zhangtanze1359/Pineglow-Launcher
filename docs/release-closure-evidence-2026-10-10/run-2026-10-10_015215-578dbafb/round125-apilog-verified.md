# 记录 125：**P-APILOG 行为验证通过** —— 上一轮的前提错了，这一轮换对了触发方式

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 14:26–14:44（+08:00）
性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 上一轮为什么没验成

我想"制造一次 500"，用的是 **`/api/instances/不存在的实例/loader`** → 返回 **400**。
原因：`RequireInstance` 抛 `InvalidOperationException`，**调度器把这类"已知错误"转成 400**，
**只有未预期的异常才走 500 分支** —— 我根本没碰到 P-APILOG 要改的那条路径。

## 本轮：试三种触发方式，找出哪一种真会 500

| 触发方式 | HTTP | 说明 |
| --- | --- | --- |
| **畸形 JSON 体**（`{ this is not json`） | **500** ✅ | `JsonException` —— **未预期异常** |
| JSON 类型不对（`"loaderType": 12345`） | 400 | 被当成已知错误 |
| JSON 数组而非对象（`[1,2,3]`） | 400 | 同上 |

**只有"畸形 JSON"产生真正的 500。** 顺带这也把产品的错误分层**测清楚了一格**：
**`InvalidOperationException` → 400；其它异常 → 500。**

## 结果：日志确实记下了，而且带调用栈

```
[LocalApi] …失败 lines = 1
  2026-10-12 00:10:34.263 [ERROR] [LocalApi] POST /api/instances/d39a5594071d47ae86538b2e17db72b6/loader
    失败: 't' is an invalid start of a property name. Expected a '"'

stack lines ('   at ') = 9
  at System.Text.Json.ThrowHelper.ThrowJsonReaderException(Utf8JsonReader& json, …)
  at System.Text.Json.Utf8JsonReader.ReadSingleSegment()
  at System.Text.Json.Utf8JsonReader.Read()
  …
```

| 断言 | 结果 |
| --- | --- |
| 未预期异常 → **500** | **✅** |
| **日志里有 `[LocalApi] … 失败`**，含 **HTTP 方法 + 路径 + 消息** | **✅** |
| **带调用栈**（9 行 `at …`） | **✅** |

**修复前**：这条路径只写 `Debug.WriteLine`（不进日志文件）—— 那是第 55 轮从代码上确认的。
**现在它在日志文件里，带完整栈。**

## 证据等级更新

| 补丁 | 原等级 | 现在 |
| --- | --- | --- |
| **P-APILOG** | C 编译 | **A 行为** ✅ |

**统计：A 行为 14 条 / B 测试 2 / C 编译 2 / D 文档 1。**

**只剩两条 C**：
- **P-LOGSHARE** —— 可以验（两个进程同时写同一个日志文件，看第二个会不会 `IOException`）；
- **P-STARTUPLOG** —— **本环境不可复现**（需要一台没装 WebView2 的机器）。

## 这两轮合起来说明的一件事

**"只有编译级证据"的 4 条里，我上一轮说"3 条可以验"。这两轮验成了 2 条**（P-CONFIGLOG、P-APILOG），
**第 3 条（P-LOGSHARE）下一轮可以验**。

也就是说：**那 4 条不是"验不了"，是我此前没去验。** 标注证据等级这件事本身，
**直接把"没验"变成了"去验"** —— 这比多写一段总结有用。

## 当前状态

| 项 | 状态 |
| --- | --- |
| 已就绪的补丁 | 19 条（16 个脚本） |
| 证据等级 | **A 14 / B 2 / C 2 / D 1** |
| 仍可补验 | **P-LOGSHARE**（下一轮）；P-STARTUPLOG 不可复现 |
| 唯一未做的功能 | 缺陷 13 ③（设置页 Java 安装入口） |
| 待你决定 | ① 提交（(a) ~2750 / (a′) 2182 / (b) **204**）；② C 的范围；③ 是否做 13 ③ |

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
