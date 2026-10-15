# Java 探测（`/api/java`）与恢复区（`/api/instances/{id}/recovery`）= **PASS**（第 71 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
这两个端点是此前**没有单独验过**的（Java 只验过"整合包导入会自动装 Java"，恢复区只验过"删 Mod 会隔离"）。

## 1. `/api/java`：JavaLocator 确实能找到并识别 JRE

把启动器的 `JAVA_HOME` 指向一个真实的 Adoptium JRE 17：

```
GET /api/java -> ok=True 发现 1 个安装，其中有效 1 个；命中 JAVA_HOME 的=1
   C:\…\pg-world\jre17\bin\java.exe  major=17  vendor=Eclipse Adoptium  valid=True
设置 javaPath 后 /api/java 回显的 javaPath=C:\…\pg-world\jre17\bin\java.exe
```

⇒ **能发现 JAVA_HOME 里的运行时、能解析主版本与厂商、能判有效**；`javaPath` 配置项正确回显 ✅

## 2. 恢复区：隔离 → 列出 → 还原，全链可复核

| 步骤 | 结果 |
| --- | --- |
| 初始 `GET …/recovery` | `{"instanceId":…,"retentionDays":30,"recoveries":[]}` ✅ |
| 把一个 Mod 移入恢复区 | `{"ok":true,"fileName":"RecoveryProbe.jar","operationId":"20261006-032652-13f79f2c","recoverable":true,"status":"quarantined","files":[{"relativePath":"mods\\RecoveryProbe.jar","state":"quarantined",…}]}`，且**文件确实从 `mods/` 消失** ✅ |
| 再列恢复区 | **1 条记录**：`{operationId, reason:"mod-delete", status:"quarantined", createdAt, expiresAt:"2026-11-05…"}` ✅（保留期 30 天）|
| `POST …/recovery/{operationId}/restore` | `{"ok":true,"status":"restored","overwrite":false,"conflicts":0,"files":[{"relativePath":"mods\\RecoveryProbe.jar","state":"restored","sha256":"40AFF2E9…"}]}`，**文件回到 `mods/`** ✅ |
| 还原后再列 | 记录仍在（`status` 变为已还原）—— 即**记录作为历史保留到保留期结束**，不是"还原后立刻消失" |

**判定：`PASS：Java 探测能发现 JAVA_HOME 里的 JRE；恢复区能列出隔离记录并成功还原文件`**（逐项 **6/6**）

> 说明：`restore` 的返回里带 **`overwrite:false` 与 `conflicts:0`** —— 与第 44 轮"还原是非破坏性导入"的结论一致（不会覆盖现存的同名文件）。

## 3. 我自己的两个坑（脚本侧）

1. 一处**括号没闭合**（`if ($a -and ($b -and ($c)) { … }`）⇒ 解析器报 `L84`，脚本没跑。
2. `[byte[]](1..256)` —— **256 超出 byte 范围**（0–255）⇒ 运行时 `InvalidCastIConvertible`。改成 `[byte[]](0..255)`。
   （都是"写测试脚本"的常见坑，与产品行为无关。）

## 4. 复现

```powershell
powershell -File ...\run-20261006-2350-java-recovery\verify-java-recovery.ps1 `
  -ExePath <候选 EXE> -JavaHome <一个真实 JRE 目录> -JsonPath <证据>\java-recovery.json -WorkRoot %TEMP%\pg-javarec3
# 期望：PASS（6/6）
```

产物：`java-recovery.json`（Java 列表原文 + 隔离/还原全链响应）、`java-recovery.log`、`verify-java-recovery.ps1`。
