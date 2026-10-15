# 记录 59：**Quilt** 在十三条补丁版上也走通「启动到标题界面」

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 22:00–22:08（+08:00）
性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

---

## 为什么做这个

报告里「补丁版本上的验证版图」一直留着**四种加载器全矩阵**未重跑。全矩阵要真实网络与长时间下载，
但其中**已导入的实例**不需要下载——**启动它们只是几十秒的事**。

此前补丁版上只验过 **Fabric**（第 31 轮）。本轮把 **Quilt** 补上。

## 结果：PASS

| 项 | 读数 |
| --- | --- |
| 被验产物 | `141942AA5D1DCB1618936D676188FF3A5586C2EA5146A35D11C9CABEDC235369`（= **十三条补丁**的构建） |
| apiUp | **True** |
| 目标实例 | `d7-quilt 导入`（**Quilt** / `quilt-loader-0.30.1-1.21.11`，MC 1.21.11） |
| `prelaunch-check` | **ok=True，errors=0，warnings=0** |
| `launch` | **ok=True**，游戏 pid 7952 |
| **到达标题界面** | **True**（`Sound engine started`） |
| 残留进程 | **0** |

## 补丁版上的加载器启动覆盖

| 加载器 | 补丁版上验过吗 |
| --- | --- |
| Fabric | ✅ 第 31 轮（8 条补丁时） |
| **Quilt** | ✅ **本轮**（13 条补丁） |
| Forge | ✗ |
| NeoForge | ✗ |

**仍未覆盖**：Forge / NeoForge 的启动，以及**四种加载器的全新安装**（需要真实网络与较长下载）。
已导入实例的启动覆盖现在是 **2/4**。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
