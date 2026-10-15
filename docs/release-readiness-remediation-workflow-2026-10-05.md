# 松光启动器发版前收口任务流程

日期：2026-10-05，Asia/Hong_Kong。状态：仅文档交付，当前不授权实现/发布。

依据：同级 release-readiness-review-2026-10-05.md 和原始证据。只关闭发版阻断、补必要验收，不要求全部市场路线图完成。

## 0. 角色与现场保护

SourceRoot=D:\松光启动器；HEAD=f145d14eb26b0b7459a28491e764ac8c573085d3 + 当前脏源码，开始前重新核对。

DeliveryRoot=当前 e423 工作树 docs；不得以旧工作树替代 D 盘源码。

获得后续执行授权的 MiniMax/执行 AI 实现、自检；Codex 独立验收。每包完成停在“具备独立复验条件，等待 Codex 独立验收”。

执行前记录 HEAD、完整 status、diff、精确允许文件 SHA、工具版本及清单。新 GUID 冷副本复验，真实配置/存档/游戏/凭据和历史失败只读。后端读/写/删全部隔离，portable.flag 不能代替 Windows 凭据隔离。

禁止 reset/clean/restore/checkout/stash、stage-all、批量删除、覆盖历史、提交/推送/tag/发布、恢复 Harness、向他人发送消息。只停止本轮明确拥有的进程。

## 1. R02：源码与资源闭包

允许：source-snapshot-inputs.txt；本次专用资源检查；必要构建资源门槛/测试及文档。pineglow.css/pine-forest.svg 保留当前原件，只纳入输入，不重做视觉。

步骤：

1. 冻结两资源与引用文件 SHA，核对 tracked 状态。
2. 精确追加白名单，不批量纳入 ui/ 或全部未跟踪资产。
3. 检查 HTML/CSS/JS 本地资源闭包，过滤远程/data/锚点引用。
4. 独立夹具漏资源形成 RED；完整冷输入 GREEN。
5. 副本、正式包的资源 SHA 对源一致；HTTP/MIME 和 GUI 使用最新视觉。

证据：输入清单、资源 RED/GREEN、产物清单及 SHA。停止：不修改前端业务/视觉。

## 2. R05：可信运行器

允许：verify-source-snapshot.ps1、专用进程边界测试、文档。

根因：ASCII .cmd 损坏中文 EvidenceDir；日志/测试/产物缺失没有充分门禁。

步骤：

1. GUID 中文/空格路径复现；保留 cmd 原始报错和步骤误判。
2. Unicode 安全进程参数/输出重定向，退出码立即捕获。
3. 日志不存在、零测试、命令非零、超时、产物缺失分别非零失败。
4. 英文/中文/空格路径及故障用例全部验证。
5. 保留非 RID locked-mode 与 RID 普通还原的边界描述。

停止：可信验证器可独立复验，不改写历史失败为 PASS。

## 3. R01：完整发行方式和升级模型

允许：主 csproj 发布配置；ResolveUiRoot 必要区域；必要 WebShell 资源定位；UpdateService/UpdateManifest/LocalApi update handler；更新相关 api.js/launcher.js/i18n；release.yml/包装脚本/聚焦测试及文档。

只读：账号、游戏启动参数、新闻/AI/存档/Mod 业务、安全守卫与凭据产品逻辑。

当前 RED：独立 EXE 缺 Skia 原生库崩溃，外置 UI/DLL 未交付。

架构：

- A：真正独立单文件/自解包。验证 native/content 包含、实际解包位置、UI定位及旧 EXE 更新协议。不能只加 MSBuild 属性就完成。
- B：完整目录 ZIP/安装包。同步完整包 manifest、校验、安全解包、版本目录原子切换、完整回滚及旧 EXE 客户端迁移。

优先评估兼容旧 EXE 更新协议的 A；如无法成立，形成 B 的兼容矩阵再实施。选择不授权线上操作。

步骤：

1. 原 Release 参数冷发布并保留 standalone RED。
2. 统一发行资产与更新/回滚单位，不能新服务配旧 UI。
3. 排除非必要 PDB，保留许可证与必要资源，声明系统/架构/WebView条件。
4. 正式附件在新中文/空格目录冷启动，不依赖开发树、历史文件或 PINEGLOW_UI_DIR。
5. 真实窗口、九页必要资源、live API、版本验证。
6. 旧版迁移与占用/校验失败/无权限时恢复完整可运行旧版本。

