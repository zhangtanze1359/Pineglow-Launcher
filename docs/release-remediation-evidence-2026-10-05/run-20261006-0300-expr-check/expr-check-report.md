# 表达式/引用静态检查：actionlint 的"可静态化子集"（第 16 轮）

日期：2026-10-06（Asia/Hong_Kong）。脚本：`expr-check.py`；输出：`expr-check-output.txt`。

## 1. 为什么做

actionlint 在本机拿不到（网络阻断，见 `run-20261006-0050-actionlint/`），而它检查的**很大一类是纯静态的**：
`${{ }}` 里引用的东西到底存不存在。这类错误只在发布时才炸，本地从来没有验过：

| 检查项 | 判据 |
| --- | --- |
| `steps.<id>.…` | 同一 job 里**在它之前**必须真有这个 id 的步骤（含顺序） |
| `matrix.<key>` | job 的 `strategy.matrix`（含 `include` 里的键）必须有这个键 |
| `env.<name>` | workflow / job / step 三级 env 里必须定义过 |
| `needs.<job>` | 必须是真实 job 名 |
| `upload-artifact` 的 `path:` | 该文件应当由同一 job 里**更早**的步骤产生 |
| `secrets.*` / `github.*` / `inputs.*` / `vars.*` | 本地无从验证 → **只列为"不可验证"，不算失败** |

**边界（不得当成 actionlint 通过）**：不校验表达式语法细节、类型、函数、shell 语义、action 输入 schema、
runner 行为与权限。

## 2. 结果

```
EXPR-CHECK RESULT=PASS problems=0 red_selftest=True
```

- **0 条问题**：两份工作流里所有 `steps.<id>` / `matrix.<key>` / `needs.<job>` / `env.<name>` 引用都能落地
  （例如 `steps.version.outputs.version` 确实由 release 作业里更早那个 `id: version` 的步骤产生）。
- **7 条 upload-artifact 路径全部追溯到更早的产生步骤**：
  `ui-resource-closure.json`、`test-inventory-report.json`（build 与 release 各一份）、
  `dist/standalone-cold-start.json`、`dist/live-update-stageA.json`、`dist/mirror-status.json`，
  以及 build 作业的 `bin/…/<rid>/publish/`。
- **13 条"不可验证"**（都是 `github.*` / `secrets.GITEE_TOKEN` 这类需要事件/仓库上下文的），如实单列，
  不混进"通过"。

## 3. 自证：检查器不是摆设（RED）

把 release.yml 复制到临时目录并**注入两个错误引用**
（`${{ steps.nosuchstep.outputs.nope }}`、`${{ matrix.nokey }}`），检查器报出：

```
✗ release.yml:release:步骤6(Publish (win-x64 single file)) .run: 引用了 `steps.nosuchstep`，
  但同一 job 里在它之前没有这个 id 的步骤（已定义: ['version']）
✗ release.yml:release:步骤6(Publish (win-x64 single file)) .run: 引用了 `matrix.nokey`，但 matrix 里没有这个键
```

两个注入都被抓到 ⇒ 结论"0 条问题"不是因为检查器什么都不看。

## 4. 本轮抓到的两个问题都在**检查器自己**身上（如实记录）

1. **假阳性**：第一版对 `build.yml` 的上传路径
   `dev/PineglowLauncher/bin/Release/net10.0/${{ matrix.rid }}/publish/` 报"找不到产生它的步骤"。
   实际上它正是上一步 `dotnet publish`（**不带** `/p:PublishDir`）的**默认输出目录**——
   是我的检查器不认这条规则，不是工作流的问题。已给检查器加上这条规则，
   并且在输出里**写明是按"默认输出目录"匹配、不是文件名匹配**（免得这条规则变成通用免检）。
2. **控制台编码崩掉**：脚本第一次运行在打印 `✓` 时直接
   `UnicodeEncodeError: 'gbk' codec can't encode character '\u2713'`。
   与本仓库 `VerifyHost` 把控制台输出固定成 UTF-8 是同一类问题；已用
   `sys.stdout.reconfigure(encoding="utf-8", errors="replace")` 修掉。
   ——"工具在中文 Windows 上会以意想不到的方式坏掉"这件事，本会话已经遇到第 N 次。

## 5. 未做

- **未**执行任何 `git`/发布动作；本文件与脚本都在证据目录（不进候选源码）。
- **未**把它接进 CI：它是 actionlint 的**替代品不是等价物**，接进去会给人"CI 已做静态检查"的错觉；
  真要在 CI 做这件事，应当安装 actionlint 本身。
