# 独立启动取证辅助脚本记录限制

原启动命令的真实退出码为 1，不能记作整条启动命令 exit 0。六个用例分别保留了结构化结果、产品命令行和新日志，均达到本次 35 秒观察条件。

取证命令在复制配置时尝试设置不存在的 `ProxyPassword` 属性，产生非终止 `SetValueInvocationException`；原对象没有此字段。没有修改原用户配置，仅修改 GUID 隔离副本。执行继续完成六个启动用例。辅助脚本成功路径未显式 `exit 0`，外层读取 `$LASTEXITCODE`，存在沿用此前 robocopy 返回码的缺陷。因此外层 1 不可直接归为产品启动失败，也不能从六个 PASS 反推外层实际 exit 0。

原始工具输出包含：

```text
exit_code: 1
SetValueInvocationException:
Exception setting "ProxyPassword": "The property 'ProxyPassword' cannot be found on this object. Verify that the property exists and can be set."
```

原 `verify-owned-launches.ps1` 及日志保留，不覆写。另用 `verify-retained-launch-results.ps1` 验证六个结构化结果对应日志/命令行完整，使用明确退出码；该校验只验留存证据，不声称重新启动六个实例。

原脚本停止返回 PID 后，两个 Java 子进程仍在。`process-cleanup.json` 保留该状态；逐一验证其 EXE 位于本次 GUID 隔离根，再停止这两个自有子进程；`process-cleanup-final.json` 记录剩余为 0。没有停止用户原实例进程。
