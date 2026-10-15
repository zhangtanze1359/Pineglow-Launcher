# 第 37 轮：缺陷 13 的端到端实测 —— 无 Java 机器上的首启死路（已观测，不再是推断）

运行标识：`run-2026-10-10_015215-578dbafb`（第 37 轮追加）
时间：2026-10-11 19:06–19:10，Asia/Hong_Kong（+08:00）
本轮性质：**只读 + 隔离夹具实测，产品目录零改动**（0 staged）。

第 36 轮把缺陷 13 记为「代码 + 界面级证据」，并如实标注**未跑端到端**（此前所有夹具都从既有配置继承了 `JavaPath`）。
本轮把它补上——而且本机**确实没有任何系统 Java**，所以这个场景天然可复现。

---

## 1. 夹具：真的"没有 Java"

| 准备 | 结果 |
| --- | --- |
| `<夹具>/data/runtimes` 目录 | **不存在** |
| `config.JavaPath` | **清空** |
| 各实例的 `JavaPath` | **全部清空**（0 个实例带 JavaPath） |
| 本机系统 Java（`java` 在 PATH / `JAVA_HOME` / 注册表 `JavaSoft`） | **都没有**（第 36 轮已核实） |

---

## 2. 实测：三步走完，每一步都实测到了

### 第一步：产品自己也认为"没有 Java"

```
GET /api/java  ->  installations = 0
```

**这正是驱动设置页文案的那个输入**：`ui/js/launcher.js:631` 的逻辑是
`java.length ? '自动（检测到 n 个 JDK）' : '使用内置 JRE（默认）'`。
`installations = 0` ⇒ **设置页会显示「使用内置 JRE（默认）」**。

### 第二步：启动被明确拒绝

```
POST /api/instances/{id}/prelaunch-check
  ok = False
  errors = 未找到可用的 Java 运行时（该版本需要 Java 21），请先在设置中指定 Java 路径

POST /api/instances/{id}/launch
  HTTP 400
```

### 第三步：**没有任何东西去装那个"内置 JRE"**

```
启动后 <夹具>/data/runtimes 目录是否被创建 = False
```

即：产品在拒绝启动之后，**既没有去下载 JRE，也没有给用户任何可点的安装入口**
（`/api/java` 只有 GET；`ui/` 里没有任何"安装 Java"的入口——第 36 轮已核实）。

---

## 3. 用户的完整死路（现已全部实测）

1. 新机器（无 Java）装上启动器；
2. 设置页显示 **「使用内置 JRE（默认）」** ← 第 1 步实测（`installations = 0`）
3. 装个原版、点「开始游戏」；
4. 得到 **「未找到可用的 Java 运行时…请先在设置中指定 Java 路径」** ← 第 2 步实测
5. 去设置页：**只有"指定路径"，没有安装**；页面还在说"在用内置 JRE" ← 第 3 步实测（`runtimes` 从未创建）

**结论**：缺陷 13 **从"代码 + 界面级证据"升级为"端到端已观测"**，不再有任何未验证的部分。
README 的「Java：自动检测与自动安装」在这条最常见的首启路径上**不成立**——
"自动安装"实际只挂在**整合包导入**上（`InstancePackService.cs:757` 是全仓唯一的安装调用点）。

---

## 4. 累计状态

| 项 | 状态 |
| --- | --- |
| 已就绪且十补丁合并验证通过的补丁 | P-UA、P-FABRICMETA、P-PROXYJVM、P-FAKEIP、P-IMPORTLOG、P-DURABLEQUEUE、P-DEADLOCK、P-LOGSHARE、P-RUNSYNC、C |
| 缺陷 13 | **已复现（端到端）**，修复方向见报告待办第 19 项 |
| 仍未修 | 缺陷 1、4、8、10；缺陷 12 的"第二个窗口/僵尸窗口"；缺陷 13 |
| 待你决定 | C 的范围；提交 163 个未跟踪 + 41 个已修改文件；修哪几条缺陷 |

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
