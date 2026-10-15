# MR19 补测 · CI 工作流不变量（缺口登记表 A11）

- **run-id**：`20261003-260000Z`
- **性质**：把 MR19 的 CI 不变量钉进**本地测试套件**，并实跑 actionlint 作语法层验证
- **依据**：`gap-register.md` §三 A11："**actionlint CI 门禁**未加；CI YAML 是否纳入源码快照白名单"
- **最终状态**：**证据就绪**（13 个不变量用例；3 个 RED 成立；actionlint 1.7.7 exit 0）

---

## 一、先澄清 A11 里的一半疑问：**CI YAML 不需要进白名单**

`git ls-files -- .github/` 输出：

```
.github/workflows/build.yml
.github/workflows/release.yml
```

两者都是**已跟踪文件** → 走 `git ls-files` 进快照，**不需要**白名单条目
（白名单只登记**未跟踪**的源码输入）。**该疑问到此关闭。**

---

## 二、为什么不"直接加一个 CI 步骤"

本机**没有 runner**。加一个**从未被执行过**的 CI 步骤，等于把未验证的东西写进流水线 ——
而 A11 的原意是"防止工作流语义回退"。

因此本包采用**两条互补的路**：

| 路 | 做什么 | 能被谁验证 |
|---|---|---|
| **① 本地不变量测试** | 把"工作流里必须成立的事"写成 13 个用例，进**常规全量套件** | 每次 `dotnet test` 都验 |
| **② actionlint 实跑** | 对两个工作流跑 **actionlint 1.7.7**（语法层） | 本包实跑并留日志；二进制身份记 SHA |

> **口径**：① 是**文本级**检查，**不是** YAML 解析，也**不能**替代 actionlint 的语法校验；
> 它防的是"守卫被删/顺序被挪/给 RID 发布加回锁定"这类**语义回退**。

---

## 三、13 个不变量用例

| # | 不变量 | 为什么重要 |
|---|---|---|
| 1–2 | 两个工作流都有 lock 守卫步骤，且**检查 RID 段**（`net10.0/`）与 **`Microsoft.NET.ILLink.Tasks`**，覆盖**两个** lock 文件，且**会失败退出**（`exit 1` / `throw`） | 守卫只警告不失败 = 没守；漏一个 lock = 漏一半 |
| 3–4 | 两个工作流都有**非 RID 的 `--locked-mode` 还原** | 守卫保护的就是这条还原 |
| 5–6 | **守卫必须排在 RID 发布之前** | 排在后面等于没守（RED 实测能检出） |
| 7–8 | **RID 发布不得使用 `RestoreLockedMode`**，且 NU1004 互斥理由写在注释里 | 实测必然 NU1004（MR19 §2.1/2.2）；注释防止后人"顺手加上锁定" |
| 9–10 | 工作流**不得引用 RID 专用 lock 文件**（`packages.<rid>.lock.json`） | 本项目只有一份 lock，出现第二份就是形态被改 |
| 11 | `build.yml` **仍接入 `--bisect-smoke`** | 那是 MR06/MR07 端到端冒烟的唯一入口 |
| 12–13 | 两个工作流的 SDK 都**从 `global.json` 取版本** | SDK 漂移会让锁定还原与构建结果不可复现 |

实测输出（节选）：

```
[A11] build.yml：守卫在第 58 行、Publish 在第 126 行（顺序正确）
[A11] release.yml：守卫在第 35 行、Publish 在第 53 行（顺序正确）
[A11] build.yml：1 条 RID 发布均未使用 RestoreLockedMode，且注释记录了 NU1004 理由
[A11] build.yml 仍接入 --bisect-smoke
```

---

## 四、actionlint 实跑（语法层）

```
$ %TEMP%\actionlint-check\actionlint.exe --version
  1.7.7 / installed by downloading from release page / built with go1.23.4 for windows/amd64

$ actionlint -shellcheck= -pyflakes= .github/workflows/build.yml .github/workflows/release.yml
  （无输出）   EXIT = 0
```

二进制身份（SHA256）：`6D470A52039A433BCCDF57BD65170885A5B23E511155D0DE587C2C3CE0EB1C24`
（本会话在 MR19 那轮已用技能里钉住的校验和验证过这个版本与资产。）

---

## 五、三个 RED（证明不变量有牙齿）

