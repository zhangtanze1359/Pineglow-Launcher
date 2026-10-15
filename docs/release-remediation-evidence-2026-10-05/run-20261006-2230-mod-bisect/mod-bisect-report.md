# Mod 二分定位（bisect）= **PASS**：3 轮定位到植入的元凶，且文件全部恢复（第 68 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
这条功能此前**完全没覆盖**（本会话只碰过它的守卫：实例正在 bisect 时不允许移除实例）。

## 1. 做法：植入一个"元凶"，看它能不能二分辨出来

在实例 `mods/` 里放 **8 个假 jar**（`mod1.jar` … `mod8.jar`），把 **`mod5.jar`** 当元凶，按源码语义逐轮回报：

- `start` → 服务端**禁用一半候选**；
- 回报 `crashed=true`（禁用这半后**仍然崩**）⇒ 元凶在**仍启用的另一半**里；
- 回报 `crashed=false`（禁用这半后**不崩**）⇒ 元凶在**被禁用的这半**里。

## 2. 实测结果（逐轮原始输出）

```
初始 GET /api/instances/{id}/bisect → {"ok":true,"pending":false}
start → ok=True step=1 禁用半区=mod1.jar,mod2.jar,mod3.jar,mod4.jar  候选=mod1..mod8
  第 1 轮：对 step=1 回报 crashed=True  → 新 step=2 禁用=mod5.jar,mod6.jar   finished=False
  第 2 轮：对 step=2 回报 crashed=False → 新 step=3 禁用=mod5.jar            finished=False
  第 3 轮：对 step=3 回报 crashed=False → 新 step=3 禁用=(空)                finished=True culprit=mod5.jar
定位结束：finished=True culprit=mod5.jar ；mods/ 下 .jar=8 .jar.disabled=0
giveup 前禁用=4 个 → giveup ok=True ；恢复后 .jar=8 .jar.disabled=0
过期 step（100 vs 实际 1）→ status=409 {"ok":false,"error":"该轮已经推进，请用最新一轮的状态重新提交。","step":1,…}
```

**判定：`PASS：二分定位在 3 轮内定位到植入的元凶，且 finished/giveup 后文件全部恢复；过期 step 被拒`**（逐项 **5/5**）

| 检查 | 结果 |
| --- | --- |
| `start` 会禁用一半候选 | ✅（8 个里禁 4 个）|
| 定位到**植入的**元凶 | ✅ `mod5.jar` |
| 结束后**所有 jar 恢复启用** | ✅ `.jar=8 / .jar.disabled=0` |
| `giveup` 后文件恢复 | ✅ `.jar=8 / .jar.disabled=0` |
| **过期 step 被拒** | ✅ **409** `该轮已经推进，请用最新一轮的状态重新提交。` |

> 顺带确认：8 个候选**恰好 3 轮**收敛（log₂8 = 3），与二分语义一致；且服务端会拒绝"过期轮次"的反馈
> （源码注释写明是为防止双击提交把答案记到错误的轮次上）——这条**实测有效**。

## 3. 我自己的三个坑（都在断言/脚本侧，产品行为正确）

1. **变量跨轮复用**：我把 `$disabled` 在循环里逐轮覆盖，最后断言"start 时禁用了半区"用的是**最后一次**（空）的值 ⇒ 假 FAIL。
   改法：断言直接看**响应体**（`@(Prop $start.body 'disabledHalf').Count`），不引入跨轮变量。
2. **`String.Replace` 会替换所有出现**：我想"只在 start 后插一行"，结果同一句在循环里也被插入 ⇒ 仍被覆盖。
3. **行尾差异**：多行锚点用了 `\r\n`，而文件是 LF ⇒ 锚点匹配失败（本会话第 N 次踩行尾）。

## 4. 复现

```powershell
powershell -File ...\run-20261006-2230-mod-bisect\verify-mod-bisect.ps1 `
  -ExePath <候选 EXE> -JsonPath <证据>\mod-bisect.json -WorkRoot %TEMP%\pg-bisect4
# 期望：PASS（5/5），JSON 里含逐轮 step/禁用半区/finished/culprit
```

产物：`mod-bisect.json`（逐轮记录 + 断言）、`mod-bisect.log`（时间线）、`verify-mod-bisect.ps1`。