证据：RED/GREEN、最终附件 SHA/size/清单、GUI、版本、升级/回滚。停止：候选准备完成，不上传/发布。

## 4. R03：清单入口和迁移

允许：ConfigStore 更新默认源/迁移；FetchManifestAsync/清单回退；更新 API/用户提示；release.yml 清单镜像；专项测试/文档。

步骤：

1. 冻结默认 Gitee 404，区分清单失败与资产回退。
2. 稳定 HTTPS 主/备用清单，404/超时/坏 JSON 确定降级，保留用户自定义源。
3. 旧客户端固定 Gitee URL 迁移和清单协议兼容矩阵。
4. 缺 token/镜像失败不得声称固定 update.json 已更新或输出无效地址。
5. hash/size/version/RID/资产类型对应 R01 最终包。
6. 先本地 HTTP/fake 验逻辑；真实 HTTPS 另补。外部写入须发布授权。

停止：本地方案可复验；待上线动作单列。

## 5. R04：候选 SHA 与 CI/Release 互锁

允许：build.yml/release.yml、可靠正式测试清单/聚焦质量工具测试、文档；VerifyHost 测试隔离必要区域。

步骤：

1. 核对当前 21 个生产前端文件及 scripts/tests 相关工具测试。
2. 统一清单/有门槛的可靠发现，漏必需文件/零文件失败。
3. C#/Node、资源闭包、包冷启动、版本/manifest 形成必需门槛。
4. Release 依赖同 SHA 通过，tag 不能绕过。不得为验证触发正式发布。
5. actionlint 校验 Actions；本地结构通过不替代 runner。
6. VerifyHost 冒烟补凭据后端隔离或一次性用户/runner，不在真实用户会话仅凭 portable 运行。

验收：失败、零测试、少必需文件、错误 SHA 不能放行。没有提交/推送授权时远程 CI 写 NOT_RUN。

## 6. R06：最终附件真实验收

默认收窄 Windows win-x64；宣称跨平台才增加各目标正式验收。

| 流程 | 必须观察 |
| --- | --- |
| 首启 | 正式附件/新目录，九页资源、live API/版本，无开发依赖 |
| 账号 | 微软设备码/取消/超时/切换/续期、离线；无秘密泄漏 |
| Java/版本 | 实际 Java、原版下载/修复/启动/退出、承诺的加载器样例 |
| 下载 | 中断重启、终态进度、取消重试、重复任务及实际文件一致 |
| 整合包 | 已承诺格式导入，取消/失败保留或清理、重试 |
| Mod/恢复 | running只读；替换/回滚/记录/UI；junction/跨实例边界 |
| 存档 | 正确实例备份/恢复，失败不损坏原件 |
| 新闻/AI/皮肤 | 真外链、诊断本地/外发同意/取消、皮肤渲染 |
| 更新 | 旧版检查→完整资产→校验→退出→替换→重启，同版一致 |
| 故障 | 坏 hash/截断/占用/无权限/主源失败，旧版能恢复且提示真实 |

依据 release notes 承诺选择必需范围。缺账号/网络/GUI写 NOT_RUN，不能用 mock/替身/BAT 沙盒替代。

## 7. 冻结、独立验收、待发布

本轮不执行提交/推送/tag。后续获对应授权：

1. 选大于 v1.0.0 的版本，统一 tag/程序集/运行时/manifest/资产；发布说明写已知限制和迁移。
2. 精确准备候选提交，不能纳入全部历史证据/临时文件/真实数据/密钥。
3. 冷 checkout 复验同 SHA，核实远程 CI 与产物绑定。
4. 封存输入、命令、TRX、GUI、资产及 manifest SHA；空证据/漏目标/坏 detached hash 必须失败。
5. Codex 重跑最强反证：新目录完整附件、旧版升级、失败回滚。
6. 技术全部满足后交付候选与上线动作，用户明确批准后才发布。

不以自检 PASS、CI SUCCESS、测试数量或文档完成自行正式放行。
