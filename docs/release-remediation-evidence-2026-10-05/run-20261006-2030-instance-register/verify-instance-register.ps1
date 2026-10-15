#Requires -Version 5.1
<#
.SYNOPSIS
    **"从列表移除" ↔ "把目录挂回"** 实测（第 64 轮，MR03）：`POST /api/instances/{id}/delete` 是否只改配置不删文件；
    `POST /api/instances/register` 能否把目录挂回；重复挂回与不存在的目录是否给出确定性错误。
.DESCRIPTION
    源码事实：`HandleDeleteInstanceAsync`（LocalApiService.cs:1261）注释写明"只改配置：此处不存在任何 File/Directory 写操作"，
    并在 Bisect 进行中 / 实例运行中 抛 400；`HandleRegisterInstanceAsync`（:1318）校验目录存在、去重后只写配置。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $Version = '1.5.2',
    [string] $WorkRoot,
    [int] $InstallTimeoutSec = 900,
    [int] $StartupTimeoutSec = 120
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
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 5) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail }
    }
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('register-' + [guid]::NewGuid().ToString('N'))
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

# ⓪ 先做一次"会落盘"的操作（第 65 轮发现：无 config.json 时默认实例 id 每次都变 ⇒ 必须先让它落盘，
#    否则本测试会因"实例不存在"而假失败——那是另一个缺陷，不是 register/delete 的问题）
$save = Api $port $token 'POST' '/api/accounts/offline' '{"userName":"RegisterProbe"}' 60
Log ("预置落盘（建离线账号）-> " + $save.text.Substring(0, [Math]::Min(90, $save.text.Length)))

# ① 装一个版本（顺带建默认实例）
$sw = [Diagnostics.Stopwatch]::StartNew()
$q = Api $port $token 'POST' '/api/versions/download' ('{"versionId":"' + $Version + '"}') 120
$taskId = ''
if ($q.ok -and $q.body -and ($q.body.PSObject.Properties.Name -contains 'id')) { $taskId = [string]$q.body.id }
$task = $null
while ($sw.Elapsed.TotalSeconds -lt $InstallTimeoutSec) {
    Start-Sleep -Seconds 3
    $list = Api $port $token 'GET' '/api/downloads' $null 60
    $tasks = @()
    if ($list.ok -and $list.body -and ($list.body.PSObject.Properties.Name -contains 'tasks')) { $tasks = @($list.body.tasks) }
    $task = $tasks | Where-Object { $_.id -eq $taskId } | Select-Object -First 1
    if ($task -and ($task.status -eq 'completed' -or $task.status -eq 'failed')) { break }
}
$sw.Stop()
$st = '?'; if ($task) { $st = [string]$task.status }
Log ("安装 " + $Version + ": status=" + $st + " 用时=" + [int]$sw.Elapsed.TotalSeconds + "s")

$state0 = Api $port $token 'GET' '/api/state' $null 60
$inst0 = @($state0.body.instances) | Select-Object -First 1
$instId = ''; $gameDir = ''
if ($inst0) {
    if ($inst0.PSObject.Properties.Name -contains 'id') { $instId = [string]$inst0.id }
    if ($inst0.PSObject.Properties.Name -contains 'gameDirectory') { $gameDir = [string]$inst0.gameDirectory }
}
Log ("默认实例 id=" + $instId + " gameDir=" + $gameDir)
$jar = Join-Path $gameDir ("versions\" + $Version + "\" + $Version + ".jar")
$jarBefore = Test-Path $jar
$jarBytesBefore = 0
if ($jarBefore) { $jarBytesBefore = (Get-Item $jar).Length }
Log ("移除前：versions jar 存在=" + $jarBefore + " 大小=" + $jarBytesBefore)

# ② 从列表移除（应当只改配置）
$del = Api $port $token 'POST' ("/api/instances/" + $instId + "/delete") '{}' 60
$state1 = Api $port $token 'GET' '/api/state' $null 60
$count1 = @($state1.body.instances).Count
$stillThere = @($state1.body.instances | Where-Object { $_.id -eq $instId }).Count
$jarAfterDelete = Test-Path $jar
$jarBytesAfterDelete = 0
if ($jarAfterDelete) { $jarBytesAfterDelete = (Get-Item $jar).Length }
Log ("移除响应=" + $del.text.Substring(0, [Math]::Min(120, $del.text.Length)) + " ；实例数 " + @($state0.body.instances).Count + " -> " + $count1 + " ；该实例仍在列表=" + $stillThere)
Log ("移除后：versions jar 存在=" + $jarAfterDelete + " 大小=" + $jarBytesAfterDelete + "（应与移除前一致 ⇒ 只改配置）")

# ③ 挂回
$reg = Api $port $token 'POST' '/api/instances/register' (@{ gameDirectory = $gameDir; name = 'restored'; minecraftVersion = $Version }) 60
$state2 = Api $port $token 'GET' '/api/state' $null 60
$back = @($state2.body.instances | Where-Object { $_.name -eq 'restored' }).Count
Log ("挂回响应=" + $reg.text.Substring(0, [Math]::Min(200, $reg.text.Length)) + " ；列表里 'restored' 数量=" + $back)

# ④ 重复挂回 / 不存在的目录
$dup = Api $port $token 'POST' '/api/instances/register' (@{ gameDirectory = $gameDir; name = 'restored-dup'; minecraftVersion = $Version }) 60
$bad = Api $port $token 'POST' '/api/instances/register' '{"gameDirectory":"C:\\__pineglow_不存在的目录__","name":"nope"}' 60
Log ("重复挂回 -> status=" + $dup.status + " " + $dup.text.Substring(0, [Math]::Min(140, $dup.text.Length)))
Log ("不存在的目录 -> status=" + $bad.status + " " + $bad.text.Substring(0, [Math]::Min(140, $bad.text.Length)))

$checks = [ordered]@{
    delete_removed_from_list = ($del.ok -and $count1 -eq (@($state0.body.instances).Count - 1) -and $stillThere -eq 0)
    delete_kept_files = ($jarAfterDelete -and $jarBytesAfterDelete -eq $jarBytesBefore)
    register_brought_it_back = ($reg.ok -and $back -ge 1)
    duplicate_register_refused = (-not $dup.ok -and $dup.status -eq 400)
    nonexistent_dir_refused = (-not $bad.ok -and $bad.status -eq 400)
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：移除只改配置（文件完好）、挂回成功、重复与不存在目录都被 400 挡住'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    version = $Version
    instance = [ordered]@{ id = $instId; gameDirectory = $gameDir }
    before = [ordered]@{ instances = @($state0.body.instances).Count; jar = $jarBefore; jarBytes = $jarBytesBefore }
    afterDelete = [ordered]@{ response = $del.text; instances = $count1; stillListed = $stillThere; jar = $jarAfterDelete; jarBytes = $jarBytesAfterDelete }
    register = [ordered]@{ response = $reg.text; restoredCount = $back }
    duplicate = [ordered]@{ status = $dup.status; response = $dup.text }
    nonexistent = [ordered]@{ status = $bad.status; response = $bad.text }
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
