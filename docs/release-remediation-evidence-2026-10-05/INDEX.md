# 证据索引（从这里开始找，不要翻 104 个目录）

日期：2026-10-06。本目录共 **104** 个 `run-*` 证据目录；本文件是**唯一入口**：按"你想确认什么"给 1–2 个目录，其余都是原始存档。

## 0. 先读这三份（都不是证据，是结论）

| 想确认什么 | 读哪个 |
| --- | --- |
| **能不能发布、还差什么**（一页答卷）| `docs/FINAL-ANSWER-2026-10-06.md` |
| **要我拍板什么**（范围 + 4 个 blocker + 20 项决定 + 3 项授权）| `run-20261006-1600-decision-package/DECISION-PACKAGE.md` |
| **批准后怎么修**（6 份补丁 + 9 步清单 + 一键脚本）| `run-20261006-2130-patch-set/PATCH-SET.md`、`run-20261006-3100-approval-runbook/` |

## 1. 四个 blocker（缺陷 → 复现 → 补丁 → 验证）

| 缺陷 | 复现证据 | 补丁有效性证据 |
| --- | --- | --- |
| **B0** 默认实例/账号不落盘 | `run-20261006-1430-instance-id-instability/`（三次 state 三个 id、launch 400）| `run-20261006-2500-patched-build/`（三次 id 相同、launch 不再"实例不存在"）|
| **B1** 加载器缺 `-Djava.library.path` | `run-20261006-1250-fabric-launch/`（候选 ~10 秒崩、208 行）| **端到端**：`run-20261006-2600-patched-fabric-launch/`（10 图集/0 崩溃）；回归：`run-20261006-2700-patched-regressions/` |
| **B2** 安装器 URL 多一段 | `run-20261006-1900-forge-url-probe/`（404；正确 URL 200）| `run-20261006-2800-patched-forge/`（404 消失）|
| **B2b** 缺 `launcher_profiles.json` | `run-20261006-2800-patched-forge/installer-diag.json`（安装器原文）| 同目录 `installer-diag-with-profile.json`（exit 0）；环境限制：`run-20261006-2900-patched-forge-fixed/` |

## 2. 门槛与回归（"改完还过不过"）

| 想确认什么 | 读哪个 |
| --- | --- |
| 冻结候选本身过不过门槛 | `run-20261006-2400-final-refresh/final/`（GREEN / 失败 0 / 产物逐字节一致）|
| **6 份补丁一起上过不过门槛** | `run-20261006-3000-patched-final-gate/final/`（11 步全 PASS；汇总的 2 项"失败"是副本产物）|
| 补丁编译 + C# 全量测试 | `run-20261006-2500-patched-build/`（0 警告 0 错误、**904/904**）|

## 3. R06 真机验收（按功能域）

| 功能域 | 证据目录 |
| --- | --- |
| 原版安装/启动/退出/进世界 | `run-20261006-0810-launch-acceptance/`、`run-20261006-1030-world-entry/` |
| 多实例并发 / 运行守卫 | `run-20261006-1100-multi-instance/`、`run-20261006-0930-running-guards/` |
| 文件层事务 / 恢复区 | `run-20261006-1000-file-transactions/`、`run-20261006-2350-java-recovery/` |
| 更新全链（含"重启后新进程服务"）| `run-20261006-1130-update-chain/` |
| 内容子系统（mod / 资源包 / 光影）| `run-20261006-1400-content-*`、`run-20261006-2300-resourcepacks/` |
| 镜像源（BMCLAPI）| `run-20261006-1500-mirror-mode/` |
| 整合包导入 + 取消 | `run-20261006-1210-modpack-import/`、`run-20261006-1700-import-cancel/` |
| 服务器列表 + ping | `run-20261006-2200-servers/` |
| Mod 二分定位 | `run-20261006-2230-mod-bisect/` |
| 第三方账号失败路径 | `run-20261006-2330-thirdparty-auth/` |
| 已发布 v1.0.0 的 EXE-only 缺陷复现（G07/R01）| `run-20261006-0530-published-v100-repro/` |

## 4. 范围与流程（决定项的依据）

| 想确认什么 | 读哪个 |
| --- | --- |
| 候选范围到底多大、多少没复核 | `run-20261006-0700-scope-warning/`、`run-20261006-0730-scope-recount/` |
| 锁定还原/闭包/清单/可复现 | `run-20261006-0300-locked-restore/`、`run-20261006-0400-ui-closure/`、`run-20261006-0500-manifest/`、`run-20261006-2400-final-refresh/` |
| 逐轮完整记录（含我的错误与更正）| `REMEDIATION-PROGRESS-2026-10-05.md`（82 条）|

> 其余目录是过程存档（同一结论的多次复跑、早期探针）；`REMEDIATION-PROGRESS-2026-10-05.md` §3 有全部 GREEN 项的逐条索引。
