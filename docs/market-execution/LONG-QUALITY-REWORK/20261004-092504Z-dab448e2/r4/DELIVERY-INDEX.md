# 交付索引（包外，封存后写入）

本目录（`r4/`）包含本轮交付包与其**包外**封存/审计材料。包内内容在封存后未再被写入。

## 交付包

`FINAL-LOCAL-QUALITY/20261004-092504Z-dab448e2-rework/`

| 项 | 值 |
| --- | --- |
| 包内文件数（manifest 登记） | **121** |
| 包内总字节 | **1,422,834** |
| `manifest.json` SHA256 | **`7996AE0CBF6B78F5E523834DC0E7CCC5A7AF55C13357EB5D1653CDF69CDC3DAE`** |
| 分离摘要 | `20261004-092504Z-dab448e2-rework.manifest.sha256` |
| 分离摘要匹配 | **是** |

## 包外审计与复验（封存后写入，均在包外）

| 文件 | 结果 |
| --- | --- |
| `out-of-package-audit.json` | PASS — 121/121 逐项核 size+SHA，未登记文件 0，分离摘要匹配 |
| `post-seal-reverify.json` | PASS — 第二次独立遍历，结果一致 |
| `independent-seal-crosscheck.json` | PASS — 用 Python 独立实现复核（与 Node 封存器不同代码路径）：121/121 通过、0 失败、0 未登记，并交叉核对包内 16 项机器判定声明全部成立 |
| `post-delivery-source-check.json` | PASS — 交付后重新采集源码现场：292 项输入与封存终态 **0 漂移**，git status 与 HEAD 与封存时**完全一致** |
| `seal.log`、`verify-seal.log`、`collect-facts.log` | 原始命令输出 |

## 源码现场

- SourceRoot `D:\松光启动器`，HEAD `f145d14eb26b0b7459a28491e764ac8c573085d3`，分支 `main`
- 工作区仍为 dirty；`git status --porcelain -uall` 在本轮**开始前**与**交付后**为**同一份文本**（1425 行），
  HEAD 未变，未新增/删除任何文件，未提交、未清理、未回退
- 本轮实际改动 6 个已存在文件（见包内 `verdict/changed-files.json`）

## 术语与占位符（包内报告使用，封存后在此补充说明）

包内报告为了保持路径可移植，使用了以下占位符；此处给出本轮的实际取值：

| 占位符 | 含义 | 本轮实际值 |
| --- | --- | --- |
| `<ev>` | 本轮证据根 | `C:\Users\Administrator\.codex\worktrees\34ca\松光启动器\docs\market-execution\LONG-QUALITY-REWORK\20261004-092504Z-dab448e2` |
| `<exec>` | R3 最终 GUID 执行副本 | `C:\Users\Administrator\AppData\Local\Temp\dsr-rework-r3c-10ed6d4c` |
| `<src>` | 产品源码根（只读，不在此构建） | `D:\松光启动器` |
| `<旧封存包>` | 上一轮封存的 LONG-QUALITY-GOAL 最终包（严格只读） | `...\docs\market-execution\LONG-QUALITY-GOAL\20261003-092612Z-b4d896a8\FINAL-LOCAL-QUALITY\20261003-092612Z-b4d896a8` |

包内原始 JSON 记录（`runner/run-binding.json`、`runner/runner-verdict.json`、`runner/stages/*.json`、
`attempts/*/logs/*`）保留了生成时的绝对路径，这是**原始证据的本性**，不是复验依赖：
复验只需按 `manifest.json` 的相对路径核对存在/字节数/SHA，并读取其中的计数器与原因码。
`<exec>` 与 `<ev>` 均为本轮新建的独立目录，旧副本从未被就地改写。

## 结论

六项返工（LQ-01 ~ LQ-06）全部关闭，`implementationReady = true`。
`FormalPhaseApproval = NOT_GRANTED`，`ReleaseApproval = NOT_GRANTED`。

**具备独立复验条件，等待 Codex 独立验收。**
