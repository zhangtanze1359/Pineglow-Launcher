# 补丁应用顺序（20 条，已校验）

> 生成于记录 48。**每个脚本都已校验：存在、带 UTF-8 BOM、语法可解析**（缺 BOM 时 PowerShell 5.1 会按 GBK 读取，脚本里的中文锚点会静默失配）。

## 用法

```powershell
# 1. 先建一个隔离副本（脚本只接受 -Root，不会碰你的产品树）
# 2. 按下面的顺序执行；每个脚本自带"恰好匹配 N 次"的自检，锚点不符会直接报错而不是静默改错
$d = '<本目录>'
& "$d\apply-proposed-fixes.ps1"  -Root <副本> -OutDir <输出>
& "$d\apply-durable-queue.ps1"   -Root <副本> -OutDir <输出>
& "$d\apply-deadlock-fix.ps1"    -Root <副本> -OutDir <输出>
& "$d\apply-logshare-fix.ps1"    -Root <副本> -OutDir <输出>
& "$d\apply-runsync.ps1"         -Root <副本> -OutDir <输出>
& "$d\apply-java-label.ps1"      -Root <副本> -OutDir <输出>
& "$d\apply-optifine-doc.ps1"    -Root <副本> -OutDir <输出>
& "$d\apply-deadlock-test.ps1"    -Root <副本> -OutDir <输出>
& "$d\apply-c-rid-lock.ps1"       -Root <副本> -OutDir <输出>
```

## 顺序与内容

| # | 脚本 | 改的是什么 |
| --- | --- | --- |
| 1 | `apply-proposed-fixes.ps1` | 5 条产品修复：P-UA / P-FABRICMETA / P-PROXYJVM / P-FAKEIP / P-IMPORTLOG |
| 2 | `apply-durable-queue.ps1` | P-DURABLEQUEUE（可恢复任务队列） |
| 3 | `apply-deadlock-fix.ps1` | P-DEADLOCK（UI 线程死锁，3 行） |
| 4 | `apply-logshare-fix.ps1` | P-LOGSHARE（第二个实例无日志，1 行） |
| 5 | `apply-runsync.ps1` | P-RUNSYNC（同类风险的另外 10 个调用点） |
| 6 | `apply-java-label.ps1` | P-JAVALABEL（"使用内置 JRE" 谎话） |
| 7 | `apply-optifine-doc.ps1` | P-OPTIFINE（README 过度承诺） |
| 8 | `apply-deadlock-test.ps1` | 死锁回归测试（新增测试文件） |
| 9 | `apply-startup-log.ps1` | P-STARTUPLOG（启动失败可读性） |
| 10 | `apply-config-log.ps1` | P-CONFIGLOG（配置损坏不再静默） |
| 11 | `apply-api-log.ps1` | P-APILOG（**所有 500 落盘日志**；原第二步已删，见记录 71） |
| 12 | `apply-import-task.ps1` | P-IMPORTTASK（本地导入的进度界面；**行为已验证**） |
| 13 | `apply-loader-entry.ps1` | P-LOADERENTRY（**后端**：为已存在实例安装加载器；UI 入口未含） |
| 14 | `apply-java-auto.ps1` | P-JAVAAUTO（Java 自动安装接到启动路径；**须在 `apply-runsync.ps1` 之后**） |
| 15 | `apply-loader-ui.ps1` | P-LOADERUI（**UI 入口**；**须在 `apply-java-label.ps1` 之后**；运行时未验） |
| 16 | `apply-single-instance.ps1` | P-SINGLEINSTANCE（缺陷 12 的僵尸：单实例守卫；**编译级验证通过，行为待验**） |
| 17 | `apply-c-rid-lock.ps1` | C（三份 RID lock + 工作流 + 不变量） |

## 校验结果

| 脚本 | 存在 | UTF-8 BOM | 语法 |
| --- | --- | --- | --- |
| `apply-proposed-fixes.ps1` | True | True | OK |
| `apply-durable-queue.ps1` | True | True | OK |
| `apply-deadlock-fix.ps1` | True | True | OK |
| `apply-logshare-fix.ps1` | True | True | OK |
| `apply-runsync.ps1` | True | True | OK |
| `apply-java-label.ps1` | True | True | OK |
| `apply-optifine-doc.ps1` | True | True | OK |
| `apply-deadlock-test.ps1` | True | True | OK |
| `apply-c-rid-lock.ps1` | True | True | OK |

**全部可用 = True**

顺序的约束只有一条：**`apply-c-rid-lock.ps1` 最后**（它要生成 RID lock 并改写工作流与不变量测试）。其余 8 个互不重叠，顺序无关——这一点由记录 41 的合并验证证明（十二条一起应用，无一处冲突）。

---

## ⚠️ 顺序说明（第 103 轮审计发现，务必读）

本文件列出的顺序（记作 **B**）与**实际被验证过的顺序**（记作 **A**）**不同** ——
集合完全一致（16 个脚本，无多无少），但排列不同。

**两条依赖在本文件里都满足**：
- `apply-java-auto` 在 `apply-runsync` 之后 ✓（第 15 位 vs 第 5 位）
- `apply-loader-ui` 在 `apply-java-label` 之后 ✓（第 16 位 vs 第 6 位）

**但必须说清**：历次合并验证（记录 83/90/96/106/110）用的是 **A**，
**本文件的 B 顺序从来没有被完整跑过一次**。

A 与 B 的差异位置：

| 位置 | 本文件（B） | 已验证的（A） |
| --- | --- | --- |
| 6 | `apply-java-label` | `apply-java-auto` |
| 8 | `apply-deadlock-test` | `apply-loader-ui` |
| 9 | `apply-c-rid-lock` | `apply-optifine-doc` |
| 15 | `apply-java-auto` | `apply-loader-entry` |
| 16 | `apply-loader-ui` | `apply-c-rid-lock` |

**第 104 轮：本顺序已按原样跑通**（记录 114）——build 0/0、C# 949/0、Node 305/0、UI 闭包 0、锁定发布 exit 0、冷启动 PASS，**产物哈希 `DCB8FD1F…` 与另一顺序（A）逐字节相同**。**所以不存在没写下来的隐含依赖；本文件是可信的交接件。**（上一轮那句"两条都合法是判断不是观测"至此成立为观测。）
下一步应当**按本文件的顺序重跑一次**；若失败，说明存在**没写下来的隐含依赖**。