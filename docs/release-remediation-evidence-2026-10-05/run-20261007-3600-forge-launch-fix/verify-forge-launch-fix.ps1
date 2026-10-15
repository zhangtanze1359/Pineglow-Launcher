#Requires -Version 5.1
<#
.SYNOPSIS
    捕获 Forge 实例的真实启动命令行，然后**按修复方案修正**（补回被去重的选项 token + 替换 Forge 占位符并正确引用）后手工启动，
    验证能否真正进渲染管线（第 85 轮）。
.DESCRIPTION
    已知两个装配缺陷（第 84 轮定位）：
      ① `LauncherService.cs:197 DistinctPreserveOrder()` 把重复的 `--add-opens/--add-exports` token 去重成 1 个；
      ② `BuildReplacements` 里没有 `library_directory` / `classpath_separator` ⇒ 生成的命令行里它们是字面量。
    本脚本不改产品源码：先抓产品生成的原命令行，再在**同一条命令行**上按修复方案修正并运行。
#>
param(
    [Parameter(Mandatory)][string] $ColdDir,
    [Parameter(Mandatory)][string] $JsonPath,
    [int] $ObserveSeconds = 200
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'forge-fix-step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }

$cold = (Resolve-Path -LiteralPath $ColdDir).Path
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'fix-stdout.log') -RedirectStandardError (Join-Path $cold 'fix-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 启动器未就绪'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)
$hdr = @{ 'X-Pineglow-Token' = $token }
$st = Invoke-RestMethod "http://127.0.0.1:$port/api/state" -Headers $hdr -TimeoutSec 20
$inst = @($st.instances) | Where-Object { $_.loaderType -eq 'Forge' } | Select-Object -First 1
if (-not $inst) { Log '❌ 找不到 Forge 实例'; exit 2 }
$instDir = [string]$inst.gameDirectory
Log ("Forge 实例 id=" + $inst.id + " dir=" + $instDir)

# ---- 抓产品生成的真实命令行（40ms 轮询）
$before = @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -EA SilentlyContinue | ForEach-Object { $_.ProcessId })
$r = Invoke-RestMethod "http://127.0.0.1:$port/api/instances/$($inst.id)/launch" -Method POST -Headers $hdr -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes('{}')) -TimeoutSec 60
Log ("launch -> " + ($r | ConvertTo-Json -Compress))
$cmd = ''; $sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 30) {
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='java.exe' OR Name='javaw.exe'" -EA SilentlyContinue)) {
        if ($before -notcontains $p.ProcessId) { $cmd = [string]$p.CommandLine; break }
    }
    if ($cmd) { break }
    Start-Sleep -Milliseconds 40
}
$sw.Stop()
Log ("捕获命令行长度=" + $cmd.Length + "（用时 " + [int]$sw.Elapsed.TotalSeconds + "s）")
if (-not $cmd) { Log '❌ 未捕获到 java 命令行'; exit 2 }
[IO.File]::WriteAllText((Join-Path $outDir 'forge-product-cmdline.txt'), $cmd, (New-Object Text.UTF8Encoding($false)))

# ---- 按修复方案修正
$m = [regex]::Match($cmd, '^"([^"]+)"\s*(.*)$')
$java = $m.Groups[1].Value; $args = $m.Groups[2].Value
$libs = Join-Path $instDir 'libraries'
$fix = $args
$beforeOpens = ([regex]::Matches($fix, '--add-opens')).Count
$beforeExports = ([regex]::Matches($fix, '--add-exports')).Count
# ① 补回被去重掉的重复选项 token
$fix = [regex]::Replace($fix, '--add-opens (\S+) (\S+) --add-exports (\S+) (\S+)', '--add-opens $1 --add-opens $2 --add-exports $3 --add-exports $4')
# ② 替换 Forge 占位符；对含空格的路径**加引号**
$fix = $fix.Replace('-DlibraryDirectory=${library_directory}', '-DlibraryDirectory="' + $libs + '"')
$fix = [regex]::Replace($fix, '-p (\$\{library_directory\}\S*)', {
        param($mm)
        $v = $mm.Groups[1].Value.Replace('${library_directory}', $libs).Replace('${classpath_separator}', ';')
        return '-p "' + $v + '"'
    })
