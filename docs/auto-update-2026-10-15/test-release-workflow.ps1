$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$fixture = Join-Path $PSScriptRoot ('workflow-fixture-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'dist'),(Join-Path $fixture 'scripts'),(Join-Path $fixture 'release-notes') -Force | Out-Null
$before = Get-Location
try {
    Copy-Item -LiteralPath (Join-Path $repo 'scripts/get-release-notes.ps1') -Destination (Join-Path $fixture 'scripts/get-release-notes.ps1')
    Copy-Item -LiteralPath (Join-Path $repo 'release-notes/v1.0.1.md') -Destination (Join-Path $fixture 'release-notes/v1.0.1.md')
    [IO.File]::WriteAllBytes((Join-Path $fixture 'dist/synthetic.exe'), [byte[]](1,2,3,4))
    $yaml = [IO.File]::ReadAllText((Join-Path $repo '.github/workflows/release.yml'))
    $generate = [regex]::Match($yaml, '(?ms)^      - name: Rename exe \+ generate manifest.*?        run: \|\r?\n(?<body>.*?)(?=^      #)').Groups['body'].Value
    if (-not $generate) { throw '无法定位真实工作流的清单生成块' }
    $generate = $generate.Replace('${{ steps.version.outputs.version }}', '1.0.1').Replace('${{ github.ref_name }}', 'v1.0.1').Replace('${{ github.repository }}', 'zhangtanze1359/Pineglow-Launcher')
    Set-Location -LiteralPath $fixture
    & ([scriptblock]::Create($generate))
    $original = Get-Content -LiteralPath dist/manifest.json -Raw | ConvertFrom-Json
    $body = [IO.File]::ReadAllText((Join-Path $fixture 'dist/release-notes.md'))
    if ($body -cne $original.notes -or $body -notmatch '自动检查新版本') { throw 'GitHub 正文与清单介绍不一致' }
    'PASS Workflow_GeneratedManifestAndReleaseBodyUseSameIntroduction'
    $mirror = [regex]::Match($yaml, '(?ms)^          # 3\. 重写 manifest.*?\r?\n(?<body>.*?)(?=^          # 4\.)').Groups['body'].Value
    if (-not $mirror) { throw '无法定位真实工作流的镜像改写块' }
    $ver = '1.0.1'
    $giteeUrl = 'https://example.invalid/Pineglow-Launcher-win-x64-single-file.exe'
    $ghUrl = $original.url
    $exePath = Join-Path $fixture 'dist/Pineglow-Launcher-win-x64-single-file.exe'
    $status = @{}
    & ([scriptblock]::Create($mirror))
    $mirrored = Get-Content -LiteralPath dist/manifest.json -Raw | ConvertFrom-Json
    if ($mirrored.notes -cne $original.notes) { throw 'Gitee 镜像改写丢失版本介绍' }
    if ($mirrored.fallbackUrl -ne $original.url) { throw 'Gitee 镜像丢失 GitHub 备用资产' }
    'PASS Workflow_MirrorPreservesIntroductionAndGitHubFallback'
} finally {
    Set-Location -LiteralPath $before
    # Only exact files and empty directories created by this fixture are removed.
    foreach ($rel in @('dist/synthetic.exe','dist/Pineglow-Launcher-win-x64-single-file.exe','dist/manifest.json','dist/release-notes.md','scripts/get-release-notes.ps1','release-notes/v1.0.1.md')) {
        Remove-Item -LiteralPath (Join-Path $fixture $rel) -ErrorAction SilentlyContinue
    }
    foreach ($rel in @('dist','scripts','release-notes')) { Remove-Item -LiteralPath (Join-Path $fixture $rel) -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $fixture -ErrorAction SilentlyContinue
}
