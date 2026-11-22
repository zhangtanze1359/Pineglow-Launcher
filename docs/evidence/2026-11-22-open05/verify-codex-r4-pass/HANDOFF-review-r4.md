# OPEN-05 第 4 轮独立复验

**通过。** 目标为本地 `main@a5e97306d8251976ac11035dfb9ca6b8c41151d4`，父提交为 `fe49b34f15c3ca39207fc64bcde34aec73ead74e`。第 3 轮的三项数量差异均已更正。夹具 SHA-256 与执行者的 LF 文件一致，README 仍可注明换行口径以方便独立复核。技术结论沿用第 3 轮。 `FormalPhaseApproval=NOT_GRANTED`。

## 复验输入与执行记录

2026-11-22 23:21:11 至 23:21:12（UTC+08:00）。开工时 `git status --short --untracked-files=all` 无输出。第 0 步原始输出依次为 `pwd`、`git rev-parse --show-toplevel` 和 `git rev-parse HEAD`。

```text
C:\Users\Administrator\.t3\worktrees\松光启动器\t3-3640cdba-44c6bd9a8c050fee
C:/Users/Administrator/.t3/worktrees/松光启动器/t3-3640cdba-44c6bd9a8c050fee
a5e97306d8251976ac11035dfb9ca6b8c41151d4
```

在上述独立工作树运行只读点验，并以 `-C D:/Projects/松光启动器` 读取本地主工作区。Git 读取使用 `-c core.longpaths=true`。点验命令整体退出 0，失败项 0。检查了 41 组用例证据文件，执行行为测试 0 例。逐项预期、实际和原始输出见 [点验记录](../evidence/2026-11-22-open05-review4/verification.txt)。

| 命令 | 预期与实际 | 退出码 |
| --- | --- | --- |
| `git -C D:/Projects/松光启动器 show --stat a5e9730` | 仅改执行证据 README，3 行增加、3 行删除 | 0 |
| `git diff fe49b34 a5e9730 -- scripts/` | 无输出 | 0 |
| `git diff --exit-code fe49b34 a5e9730 -- <受检路径>` | `scripts/`、`raw/`、`patched/`、夹具、第二轮归档均无差异 | 0 |
| `git diff --check fe49b34 a5e9730` | 无输出 | 0 |
| `Test-Path -LiteralPath <文件> -PathType Leaf` | 从夹具源码取得用例名并展开三棵树的五种路径写法。29 个树审计用例、12 个台账用例，每例元数据、stdout、stderr 均存在 | 点验 0 |
| `Get-ChildItem -LiteralPath <目录> -File -Force -Recurse`、`git ls-tree -r a5e9730 -- <目录>` | 磁盘与 Git 树均为 `raw/` 166 个、第 2 轮归档 93 个 | 0 |
| `git ls-tree -r <提交> -- <raw/>`、`git cat-file -t <对象>` | 从 `0fd28df` 取得 15 个 `tree-pathform-*`、10 个历史反证，25 个路径在 `fe49b34` 和 `a5e9730` 中均保留相同 blob ID，Git 对象均可读取为 blob | 0 |
| `git ls-files --eol`、`Get-FileHash -LiteralPath <夹具>`、`git cat-file blob a5e9730:<夹具>` | 分别核对执行者副本、独立检出与提交对象，见下表 | 0 |

## 三项数字

| 核对项 | 第 3 轮实测 | README 新值 | 第 4 轮实测 |
| --- | --- | --- | --- |
| r4 stdout 和 stderr | 82 个，41 对 | 82 个，41 对 | 41 个 `.out.txt`、41 个 `.err.txt`，共 82 个 |
| 反证输出 | 父版 10 对、修复版 2 对，共 24 个 | 父版 10 对、修复版 2 对，共 24 个 | `P` 10 对 20 个、`F` 2 对 4 个，逐组配对齐全，共 24 个 |
| 第 2 轮复验归档 | 93 个 | 93 个 | 磁盘与目标提交均为 93 个 |

`41 元数据 + 41 stdout + 41 stderr + 24 反证 + 4 探针 + 15 r3 遗留 = 166`，与磁盘及目标提交中的 `raw/` 总数相同。[执行证据 README](../evidence/2026-11-22-open05/README.md#产物清单实测)。

## 夹具哈希

| 字节来源 | 换行 | SHA-256 |
| --- | --- | --- |
| `a5e9730` Git blob、执行者主工作区的 `matrix-fixture.ps1`、执行工作树的 `_run-matrix.ps1` | LF | `7D354D07BA25B76B92E77030959AA7BEA79546C735D4C269F4DD276449049214` |
| 本轮独立工作树的 `matrix-fixture.ps1` | CRLF | `DF30F7319B1210824516755A5F0F97DA8CF78E6109E753E5C0EF589A0BDD5B3E` |

Git blob 为 14,591 字节。将 222 个 LF 转换为 CRLF 后为 14,813 字节，SHA-256 与独立检出一致。执行者所称的相同哈希在其实际副本成立。README 两处裸写 SHA-256，按磁盘字节复核的读者可能得到另一正确值。最小澄清是把两处标为「Git blob（LF）SHA-256」，在首处注明 Windows CRLF 检出的实测值。该口径说明是第 3 轮已有建议，三项数量判据均已通过。

## 未验证项

本轮未重跑 41 例矩阵、卷根、splat 或真实树对照。UNC、物理整盘、`gates.cjs`、.NET、前端、CI、Avalonia/WebView2 宿主、安装包与发布均未验证。此次只写入独立复验记录，未提交、推送、打 tag 或发 Release。

RESULT: PASS
