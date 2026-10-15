# 新阻断的限定修复与复验流程

日期：2026-10-07。产品根 D:\松光启动器。此文件是诊断后的方案；本轮未实施。

## 范围和基线

只处理两项新根因：LauncherService 启动参数/占位符/类路径装配；LocalApiService Java自动选择未读取版本元数据。当前 HEAD f145d14eb26b0b7459a28491e764ac8c573085d3，47 modified，0 staged。保护昨天五项修复、所有历史证据与其他未提交工作。

拟允许生产路径仅为：
- dev/PineglowLauncher/Pineglow.Launcher.Services/LauncherService.cs
- dev/PineglowLauncher/Pineglow.Launcher.Services/LocalApiService.cs

可增加上述行为的聚焦测试到 dev/PineglowLauncher.Tests，并将新文件精确加入 scripts/source-snapshot-inputs.txt。若发现必须改第三个生产文件，应先记录实际原因与范围；不要顺带重构。docs 只增加日期化交付。禁止改用户配置/凭据、历史证据、缓存goal；禁止 reset/restore/checkout/clean/stash、批量暂存、commit/push/tag/发布。

## 执行顺序

1. 再核对当前目标与脏状态，若另一个执行者正在写源文件，避免并发；记录允许文件SHA256。禁止以缓存更新时间当作实际执行时间。

   本次最终回读current goal已为空、lastTurn85且无open turn；继续前需通过正常会话入口重新发起限定目标，不能假设旧目标仍在运行，不直接编辑缓存。
2. 审查两份现成补丁；git apply --check 已通过，仅说明文本适用。不能直接认定补丁正确：RepeatableJvmOptions 不是所有JVM选项的可靠语法定义；不能在 Java 主版本未知时静默认为兼容；自动安装的新增网络动作须确认取消、错误传播及现有产品约定。
3. 先加入隔离回归并保存真实 RED：重复 --add-opens/--add-exports 的token和值完整保留；中文/空格目录占位符完全替换；现代Forge/NeoForge有自供client时避免模块冲突；Vanilla/Fabric/Quilt仍保留所需jar；Java元数据直接声明/父级继承、仅17可用但要求21、未知版本、手工指定运行时边界。
4. 最小修复：避免对语义token任意去重；补齐两个占位符；以实际profile明确判定client装配。启动时传入实例目录使用已有元数据匹配；缺少兼容Java时给予可读结果，按现有约定决定下载，不能无条件吞掉未知版本或网络失败。
5. 聚焦 GREEN 后创建新的 GUID 完整候选快照，运行 scripts/verify-source-snapshot.ps1；核对真实执行数/skip/退出码/日志，重新记录新EXE SHA256。旧305/917/19只是旧输入的证据，不应固定为修复后的测试计数。
6. 真实产品启动验证：Forge1.20.1选择17；NeoForge1.21.1选择21；Vanilla/Fabric/Quilt回归；补充1.21+ Fabric/Quilt使用元数据选择21。至少核对产品选Java、参数token、0占位符残留、Java存活、LWJGL/图集/无崩溃；进入世界和长时游玩分别留证，不能只由渲染标记推出。
7. 输出来源/执行/证据根与哈希、RED/GREEN、真实游戏边界、剩余任务；终态为“具备独立复验条件，等待 Codex 独立验收”。

## 不依赖本轮修复的外部条件

微软真人登录/权益/续期等需要账号；AI调用需要第三方密钥；Gitee固定源需要token；正式提交、推送、tag、Release/Gitee写入需要对应授权。RID锁定还原NU1004需单独补证。不要把这些条件合并为两项本地修复的先决条件，也不要把本地通过变成发布批准。
