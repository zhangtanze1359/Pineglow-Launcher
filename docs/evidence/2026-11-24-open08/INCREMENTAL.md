# OPEN-08 补充验证：增量构建矩阵（2026-11-24）

## 为什么补这一项

初次验证只测了「带 Key / 不带 Key 各构建一次」，且用的是 `--no-incremental`。
复核方很可能追问：**增量构建下更换或移除 Key 会不会残留旧值？**

这一项就是回答它。

## 结论

**四种增量场景全部正确**，包括最有风险的两条：

| # | 场景 | 期望 | 实测 |
| --- | --- | --- | :---: |
| 1 | 首次构建，带 KEY-A | DLL 含 A、不含 B | ✅ |
| 2 | 增量构建，仍带 KEY-A | 含 A、不含 B（不丢失） | ✅ |
| 3 | **增量构建，换成 KEY-B** | **不含 A、含 B（换 Key 必须生效）** | ✅ |
| 4 | **增量构建，移除 Key** | **A、B 都不含（不残留）** | ✅ |

## 为什么这个结论可信

**MSBuild SDK 把自定义 `AssemblyAttribute` 的参数纳入了缓存哈希**，这一点是回读 SDK 源码确认的：

```text
Microsoft.NET.GenerateAssemblyInfo.targets
  <Hash ItemsToHash="@(AssemblyAttribute->'%(Identity)%(_Parameter1)%(_Parameter2)...')">
```

因为 `_Parameter2` 就是 Key 值，**Key 一变，哈希就变，`CoreGenerateAssemblyInfo` 就会重新生成**。
所以增量构建的正确性来自机制本身，不是运气。

## 过程中的一段误判（如实记录）

测试中途我曾报告「增量构建下换 Key 不生效」，判定实现有缺陷。**那是错的。**

真正的原因是**我的验证脚本**：PowerShell 里用 `[System.Text.Encoding]::UTF8.GetString(bytes).Contains(...)` 扫二进制，
以及我写的逐字节循环版本，**两种写法都给出了假阴性**。

> **修正（2026-11-24 复核后）**：我此前把原因归为「UTF-8 解码遇到无效字节序列会停止」。
> **这个说法未被复现证实**——复核方在本机复测不成立。
> 准确的表述是：**我的 PowerShell 脚本存在缺陷，导致假阴性；具体机制未查明**。
> 不编造根因，只记录事实。

改用 Python 直接做 `b'...' in data` 子串判断后，同样的构建产物立刻显示 True。
**错的不是实现，是验证工具。** 这也是本轮改用独立 Python 脚本的原因。

## 复现方式

脚本：[`incremental-matrix.py`](incremental-matrix.py)

```bash
# 先建工作树（脚本硬编码了该路径）
git -c core.longpaths=true worktree add D:/Projects/_worktrees/pg-open08-chk HEAD --detach
python docs/evidence/2026-11-24-open08/incremental-matrix.py
git worktree remove D:/Projects/_worktrees/pg-open08-chk --force
```

**注意**：脚本依赖该固定路径的工作树。工作树清理后直接重跑会失败（会得到 4 个 FAIL），
那是路径不存在导致的，不是实现问题。重跑前必须先建工作树。

## 本轮未留下的原始输出

那次通过时的完整控制台输出**没有落盘**——我先清理了工作树，之后才想归档。
**这是记录流程的失误：应当先保存输出再清理。**

因此本文件只保留结论与复现脚本，不声称"有可复核的原始日志"。若要复核，请按上面的命令重建工作树后重跑。
