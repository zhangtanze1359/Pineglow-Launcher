param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$NotesRoot = (Join-Path $PSScriptRoot '../release-notes')
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+(\.\d+){1,3}$') { throw "不支持的正式版本号: $Version" }
$notesPath = Join-Path $NotesRoot "v$Version.md"
if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) {
    throw "缺少新版本介绍: $notesPath。请在打标签前编写该版本的更新说明。"
}
$notes = [IO.File]::ReadAllText($notesPath, [Text.Encoding]::UTF8).Trim()
if ([string]::IsNullOrWhiteSpace($notes)) { throw "新版本介绍不能为空: $notesPath" }
$notes
