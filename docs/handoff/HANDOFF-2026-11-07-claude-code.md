# HANDOFF 2026-11-07 · Claude Code（批量执行 · 前端）

> 角色：大范围机械改动的执行者。**前置：D1 合流分支 `integration/v1.1.0-20261107` 已存在且全绿。** 不要在 `main` 或 `remediation/v1.1.0` 上直接改。

## 一、路径边界

**允许写**：`D:/Projects/_worktrees/pineglow-integration-20261107/` 下的 `ui/`、`scripts/tests/`、`scripts/*.cjs`、`dev/PineglowLauncher/VerifyHost/**`（仅 CC2 需要的验证器），以及该分支的 `docs/`（只写 `docs/m1-3-*` / `docs/m2-3-*` 两份记录）

**只读**：主仓库全部、两个旧 worktree、`_archive/bundles/*.bundle`

**禁止**

- 改 `dev/PineglowLauncher/**` 的产品逻辑（C# 侧本轮不归你；发现 C# 缺陷→写进记录，交回 DSH）
- `git push` / tag / 改 `.github/workflows/*` 的通过条件 / 删测试 / 放宽阈值
- `git add -A`
- 引入构建工具（webpack/vite/esbuild 都不行）——项目明确「前端仍不引入构建工具」
- 把 `scripts/_gen.cjs`、`_migrate.py`、`_batch_resources.cjs` 当垃圾删

## 二、CC1：M1-3 收尾 —— `launcher.js` 按视图模块化（`rk7I4E`，P1）

### 现状（已核实）

- `ui/js/launcher.js` **249 KB 单文件**（2026-11-04 时间戳），全站逻辑都在里面。
- `27545a8` 已完成「脚本加载清单收敛为单一来源 + 漂移守卫」（`ui/tests/ui-script-order.test.cjs`），这是模块化的第一步，**不要推翻它，要在它之上继续**。
- CSS 侧硬编码色值已收敛到 token 并有机器门槛（`scripts/check-css-tokens.cjs`，`67626e2`）。
- 前端测试基线：**481/481**（`708073c` 后）。你的改动必须保持全绿，且新增测试要登记进 `scripts/test-inventory.txt`。

### 要做的

1. **先量再拆**：给出 `launcher.js` 的函数级清单（函数名 / 行数 / 归属视图 / 跨视图引用数），作为拆分依据。不许凭文件名猜边界。
2. 按视图拆成独立脚本（如 `ui/js/views/saves.js` 等），拆法必须服从三条：
   - 每个文件只依赖显式共享层（`api.js`、`i18n.js`、`state.js` 之类），**不许有隐式全局互相写**；
   - 加载顺序仍由那份单一清单决定，`ui-script-order.test.cjs` 必须仍然通过；
   - 拆分前后**对外行为不变**：同一交互的 DOM、class、文案、事件名、API 调用序列一字不改。
3. 每拆一个视图就跑一次前端全量测试，红了立刻回退那一个视图的拆分，**不要攒到最后一起修**。
4. 「50+ 硬编码清理表」：把 `figma.css` 风格硬编码（色值、间距、字号）逐条列出并对照 `docs/design-system.md` 给结论。注意：`design-system.md` 与 `development-guide.md` 的 token 规则**互相打架**（历史遗留），发现冲突时不要自己裁决，列进「需用户定夺」一节。
5. 冷启动逐字节比对：拆分前后各起一次界面，比对首页 DOM 快照。11-07 清理删过 `publish`/`runtime` 产物，若无法构建单文件 EXE，就用现有的 WebView 宿主或本地静态服务做 DOM 比对，并在记录里写明**你实际用的是哪种方式**——不许把「没验」写成「验过」。

### 产出

- 拆分提交（建议按视图分多个提交，单个可回滚）
- `docs/m1-3-frontend-modularization-2026-11-07.md`：函数级清单、模块边界图、逐视图回归日志（测试数/exit code）、硬编码清理表、需用户定夺项、未验证项
- 前端测试数、`ui-script-order` 守卫、`check-css-tokens`、资源闭包四项的 exit code

## 三、CC2：M2-3 收尾 —— 验证器统计口径 + 清单双向断言（`rNmpJX`，P2，等 CC1 完成）

### 现状（已核实）

- `bisect-smoke` **已**在 `.github/workflows/build.yml:113`（Windows only），所以 M2-3 的「入 CI」这一半已经存在，**不要重复加**。
- 决策文档已出：`docs/decision-gui-testing-2026-11-06.md`（在 docs 分支上）。
- 待做的两件：**九页验证器统计口径修正**（历史误计：某一页图片数分母算错，导致 PASS 造假）、**`test-inventory.txt` 双向断言计数一致**。

### 要做的

1. 复现口径错误：按 `docs/decision-gui-testing-2026-11-06.md` 与实际验证器代码（`VerifyHost`）找到分母算法，写一条**会失败的测试**固定住现状，再修。
2. 修正后同一输入下 PASS/FAIL 必须可解释：给出「修前/修后同一页面同一输入」的对照表（图片数、参与统计的页数）。
3. `scripts/test-inventory.txt` 双向断言：清单里有但没跑的文件、跑了但没登记的文件，两个方向都要能被检出；用一次人为制造的不一致验证守卫真的会红。

### 产出

`docs/m2-3-validator-accounting-2026-11-07.md`：修前/修后对照表、新增守卫测试、`test-inventory` 双向验证输出、exit code。

## 四、收尾门槛

```bash
export PATH="/c/Windows/System32:/c/Windows:$PATH"
cd "D:/Projects/_worktrees/pineglow-integration-20261107"
dotnet restore
dotnet test dev/PineglowLauncher.Tests -c Release     # 只跑不改，确认你没碰坏 C# 侧
node scripts/run-inventory-tests.cjs
node scripts/check-css-tokens.cjs
node scripts/check-ui-resource-closure.cjs
```

任一项红就修到绿，或者**回退你的那部分改动**并在记录里写明——不许留红交出去。

## 五、历史教训（这个项目上真实发生过）

- 结论写在证据前面（验证器误报 PASS、报告漏记 FAIL）是最大的返工来源。你写「已通过」时，必须能指向一次真实运行的输出。
- 中文路径下 `fs.cpSync` 会因编码另建乱码目录且**不报错**（11-06 实测：祖先目录含非 ASCII 时目标 0 文件）。涉及临时目录复制时先 `mkdirSync` 再逐文件复制。
- grep 出来的「380/380」这类数字只能来自测试输出，不要拿它当自己的结论。
