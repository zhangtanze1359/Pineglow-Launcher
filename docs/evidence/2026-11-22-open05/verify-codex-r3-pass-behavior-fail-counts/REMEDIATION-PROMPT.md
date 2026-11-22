# OPEN-05 第 3 轮整改提示词 · 2026-11-22

你是 OPEN-05 的证据文档整改执行者。先读 `AGENTS.md` 与 `docs/handoff/HANDOFF-2026-11-22-codex-open05-review3.md`，以 `fe49b34` 为比较基准，记录 HEAD、工作目录和现场状态。

本次授权范围是更正 `docs/evidence/2026-11-22-open05/README.md` 的产物清单并提供核查证据。点验 `raw/` 的 166 个文件，应为 41 元数据、82 stdout/stderr、24 反证输出、4 历史探针、15 个 r3 路径用例文件。反证包括父版 10 对、修复版 2 对，每组各有 stdout 和 stderr 文件，部分 stderr 为空。第 2 轮复验归档实测 93 个文件。两支脚本的已记录哈希采用 Windows CRLF 检出字节，夹具和 r2 补丁的记录采用 Git LF blob 字节。使用实测数字，保留全部历史输出。

完成条件为 README 清单与重新枚举一致，`git diff --check` 通过，现有两支审计脚本、原始输出和归档文件的字节身份与 `fe49b34` 一致。提交命令、cwd、时间/时区、退出码及核查证据供独立复验。提交、推送、打 tag、发 Release 及阶段批准均需另有授权，记录 `FormalPhaseApproval=NOT_GRANTED`。
