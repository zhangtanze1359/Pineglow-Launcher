#Requires -Version 5.1
<#
.SYNOPSIS
    在**打过补丁的构建**上验证 Forge 启动（第 86 轮）：把三处修复真正编进产品，再由**产品自己**启动 Forge 实例。
.DESCRIPTION
    步骤：复制仓库 → git init → git apply 补丁 → dotnet build → 把 apphost 及依赖覆盖进已有便携冷目录
    （复用里面已导入好的 Forge 实例、Java、assets）→ 启动 → 调 `/launch` → 观察 200 秒 → 检查游戏日志标记。
    另做一次 Vanilla 1.18.2 回归（同一构建），确认去重/占位符改动没有弄坏普通路径。
#>
param(
    [Parameter(Mandatory)][string] $PatchPath,
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $DestRoot,
    [int] $ObserveSeconds = 200,
    [string] $VanillaVersion = '1.18.2'
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'forgefix-step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }
function Api([int]$port, [string]$token, [string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 60) {
    $hdr = @{ 'X-Pineglow-Token' = $token }
    try {
        if ($null -ne $Body) {
            $json = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Compress }
            $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec $TimeoutSec
        } else { $r = Invoke-RestMethod "http://127.0.0.1:$port$Path" -Method $Method -Headers $hdr -TimeoutSec $TimeoutSec }
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 6) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail }
    }
}
function Prop($obj, [string]$name) { if ($null -eq $obj) { return $null }; foreach ($p in $obj.PSObject.Properties) { if ($p.Name -ieq $name) { return $p.Value } }; return $null }
function JavaAlive([string]$instDir) { return @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($instDir)) }).Count }

