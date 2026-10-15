#Requires -Version 5.1
<#
.SYNOPSIS
    MR01：只读采集功能事实矩阵所需的客观计数与清单。

.DESCRIPTION
    只读。不修改任何文件。输出供 MR01 文档使用的事实基线：
      - 服务类清单与公开方法
      - LocalApiService 注册的 API 路由与 handler
      - ui/js 文件与 i18n 键
      - C# 测试方法（按类）与 Node 测试文件
    所有数字均为本轮实际采集，不引用历史报告数字。
#>
[CmdletBinding()]
param(
    [string] $SourceRoot = 'D:\松光启动器'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$svcDir  = Join-Path $SourceRoot 'dev\PineglowLauncher\Pineglow.Launcher.Services'
$testDir = Join-Path $SourceRoot 'dev\PineglowLauncher.Tests'
$uiDir   = Join-Path $SourceRoot 'ui'

Write-Host ''
Write-Host '########## 1. 服务类清单 ##########' -ForegroundColor Cyan
$svcFiles = Get-ChildItem $svcDir -Filter *.cs | Sort-Object Name
foreach ($f in $svcFiles) {
    $cls = Select-String -Path $f.FullName -Pattern '^\s*(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)' |
           ForEach-Object { $_.Matches[0].Groups[1].Value } | Select-Object -First 1
    $methods = @(Select-String -Path $f.FullName -Pattern '^\s*public\s+(?:static\s+|async\s+|virtual\s+)*[\w<>\[\],\?\. ]+\s+(\w+)\s*\(').Count
    Write-Host ("  {0,-42} class={1,-34} publicMethods={2}" -f $f.Name, $cls, $methods)
}
Write-Host "  服务文件总数: $($svcFiles.Count)"

Write-Host ''
Write-Host '########## 2. API 路由（LocalApiService 注册处）##########' -ForegroundColor Cyan
$api = Join-Path $svcDir 'LocalApiService.cs'
$routes = Select-String -Path $api -Pattern '/api/[A-Za-z0-9_{}/\-.]*' -AllMatches |
          ForEach-Object { $_.Matches.Value } | Sort-Object -Unique
Write-Host "  唯一 /api 路径片段数: $($routes.Count)"
$handlers = Select-String -Path $api -Pattern 'private\s+static\s+async\s+Task\s+(Handle\w+Async)' -AllMatches |
            ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique
Write-Host "  handler 方法数: $($handlers.Count)"
$handlers | ForEach-Object { Write-Host "    $_" }

Write-Host ''
Write-Host '########## 3. UI 与 i18n ##########' -ForegroundColor Cyan
Get-ChildItem $uiDir -Recurse -File -Include *.js,*.html,*.css |
    Where-Object { $_.FullName -notmatch '\\vendor\\' } |
    Sort-Object FullName |
    ForEach-Object { Write-Host ("  {0,-46} {1,8} bytes" -f $_.FullName.Substring($SourceRoot.Length + 1), $_.Length) }
$i18n = Join-Path $uiDir 'js\i18n.js'
if (Test-Path $i18n) {
    $i18nText = [System.IO.File]::ReadAllText($i18n)
    $zh = ([regex]::Matches($i18nText, '"[a-zA-Z0-9_]+"\s*:')).Count
    Write-Host "  i18n.js 中形如 key:value 的条目数(近似): $zh"
    $langs = Select-String -Path $i18n -Pattern '^\s*(zh|en|ja|ru)\w*\s*:\s*\{' | ForEach-Object { $_.Line.Trim() }
    Write-Host "  语言区块: $($langs -join ' | ')"
}

Write-Host ''
Write-Host '########## 4. C# 测试（按文件：Fact/Theory 数）##########' -ForegroundColor Cyan
$totalFact = 0; $totalTheory = 0
foreach ($f in (Get-ChildItem $testDir -Filter *.cs | Sort-Object Name)) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    $fact  = ([regex]::Matches($t, '\[Fact\]')).Count
    $theo  = ([regex]::Matches($t, '\[Theory\]')).Count
    $totalFact += $fact; $totalTheory += $theo
    Write-Host ("  {0,-46} [Fact]={1,-4} [Theory]={2}" -f $f.Name, $fact, $theo)
}
Write-Host "  C# 测试属性合计: [Fact]=$totalFact  [Theory]=$totalTheory  合计=$($totalFact+$totalTheory)"

Write-Host ''
Write-Host '########## 5. 正式 Node 测试文件 ##########' -ForegroundColor Cyan
$nodeTests = Get-ChildItem (Join-Path $uiDir 'tests') -Filter *.test.cjs | Sort-Object Name
$nodeTests | ForEach-Object { Write-Host ("  {0,-42} {1,7} bytes  mtime={2}" -f $_.Name, $_.Length, $_.LastWriteTime.ToString('yyyy-MM-dd')) }
Write-Host "  正式 .test.cjs 数量: $($nodeTests.Count)"

Write-Host ''
Write-Host '########## 6. 关键缺口指针（供 MR01 引用）##########' -ForegroundColor Cyan
$gapPatterns = @(
    @{ n = 'mrpack 导入';      p = 'ImportModrinthPackAsync|DetectModpackFormat' },
    @{ n = '实例移除';          p = 'HandleDeleteInstanceAsync' },
    @{ n = 'Mod 删除';         p = 'void DeleteMod' },
    @{ n = 'Bisect 禁用/恢复'; p = 'RestoreBisectMods|DisableMod\(' },
    @{ n = 'UpdateService';    p = 'class UpdateService|ApplyUpdate|DownloadUpdateAsync' },
    @{ n = '加载器枚举';       p = 'enum ModLoaderKind' }
)
foreach ($g in $gapPatterns) {
    $hits = @(Select-String -Path (Join-Path $svcDir '*.cs') -Pattern $g.p)
    $locs = $hits | Select-Object -First 4 | ForEach-Object { "$([System.IO.Path]::GetFileName($_.Path)):$($_.LineNumber)" }
    Write-Host ("  {0,-20} 命中 {1,3} 处  e.g. {2}" -f $g.n, $hits.Count, ($locs -join ', '))
}
