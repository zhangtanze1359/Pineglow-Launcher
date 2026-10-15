# 记录 95：补上 P-JAVAAUTO 的**覆盖缺口** —— 配置路径现在也要存在才算数

运行标识：`run-2026-10-10_015215-578dbafb` ｜ 2026-10-12 06:00–06:18（+08:00）
性质：**产品目录零改动**（0 staged）。补丁在隔离副本验证。

---

## 上一轮定位的缺口

记录 94 实测：`ResolveJavaPath` 的**前两个分支不做存在性检查**，直接返回配置里的路径。
于是「配置写着某个路径、那个路径后来失效」（换机器、清了缓存、卸载了 JDK）
**在第一行就返回**，永远走不到下面的扫描与自动安装 → 启动以 500 结束。

**P-JAVAAUTO 只覆盖了"扫描一个 Java 都没找到"那条路，没覆盖这一条。**

## 本轮补上：第 4 步

```csharp
if (!string.IsNullOrWhiteSpace(instance.JavaPath) && File.Exists(instance.JavaPath))  return instance.JavaPath;
if (!string.IsNullOrWhiteSpace(config.JavaPath)   && File.Exists(config.JavaPath))    return config.JavaPath;
```

**两个分支各加一个 `File.Exists`** —— 失效的配置路径现在会**落到扫描 + 自动安装**那条路上，
与"这台机器上从来没装过 Java"的处理方式一致。

## 验证

```
[OK] P-JAVAAUTO 1/3 the helper
[OK] P-JAVAAUTO 2/3 wrong version -> auto-install
[OK] P-JAVAAUTO 3/3 no Java -> auto-install
[OK] P-JAVAAUTO 4/4 configured paths must exist
steps applied = 4

buildExit=0   0 警告 0 错误
testExit=0    失败: 0，通过: 942
```

并直接确认改动落到了产品文件里：

```
2351: if (!string.IsNullOrWhiteSpace(instance.JavaPath) && File.Exists(instance.JavaPath))
2356: if (!string.IsNullOrWhiteSpace(config.JavaPath) && File.Exists(config.JavaPath))
```

## 现在 P-JAVAAUTO 覆盖了**两个入口**

| 入口 | 改前 | 改后 |
| --- | --- | --- |
| 配置里指定了路径（**存在**） | 用它 | 用它（不变） |
| 配置里指定了路径（**已失效**） | **直接返回 → 启动 500** | **落到扫描 + 自动安装** |
| 没配置，扫描找得到 | 用它 | 用它（不变） |
| 没配置，扫描找不到 | 抛错 | **自动安装** |
| 找到了但版本不对 | 抛错 | **自动安装** |
| 自动安装失败 | — | **仍抛出原来那句可操作的提示**（附失败原因） |

## 一处小瑕疵（如实记）

四步的标签现在是 `1/3、2/3、3/3、4/4` —— **不一致**（前三个写的是 /3，第四个写的是 /4）。
这是我在原有三步后面追加第四步时没回头改标签。**功能无影响，但标签会误导人以为只有三步。**

## ⚠️ 验证边界（不变）

**编译级验证通过**；"无 Java 的机器上点开始游戏真的会自动装好并启动"**仍未验证**（需要一次真实 JRE 下载）。
而且记录 94 那个回归检查**也还没补做**（需要一个 Java 存在的夹具）。

状态串：`TechnicalAcceptance=NOT_PASSED`；`FormalPhaseApproval=NOT_GRANTED`；`ReleaseApproval=NOT_GRANTED`。