# ---- 1) 复制仓库 + 应用补丁 + 构建
$repo = 'D:\松光启动器'
if (-not $DestRoot) { $DestRoot = Join-Path $env:TEMP 'pg-forgefix' }
if (Test-Path $DestRoot) { Remove-Item -LiteralPath $DestRoot -Recurse -Force -EA SilentlyContinue }
[void](New-Item -ItemType Directory -Path $DestRoot -Force)
Log ('复制仓库 → ' + $DestRoot)
$rc = Start-Process -FilePath 'robocopy' -ArgumentList @($repo, $DestRoot, '/E', '/XD', '.git', 'bin', 'obj', 'node_modules', 'docs', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:1', '/W:1') -Wait -PassThru -WindowStyle Hidden
Log ('  robocopy exit=' + $rc.ExitCode)
if ($rc.ExitCode -ge 8) { Log '❌ 复制失败'; exit 2 }
Push-Location $DestRoot
& git init -q 2>&1 | Out-Null
$chk = & git apply --check $PatchPath 2>&1
Log ('  git apply --check exit=' + $LASTEXITCODE + ' ' + ($chk -join ' '))
& git apply $PatchPath 2>&1 | Out-Null
Log ('  git apply exit=' + $LASTEXITCODE)
$b = & dotnet build 'dev\PineglowLauncher\Pineglow.Launcher.csproj' -c Release --nologo 2>&1
$bcode = $LASTEXITCODE
Log ('  build exit=' + $bcode)
if ($bcode -ne 0) { ($b | Select-Object -Last 8) | ForEach-Object { Log ('    ' + $_) }; Pop-Location; exit 1 }
$out = Join-Path $DestRoot 'dev\PineglowLauncher\bin\Release\net10.0'
Pop-Location
Log ('  构建产物: ' + (Join-Path $out 'Pineglow.Launcher.exe') + ' 存在=' + (Test-Path (Join-Path $out 'Pineglow.Launcher.exe')))
'确认补丁已在副本源码里:'
foreach ($pat in 'DeduplicateJvmArguments', 'library_directory', 'SuppliesOwnClient') {
    $c = @(Select-String -Path (Join-Path $DestRoot 'dev\PineglowLauncher\Pineglow.Launcher.Services\LauncherService.cs') -Pattern $pat).Count
    Log ('    ' + $pat + ' 出现 ' + $c + ' 次')
}

# ---- 2) 把打过补丁的 apphost 覆盖进已有便携冷目录（复用已导入的 Forge 实例）
$cold = (Resolve-Path -LiteralPath $ColdDir).Path
Log ('覆盖冷目录 EXE 与依赖: ' + $cold)
Copy-Item (Join-Path $out '*') $cold -Force -Recurse -EA SilentlyContinue
if (-not (Test-Path (Join-Path $cold 'portable.flag'))) { [void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force) }
Log ('  冷目录 EXE=' + (Get-Item (Join-Path $cold 'Pineglow.Launcher.exe')).Length + ' B（apphost）')

# ---- 3) 启动并跑 Forge
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'fx-stdout.log') -RedirectStandardError (Join-Path $cold 'fx-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 打过补丁的构建没起来'; exit 2 }
Log ('启动器 pid=' + $proc.Id + ' apiPort=' + $port)
$st = Api $port $token 'GET' '/api/state' $null 30
$forge = @(Prop $st.body 'instances') | Where-Object { (Prop $_ 'loaderType') -eq 'Forge' } | Select-Object -First 1
if (-not $forge) { Log '❌ 冷目录里没有 Forge 实例'; exit 2 }
$fDir = [string](Prop $forge 'gameDirectory')
Log ('Forge 实例 id=' + (Prop $forge 'id') + ' dir=' + $fDir)
$pre = Api $port $token 'GET' ("/api/instances/" + (Prop $forge 'id') + "/prelaunch-check") $null
Log ('  prelaunch ok=' + [string](Prop $pre.body 'ok'))
$launch = Api $port $token 'POST' ("/api/instances/" + (Prop $forge 'id') + "/launch") '{}' 120
Log ('  launch -> ' + $launch.text)
$cmd = ''
$dl2 = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $dl2) {
    Start-Sleep -Milliseconds 200
    $mine = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($fDir)) })
    if ($mine.Count -gt 0) { $cmd = [string]$mine[0].CommandLine; break }
}
$hasLib = ($cmd -match '-Djava\.library\.path')
$opens = ([regex]::Matches($cmd, '--add-opens')).Count
$ph = ([regex]::Matches($cmd, '\$\{(library_directory|classpath_separator|version_name)\}')).Count
$hasVanillaJar = ($cmd -match 'versions\\1\.20\.1\\1\.20\.1\.jar')
[IO.File]::WriteAllText((Join-Path $outDir 'forgefix-cmdline.txt'), $cmd, (New-Object Text.UTF8Encoding($false)))
Log ('  命令行长度=' + $cmd.Length + ' 含库路径=' + $hasLib + ' --add-opens 次数=' + $opens + ' 剩余占位符=' + $ph + ' 类路径含原版 jar=' + $hasVanillaJar)
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 10
    $alive = JavaAlive $fDir
    $lg = Join-Path $fDir 'logs\latest.log'
    $tail = ''
    if (Test-Path $lg) { $t = @(Get-Content $lg -Tail 1 -EA SilentlyContinue); if ($t.Count -gt 0) { $tail = [string]$t[0] } }
    Log ('  [forge] {0,4}s java={1}  {2}' -f [int]$sw.Elapsed.TotalSeconds, $alive, $tail.Substring(0, [Math]::Min(95, $tail.Length)))
    if ($alive -eq 0 -and $sw.Elapsed.TotalSeconds -gt 20) { break }
}
$lines = @(); $lgf = Join-Path $fDir 'logs\latest.log'
if (Test-Path $lgf) { $lines = @(Get-Content $lgf -EA SilentlyContinue | ForEach-Object { [string]$_ }) }
@($lines) | Set-Content (Join-Path $outDir 'forgefix-game.log') -Encoding utf8
$mk = [ordered]@{
    logLines = $lines.Count
    lwjgl = @($lines | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    sound = @($lines | Where-Object { $_ -match 'Sound engine started' }).Count
    atlas = @($lines | Where-Object { $_ -match 'Created: .*atlas' }).Count
    crash = @($lines | Where-Object { $_ -match 'Failed to locate library|has crashed|UnsatisfiedLinkError' }).Count
}
$aliveNow = JavaAlive $fDir
Log ('  Forge 标记: 行数=' + $mk.logLines + ' lwjgl=' + $mk.lwjgl + ' 声音=' + $mk.sound + ' 图集=' + $mk.atlas + ' 崩溃=' + $mk.crash + ' java存活=' + $aliveNow)
@(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($fDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

# ---- 4) Vanilla 回归（同一构建）
$vres = $null
$q = Api $port $token 'POST' '/api/versions/download' ('{"versionId":"' + $VanillaVersion + '"}') 120
$taskId = [string](Prop $q.body 'id'); $task = $null; $swq = [Diagnostics.Stopwatch]::StartNew()
while ($swq.Elapsed.TotalSeconds -lt 1200) {
    Start-Sleep -Seconds 3
    $list = Api $port $token 'GET' '/api/downloads' $null 60
    $tasks = @(); $tt = Prop $list.body 'tasks'; if ($tt) { $tasks = @($tt) }
    $task = $tasks | Where-Object { (Prop $_ 'id') -eq $taskId } | Select-Object -First 1
    if ($task -and ((Prop $task 'status') -eq 'completed' -or (Prop $task 'status') -eq 'failed')) { break }
}
$cr = Api $port $token 'POST' '/api/instances' ('{"name":"fx-vanilla","version":"' + $VanillaVersion + '"}') 60
$vId = ''; if ($cr.ok -and $cr.body -and ($cr.body.PSObject.Properties.Name -contains 'instance')) { $vId = [string](Prop $cr.body.instance 'id') }
$st2 = Api $port $token 'GET' '/api/state' $null 30
$vi = @(Prop $st2.body 'instances') | Where-Object { (Prop $_ 'id') -eq $vId } | Select-Object -First 1
$vDir = ''; if ($vi) { $vDir = [string](Prop $vi 'gameDirectory') }
if ($vDir) {
    $null = Api $port $token 'POST' ("/api/instances/" + $vId + "/launch") '{}' 120
    $vcmd = ''
    $dl3 = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $dl3) {
        Start-Sleep -Milliseconds 200
        $mine = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($vDir)) })
        if ($mine.Count -gt 0) { $vcmd = [string]$mine[0].CommandLine; break }
    }
    $swv = [Diagnostics.Stopwatch]::StartNew()
    while ($swv.Elapsed.TotalSeconds -lt 90) { Start-Sleep -Seconds 10; if ((JavaAlive $vDir) -eq 0 -and $swv.Elapsed.TotalSeconds -gt 20) { break } }
    $vlg = Join-Path $vDir 'logs\latest.log'
    $vl = @(); if (Test-Path $vlg) { $vl = @(Get-Content $vlg -EA SilentlyContinue | ForEach-Object { [string]$_ }) }
    $vres = [ordered]@{
        cmdLength = $vcmd.Length; hasLib = ($vcmd -match '-Djava\.library\.path')
        lwjgl = @($vl | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
        atlas = @($vl | Where-Object { $_ -match 'Created: .*atlas' }).Count
        crash = @($vl | Where-Object { $_ -match 'Failed to locate library|has crashed|UnsatisfiedLinkError' }).Count
    }
    Log ('  Vanilla 回归: 命令行=' + $vres.cmdLength + ' 含库路径=' + $vres.hasLib + ' lwjgl=' + $vres.lwjgl + ' 图集=' + $vres.atlas + ' 崩溃=' + $vres.crash)
    @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -match [regex]::Escape($vDir)) }) | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

