#Requires -Version 5.1
<#
.SYNOPSIS
    为"进入世界/加载存档"验收**离线准备一个真实存档**（第 56 轮）：
    下载 Adoptium JRE 17 + Mojang 官方 1.20.1 server.jar → 跑一次服务器生成世界 → 停服 → 校验世界文件。
.DESCRIPTION
    1.20.1 客户端支持 `--quickPlaySingleplayer <世界名>`，配合产品的 `CustomGameArgs` 就能**不用 GUI** 直接进世界。
    世界用一个官方服务端生成（flat 地图，几秒完成）。所有产物与 sha1 都落盘。
#>
param(
    [Parameter(Mandatory)][string] $WorkRoot,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $LevelName = 'QuickWorld',
    [int] $ServerTimeoutSec = 240
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
if (-not (Test-Path $outDir)) { [void](New-Item -ItemType Directory -Path $outDir -Force) }
$logPath = Join-Path $outDir 'worldgen-step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }

$root = (Resolve-Path -LiteralPath $WorkRoot).Path
$jreDir = Join-Path $root 'jre17'
$srvDir = Join-Path $root 'server1201'
[void](New-Item -ItemType Directory -Path $srvDir -Force)

# ---- 1) Adoptium JRE 17
$javaExe = Join-Path $jreDir 'bin\java.exe'
if (-not (Test-Path $javaExe)) {
    $zip = Join-Path $root 'jre17.zip'
    if (-not (Test-Path $zip)) {
        $u = 'https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jre/hotspot/normal/eclipse?project=jdk'
        Log "下载 Adoptium JRE 17 ..."
        Invoke-WebRequest $u -OutFile $zip -UseBasicParsing -TimeoutSec 600
    }
    Log ("JRE zip=" + (Get-Item $zip).Length + " B，解压 ...")
    [void](New-Item -ItemType Directory -Path $jreDir -Force)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $jreDir)
    # zip 里多一层目录
    if (-not (Test-Path $javaExe)) {
        $inner = Get-ChildItem $jreDir -Directory | Select-Object -First 1
        if ($inner) { Get-ChildItem $inner.FullName -Force | ForEach-Object { Move-Item $_.FullName $jreDir -Force } ; Remove-Item $inner.FullName -Recurse -Force -EA SilentlyContinue }
    }
}
if (-not (Test-Path $javaExe)) { Log '❌ 没拿到 java.exe'; exit 2 }
$oldEA = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
$ver = (& $javaExe -version 2>&1 | Select-Object -First 1)
$ErrorActionPreference = $oldEA
Log ("java: " + $ver)

# ---- 2) 官方 1.20.1 server.jar（校验 sha1）
$expected = '84194a2f286ef7c14ed7ce0090dba59902951553'
$serverJar = Join-Path $srvDir 'server.jar'
if (-not (Test-Path $serverJar)) {
    Log '下载 1.20.1 server.jar ...'
    Invoke-WebRequest "https://piston-data.mojang.com/v1/objects/$expected/server.jar" -OutFile $serverJar -UseBasicParsing -TimeoutSec 900
}
$sha = (Get-FileHash -LiteralPath $serverJar -Algorithm SHA1).Hash.ToLower()
Log ("server.jar=" + (Get-Item $serverJar).Length + " B sha1=$sha 期望=$expected 一致=" + ($sha -eq $expected))

# ---- 3) 配置并跑一次服务器生成世界
[IO.File]::WriteAllText((Join-Path $srvDir 'eula.txt'), "eula=true`n", ([System.Text.ASCIIEncoding]::new()))
$props = @(
    'level-name=' + $LevelName
    'level-type=flat'
    'online-mode=false'
    'max-players=1'
    'spawn-protection=0'
    'view-distance=4'
    'simulation-distance=4'
    'difficulty=peaceful'
    'gamemode=creative'
    'server-port=25599'
    'motd=Pineglow worldgen'
    'enable-query=false'
    'enable-rcon=false'
)
[IO.File]::WriteAllText((Join-Path $srvDir 'server.properties'), (($props -join "`n") + "`n"), ([System.Text.ASCIIEncoding]::new()))
Log '启动服务器（生成世界）...'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $javaExe
$psi.Arguments = '-Xmx1024M -Xms512M -jar server.jar nogui'
$psi.WorkingDirectory = $srvDir
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true
$p = [System.Diagnostics.Process]::Start($psi)
$sb = New-Object System.Text.StringBuilder
$done = $false
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $ServerTimeoutSec) {
    Start-Sleep -Seconds 2
    while (-not $p.StandardOutput.EndOfStream) {
        $line = $p.StandardOutput.ReadLine()
        if ($null -ne $line) {
            [void]$sb.AppendLine($line)
            if ($line -match 'Done \(') { $done = $true }
        }
        if ($p.StandardOutput.EndOfStream) { break }
    }
    if ($done) { break }
    if ($p.HasExited) { break }
}
Log ("服务器 Done=" + $done + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s")
if ($done) {
    try { $p.StandardInput.WriteLine('stop'); $p.StandardInput.Flush() } catch { }
    $p.WaitForExit(60000) | Out-Null
} else {
    try { $p.Kill() } catch { }
}
$err = ''; try { $err = $p.StandardError.ReadToEnd() } catch { }
[IO.File]::WriteAllText((Join-Path $outDir 'server-output.log'), $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText((Join-Path $outDir 'server-stderr.log'), $err, (New-Object Text.UTF8Encoding($false)))

# ---- 4) 校验世界文件
$worldDir = Join-Path $srvDir $LevelName
$levelDat = Join-Path $worldDir 'level.dat'
$region = Join-Path $worldDir 'region'
$mca = @(); if (Test-Path $region) { $mca = @(Get-ChildItem $region -File -Filter '*.mca' -EA SilentlyContinue) }
$ok = (Test-Path $levelDat) -and ($mca.Count -gt 0)
Log ("世界目录=" + $worldDir + " level.dat=" + (Test-Path $levelDat) + " region/*.mca=" + $mca.Count + " 判定=" + $(if ($ok) { 'PASS' } else { 'FAIL' }))
if (-not $ok) { Log '服务器输出尾部：'; @($sb.ToString() -split "`n" | Select-Object -Last 12) | ForEach-Object { Log ('  ' + $_) } }

$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    javaExe = $javaExe
    javaVersion = ($ver | Out-String).Trim()
    serverJar = [ordered]@{ path = $serverJar; bytes = (Get-Item $serverJar).Length; sha1 = $sha; expectedSha1 = $expected; match = ($sha -eq $expected) }
    serverDone = $done
    world = [ordered]@{ dir = $worldDir; levelDat = (Test-Path $levelDat); regionFiles = $mca.Count; sizeBytes = $(if (Test-Path $worldDir) { [int64]((Get-ChildItem $worldDir -Recurse -File -EA SilentlyContinue | Measure-Object -Property Length -Sum).Sum) } else { 0 }) }
    verdict = $(if ($ok) { 'PASS：已生成可用的 1.20.1 存档' } else { 'FAIL' })
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
exit $(if ($ok) { 0 } else { 1 })
