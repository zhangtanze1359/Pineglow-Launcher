# 发布阻断闭环流程

日期：2026-10-16，Asia/Hong_Kong。该文档为本轮检查的交接材料，不自行授权产品修复、提交或发布。

## 基线和目标

SourceRoot 为 `D:/松光启动器`，HEAD `1356efa70cfd91f6289ee6e0dc6af5160b444ab3`；保留当前 FA-01 至 FA-05 未提交修复及全部审计材料。先核对 `docs/release-preparation-2026-10-16/baseline-status.txt` 与 `input-manifest.json`，漂移需明确记录。

目标：根因驱动修复实际发行候选的 HttpListener 启动失败；正常启动真实 API/UI；失败时验证脚本能生成可信 FAIL 证据；新版本介绍包含已完成的修复。最强 RED 是本轮同一个自包含 EXE 在两个全新中文路径、不同端口下失败，详见本轮 README 和两份 launcher 异常日志。不是仅凭旧开发运行时的历史失败。

## 顺序

1. 保存当前 HEAD、dirty、运行时与产品输入，确认没有遗留本轮进程。不要关闭未知启动器、WebView2 或用户浏览器。所有验证使用 portable.flag、新数据目录、独立端口和本轮临时目录。
2. 先调查 RP-01：比较最小 HttpListener 程序、加载产品程序集的探针、真实入口三层；记录 OS、架构、实际 coreclr/依赖模块、异常完整 NativeErrorCode。以 .NET 10.0.9 发行包为主证；10.0.0 仅作区分假设的对照，不能直接成为正式修复。
3. 检查 NativeLibrary/DllImport/HttpListener 初始化相关调用及依赖加载顺序，定位哪个差异改变行为。每次诊断只变一个变量，保留成功与失败原始证据。不要根据“句柄无效”直接改端口、系统 URL ACL、防火墙、TLS 或全局运行时。
4. 有证据后选择最小产品修复。若必须替换本地 HTTP 服务器、调整依赖或版本策略，先形成具体设计和兼容影响，再取得对应范围授权；不要借此重写整个 LocalApiService。
5. RP-02 可独立修复：在条件分支外初始化服务资源集合，保证启动失败仍落盘 JSON 并 exit 1；覆盖健康失败和健康成功两条路径，不能跳过健康门槛来变绿。本轮隔离副本只是诊断工具，正式脚本仍待修改。
6. RP-01 修复后按正式参数重新生成单文件 EXE。只将本次 EXE 放到另一全新中文/空格目录，清除开发环境旋钮，检查真实 API 健康、token、关键资源 MIME/字节与候选源码哈希、正常页操作和退出/重启。确认不依赖外置 UI/DLL、旧 data/UI 或开发树。
7. C# 回归先锁定 HttpListener 相关有效失败并使用有界挂起诊断，再完成全量测试。此前的部分通过/中止记录不能代替全量通过。Node 重新按清单执行，本轮基线 379/379、28 个文件；数量变化需解释。校验 UI 闭包和测试清单。
8. 将本轮 `v1.0.1-notes-draft.md` 经核对同步至正式版本说明。确认本轮选定版本，EXE/程序集/manifest/Release body 使用同一个版本与介绍；验证 SHA256/size 与实际最终附件一致。不能将当前失败 EXE 上传，也不能复用它的 SHA 作为修复后产物 SHA。
9. 独立复验通过后才处理用户的条件性提交：精确审查和暂存路径，比较 expected 与 staged；不 stage-all。记录提交前后输入一致性。用户此次没有授权推送、打 tag 或上传 Release，停在本地提交与可复验候选，不自动触发远程发布。

## 修改范围和证据

优先只读：`dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs`、`Pineglow.Launcher/App.cs`、`Pineglow.Launcher/Program.cs`、WebShellWindow、csproj、lock、工作流、系统与实际模块信息。获得修复授权后仅改已证明必要的启动/兼容路径及对应测试、冷启动验证脚本和版本说明。FA 修复、真实配置、账户/游戏文件、皮肤实现与其他功能不顺带改动。

若需修改 `.github/workflows/`，先使用 authoring-github-workflows 技能，并执行 actionlint；本轮并未修改工作流。普通 RID publish 与非 RID locked restore 必须分别记录，不能宣称 RID 锁定门槛通过。

新 EvidenceRoot 使用日期和运行编号；保存 RED、诊断对照、最小补丁、GREEN、所有命令真实退出码/测试数/skip/挂起列表、原始日志、最终源码和产物哈希。禁止覆盖旧证据或把 fixture/mock 提升为真实 WebView、账户、游戏或升级证明。

## 停止条件

各必需门槛完成后交付“具备独立复验条件，等待 Codex 独立验收”；遇到尚未解决的启动失败或关键证据缺口，明确暂不放行。技术通过、提交与正式发布分别记录；未获发布授权时 `FormalPhaseApproval=NOT_GRANTED`。材料完成不等于产品修复已获授权。
