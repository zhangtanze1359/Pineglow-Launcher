$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot '../../scripts/get-release-notes.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('pineglow-release-notes-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$good = Join-Path $fixture 'v1.0.1.md'
$blank = Join-Path $fixture 'v2.0.0.md'
try {
    $expected = "## 新增`n自动更新提醒`n修复路径 C:\games\测试 & <script>说明</script>"
    [IO.File]::WriteAllText($good, $expected, $utf8)
    [IO.File]::WriteAllText($blank, '  ', $utf8)
    $actual = & $scriptPath -Version '1.0.1' -NotesRoot $fixture
    if ($actual -cne $expected) { throw '介绍的中文/换行/特殊字符未原样保留' }
    'PASS ReleaseNotes_PreservesUnicodeNewlinesAndLiteralMarkup'
    foreach ($version in @('2.0.0', '3.0.0', '../escape')) {
        $failed = $false
        try { & $scriptPath -Version $version -NotesRoot $fixture | Out-Null } catch { $failed = $true }
        if (-not $failed) { throw "本应拒绝的版本被接受: $version" }
        "PASS ReleaseNotes_RejectsBlankMissingOrUnsafeVersion $version"
    }
    $notes = & $scriptPath -Version '1.0.1'
    if ($notes -notmatch '自动检查新版本') { throw '下一版本的真实介绍缺失' }
    'PASS ReleaseNotes_NextReleaseHasConcreteIntroduction'
} finally {
    Remove-Item -LiteralPath $good,$blank -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $fixture -ErrorAction SilentlyContinue
}
