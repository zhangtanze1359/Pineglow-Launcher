# 内容子系统实测：**搜索（5 类）+ 真实 Mod 安装 + 任务列表 = PASS**（第 62 轮）

日期：2026-10-06（Asia/Hong_Kong）。本轮**未改动任何 tracked 文件**。
这是本会话此前**没碰过**的一块（只验过 `/api/content/icon`）：内容搜索与"装 Mod"到底能不能用。

## 1. 五类搜索：全部返回真实数据

| 查询 | 结果数 | 首个 id |
| --- | --- | --- |
| Modrinth **mod**（`q=sodium&gameVersion=1.18.2&loader=fabric`）| **18** | `AANobbMI`（Sodium）|
| Modrinth **整合包**（`q=fabulously`）| **20** | `1KVo5zza`（Fabulously Optimized）|
| Modrinth **资源包**（`q=faithful&gameVersion=1.18.2`）| **20** | `w0TnApzs` |
| Modrinth **光影**（`q=complementary&loader=iris`）| **10** | `HVnmMxH1` |
| **CurseForge** mod（`q=jei&source=curseforge`）| **24** | `238222`（JEI 的 CurseForge id）|

## 2. 真实装一个 Mod：成功落盘

```
POST /api/content/install {"category":"mod","projectId":"AANobbMI","instanceId":"4e7853c1…","source":"modrinth"}
→ {"ok":true,"id":"ct1791249961745","category":"mod","status":"queued"}
轮询 /api/content/tasks → {"status":"completed","progress":1,"message":"安装完成","title":"Sodium 0.4.1"}
实例 mods/ 落盘 → sodium-fabric-mc1.18.2-0.4.1+build.15.jar  （1,318,645 B）
```

⇒ **搜索 → 选择 → 安装 → 任务状态 → 文件落盘** 这条链是通的（装的还是版本匹配 1.18.2 的那个文件）。

## 3. 一个我留作"开放问题"的观察（不写成结论）

`source=curseforge` 的搜索**在便携目录里没有 `secrets.json`、也就是没有 CurseForge Key 的情况下仍然返回了 24 条真实数据**；
而我用 PowerShell **直连** `https://api.curseforge.com/v1/mods/search…`（不带 key）得到的是 **403**。
⇒ 二者不一致。可能的解释（**本轮未定**）：本机 fake-IP/透明代理对**直连**请求与**经系统代理**请求的应答不同；
或产品在无 Key 时走了别的途径。**我没有下结论**——要定因需要抓启动器实际请求（第 52 轮的记录型代理只能看到主机名，
`api.curseforge.com` 会显示出来，但路径与头部看不到）。
**对验收的影响**：本轮只声明"在本机上，产品的内容搜索/安装确实работает（返回真实数据、真实落盘）"，不声明"无 Key 也能用"。

## 4. 我自己的一个坑（第 8 次）

脚本第一版用 `$obj.PSObject.Properties.Name -contains 'projectId'` 取 id —— **`-contains` 是大小写敏感的**，
而接口序列化成了 `ProjectId` ⇒ 取不到 id ⇒ 安装那一步被跳过、被判 FAIL（**不是产品问题**）。
修法：按 `projectId/ProjectId/id/Id/slug/Slug` 逐个**大小写敏感地**试，或统一用 `-ieq`。
另外我的"等任务完成"写成了"等任务列表变空"——而**已完成的任务仍留在列表里**，于是白等了 150 秒（产品行为正确）。

## 5. 复现

```powershell
powershell -File ...\run-20261006-1930-content-subsystem\verify-content-subsystem.ps1 `
  -ExePath <候选 EXE> -JsonPath <证据>\content-subsystem.json -WorkRoot %TEMP%\pg-content2
```

产物：`content-subsystem.json`（五类搜索的原始响应片段 + 安装结果 + 逐项判定）、`content-subsystem.log`（逐步时间线）。