| RED | 改什么 | 结果 |
|---|---|---|
| **1** | 把守卫步骤名 `Guard lock files…` 改成 `Check lock files`（模拟守卫被删/改名） | **2 失败**：守卫存在性 + 顺序检查 |
| **2** | **真的把守卫整块移到 Publish 之后** | **1 失败**，消息精确：`build.yml 的守卫（第 113 行）必须排在 RID 发布（第 111 行）之前` |
| **3** | 给 RID 发布加回 `/p:RestoreLockedMode=true` | **1 失败**：`RidPublish_MustNotUseRestoreLockedMode` |

**RED-2 我构造错了两次**（第一次只改步骤名、没真挪动 → 0 失败，行为正确）——
第二次按行块真正搬移才复现。**这次"没变红"恰恰说明断言没有被名字迷惑**，但过程记在这里以免被误读成"测试无效"。

三个 RED 后 `build.yml` **逐字节还原**（每次打印 `还原 = True`），随后 actionlint 复跑仍 **exit 0**。

---

## 六、本轮自找并修掉的一个测试设计缺陷

第一次运行 **12 通过 / 1 失败**：`RidPublish_MustNotUseRestoreLockedMode(build.yml)`。

原因：`build.yml` 第 121 行的**注释**里正当地写着"实测 `dotnet publish -r win-x64 -p:RestoreLockedMode=true` 必然失败于 NU1004"，
而我的过滤器把**注释也当成命令**了 → 断言把**说明文字**当成了违规。

**修正**：只检查**可执行行**（排除 `#` 开头的行）。修正后 **13/13 通过**。

> 这与本会话反复出现的教训同源：**测试的"输入提取"本身也会出错**，
> 而注释/说明文字是最容易被误当成代码的输入。

---

## 七、验证

| 检查 | 结果 |
|---|---|
| 不变量用例 | **13 通过 / 0 失败** |
| actionlint（还原后复跑） | **exit 0** |
| C# 全量 | **786 通过 / 0 失败 / 0 跳过**（773 + 13） |
| Node 全量 | **230 通过 / 0 失败**（18 个文件） |
| 工作流文件 | 三个 RED 后逐字节还原 |

---

## 八、实际改动

| 文件 | 改动 |
|---|---|
| `dev/PineglowLauncher.Tests/WorkflowInvariantTests.cs` | **新增**（13 个用例） |
| `scripts/source-snapshot-inputs.txt` | 登记新测试（类别 B 37→38） |
| `docs/market-execution/gap-register.md` | A11 标记为**已关闭** |
| `docs/market-task-plan-2026-10-03.md` | MR19 行补记 |

**生产代码与工作流本身零改动**（工作流只在 RED 期间临时改动，已还原）。

---

## 九、仍未覆盖

| 项 | 说明 |
|---|---|
| **真实 GitHub Actions 运行** | 仍未验证（本机无 runner）。本包证明的是"工作流文本满足不变量 + 语法通过 actionlint"，**不是**"在 GitHub 上真的会这样跑" |
| **把不变量检查接进 CI** | 本包**没有**改工作流（那会引入一个我无法验证的步骤）。若你要接，做法是在 build.yml 里加一步 `dotnet test --filter WorkflowInvariantTests`（本包的用例已经能被这样调用） |
| `release.yml` 的 Gitee 镜像步骤 | 未审查（属发布路径，需要凭据与真实发布才能验证） |

---

## 十、证据清单

```
green/01-actionlint.log               actionlint 1.7.7 首次实跑（exit 0）+ 二进制 SHA
green/01-invariants-first.log         首次运行 12 通过 / 1 失败（暴露注释误判）
green/02-invariants-fixed.log         排除注释后 13 通过
green/03-invariants-final.log         最终 13 通过 / 0 失败
green/04-csharp-full.log              786 通过 / 0 失败
green/05-node-full.log                230 通过 / 0 失败
green/06-actionlint-final.log         还原后 actionlint 复跑（exit 0）
red/build.yml.before                  RED 前工作流备份
red/01-red-no-guard.log               RED-1：守卫改名 → 2 失败
red/02-red-renamed.log                RED-2 第一次（构造错误：没真挪动 → 0 失败）
red/03-red-locked-publish.log         RED-3：加回锁定 → 1 失败
red/04-red-guard-moved.log            RED-2 正确版：守卫移到发布之后 → 1 失败
manifest.json
```

**本报告不宣布阶段放行、不提交、不发布。**
