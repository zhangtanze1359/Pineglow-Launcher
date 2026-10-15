# 缺 Java 的用户会看到什么：能挡住，但没有"安装 Java"入口（第 41 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
方法：`probe-java-entry.ps1`（本目录）启动候选产物，列出 Java 安装、建实例、跑 `prelaunch-check`、试 `launch`。

## 1. 代码事实（本轮核对）

| 事实 | 位置 |
| --- | --- |
| `JavaRuntimeService.InstallAsync`（自动下载 Java）**只有一个调用点**：整合包导入 | `InstancePackService.cs:755-757` |
| 可自动下载的大版本只有 **17 / 21 / 25**，其它抛"暂不支持自动下载 Java N" | `JavaRuntimeService.cs:36-48` |
| 安装位置约定 `<配置目录>/runtimes/java-<大版本>-<os>-<arch>/`，并**校验下载包 SHA256** | `JavaRuntimeService.cs:84-125` |
| 界面里与 Java 有关的控件只有 `#btnJrePath`（显示/选择已安装的 Java 路径） | `ui/js/launcher.js:666,3545` |
| i18n 写"使用内置 JRE（默认）"/"Use Bundled JRE (Default)"，而 53 MB 单文件产物里**没有 JRE** | `ui/js/i18n.js:231,233,793,795` |

⇒ **结论**：`/api/java` 是 **GET only**，没有"安装 Java"的接口；普通（非整合包）用户若机器上没有 Java，
界面**没有安装入口**，只能自行安装后把路径指给它。

## 2. 实测：缺 Java 时的可观测反馈

```
/api/java   installations=0   javaPath=''
/api/state  java=0            config.javaPath=''
创建实例     id=b0b06482…  version=1.20.1  gameDir=<便携 data>\.minecraft
prelaunch-check  →  ok=False
   错误：未找到可用的 Java 运行时，请先在设置中指定 Java 路径
POST /api/instances/{id}/launch  →  HTTP 400（被正确挡住，不会带着缺 Java 硬启动）
```

好的部分：**检测到位、提示明确、启动被硬拦**（400），不会出现"点启动没反应"。
不足的部分：提示要求用户"先在设置中**指定 Java 路径**"，而设置页只能"指定"、**不能"安装"**；
文案里的"内置 JRE"会让用户以为产物自带运行时。

## 3. 影响与建议（属产品决定，我没改）

1. **发布说明必须写清楚运行环境**：需要 **Java 17 / 21 / 25**（与现代版本对应；老版本要 Java 8，
   而自动下载**不支持 8**）。README 的环境表若没有这一条，应补上。
2. **可选产品决定**：在设置页加一个"安装 Java"动作（能力其实已经具备，只是没暴露给普通用户），
   或至少把文案从"使用内置 JRE（默认）"改成"由启动器管理的 Java（缺失时可自动下载）"。
3. 代价提示：改 `ui/**` 会让嵌入 UI 指纹、单文件 SHA256 与**全部** UI 证据失效 ⇒ 需整轮重跑；
   只改 i18n 文案同样属于 `ui/**` 变更。

## 4. 边界

- 本条**不**判断"要不要内置 JRE"（那会让产物 +45MB，属产品定位）；只把事实与代价列清楚。
- 本轮**没有**跑 `InstallAsync` 本身（唯一入口是整合包导入）；它的"来源可用 + 目录约定 + 被产品发现"
  已在 `run-20261006-1010-java-ready/` 用真实 JRE 单独验证。
