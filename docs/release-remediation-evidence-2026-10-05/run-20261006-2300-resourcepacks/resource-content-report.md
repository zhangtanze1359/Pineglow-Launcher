# 资源包 / 光影 的搜索与安装 = **PASS**（第 69 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
第 62 轮只验了 `category=mod` 的内容安装；本轮把**另外两个分类**（`resourcepack` / `shader`）补上——它们装进的是**不同的目录**，容易出错。

## 1. 实测结果

| 分类 | 搜索 | 安装任务 | 落盘 | 列表接口 | state 计数 |
| --- | --- | --- | --- | --- | --- |
| **resourcepack** | `q=faithful` → **20** 条，取 `w0TnApzs`（Faithful 32x）| `{"ok":true,"category":"resourcepack","status":"queued"}` → **completed**（`安装完成`，title `Faithful 32x - 1.18.2.zip`，9 秒）| **`resourcepacks/Faithful 32x - 1.18.2.zip` 10,868,241 B** ✅ | `/api/instances/{id}/resourcepacks` → `packs:[{fileName:…,fullPath:…}]` ✅ | `resourcePacksCount=1` ✅ |
| **shader** | `q=complementary&loader=iris` → **10** 条，取 `HVnmMxH1`（Complementary Shaders - Reimagined）| `{"ok":true,"category":"shader","status":"queued"}` → **completed**（title `ComplementaryReimagined_r5.9.3.zip`，3 秒）| **`shaderpacks/ComplementaryReimagined_r5.9.3.zip` 553,397 B** ✅ | `/api/instances/{id}/shaderpacks` → `packs:[…]` ✅ | `shaderPacksCount=1` ✅ |

**判定：`PASS：资源包与光影都能搜到、能装进对应目录、任务 completed、state 计数正确`**（逐项 **7/7**）

⇒ 三个内容分类（mod / resourcepack / shader）现在**都有真机证据**：搜索命中、安装任务终态、**文件落在正确目录**、列表接口与 state 计数一致。

## 2. 我自己的三个坑（都在脚本侧，产品行为正确）

1. **`$PID` 是只读自动变量**：我用了 `$pid` 当变量名 ⇒ `Cannot overwrite variable PID because it is read-only or constant`，脚本直接中断。
   教训：**别用 `$pid/$home/$host` 这类自动变量名**。
2. 更糟的是我随后的"批量改名"：`$t.Replace('$pid = ','')` **把两处初始化赋值整行删掉**了 ⇒ 变量未初始化，`Set-StrictMode` 再次中断。
   教训：**不要对脚本做"全局字符串手术"**——按精确行替换，并且**替换后立刻回读那几行**（这次回读才发现 `''; $title = ''` 这种残骸）。
3. 轮询这次写对了：**按任务的 `status` 字段判终态**（第 62 轮踩过"等列表变空"的坑）。

## 3. 复现

```powershell
powershell -File ...\run-20261006-2300-resourcepacks\verify-resource-content.ps1 `
  -ExePath <候选 EXE> -JsonPath <证据>\resource-content.json -WorkRoot %TEMP%\pg-resp3
# 期望：PASS（7/7），JSON 里含两类的搜索命中/任务终态/落盘文件与字节数/列表响应
```

产物：`resource-content.json`（结构化结果）、`resource-content.log`（时间线）、`verify-resource-content.ps1`。