$checks = [ordered]@{
    forge_cmdline_has_libpath = $hasLib
    forge_tokens_preserved = ($opens -ge 2)
    forge_placeholders_substituted = ($ph -eq 0)
    forge_no_vanilla_jar_in_cp = (-not $hasVanillaJar)
    forge_lwjgl = ($mk.lwjgl -gt 0)
    forge_atlases = ($mk.atlas -gt 0)
    forge_no_crash = ($mk.crash -eq 0)
    vanilla_regression_ok = ($vres -and $vres.hasLib -and $vres.lwjgl -gt 0 -and $vres.atlas -gt 0 -and $vres.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
$verdict = 'PASS：打过补丁的产品自己把 Forge 实例启动到渲染管线，且 Vanilla 回归正常'
if ($fails.Count -gt 0) { $verdict = 'FAIL/需解释：' + $fails.Count + ' 项（' + (($fails | ForEach-Object { $_.Key }) -join ',') + '）' }
Log ('逐项: ' + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + '=' + $_.Value.ToString() }) -join '  '))
Log ('判定: ' + $verdict)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    patchedBuildRoot = $DestRoot; coldDir = $cold
    forgeCmdlineLength = $cmd.Length; addOpensCount = $opens; placeholdersLeft = $ph; vanillaJarInClasspath = $hasVanillaJar
    forgeMarkers = $mk; forgeJavaAliveAtEnd = $aliveNow; vanilla = $vres
    checks = $checks; verdict = $verdict
} | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
