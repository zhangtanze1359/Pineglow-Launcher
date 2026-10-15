#Requires -Version 5.1
<#
.SYNOPSIS
    **Mod 二分定位（bisect）状态机实测**（第 68 轮）：植入一个"元凶"，看它能不能二分辨出来，并且**最后把文件都恢复**。
.DESCRIPTION
    路由：`GET /api/instances/{id}/bisect`（未完成会话查询）、`POST …/bisect/start`、`POST …/bisect/test {crashed,step}`、`POST …/bisect/giveup`。
    语义（源码）：`start` 会**禁用一半候选**；用户回报 `crashed`：
      · `crashed=true`（禁用这半后**仍然崩**）⇒ 元凶在**另一半（仍启用）**里；
      · `crashed=false`（禁用这半后**不崩**）⇒ 元凶在**被禁用的这半**里。
    做法：在实例 `mods/` 里放 8 个假 jar，把 `mod5.jar` 当元凶，按上面的语义逐轮回报，直到 `finished=true`，
    再核对 `culprit`、以及**所有 jar 是否都已恢复启用**（无 `.jar.disabled` 残留）。随后再测 `giveup` 与"过期 step"的拒绝。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [string] $Version = '1.5.2',
    [int] $ModCount = 8,
    [int] $CulpritIndex = 5,
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 900,
    [int] $MaxRounds = 12
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'step.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
function Log([string]$m) { $l = (Get-Date).ToString('HH:mm:ss.fff') + '  ' + $m; [IO.File]::AppendAllText($logPath, $l + "`r`n", (New-Object Text.UTF8Encoding($false))); Write-Host $l }
function Get-FreePort { for ($i = 0; $i -lt 60; $i++) { $l = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0); $l.Start(); $p = ([Net.IPEndPoint]$l.LocalEndpoint).Port; $l.Stop(); if ($p -gt 1024) { return $p } }; throw 'no port' }
function Api([int]$port, [string]$token, [string]$Method, [string]$Path, $Body, [int]$TimeoutSec = 120) {
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
function Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    foreach ($p in $obj.PSObject.Properties) { if ($p.Name -ieq $name) { return $p.Value } }
    return $null
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('bisect-' + [guid]::NewGuid().ToString('N'))
$cold = Join-Path $runDir 'cold'; $dataDir = Join-Path $cold 'data'
[void](New-Item -ItemType Directory -Path $dataDir -Force)
Copy-Item -LiteralPath $exe -Destination (Join-Path $cold 'Pineglow.Launcher.exe') -Force
[void](New-Item -ItemType File -Path (Join-Path $cold 'portable.flag') -Force)
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
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
# 先落盘一次（第 65/66 轮教训）
$null = Api $port $token 'POST' '/api/accounts/offline' '{"userName":"BisectProbe"}'

# 装版本（为了有 gameDir / 版本；不启动游戏）
$sw = [Diagnostics.Stopwatch]::StartNew()
$q = Api $port $token 'POST' '/api/versions/download' ('{"versionId":"' + $Version + '"}') 120
$taskId = Prop $q.body 'id'
$task = $null
while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 3
    $list = Api $port $token 'GET' '/api/downloads' $null 60
    $tasks = @(); $t2 = Prop $list.body 'tasks'; if ($t2) { $tasks = @($t2) }
    $task = $tasks | Where-Object { (Prop $_ 'id') -eq $taskId } | Select-Object -First 1
    if ($task -and ((Prop $task 'status') -eq 'completed' -or (Prop $task 'status') -eq 'failed')) { break }
}
$sw.Stop()
Log ("安装 " + $Version + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s")

$state = Api $port $token 'GET' '/api/state' $null 60
$inst = @(Prop $state.body 'instances') | Select-Object -First 1
$instId = [string](Prop $inst 'id')
$gameDir = [string](Prop $inst 'gameDirectory')
$modsDir = Join-Path $gameDir 'mods'
[void](New-Item -ItemType Directory -Path $modsDir -Force)
$culprit = 'mod' + $CulpritIndex + '.jar'
for ($i = 1; $i -le $ModCount; $i++) { [IO.File]::WriteAllBytes((Join-Path $modsDir ('mod' + $i + '.jar')), [byte[]](1..64)) }
Log ("实例 id=" + $instId + " modsDir=" + $modsDir + " 植入 " + $ModCount + " 个假 jar，元凶=" + $culprit)

$pending0 = Api $port $token 'GET' ("/api/instances/" + $instId + "/bisect") $null 60
Log ("初始 pending -> " + $pending0.text.Substring(0, [Math]::Min(140, $pending0.text.Length)))

# ---- ① start
$start = Api $port $token 'POST' ("/api/instances/" + $instId + "/bisect/start") '{}' 120
$sess = $start.body
$step = [int](Prop $sess 'step')
$disabled = @(Prop $sess 'disabledHalf')
Log ("start -> ok=" + $start.ok + " step=" + $step + " 禁用半区=" + ($disabled -join ',') + " 候选=" + (@(Prop $sess 'candidates') -join ','))
$rounds = New-Object System.Collections.Generic.List[object]
$rounds.Add([pscustomobject]@{ round = 0; step = $step; disabled = $disabled; reported = '' }) | Out-Null

# ---- ② 逐轮回报，直到 finished
$finished = [bool](Prop $sess 'finished')
$culpritFound = ''
$r = 0
while (-not $finished -and $r -lt $MaxRounds) {
    $r++
    $inDisabled = ($disabled -contains $culprit)
    $crashed = -not $inDisabled      # 元凶不在被禁用半区 ⇒ 禁用无效 ⇒ 仍然崩
    $body = @{ crashed = $crashed; step = $step }
    $test = Api $port $token 'POST' ("/api/instances/" + $instId + "/bisect/test") $body 120
    $sess = $test.body
    $prevStep = $step
    $step = [int](Prop $sess 'step')
    $disabled = @(Prop $sess 'disabledHalf')
    $finished = [bool](Prop $sess 'finished')
    $culpritFound = [string](Prop $sess 'culprit')
    $rounds.Add([pscustomobject]@{ round = $r; answeredStep = $prevStep; reported = $crashed; newStep = $step; disabled = $disabled; finished = $finished; culprit = $culpritFound }) | Out-Null
    Log ("  第 " + $r + " 轮：对 step=" + $prevStep + " 回报 crashed=" + $crashed + " -> 新 step=" + $step + " 禁用=" + ($disabled -join ',') + " finished=" + $finished + " culprit=" + $culpritFound)
    if (-not $test.ok) { Log ("  测试回报被拒: " + $test.status + " " + $test.text.Substring(0, [Math]::Min(160, $test.text.Length))); break }
}

$jarsEnabled = @(Get-ChildItem $modsDir -File -Filter '*.jar' -EA SilentlyContinue).Count
$jarsDisabled = @(Get-ChildItem $modsDir -File -Filter '*.jar.disabled' -EA SilentlyContinue).Count
Log ("定位结束：finished=" + $finished + " culprit=" + $culpritFound + " ；mods/ 下 .jar=" + $jarsEnabled + " .jar.disabled=" + $jarsDisabled)

# ---- ③ giveup 路径：再开一轮然后放弃
$start2 = Api $port $token 'POST' ("/api/instances/" + $instId + "/bisect/start") '{}' 120
$sess2 = $start2.body
$disabled2 = @(Prop $sess2 'disabledHalf')
$disabled2Count = $disabled2.Count
$giveup = Api $port $token 'POST' ("/api/instances/" + $instId + "/bisect/giveup") '{}' 120
$jarsEnabled2 = @(Get-ChildItem $modsDir -File -Filter '*.jar' -EA SilentlyContinue).Count
$jarsDisabled2 = @(Get-ChildItem $modsDir -File -Filter '*.jar.disabled' -EA SilentlyContinue).Count
Log ("giveup 前禁用=" + $disabled2Count + " 个 -> giveup ok=" + $giveup.ok + " ；恢复后 .jar=" + $jarsEnabled2 + " .jar.disabled=" + $jarsDisabled2)

# ---- ④ 过期 step 必须被拒
$start3 = Api $port $token 'POST' ("/api/instances/" + $instId + "/bisect/start") '{}' 120
$step3 = [int](Prop $start3.body 'step')
$stale = Api $port $token 'POST' ("/api/instances/" + $instId + "/bisect/test") (@{ crashed = $true; step = ($step3 + 99) }) 120
Log ("过期 step（" + ($step3 + 99) + " vs 实际 " + $step3 + "）-> status=" + $stale.status + " " + $stale.text.Substring(0, [Math]::Min(160, $stale.text.Length)))
$null = Api $port $token 'POST' ("/api/instances/" + $instId + "/bisect/giveup") '{}' 120

$checks = [ordered]@{
    start_disabled_a_half = ($start.ok -and (@(Prop $start.body 'disabledHalf').Count -gt 0))
    narrowed_to_planted_culprit = ($finished -and ($culpritFound -eq $culprit))
    all_mods_restored_after_finish = ($jarsEnabled -eq $ModCount -and $jarsDisabled -eq 0)
    giveup_restored_files = ($giveup.ok -and $jarsEnabled2 -eq $ModCount -and $jarsDisabled2 -eq 0)
    stale_step_rejected = ((-not $stale.ok) -and ($stale.status -eq 409 -or $stale.status -eq 400))
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：二分定位在 ' + $r + ' 轮内定位到植入的元凶，且 finished/giveup 后文件全部恢复；过期 step 被拒'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    instance = [ordered]@{ id = $instId; gameDirectory = $gameDir; modsDir = $modsDir }
    planted = [ordered]@{ modCount = $ModCount; culprit = $culprit }
    start = $start.text
    rounds = $rounds
    result = [ordered]@{ finished = $finished; culprit = $culpritFound; rounds = $r; jarsEnabled = $jarsEnabled; jarsDisabled = $jarsDisabled }
    giveup = [ordered]@{ disabledCount = $disabled2Count; response = $giveup.text; jarsEnabled = $jarsEnabled2; jarsDisabled = $jarsDisabled2 }
    staleStep = [ordered]@{ actualStep = $step3; sentStep = ($step3 + 99); status = $stale.status; response = $stale.text }
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
