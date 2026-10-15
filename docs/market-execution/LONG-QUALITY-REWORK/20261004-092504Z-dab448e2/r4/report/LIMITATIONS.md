# 未验证项与边界（LIMITATIONS）

本包**只**证明 Windows 本机离线范围内的质量门槛。以下内容**没有**被验证，也不被本包声称：

1. **真实 Avalonia / WebView2 GUI**：所有前端行为断言都在受控 DOM 桩 + vm 中调用真实生产入口；
   受控 DOM 不等于真实 WebView。
2. **真实 Minecraft 与真实整合包导入**：导入终态测试替代真实 API 响应，没有联网导入真实整合包。
3. **原生凭据后端**：没有验证真实 Windows 凭据管理器链路。
4. **真实更新替换与进程交接**：没有执行真实的自更新替换。
5. **跨平台**：只在本机 Windows 上运行。
6. **干净提交 / 远程 CI / 发布**：本轮不提交、不推送、不发布，也没有在干净检出上跑 CI。
7. **C# 覆盖面**：执行的是明确筛选集合 13 + 5 + 6 + 49 = **73/73**；
   `ImportWithALiveTokenButCancelledMidway` 继续排除，**不声称全量 C# 通过**。
8. **RA-01 / RA-02**：受限恢复安全反证仍按既有平台边界排除，未在本轮执行。
9. **历史证据**：真实 3 根覆盖审计报告 `knownDrift = 3`，这是旧封存包已显式登记的历史差异；
   不得被描述为“历史证据恢复无缺陷”。
10. **本轮之前的 dirty 状态**：`D:\松光启动器` 在本轮开始前就是 dirty；只有
    `verdict/changed-files.json` 中列出的 6 个文件是本轮改动，其余不归本轮。

## 审批状态

- `FormalPhaseApproval = NOT_GRANTED`
- `ReleaseApproval = NOT_GRANTED`
- `technicalAcceptance = AWAITING_INDEPENDENT_REVIEW`

## 停止点

不提交、不推送、不发布、不扩张功能、不自动派发下一目标。
