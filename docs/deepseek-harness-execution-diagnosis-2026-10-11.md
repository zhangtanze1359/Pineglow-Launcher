# DeepSeek Harness 执行中断诊断

日期：2026-10-11，Asia/Hong_Kong。对象：D:\松光启动器 的发布收口会话。范围：只读诊断、证据保存和恢复方案；未实施恢复、产品修复或独立发布验收。

## 结论

实际阻断执行的是模型请求返回 `400 status code (no body)` / `INVALID_REQUEST`。路由为 `opencode-go / deepseek-v4.1-flash`，推理等级 `max`。第 11 轮第 23 步首次发生，第 12–15 轮均在第 1 步再次失败，共 5 次；后四轮没有执行项目工具命令。

长会话请求是首要怀疑方向。最后成功响应记录的输入为 837 个非缓存 token + 663296 个缓存读取 token = 664133 token，约 66.4 万。该数值证明请求规模较大，但不证明超过服务端实际限制。没有错误正文，不能确定是上下文长度、序列化请求大小、历史回放格式或网关其他校验问题。

会话随后已经成功 `/compact`，但压缩后没有任何新的模型请求。因此当前状态是 **已压缩，恢复效果未验证**。原目标已被清除，不是已完成，也不是 256 轮用尽。

## 已核实的时间线

以原始事件 Unix 毫秒转换为香港时间，不以文件修改时间推断动作发生时间。

| 香港时间 | 原始 seq | 事实 |
| --- | --- | --- |
| 10 月 10 日 04:48:11 | 1739 | 第 11 轮第 23 步模型请求 400 |
| 10 月 10 日 12:24:14 | 1751 | 第 12 轮第 1 步，同样 400 |
| 10 月 10 日 12:28:09 | 1761 | 第 13 轮第 1 步，同样 400 |
| 10 月 10 日 12:45:19 | 1772 | 第 14 轮第 1 步，同样 400 |
| 10 月 10 日 13:03:57 | 1784 | 第 15 轮第 1 步，同样 400 |
| 10 月 10 日 13:04:01 | 1787 | goal/change，operation=clear |
| 10 月 10 日 13:04:12 | 1788 | 用户来源 compact 命令开始 |
| 10 月 10 日 13:05:18 | 1793 | 命令成功：`Compacted 674 history items (~477837 tokens).` |
| 10 月 10 日 22:06:39 | 1794 | 最后事件 session/end-seed；没有压缩后模型请求 |

第 11 轮第 22 步曾有一次 `Connection error` / `TRANSPORT`，重试后成功响应。这与之后连续出现的 400 应分别统计。

## 判断依据与边界

- 第 11 轮与第 12–15 轮的 request/header 配置哈希相同，工具数均为 79，工具定义哈希相同。没有发现失败前切换模型或突然增加工具定义的证据。
- 原始记录包含 335 个 tool/call 与 335 个 tool/result，调用 ID 均有对应结果；这降低了缺失工具结果的嫌疑，但不能证明网关实际接收的历史回放序列一定合法。
- 缓存投影记录：goal.current=null，goal.failure=null，lastTurn=15，openStep=null，pendingCalls={}。记录里没有挂起的工具调用。
- 压缩后的 surfaceTokens=7679、contextWindow=1000000 是 Harness 的本地统计；不能当作服务端实际接收大小或服务端限制。
- `C:\Users\Administrator\.dsh\profiles\desktop\cordis.patch.yml` 第 61–69 行已经存在该路由的提前压缩策略：thresholdRatio=0.30、retainRatio=0.08。本轮确认了文件内容，没有核实当前进程合并加载后的有效配置。不能把“没有配置压缩”当作根因，不能重复追加同一策略。
- 没有服务端 body、请求 ID 或原始网关验证信息，因此插件故障、余额问题、鉴权问题、安全限制、精确上下文上限均未获证实。也没有据 HTTP 400 推断 Daybreak 拒绝。

## 两个相邻的验证脚本问题

### 续传探针的观察来源错误

文件：`docs/release-closure-evidence-2026-10-10/run-2026-10-10_015215-578dbafb/probe-range-resume.ps1`。

