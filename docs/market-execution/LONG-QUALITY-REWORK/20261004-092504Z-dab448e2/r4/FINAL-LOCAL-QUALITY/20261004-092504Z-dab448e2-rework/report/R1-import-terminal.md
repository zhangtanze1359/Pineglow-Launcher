# R1 — 导入真实终态（LQ-01 修复）

## 1. 缺陷

`ui/js/launcher.js` 的 `handleImportFiles` 用**一个** `cancelRequested` 同时表达两件事：
“用户点过取消”和“服务端已确认取消”。终态分支因此写成：

```js
if (cancelRequested || (err && err.cancelled)) { /* 宣称“导入已取消/未生成实例” */ }
```

于是取消 API 失败、或服务端只回 `pendingCancel`（仅登记取消意图）时，随后发生的**普通导入错误**
也会被显示成“导入已取消”。用户会以为导入已中止、没有生成实例。

## 2. 修复（最小改动）

三个概念分开保存：

| 变量 | 含义 | 何时置位 |
| --- | --- | --- |
| `cancelRequested` | 用户点击过取消（**意图**） | 点击取消按钮 |
| `cancelConfirmed` | 服务端**明确确认**取消 | `cancelImport` 返回 `cancelled === true` |
| `err.cancelled` | 导入错误自身携带**已确认取消**信号 | 由服务端响应决定 |

终态判定改为：只有 `err.cancelled === true` 或 `cancelConfirmed` 才显示“导入已取消 / 未生成实例”。
其余情况如实报告失败，并在用户点过取消时追加新提示
`import.outcomeUnconfirmed`（中英双语），明确说明**服务端未确认取消、结果尚未确认、请到实例列表核对**。

改动文件：

- `ui/js/launcher.js` — `handleImportFiles` 取消终态分支
- `ui/js/i18n.js` — 新增 `import.outcomeUnconfirmed`（zh + en）
- `ui/tests/goal-core-flow.test.cjs` — IMPORT-03/04 终态断言 + 3 条 INDEPENDENT 反证
- `ui/tests/import-cancel.test.cjs` — i18n 键清单 + 静态扫描（意图不得直接决定终态）

## 3. RED → GREEN

| 步骤 | 命令 | 结果 |
| --- | --- | --- |
| RED（修前，源码侧） | `node --test --test-reporter=tap ui/tests/goal-core-flow.test.cjs` | tests 31 / pass 27 / **fail 4** / skip 0，exit 1 |
| GREEN（修后，聚焦） | 同上 | tests 31 / **pass 31** / fail 0，exit 0 |
| GREEN（修后，i18n/静态） | `node --test --test-reporter=tap ui/tests/import-cancel.test.cjs` | tests 5 / pass 5 / fail 0 |
| GREEN（全量 Node） | `node --test --test-reporter=tap ui/tests/*.test.cjs` | tests 269 / pass 269 / fail 0 / skip 0 |

## 4. 独立仪器复验（修后）

用 Codex 保留的反证测试（期望未改动，仅重绑本轮副本）：

```
node --test --test-reporter=tap --test-name-pattern INDEPENDENT \
  independent/r3-reproof/import-terminal-disproof.test.cjs
→ tests 3 / pass 3 / fail 0 / skipped 0, exit 0
```

修复后的终态提示（原始日志）：

- IMPORT-04：`["取消失败：unknown-operation","导入失败：ordinary transport failure 已请求取消，但服务端未确认取消，导入结果尚未确认；请到实例列表核对是否已生成实例。"]`
- IMPORT-03：`["已记录取消；若该导入随后才被受理，会自动中止。","导入失败：server still working 已请求取消，但服务端未确认取消，…"]`

保留了既有行为：真正取消（`err.cancelled`）、正常成功、取消后最终成功、取消 API 失败、按钮禁用状态。
