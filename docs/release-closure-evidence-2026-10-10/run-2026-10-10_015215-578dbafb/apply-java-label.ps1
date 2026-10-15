param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-38: stop the settings page from claiming a JRE that does not exist (defect #13, part ①).
#
# ui/js/launcher.js:631 shows set.javaBundled exactly when the user has configured no Java path AND
# the product detected no Java at all. The text was "使用内置 JRE（默认）" / "Use Bundled JRE (Default)" —
# but nothing ships or installs that JRE outside the modpack-import path (round 37 measured
# /api/java -> 0 installations, a launch refused with "请先在设置中指定 Java 路径", and
# data/runtimes never being created). The label is the one part of this defect that is a plain lie,
# so it is the part worth fixing first and cheaply.
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$U = Join-Path $Root 'ui\js'
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

$i18n = Join-Path $U 'i18n.js'
Patch $i18n "'set.javaBundled': '使用内置 JRE（默认）'," "'set.javaBundled': '未检测到 Java，请指定路径'," 'P-JAVALABEL 1/2 zh label'
Patch $i18n "'set.javaBundled': 'Use Bundled JRE (Default)'," "'set.javaBundled': 'No Java detected - please set a path'," 'P-JAVALABEL 2/2 en label'

$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutDir 'javalabel-patch-report.json') -Encoding utf8
Write-Output ("steps applied = {0}" -f $report.Count)
