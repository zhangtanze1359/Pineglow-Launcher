# actionlint 下载实测记录（R04 流程步骤 5，仍是 NOT_RUN）

日期：2026-10-06（Asia/Hong_Kong）。目的：给"actionlint 未运行"这条 NOT_RUN 一个**精确的成因证据**，
而不是笼统写"网络超时"。

## 1. 命令与结果

```
curl.exe -sSL --max-time 120 -o actionlint.zip \
  https://github.com/rhysd/actionlint/releases/download/v1.7.7/actionlint_1.7.7_windows_x86_64.zip
```

- `curl` 退出码：**0**
- 下载耗时：**2 秒**
- 落盘大小：**9 字节**
- SHA256：`0019dfc4b32d63c1392aa264aed2253c1e0c2fb09216f8e2cc269bbfb8bb49b5`
- 文件内容（十六进制）：`4E 6F 74 20 46 6F 75 6E 64` ＝ 纯文本 **`Not Found`**

DNS 观测：`github.com -> 198.18.0.74`（本机 DNS 是 fake-IP 形态，外部主机名一律落在 `198.18.0.0/15`）。

## 2. 为什么这能确定是环境而不是我的用法

- 退出码 0、2 秒返回、9 字节纯文本，**不是** GitHub 的 404 页面（那会是几十 KB 的 HTML），
  也不是"拉不下来"的超时；它是本机网络路径上一个**固定应答**。
- 资产名 `actionlint_<版本>_windows_x86_64.zip` 与 GitHub Release 的命名约定一致。
- 因此：**actionlint 在本机不可获得**，§5 步骤 5「actionlint 校验 Actions」继续保持 **NOT_RUN**，
  只能由联网环境或 CI runner 补。按现场保护约定**不再换别的方式反复尝试下载**。

## 3. 现在的替代做法及其已知盲点（不得当成等价）

只做了 PyYAML 结构校验（两份工作流可解析、job/step/needs 引用完整、无制表符缩进）。**盲点**：

- YAML 1.1 把 `on:` 解析成布尔 `True`，**触发条件（`push.tags` 等）根本没被校验**；
- 不校验表达式（`${{ }}`）里的上下文可用性、`needs` 拼写之外的语义、action 输入是否合法；
- **结构通过 ≠ actionlint 通过 ≠ runner 通过**。

## 4. 产物

- `actionlint.zip`（9 字节，原样保留，就是上面那个 `Not Found`）。
