#Requires -Version 5.1
<#
.SYNOPSIS
    **Java 探测（/api/java）** 与 **恢复区（/api/instances/{id}/recovery）** 两个此前没单独验过的只读/恢复端点（第 71 轮）。
.DESCRIPTION
    ① `GET /api/java` → `JavaLocator.ScanAsync(null)`：把 `JAVA_HOME` 指向一个真实 JRE 17，看它能否被发现并判为有效；
    ② 把一个 Mod 移入恢复区（`POST /api/instances/{id}/mods/{file}/delete`）→ `GET …/recovery` 应出现记录 →
       `POST …/recovery/{operationId}/restore` 应把文件恢复回来。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JavaHome,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [string] $Version = '1.5.2',
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 900
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }
function Api([int]$port, [string]$token, [string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 90) {
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

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$jhome = (Resolve-Path -LiteralPath $JavaHome).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('javarec-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$env:JAVA_HOME = $jhome      # 让 JavaLocator 有机会发现它
Log ("JAVA_HOME=" + $jhome)
$proc = Start-Process -FilePath (Join-Path $cold 'Pineglow.Launcher.exe') -WorkingDirectory $cold -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $cold 'stdout.log') -RedirectStandardError (Join-Path $cold 'stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '启动器未就绪'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)
$null = Api $port $token 'POST' '/api/accounts/offline' '{"userName":"JavaRecProbe"}'

# ---- ① /api/java
$java = Api $port $token 'GET' '/api/java' $null 120
$installs = @(); $j = Prop $java.body 'installations'; if ($j) { $installs = @($j) }
$foundJhome = @($installs | Where-Object { ([string](Prop $_ 'javaPath')) -like "*$([IO.Path]::GetFileName($jhome))*" }).Count
$validOnes = @($installs | Where-Object { (Prop $_ 'isValid') -eq $true }).Count
Log ("GET /api/java -> ok=" + $java.ok + " 发现 " + $installs.Count + " 个安装，其中有效 " + $validOnes + " 个；命中 JAVA_HOME 的=" + $foundJhome)
foreach ($i in ($installs | Select-Object -First 3)) { Log ("   " + [string](Prop $i 'javaPath') + "  major=" + [string](Prop $i 'versionMajor') + " vendor=" + [string](Prop $i 'vendor') + " valid=" + [string](Prop $i 'isValid')) }
$javaSet = Api $port $token 'POST' '/api/config' (@{ javaPath = (Join-Path $jhome 'bin\java.exe') }) 60
$java2 = Api $port $token 'GET' '/api/java' $null 120
$configured = [string](Prop $java2.body 'javaPath')
Log ("设置 javaPath 后 /api/java 回显的 javaPath=" + $configured)

# ---- ② 恢复区
$create = Api $port $token 'POST' '/api/instances' ('{"name":"recovery-test","version":"' + $Version + '"}')
$instId = ''; if ($create.ok -and $create.body -and ($create.body.PSObject.Properties.Name -contains 'instance')) { $instId = [string](Prop $create.body.instance 'id') }
$state = Api $port $token 'GET' '/api/state' $null 60
$inst = @(Prop $state.body 'instances') | Where-Object { (Prop $_ 'id') -eq $instId } | Select-Object -First 1
$gameDir = [string](Prop $inst 'gameDirectory')
$modsDir = Join-Path $gameDir 'mods'; [void](New-Item -ItemType Directory -Path $modsDir -Force)
$modName = 'RecoveryProbe.jar'
$modPath = Join-Path $modsDir $modName
[IO.File]::WriteAllBytes($modPath, [byte[]](0..255))
$rec0 = Api $port $token 'GET' ("/api/instances/" + $instId + "/recovery") $null 60
$rec0Count = 0; $r0 = Prop $rec0.body 'recoveries'; if ($r0) { $rec0Count = @($r0).Count }
Log ("恢复区初始记录数=" + $rec0Count + " retentionDays=" + [string](Prop $rec0.body 'retentionDays'))

$del = Api $port $token 'POST' ("/api/instances/" + $instId + "/mods/" + $modName + "/delete") '{}' 90
$opId = [string](Prop $del.body 'operationId')
Log ("移入恢复区 -> status=" + $del.status + " " + $del.text.Substring(0, [Math]::Min(200, $del.text.Length)))
$fileGone = -not (Test-Path $modPath)
$rec1 = Api $port $token 'GET' ("/api/instances/" + $instId + "/recovery") $null 60
$r1 = Prop $rec1.body 'recoveries'; $rec1List = @(); if ($r1) { $rec1List = @($r1) }
Log ("移入后恢复区记录数=" + $rec1List.Count + " -> " + $rec1.text.Substring(0, [Math]::Min(240, $rec1.text.Length)))
if (-not $opId -and $rec1List.Count -gt 0) { $opId = [string](Prop $rec1List[0] 'operationId') }

$restore = $null; $fileBack = $false
if ($opId) {
    $restore = Api $port $token 'POST' ("/api/instances/" + $instId + "/recovery/" + $opId + "/restore") '{}' 90
    $fileBack = Test-Path $modPath
    Log ("恢复 -> status=" + $restore.status + " " + $restore.text.Substring(0, [Math]::Min(200, $restore.text.Length)) + " ；文件已回来=" + $fileBack)
}
$rec2 = Api $port $token 'GET' ("/api/instances/" + $instId + "/recovery") $null 60
$r2 = Prop $rec2.body 'recoveries'; $rec2Count = 0; if ($r2) { $rec2Count = @($r2).Count }
Log ("恢复后恢复区记录数=" + $rec2Count)

$checks = [ordered]@{
    java_endpoint_ok = $java.ok
    java_scan_found_javahome = ($foundJhome -ge 1)
    java_config_echo = (-not [string]::IsNullOrWhiteSpace($configured))
    recovery_list_readable = $rec0.ok
    quarantine_recorded = ($del.ok -and $rec1List.Count -ge 1 -and $fileGone)
    restore_brought_file_back = ($restore -and $restore.ok -and $fileBack)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：Java 探测能发现 JAVA_HOME 里的 JRE；恢复区能列出隔离记录并成功还原文件'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    javaHome = $jhome
    java = [ordered]@{ endpointOk = $java.ok; installations = $installs.Count; valid = $validOnes; matchedJavaHome = $foundJhome; configuredEcho = $configured; raw = $java.text }
    recovery = [ordered]@{
        instanceId = $instId; gameDirectory = $gameDir; modName = $modName
        initialCount = $rec0Count
        deleteStatus = $del.status; deleteResponse = $del.text; fileGone = $fileGone
        afterQuarantineCount = $rec1List.Count; listResponse = $rec1.text
        operationId = $opId
        restoreStatus = $(if ($restore) { $restore.status } else { 0 }); restoreResponse = $(if ($restore) { $restore.text } else { $null }); fileBack = $fileBack
        finalCount = $rec2Count
    }
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
