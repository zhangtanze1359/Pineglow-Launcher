param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$EvidenceRoot = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $EvidenceRoot)) { throw "找不到证据树：$EvidenceRoot" }

<#
.SYNOPSIS
修复 manifest.json 的 files[] 与实际文件不一致（第 57 轮）。

做什么：
  1. 删掉自指条目（manifest 记录自己的哈希 —— 永远不可能满足）；
  2. 路径搬家：若 `changed-files.sha256.txt` 已移到 `green/` 下，把记录改成 `green/...`；
  3. **重算**每个已存在条目的 sha256（manifest 写完之后文件又被改过 → 哈希过期）；
  4. 回写 manifest，并把 `\uXXXX` 还原成可读字符（ConvertTo-Json 会把中文转义）。

只碰 manifest.json，不动任何证据文件本身。
#>
$fixed = 0
foreach ($pkg in (Get-ChildItem -LiteralPath $EvidenceRoot -Directory)) {
    foreach ($run in (Get-ChildItem -LiteralPath $pkg.FullName -Directory -ErrorAction SilentlyContinue)) {
        $manifestPath = Join-Path $run.FullName 'manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath)) { continue }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        # 没有 files 数组也要处理（否则新包永远补不上清单）
        if (-not $manifest.PSObject.Properties['files']) { $manifest | Add-Member -NotePropertyName files -NotePropertyValue @() -Force }

        $changed = $false
        $newFiles = @()
        foreach ($f in $manifest.files) {
            $rel = $f.path -replace '/', '\'

            # 1) 自指条目直接丢弃
            if ($rel -eq 'manifest.json') { $changed = $true; continue }

            # 2) 路径搬家
            if ($rel -eq 'changed-files.sha256.txt' -and (Test-Path -LiteralPath (Join-Path $run.FullName 'green\changed-files.sha256.txt'))) {
                $rel = 'green\changed-files.sha256.txt'
                $changed = $true
            }

            $full = Join-Path $run.FullName $rel
            if (-not (Test-Path -LiteralPath $full)) {
                Write-Host ("  [跳过] {0} :: {1} 不存在（无法修复，留给审计报告）" -f $run.Name, $rel) -ForegroundColor Yellow
                continue
            }

            # 3) 重算哈希
            $actual = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
            if ($actual -ne $f.sha256.ToUpper()) { $changed = $true }

            $newFiles += [pscustomobject]@{
                path      = $rel -replace '\\', '/'
                sha256    = $actual
                sizeBytes = (Get-Item -LiteralPath $full).Length
            }
        }

        # 4) 目录里有文件没登记 → 补上
        $listed = @{}
        foreach ($n in $newFiles) { $listed[($n.path -replace '/', '\')] = $true }
        foreach ($file in (Get-ChildItem -LiteralPath $run.FullName -Recurse -File)) {
            $rel = $file.FullName.Substring($run.FullName.Length + 1)
            if ($rel -eq 'manifest.json') { continue }
            if (-not $listed.ContainsKey($rel)) {
                $newFiles += [pscustomobject]@{
                    path      = $rel -replace '\\', '/'
                    sha256    = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                    sizeBytes = $file.Length
                }
                $changed = $true
                Write-Host ("  [补登] {0} :: {1}" -f $run.Name, $rel) -ForegroundColor Cyan
            }
        }

        if (-not $changed) { continue }

        $manifest.files = $newFiles
        $json = $manifest | ConvertTo-Json -Depth 20
        # 把 \uXXXX 还原成字符，保持证据可读
        $json = [regex]::Replace($json, '\\u([0-9a-fA-F]{4})', { param($m) [char][int]('0x' + $m.Groups[1].Value) })
        [IO.File]::WriteAllText($manifestPath, $json, (New-Object Text.UTF8Encoding($false)))
        $fixed++
        Write-Host ("  [已修] {0}" -f $run.FullName.Substring($EvidenceRoot.Length + 1)) -ForegroundColor Green
    }
}

Write-Host ''
Write-Host ("  共修复 {0} 个 manifest" -f $fixed) -ForegroundColor Cyan
