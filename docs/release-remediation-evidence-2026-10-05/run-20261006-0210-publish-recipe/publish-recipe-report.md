# 发布配方的本地执行 + 两个被实测推翻的说法

日期：2026-10-06（Asia/Hong_Kong）。触发点：`release` 作业的 publish 步骤带
`/p:Version=${{ steps.version.outputs.version }}`，而此前所有本地验证（验证器的 publish）**都不带**这个参数
—— 也就是说"要发布的那条配方"从没在本地跑过。

## 1. 三次发布的对照（同一 csproj、同一 DebugType/DebugSymbols 设置）

| 发布方式 | PublishDir | Version 覆盖 | EXE 体积 | EXE SHA256（前 16）|
| --- | --- | --- | --- | --- |
| 验证器（快照内，无 Version） | 快照内 | 无 | 53,052,722 | `5009cf73d0801e83` |
| 本地，**带** Version | 本地临时目录 | `/p:Version=9.9.9` | 53,093,682 | `79beb39c9a2cf0e8` |
| 本地，**不带** Version | 本地临时目录 | 无 | 53,093,682 | `75572b14e7770afe` |

两次本地发布**体积完全相同**（53,093,682），说明 **`/p:Version` 不是体积差异的原因**
（它只改变版本字节，SHA 不同、体积不变）。

## 2. 真正的原因：一个被 gitignore、且没人引用的本地资源

`ui/` 目录树对比（排除 `ui/tests/`）：工作区 13 个文件，快照 12 个，差一个
**`ui/assets/axolotl.png`（47,681 字节）**。

- `.gitignore:36` 是 `ui/assets/*` —— 该文件**被显式忽略**，因此既不在 tracked、也不在白名单里；
- 但 csproj 的嵌入 glob 是 `..\..\ui\**\*`（只排除 `ui\tests\**`），**不理会 gitignore**，
  于是本地发布会把它嵌进去；
- 体积差 40,960 字节（单文件包是压缩的，47,681 字节的 PNG 压到 ~40KB 完全合理）。

**影响评估（好消息）**：`ui/` 下的 html/css/js/svg 里**没有任何地方引用它**
（全仓引用只出现在 `axolotl-src/`、根目录 `UI原型.html`，这两者按仓库文档本身就不属于干净克隆的构建输入）。
快照侧的资源闭包检查是 **9 引用 / 缺失 0**，九页界面运行时验收也是在**快照产物**上做的——所以
**发布物不缺资源，不存在 R02 式的漏资源缺陷**。

**但这条要记住（验证完整性）**：**本地 publish 的产物与 CI 从干净检出的产物不是同一份字节**
（差一个被忽略的本地资源，+40KB）。这正是本轮之前所有 UI/运行时证据都**绑定快照产物 `5009cf73…`** 的价值所在——
绑错产物的话，"界面 9/9"证明的就不是要发出去的那份东西。

## 3. 第二个被推翻的说法：`CopyOutputSymbolsToPublishDirectory` 并不能清掉原生 PDB

csproj 原注释称："实测 DebugSymbols/DebugType 管不住运行时包带来的原生 PDB，**需要这个开关**"。
**实测（本次）**：`PublishSingleFile=true` + 该开关 = false 的前提下，发布目录里**仍然**有

```
20918272  libHarfBuzzSharp.pdb
84033536  libSkiaSharp.pdb
```

三次发布（含快照内的干净构建）**全部**带这两个 PDB，合计约 104 MB。
真正让发行物干净的是 `release.yml`「Assert artifact is a self-contained single EXE」里那句
`Get-ChildItem dist -Filter *.pdb -File | Remove-Item` —— 也就是说：**手工 `dotnet publish` 会带出这 104MB**，
而发布流程靠的是**显式清理**，不是那个开关。csproj 注释已按实测更正。

## 4. 发布配方本身：独立 18 项冷启动验收 PASS

用带 Version 的那份产物（53,093,682 / `79beb39c…`）跑 R01 的原始 RED 复现动作
（`verify-single-file-standalone.ps1`，18 项）：**判定 PASS，18/18**，其中：

| 检查 | 实测结果 |
| --- | --- |
| 出现真实主窗口（GUI 真的起来了）| `title='松光启动器'` |
| 冷启动目录无外置 UI/ 目录 | 无（dirs 为空）|
| 程序集内嵌 UI 已解包到 `data/UI/<版本-指纹>` | **`9.9.9-5f6b42a61753`** |
| 解包目录含 index.html | ✓ |
| 解包界面与候选源码逐字节一致（防「新服务配旧界面」）| ✓ |

**这一条是新的覆盖**：此前所有冷启动证据都来自 1.0.0 的验证器产物；
现在证明**带 Version 覆盖的发布配方**（也就是 release.yml 真正用的那条命令）同样产出自足产物，
而且**版本键控解包在非 1.0.0 版本下也正确**（解包目录名是 `9.9.9-<12位指纹>`，不是复用旧版本目录）。

## 5. 顺带验证：锁定文件污染的复原流程

`dotnet publish -r win-x64` 果然把 `dev/PineglowLauncher/packages.lock.json` 就地改写成 RID 形态
（8663 → **10868** 字节，SHA 由 `fbbc9ae6…` 变 `36daca91…`），与本仓库记录的现象一致。
按既定流程 `dotnet restore dev/PineglowLauncher/Pineglow.Launcher.csproj --force-evaluate` 复原后：
**8663 字节 / `fbbc9ae67b8e8cf2`**，与基线**逐字节一致**；tests 的 lock 全程未动
（12806 / `244ba2bcd1eb1f5a`）。本次实验共触发并复原两次，两次都回到了同一哈希。

## 6. 边界与未做

- **未**把本次产物当发布件：版本是测试用的 `9.9.9`，临时目录已删除（只留 JSON 与哈希）。
- **未**验证 `/p:Version` 与 tag 的一致性链条在 CI 上的表现（那需要真实 tag；本地只用抽取的步骤正文验证过逻辑）。
- `ui/assets/*` 被忽略但会被嵌入这件事**没有修改**（改 glob 会改变产物哈希、牵动全部既有证据），
  属需要用户拍板的取舍；本轮只记录事实与影响。
