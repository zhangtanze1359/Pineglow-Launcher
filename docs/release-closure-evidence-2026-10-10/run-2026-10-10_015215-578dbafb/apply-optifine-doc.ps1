param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-40: stop the README from advertising OptiFine as a supported loader (defect #8).
#
# README.md lists five loaders. Four of them have install services (FabricService / ForgeService /
# NeoForgeService / QuiltService) and InstancePackService.InstallLoaderAsync dispatches exactly those
# four, with `_ => throw new InvalidOperationException($"不支持的加载器：{kind}")` as the catch-all.
# There is no OptiFineService, and the CurseForge loader parser explicitly rejects OptiFine
# (InstancePackService.cs:669). The behaviour is honest (a clear error); the README is not.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$report = New-Object System.Collections.Generic.List[object]

function Write-PreservingEncoding([string]$file, [string]$text) {
  $bytes = [IO.File]::ReadAllBytes($file)
  $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
  [IO.File]::WriteAllText($file, $text, (New-Object Text.UTF8Encoding($hasBom)))
}

function Patch([string]$file, [string]$old, [string]$new, [string]$label) {
  $text = [IO.File]::ReadAllText($file)
  $usesCrlf = $text.Contains("`r`n")
  if ($usesCrlf) { $old = $old -replace "`r?`n", "`r`n"; $new = $new -replace "`r?`n", "`r`n" }
  else { $old = $old -replace "`r?`n", "`n"; $new = $new -replace "`r?`n", "`n" }
  $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
  if ($count -ne 1) { throw "[$label] expected exactly 1 match in $file but found $count" }
  Write-PreservingEncoding $file $text.Replace($old, $new)
  $report.Add([pscustomobject]@{ label = $label; file = $file.Substring($Root.Length + 1); ok = $true })
  Write-Output "[OK] $label"
}

$readme = Join-Path $Root 'README.md'

# 1. the supported-loader list
$old1 = '- **加载器与整合包**:Forge / Fabric / NeoForge / Quilt / OptiFine,Modrinth 整合包导入安装'
$new1 = '- **加载器与整合包**:Forge / Fabric / NeoForge / Quilt,Modrinth 整合包导入安装'
Patch $readme $old1 $new1 'P-OPTIFINE 1/2 drop OptiFine from the supported-loader list'

# 2. say it out loud in 已知限制 rather than leaving a silent gap
$old2 = '已知限制：CI 会编译 Linux/macOS 产物，但 WebView 与端到端运行目前仅在 Windows 验证，**不能视为正式跨平台发布支持**。'
$new2 = '已知限制：CI 会编译 Linux/macOS 产物，但 WebView 与端到端运行目前仅在 Windows 验证，**不能视为正式跨平台发布支持**。'
$new2 += "`n`n已知限制：**OptiFine 目前只能被识别（加载器枚举与版本名解析），不支持安装**——安装分派只有 Fabric / Quilt / Forge / NeoForge 四条路径，遇到 OptiFine 会明确报错「不支持的加载器：OptiFine」。"
Patch $readme $old2 $new2 'P-OPTIFINE 2/2 state the OptiFine limitation explicitly'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'optifine-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
