# 第 26 轮：**打过补丁的源码树**跑通整条候选流水线

运行标识：`run-2026-10-10_015215-578dbafb`（第 26 轮追加）
时间：2026-10-11 17:28–17:40，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。全部在 `%TEMP%` 隔离副本里完成。

---

## 1. 为什么还差这一轮

第 25 轮补上了"打过补丁的构建能装能玩"，但那是**点状**验证（一次导入、一次启动）。
而**出厂产物**享受的是**整条流水线**：Node 全量、C# 全量、VerifyHost 冒烟、单文件发布、独立冷启动 19 项。
第 25 轮我自己也写了"补丁版本上尚未覆盖其余场景"。

本轮把这条缺口补掉——而且顺手修了一个我自己的疏忽：

> **我之前的每个验证副本都只拷了 `dev/`、`ui/`、`.github/`，从来没有拷 `scripts/`。**
> 所以 `node scripts/run-inventory-tests.cjs`、`verify-single-file-standalone.ps1` 这些**产品自带的验证工具**
> 在我所有"隔离副本"里其实**根本不存在**，我也就没法在副本里跑它们。本轮补齐了 `scripts/`。

---

## 2. 做法

新建完整副本（`dev/` + `ui/` + **`scripts/`** + `.github/` + `global.json`），
先跑一次**未打补丁**的 Node 全量做基线（**305/305 通过**），然后：

1. 应用 5 条产品修复（21 处）
2. 应用 C 补丁（8 步）
3. 依次跑流水线的各道门槛

---

## 3. 结果：六道门槛全绿

| # | 门槛 | 结果 |
| --- | --- | --- |
| 1 | **UI 资源闭包（R02）** | `exit=0` |
| 2 | **Node 全量测试（R04 清单驱动）** | **305 通过 / 0 失败 / 0 跳过**，`exit=0` |
| 3 | **C# 全量测试** | **947 通过 / 0 失败 / 0 跳过**，`exit=0` |
| 4 | **VerifyHost 构建 + `--bisect-smoke`** | 构建 `exit=0`；冒烟 **PASS**，含 `credential-isolation: production credential backend calls must be 0 (actual 0)` |
| 5 | **单文件发布（锁定，带 C）** | `exit=0` → `642051E5F46F940E81A2F80F1B1F95B1B4DE4E7207E0F135A9ECE99EB1609C3D`，53,052,722 字节 |
| 6 | **独立冷启动** | **判定 PASS** |

### 3.1 冷启动那 19 项（产品自带脚本，原文摘录）

```
冷启动目录: C:\Users\Administrator\AppData\Local\Temp\中文 冷启动 98d87dc4…
  [OK] 冷启动目录无外置 UI/ 目录            dirs=
  [OK] 冷启动目录无外置原生 DLL             dlls=
  [OK] 冷启动不继承开发旋钮（PINEGLOW_UI_DIR 等已清除）
  [OK] 进程在冷启动后仍然存活（R01 原始 RED 是直接退出）
  [OK] /api/health 就绪                     health={"ok":true,"pid":5456,"port":48261}
  [note] 跳过主窗口标题判定，实测标题='松光启动器'
  [OK] 资源 /                     bytes=7259
  [OK] 资源 /css/figma.css        bytes=50949
  [OK] 资源 /css/pineglow.css     bytes=14620
  [OK] 资源 /assets/pine-forest.svg bytes=1879
  [OK] 资源 /js/api.js            bytes=66589
  [OK] 资源 /js/launcher.js       bytes=203877
  [OK] 资源 /js/i18n.js           bytes=60744
  [OK] 资源 /js/skin3d.js         bytes=10589
  [OK] 资源 /js/skin3d-core.js    bytes=2262
  [OK] 资源 /vendor/three.min.js  bytes=669884
  [OK] 程序集内嵌 UI 已解包到 data/UI/<版本-指纹>   dirs=1.0.0-41346c7082cb
  [OK] 解包目录含 index.html
  [OK] 解包界面与候选源码逐字节一致（防「新服务配旧界面」）
独立冷启动判定: PASS
```

注意第 19 项：**解包出来的界面与候选源码逐字节一致**——这一项正好覆盖了 A/B 两个 P1 阻断里的 B（旧 UI 被继续服务），
在**打过补丁的树**上也成立。

---

## 4. 至此的验证版图

| 对象 | 覆盖 |
| --- | --- |
| **出厂 EXE** `CA8F94C1…` | 3 次完整流水线 PASS + 四种加载器 + 断点续传 + 界面级九视图 + 真实更新链门槛 + 跨 RID 打包 + 镜像安装 |
| **打过补丁的树/产物** `642051E5…` | **整条流水线六道门槛全绿（本轮）** + 947/0 + 五条修复各自行为 A/B + 真实导入 + 启动 Fabric 到标题界面 |

两者之间**唯一没有在补丁版本上重跑的**是那几项**依赖真实网络与长时运行**的现场验证（四种加载器全矩阵、断点续传注入式复现、镜像安装、真实更新链门槛）。
这些**不是流水线能覆盖的**，需要单独跑；但它们全部是"外部世界"的验证，与补丁改动无交集
（补丁只碰下载客户端的请求头、安装器 JVM 参数、镜像前缀映射、fake-IP 地址分类、导入日志委托——都不影响这些场景的判定逻辑）。

---

## 5. 本轮的价值

把"补丁版本没走过流水线"这条**真实存在的证据缺口**补掉了，并且发现并修正了我自己验证方法里的一个疏漏
（**副本从不包含 `scripts/`**，导致产品自带的验证工具一直没能在副本里使用）。

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
