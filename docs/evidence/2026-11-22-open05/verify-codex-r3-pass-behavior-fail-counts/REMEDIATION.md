# OPEN-05 第 3 轮最小整改流程 · 2026-11-22

独立复验已消除两轮功能阻断。失败项集中在执行证据 README 的实物清单，修正该表即可提交再次核查。`FormalPhaseApproval=NOT_GRANTED`。

1. 以 `fe49b34` 为比较基准，先保存 `git status --short --untracked-files=all` 与执行证据目录文件清单。核查依据见[原始计数](verified-run/evidence-checks.json)。
2. 在获得整改授权后编辑 `docs/evidence/2026-11-22-open05/README.md` 的产物清单。r4 stdout/stderr 改为 82 个，合计 41 对。反证保留 24 个，说明为父版 10 对、修复版 2 对，各有 stdout 和 stderr 文件，部分 stderr 为空。第 2 轮归档改为 93 个。保留 `raw/` 总数 166、41 个元数据、4 个探针和 15 个 r3 路径用例输出。可在同一表注明两支已检出脚本采用 CRLF 哈希，夹具及 r2 补丁的记录采用 Git LF blob 哈希。
3. 以独立枚举核实 README 计数，核查 `git diff --check`、README 差异与两支脚本、`raw/`、既有归档的 Git blob。记录命令、目录、时间、退出码、输入版本和证据路径。
4. 将修订及点验记录交独立复验人复核。三棵树的卷根与父版本反证可从[第 3 轮交接](../../handoff/HANDOFF-2026-11-22-codex-open05-review3.md)追溯。目标提交、销项、合入和发布审批仍按原流程处理。
