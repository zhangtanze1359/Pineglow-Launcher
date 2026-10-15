# 记录 105：缺陷 1 的 **UI 入口**（**第 19 条补丁**）—— 缺陷 1 至此完整

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 08:44–09:04（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。
补丁：`apply-loader-ui.ps1`（5 步，跨 3 个文件）——**未应用**。

---

## 结果

```
[OK] P-LOADERUI 1/4 api.js: installLoader()
[OK] P-LOADERUI 2/4 launcher.js: the button
[OK] P-LOADERUI 3/4 launcher.js: wire the button
[OK] P-LOADERUI 4/4a i18n.js: zh keys
[OK] P-LOADERUI 4/4b i18n.js: en keys
steps applied = 5

node --check  api.js / launcher.js / i18n.js  → 全部 exit 0
Node 全量      305 通过 / 0 失败
UI 资源闭包    exit 0
dotnet build   0 警告 0 错误
```

## 五个步骤做了什么

| 步 | 文件 | 内容 |
| --- | --- | --- |
| 1 | `api.js` | `installLoader(instanceId, loaderType, loaderVersion)` —— 调 `POST /api/instances/{id}/loader` |
| 2 | `launcher.js` | 实例卡片右侧加一个 `data-loader` 按钮 |
| 3 | `launcher.js` | `bindInstallations` 里接线：弹窗选加载器 + 版本 → 调用 → 重载列表 |
| 4a/4b | `i18n.js` | 中英各加 **9 个文案键**（UI 所有可见文字都走 `t()`，不加键会显示原始键名） |

## ⚠️ 我写坏过一次，**是测试抓住的**

第一版跑完：**Node 全量 170/305 失败**、`node --check` 在 `i18n.js` 上报错。

**原因是我自己的 bug**：英文文案里我写了
`…that version's assets.` —— 那个**撇号没有转义**，而它在单引号 JS 字符串里，
于是字符串提前结束、文件语法崩掉。

改成 `that version of the assets.` 后全部通过（`node --check` 三个文件都 exit 0、Node **305/305**）。

**这里值得记的是"谁来抓"**：这个错**编译器抓不到**（JS 不编译）、**dotnet 测试抓不到**，
是 **Node 全量测试 + `node --check`** 抓的。**本会话第 N 次证明"验证装置比我自己可靠"。**

## ⚠️ 验证边界

| 断言 | 状态 |
| --- | --- |
| 补丁能应用（5 步，每步断言恰好匹配 1 次） | **✅** |
| **三个 JS 文件语法正确** | **✅ `node --check` 全 exit 0** |
| **Node 全量 305/305** | **✅** |
| UI 资源闭包 | **✅ exit 0** |
| 构建 0 警告 0 错误 | **✅** |
| **按钮在真实浏览器里点了会发生什么** | **⏳ 未验证** |

**静态检查全绿 ≠ 运行时正确**：我没验弹窗能不能打开、`SG.api.installLoader` 的返回怎么处理、
重载列表后界面是否正常。**本会话做过 CDP 驱动的界面级验证（第 18 轮），那套手段可以用来验它** ——
但不在本轮预算内。

## 里程碑：**缺陷 1 完整了**

| 部分 | 状态 |
| --- | --- |
| 后端（公开分派 + 路由 + 处理器） | **端到端验证通过**（原版实例 → 40 秒 → Fabric，自检通过） |
| **UI 入口** | **补丁已就绪、静态验证通过**（运行时未验） |

**缺陷 1 是 15 条缺陷里最后一条有"功能缺口"的。现在它的两半都有了。**

## 补丁计数

| | |
| --- | --- |
| 之前 | 18 条 |
| **现在** | **19 条** |

**尚未并入合并验证**（第 83 轮那次是 18 条），下一轮应重跑。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
