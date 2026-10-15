# 记录 70：缺陷 15 ② **撤回** —— 是我的测试前提自己失效了

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-11 23:42–23:55（+08:00）
性质：**只读审计 + 清理自己留下的痕迹，产品目录零改动**（0 staged）。

---

## 结论先说

**缺陷 15 的第 ② 部分不是产品缺陷，是我的测试错了。**

`prelaunch-check` 报 `ok=true / warnings=[]` **是正确的** —— 因为在它检查之前，
**产品已经把那个"不存在"的目录创建出来了**。

---

## 怎么查出来的

上一轮卡在："它自己回显了那个坏路径，却说没问题"。本轮先排除了"二进制与源码不一致"：

| 检查 | 读数 |
| --- | --- |
| 用**当前源码树**重新发布 | `CA8F94C1A6173F23F38F3D2A3C949AFFC16D5AB67752B662C7806831F350D93E` |
| 与我测试用的夹具 EXE 比较 | **逐字节相同** |
| 新构建跑同一场景 | **仍然** `{"ok":true,"errors":[],"warnings":[],"gameDir":"C:\\does-not-exist\\ghost-instance"}` |

**源码没错、二进制没错、条件看着也满足** —— 那就只剩一种可能：**条件其实不满足。**

于是我去看那个路径**现在**还在不在：

```
C:\does-not-exist\ghost-instance exists = True
   C:\does-not-exist\ghost-instance
   C:\does-not-exist\ghost-instance\mods
   C:\does-not-exist\ghost-instance\resourcepacks
   C:\does-not-exist\ghost-instance\saves
   C:\does-not-exist\ghost-instance\shaderpacks
created at: 2026/10/11 20:37:25
```

**它被创建了**，创建时间正是我测试的那一刻（20:37:25），而且带着完整的实例目录骨架
（`mods` / `resourcepacks` / `saves` / `shaderpacks`）—— 这是 `InstanceIsolationService` 在布置实例布局。

**所以 `Directory.Exists(gameDir)` 在检查时是 `true`** → 不加警告**完全正确**。
我的测试用了一个"产品会替我创建"的路径当"不存在的路径"，**前提自己失效了**。

## 撤回与保留

| 部分 | 状态 |
| --- | --- |
| **① 500 没有日志** | **成立，且补丁已验证**（记录 67：完整异常 + 五层调用栈）。**保留。** |
| **② prelaunch 假阴性** | **撤回** —— 不是缺陷，是我的测试前提错了 |

报告里缺陷 15 已相应改写：**只保留 ①，② 标注为已撤回并写明原因**。

## 清理

测试在 `C:\does-not-exist` 留下了一个目录（我创建的）。**已删除**，删除前逐项核对：
解析后的绝对路径 `= C:\does-not-exist`、创建时间是**今天**、顶层只有一个 `ghost-instance`。
（规则要求递归删除前必须核对绝对路径——已做。）

## 这是第四次「问题出在我这边」

| 轮次 | 我以为 | 实际 |
| --- | --- | --- |
| 44 | 账号令牌过期 → 401 | 没有任何 401 |
| 45 | 探针没记 URL | 一直记着 |
| 48 | 产品流水线 FAIL | 我夹具没带 `.gitignore` |
| **57** | **prelaunch 假阴性** | **产品把目录创建了，我的前提失效** |

**四次里有三次是"我的观测装置有问题"，不是产品有问题。**
这条经验比任何单条缺陷都重要：**在断言产品错了之前，先问"我的测试前提还成立吗"。**

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