第 136 行从 launcher 日志筛选“继续下载/已完成”并写入 resumeLogLines，第 142 行检查 resumeLogLines 非空，否则 exit 4。实际续传消息被捕获在下载任务消息流 messagesSeen 中，resumeLogLines 为空。因此这次 exit 4 来自探针断言观察源错误。

`d-range-resume2/range-resume.json` 同时记录：

- 预写临时文件 1048576 字节；messagesSeen 含“继续下载：1.21.11.jar（已完成 1.0 MB）”。
- originalJarSha1 与 finalJarSha1 同为 `ba2df812c2d12e0219c489c4cd9a5e1f0760f5bd`，jarMatchesOriginal=true，leftoverTemp=false。
- downloadStatus=downloading，资源消息为 3000/4591。

这支持该 JAR 下载路径识别已有部分内容且最终文件正确；不能扩大成整个安装完成、队列跨重启恢复通过，也没有服务端 Range 请求头/206 的抓取证据。最小修复方向是独立副本的断言改查 messagesSeen，保留 JAR 校验及原 exit 4 证据，并按验证目标补充真实场景。当前文件仍未修复。

### BOM 写入使编辑读取快照过期

seq1727 的 shell 调用使用 WriteAllBytes 给上述文件补写 UTF-8 BOM；seq1728 返回 `BOM re-added`、parse errors=0、run exit=4。随后 seq1735 的 edit 使用旧读取快照，seq1736 返回 `FS_STALE_VERSION`：

```text
file changed since it was read — re-read the file, then retry
```

现文件首字节为 EF-BB-BF。可以定位到同一执行者此前的写入导致快照过期；没有证据认定其他软件抢写或权限不足。应重新读取文件后再编辑，并保留 BOM。随后模型400阻断了进一步处理。

## 现场与本轮变更

仓库 HEAD：`c8aa286f65ad13433129fe0a9e86e2d3b0e9020b`。开始检查时 41 个 tracked 改动、2253 个 untracked、0 staged；取证文件会增加 untracked 数量。既有源码和实施者自检不计为本轮成果。

本轮只新增诊断报告、恢复工作流、可复制提示词及诊断证据。未编辑产品源码、原探针、Harness profile、会话或目标，未重启/停止用户进程，未提交、推送或发布。取证前后比对的原始会话、profile、原探针、原 range JSON、LocalApiService.cs、verify-source-snapshot.ps1、source-snapshot-inputs.txt 共 7 个输入哈希一致；不是对整个仓库的完整哈希认证。

原始会话：`C:\Users\Administrator\.dsh\sessions\--D-~677E~5149~542F~52A8~5668--\session-98527af6-2b06-4bf8-9c00-e4bc727dd861\session.v4.jsonl.zstd`。

缓存投影：`C:\Users\Administrator\.dsh\storages\session_projcache\sessions\session-98527af6-2b06-4bf8-9c00-e4bc727dd861.json`。

证据目录：[harness-execution-diagnosis-evidence-2026-10-11](./harness-execution-diagnosis-evidence-2026-10-11/)；summary.json 保存源哈希、5 次失败、压缩事件、统计和关键文件断言；selected-tool-results.json 保存 BOM/旧快照拒绝结果；model-failures.json 包含短暂连接失败和400，未复制模型思考或完整私有会话。

采集方法使用 Bun 解码拼接 ZSTD 帧。最初 Node 解码只读到首帧，另一次采集脚本输出路径未解码导致 ENOENT；这两项均为本轮取证工具问题，已修正并成功采集，不是 Harness 执行故障。

## 下一步与交付边界

优先按 [恢复工作流](./deepseek-harness-execution-recovery-workflow-2026-10-11.md) 验证压缩后的原会话能否响应。不要再无区别地循环恢复，也不要直接重启长目标。

本轮诊断完成；恢复未实施。A+B 的实施者自检、其他发布缺口均需各自独立验收，Harness 能响应不代表产品达到发布条件。FormalPhaseApproval=NOT_GRANTED；ReleaseApproval=NOT_GRANTED。
