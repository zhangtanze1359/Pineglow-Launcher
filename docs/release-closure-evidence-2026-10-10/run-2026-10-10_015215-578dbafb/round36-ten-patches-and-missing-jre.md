# 第 36 轮：十补丁合并复验 + 一个新发现（「内置 JRE」并不存在）

运行标识：`run-2026-10-10_015215-578dbafb`（第 36 轮追加）
时间：2026-10-11 18:56–19:05，Asia/Hong_Kong（+08:00）
本轮性质：**产品目录零改动**（0 staged）。验证在 `%TEMP%` 隔离副本里完成。

---

## 1. 十补丁合并复验（第 35 轮加了 P-RUNSYNC，旧结论已过期）

| 门槛 | 结果 |
| --- | --- |
| 应用 10 条补丁（7 个脚本，41 处改动） | 无一处冲突或失配 |
| `dotnet build -c Release` | **0 警告 0 错误** |
| **C# 全量** | **949 通过 / 0 失败**（941 + 8 个新增回归） |
| **Node 全量** | **305 通过 / 0 失败** |
| 共享 `packages.lock.json` | 应用前 / 后均 `FBBC9AE6…` |
| **锁定单文件发布** | **exit 0** → `232FEE225826148AAFFBA0BB97321F5D09D52A533F342899A41263A0A3F9DCA5`，53,056,818 字节 |
| **独立冷启动** | **PASS**（18 项检查，失败 0） |

（与第 34 轮 9 条补丁的产物同样 53,056,818 字节——P-RUNSYNC 未改变产物体积。）

---

## 2. 新发现：设置页说「使用内置 JRE（默认）」，但**并不存在这个 JRE**

### 2.1 界面在说什么

`ui/js/launcher.js:631`：当**没有检测到任何 JDK** 时，Java 那一栏显示

```js
: (java.length ? t('set.javaAuto', { n: java.length }) : t('set.javaBundled'))
```

而 `set.javaBundled` 的文案是（`i18n.js:233` / `:795`）：

```
'使用内置 JRE（默认）'  /  'Use Bundled JRE (Default)'
```

即：**在一台没有任何 Java 的机器上，设置页会告诉用户"正在使用内置 JRE"。**

### 2.2 那个"内置 JRE"从哪来

| 检查 | 结果 |
| --- | --- |
| 发布产物里有没有随附 JRE | **没有**——publish 目录只有 `Pineglow.Launcher.exe`（53 MB，装不下 JRE） |
| `JavaLocator` 去哪里找 | `JavaLocator.cs:210`：`Path.Combine(PlatformPaths.GetConfigDirectory(), "runtimes")` |
| 谁会往 `runtimes` 里装 | **全仓只有一个调用点**：`InstancePackService.cs:757`（**整合包导入**路径） |
| 有没有别的安装入口 | 没有。`/api/java` 只有 **GET**（检测/列举）；`ui/` 里**没有任何**"安装 Java"的入口 |
| 找不到 Java 时启动会怎样 | `ResolveJavaPath` 抛：`未找到可用的 Java 运行时（该版本需要 Java N），请先在设置中指定 Java 路径` |

**也就是说**：那个"内置 JRE"**只有走过整合包导入才会被下载到 `runtimes`**。
在"装个原版 → 点开始游戏"这条**最常见的首启路径**上，它不存在。

### 2.3 用户实际会遇到的循环

1. 新机器（无 Java）装上启动器；
2. 设置页显示 **"使用内置 JRE（默认）"** → 用户认为没问题；
3. 从「下载」装一个版本，点「开始游戏」；
4. 报错 **"未找到可用的 Java 运行时…请先在设置中指定 Java 路径"**；
5. 去设置页——那里**只有"指定路径"，没有安装**；而页面还在说"使用内置 JRE"。

**README 把「Java：自动检测与自动安装」列为一级功能**，但"自动安装"实际只挂在**整合包导入**上。

### 2.4 证据等级与局限（如实标注）

- **代码级 + 界面级证据**：上面每一格都是可复核的事实（文件与行号都在）。
- **本机确实没有系统 Java**（`java` 不在 PATH、`JAVA_HOME` 为空、注册表无 `JavaSoft`），所以这个场景**是真实的、不是假想**。
- **但我没有跑完整的"无 Java 端到端启动"**：我此前所有夹具都从既有配置里继承了 `JavaPath`（指向 `%TEMP%` 里的 JRE），因此那条路径从未被真正走通。**这一点留到下一轮补**。

**严重度：中高。** 命中首启最常见路径，且界面文案会**误导**用户（说"在用内置 JRE"而实际没有）。

**修复方向**（属产品改动，需授权）：① 至少把 `set.javaBundled` 的文案改成实话（例如"未检测到 Java——首次启动时会自动下载"）；② 把 `JavaRuntimeService` 接到**启动路径**上（检测不到就自动装，与整合包导入一致）；③ 设置页给一个显式的"安装 Java"入口。

---

## 3. 累计状态

| 项 | 状态 |
| --- | --- |
| 已就绪且**十补丁合并验证通过**的补丁 | P-UA、P-FABRICMETA、P-PROXYJVM、P-FAKEIP、P-IMPORTLOG、P-DURABLEQUEUE、P-DEADLOCK、P-LOGSHARE、P-RUNSYNC、C（+ 3 个回归测试文件） |
| 本轮新增缺陷 | **第 13 条**：设置页宣称"使用内置 JRE（默认）"，而该 JRE 只有整合包导入才会安装 |
| 仍未修 | 缺陷 1、4、8、10；缺陷 12 的"第二个窗口/僵尸窗口"；缺陷 13 |
| 待你决定 | C 的范围；提交 163 个未跟踪 + 41 个已修改文件；修哪几条缺陷 |

状态串：`TechnicalAcceptance=NOT_PASSED`（发布整体）；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
