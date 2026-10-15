# release 作业本地干跑（步骤正文 + RED 用例）

日期：2026-10-06（Asia/Hong_Kong）。目的：给 **只会在 CI 里跑** 的发布门槛补上本地证据。

## 1. 为什么做这个

`release.yml` 的 `release` 作业里有四条纯 PowerShell 门槛：

| 步骤 | 作用 |
| --- | --- |
| `Verify assembly version matches tag` | 程序集版本必须等于 tag |
| `Assert artifact is a self-contained single EXE` | 发布目录只能剩一个 EXE（多余 PDB/UI 会被清理，其它多余文件/目录直接失败）|
| `Rename exe + generate manifest` | 生成 `dist/manifest.json` |
| `Validate update manifest against final artifact` | 清单必须与最终附件自洽（version/HTTPS/资产名/SHA256/size）|

**它们此前从未被本地执行过**——只有"提交后 CI 会跑"这一句承诺。也就是说：门槛写错了、或者写松了却放行，
在本地是看不出来的。本干跑把 **release.yml 里真实的 `run:` 正文**抽出来执行（不是重写一份副本，
避免"验的是副本、跑的是另一份"），并对每条门槛都配 RED 用例。

## 2. 做法

- 抽步骤：用 PyYAML 读 `.github/workflows/release.yml` → `jobs.release.steps`，按**步骤名**取 `run` 正文。
- 表达式替换：只替换 3 个已知表达式（`steps.version.outputs.version`、`github.ref_name`、`github.repository`）；
  **替换后只要仍残留 `${{`，就拒绝运行该步骤**（宁可报错，也不假跑一份没替换干净的脚本）。
- 步骤正文含中文注释 → 落盘为 **UTF-8 BOM** 的 `.ps1`（宿主是 Windows PowerShell 5.1，无 BOM 会乱码）。
- 工作目录按 release 作业的形状搭：`dist/`（放本次发布附件）+ `dev/PineglowLauncher/bin/Release/net10.0/win-x64/Pineglow.Launcher.dll`。
- 被测附件：快照验证器刚发布的单文件 EXE（53,052,722 字节，sha256 `5009cf73…`），复制进 `dist/` 并按 release 命名。
- 脚本：`release-job-dryrun.py`；原始输出：`dryrun-console.log`；结构化结果：`release-job-dryrun.json`；
  实际执行的步骤正文：`step-*.ps1`（8 份，含 1.0.0 / 1.0.1 两个版本参数）。
- 干跑期间 `work/` 里那份 55 MB 副本已删除，需要时用脚本重建。

## 3. 结果

```
RELEASE-JOB-DRYRUN RESULT=PASS cases=18 failed=0
```

| 用例 | 期望 | 实测 |
| --- | --- | --- |
| A1 干净 dist | 放行 | exit 0 |
| A2 多余 `stray.pdb` + `UI/` | 放行**且真的清掉**它们 | exit 0，清理后两者都不存在 |
| A3 多余非 EXE 文件 | 失败 | exit 1 |
| A4 多余目录 | 失败 | exit 1 |
| A5 两个 EXE | 失败 | exit 1 |
| B1 DLL 版本与 tag 一致（1.0.0）| 放行 | exit 0 |
| B2 DLL 1.0.0 而 tag 1.0.1 | 失败 | exit 1 |
| C1 生成 manifest | 放行 | exit 0，字段/URL/SHA/size 自洽 |
| V1 正确清单 | 放行 | exit 0 |
| V2 version 不一致 | 失败 | exit 1 |
| V3 非 HTTPS | 失败 | exit 1 |
| V4 URL 不指向本次资产 | 失败 | exit 1 |
| V5 **大写 SHA256** | **放行**（见第 4 节）| exit 0 |
| V6 SHA256 与实际不符 | 失败 | exit 1 |
| V7 size 与实际不符 | 失败 | exit 1 |
| V8 缺清单 | 失败 | exit 1 |

生成的清单实测：

```json
{"version":"1.0.1","url":"https://github.com/zhangtanze1359/Pineglow-Launcher/releases/download/v1.0.1/Pineglow-Launcher-win-x64-single-file.exe","size":53052722,"sha256":"5009cf73d0801e83ed68e23829486b2d27d1a313e0db67b5acbdc17bf169383c","notes":"Pineglow Launcher 1.0.1","mandatory":false}
```

## 4. 干跑抓到的第一件事：我的期望是错的（而门槛是对的）

第一轮我写了"大写 SHA256 必须失败"，结果**门槛放行了**，干跑报 FAIL。查下去：

- `release.yml` 用的是 PowerShell `-notmatch '^[0-9a-f]{64}$'`，而 **`-match` 默认大小写不敏感**，
  所以 `A-F` 也算通过 —— 步骤注释里"必须是 64 位**小写**十六进制"这句话**比代码更严**。
- 客户端 `UpdateService` 这边：`IsValidSha256` 用 `Uri.IsHexDigit`（不区分大小写），
  `Sha256Matches` 用 `StringComparison.OrdinalIgnoreCase` 比较 —— **客户端本来就接受大写**。

结论：**门槛的行为是对的，我原来的期望与那句注释才是错的**。两种处理都可能：
把门槛收紧成 `-cmatch`，或改掉注释。选后者，因为收紧会让发布端**比客户端更严**，
拒绝一份客户端本可正常校验的清单 —— 那是凭空增加一种发布失败模式，没有任何安全收益
（哈希的大小写不携带语义）。已据此：

1. 把 `release.yml` 的注释与抛出信息改成"64 位十六进制（大小写不敏感，与客户端一致）"，
   并写明**不要**改成 `-cmatch` 的理由；
2. 把该用例改名为 `V5-sha-uppercase-accepted`，期望改为**必须放行**，
   这样以后有人"顺手收紧"时会被这条用例拦下。

## 5. 边界（不得过度解读）

- 只执行**纯 PowerShell 的步骤正文**；`actions/*`（checkout / upload-artifact / action-gh-release）
  一律没执行，上传与建 Release 的行为**未验证**。
- **不等于 actionlint 通过，也不等于 runner 通过**：工作流语法、表达式语义、runner 环境、
  跨步骤状态、矩阵、权限与 secrets 都不在本覆盖范围内。
- 用的是本机 Windows PowerShell 5.1（CI 的 `shell: pwsh` 是 PowerShell 7）——
  **shell 版本差异未覆盖**；这也是"本地干跑"与"runner 真实执行"之间最大的一条缺口。
- 干跑用的附件来自本机快照发布产物，不是 CI 构建产物。