$fix = $fix.Replace('${library_directory}', $libs).Replace('${classpath_separator}', ';').Replace('${version_name}', [string]$inst.minecraftVersion)
$afterOpens = ([regex]::Matches($fix, '--add-opens')).Count
$afterExports = ([regex]::Matches($fix, '--add-exports')).Count
$leftPh = ([regex]::Matches($fix, '\$\{(library_directory|classpath_separator|version_name)\}')).Count
Log ("修正：--add-opens " + $beforeOpens + "→" + $afterOpens + " ；--add-exports " + $beforeExports + "→" + $afterExports + " ；剩余占位符=" + $leftPh)
[IO.File]::WriteAllText((Join-Path $outDir 'forge-fixed-cmdline.txt'), ($java + ' ' + $fix), (New-Object Text.UTF8Encoding($false)))

# ---- 运行修正后的命令行
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $java; $psi.Arguments = $fix; $psi.WorkingDirectory = $instDir
$psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
$p = [System.Diagnostics.Process]::Start($psi)
$so = $p.StandardOutput.ReadToEndAsync(); $se = $p.StandardError.ReadToEndAsync()
$sw2 = [Diagnostics.Stopwatch]::StartNew(); $alive = $true
while ($sw2.Elapsed.TotalSeconds -lt $ObserveSeconds) {
    Start-Sleep -Seconds 5
    if ($p.HasExited) { $alive = $false; break }
}
$sw2.Stop()
$exit = if ($alive) { -999 } else { $p.ExitCode }
Log ("修正后运行：存活=" + $alive + " 用时=" + [int]$sw2.Elapsed.TotalSeconds + "s exitCode=" + $exit)
if ($alive) { try { Stop-Process -Id $p.Id -Force -EA SilentlyContinue } catch { } }
$errTail = (($se.Result) -split "`r?`n") | Where-Object { $_ -ne '' } | Select-Object -Last 12
$errTail | ForEach-Object { Log ("  ERR " + $_) }
$gameLog = Join-Path $instDir 'logs\latest.log'
$mk = [ordered]@{ logLines = 0; lwjgl = 0; sound = 0; atlas = 0; crash = 0 }
if (Test-Path $gameLog) {
    $L = @(Get-Content $gameLog -EA SilentlyContinue | ForEach-Object { [string]$_ })
    $mk.logLines = $L.Count
    $mk.lwjgl = @($L | Where-Object { $_ -match 'Backend library: LWJGL' }).Count
    $mk.sound = @($L | Where-Object { $_ -match 'Sound engine started' }).Count
    $mk.atlas = @($L | Where-Object { $_ -match 'Created: .*atlas' }).Count
    $mk.crash = @($L | Where-Object { $_ -match 'Failed to locate library|has crashed|UnsatisfiedLinkError' }).Count
    $L | Set-Content (Join-Path $outDir 'forge-fixed-game.log') -Encoding utf8
}
Log ("游戏日志标记: 行数=" + $mk.logLines + " lwjgl=" + $mk.lwjgl + " 声音=" + $mk.sound + " 图集=" + $mk.atlas + " 崩溃=" + $mk.crash)
$checks = [ordered]@{
    tokens_restored = (($afterOpens -gt $beforeOpens) -and ($afterExports -gt $beforeExports))
    placeholders_substituted = ($leftPh -eq 0)
    process_survived = $alive
    lwjgl_backend = ($mk.lwjgl -gt 0)
    atlases_created = ($mk.atlas -gt 0)
    no_crash = ($mk.crash -eq 0)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
$verdict = 'PASS：按修复方案修正后 Forge 实例能进渲染管线'
if ($fails.Count -gt 0) { $verdict = 'FAIL/需解释：' + $fails.Count + ' 项（' + (($fails | ForEach-Object { $_.Key }) -join ',') + '）' }
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
Log ('判定: ' + $verdict)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    coldDir = $cold; instanceId = [string]$inst.id; instanceDir = $instDir
    productCmdlineLength = $cmd.Length; fixedCmdlineLength = ($java + ' ' + $fix).Length
    addOpensBefore = $beforeOpens; addOpensAfter = $afterOpens; addExportsBefore = $beforeExports; addExportsAfter = $afterExports; placeholdersLeft = $leftPh
    alive = $alive; seconds = [int]$sw2.Elapsed.TotalSeconds; exitCode = $exit; stderrTail = $errTail
    markers = $mk; checks = $checks; verdict = $verdict
} | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
