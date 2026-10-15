#Requires -Version 5.1
<#
.SYNOPSIS
    **资源包 / 光影 的搜索与安装**实测（第 69 轮）：`category=resourcepack|shader` 能否真的装进对应目录。
.DESCRIPTION
    路由与分类（源码）：`GET /api/content/search?q=&category=&gameVersion=&loader=`；`POST /api/content/install {category,projectId,instanceId}`
    的 category 支持 `modpack|shader|resourcepack|mod`（`LocalApiService.cs:5352`）；列表接口 `GET /api/instances/{id}/resourcepacks|shaderpacks`。
    轮询**按任务的 status 字段**判终态（第 62 轮的教训：已完成的任务仍留在列表里，不能等列表变空）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [string] $Version = '1.18.2',
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 600
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
function Prop($obj, [string]$name) { if ($null -eq $obj) { return $null }; foreach ($p in $obj.PSObject.Properties) { if ($p.Name -ieq $name) { return $p.Value } }; return $null }

$exe = (Resolve-Path -LiteralPath $ExePath).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('resp-' + [guid]::NewGuid().ToString('N'))
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
$null = Api $port $token 'POST' '/api/accounts/offline' '{"userName":"ResourceProbe"}'
$create = Api $port $token 'POST' '/api/instances' ('{"name":"resp-test","version":"' + $Version + '"}')
$instId = ''
if ($create.ok -and $create.body -and ($create.body.PSObject.Properties.Name -contains 'instance')) { $instId = [string](Prop $create.body.instance 'id') }
$state = Api $port $token 'GET' '/api/state' $null 60
$inst = @(Prop $state.body 'instances') | Where-Object { (Prop $_ 'id') -eq $instId } | Select-Object -First 1
$gameDir = [string](Prop $inst 'gameDirectory')
Log ("实例 id=" + $instId + " gameDir=" + $gameDir)

function WaitTask([int]$port, [string]$token, [string]$taskId, [int]$timeoutSec) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); $last = $null
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        Start-Sleep -Seconds 3
        $list = Api $port $token 'GET' '/api/content/tasks' $null 60
        $tasks = @(); $t = Prop $list.body 'tasks'; if ($t) { $tasks = @($t) }
        $last = $tasks | Where-Object { (Prop $_ 'id') -eq $taskId } | Select-Object -First 1
        $st = [string](Prop $last 'status')
        if ($st -eq 'completed' -or $st -eq 'failed') { break }
    }
    return [pscustomobject]@{ task = $last; seconds = [int]$sw.Elapsed.TotalSeconds }
}

$results = [ordered]@{}
foreach ($case in @(
        @{ cat = 'resourcepack'; query = 'faithful'; extra = '&gameVersion=' + $Version },
        @{ cat = 'shader'; query = 'complementary'; extra = '&gameVersion=' + $Version + '&loader=iris' }
    )) {
    $cat = $case.cat
    $searchPath = '/api/content/search?q=' + $case.query + '&category=' + $cat + $case.extra + '&source=modrinth'
    $s = Api $port $token 'GET' $searchPath $null 120
    $hits = @(); $r0 = Prop $s.body 'results'; if ($r0) { $hits = @($r0) }
    $projId = ''; $title = ''
    if ($hits.Count -gt 0) {
        foreach ($nm in @('projectId', 'ProjectId', 'id', 'Id', 'slug', 'Slug')) { $v = Prop $hits[0] $nm; if ($v) { $projId = [string]$v; break } }
        $title = [string](Prop $hits[0] 'title')
    }
    Log ("[$cat] 搜索 '" + $case.query + "' -> 命中 " + $hits.Count + " 个，取 id=" + $projId + "（" + $title + "）")
    $install = $null; $task = $null; $taskSec = 0
    if ($projId) {
        $install = Api $port $token 'POST' '/api/content/install' (@{ category = $cat; projectId = $projId; instanceId = $instId; source = 'modrinth' }) 120
        $taskId = [string](Prop $install.body 'id')
        Log ("[$cat] 安装 -> " + $install.text.Substring(0, [Math]::Min(160, $install.text.Length)))
        $w = WaitTask $port $token $taskId $InstallTimeoutSec
        $task = $w.task; $taskSec = $w.seconds
        Log ("[$cat] 任务终态: status=" + [string](Prop $task 'status') + " message=" + [string](Prop $task 'message') + " title=" + [string](Prop $task 'title') + "（" + $taskSec + "s）")
    }
    $targetDir = Join-Path $gameDir ($(if ($cat -eq 'shader') { 'shaderpacks' } else { 'resourcepacks' }))
    $files = @()
    if (Test-Path $targetDir) { $files = @(Get-ChildItem $targetDir -File -EA SilentlyContinue) }
    foreach ($f in $files) { Log ("[$cat] " + (Split-Path $targetDir -Leaf) + "/" + $f.Name + "  " + $f.Length + " B") }
    $listApi = Api $port $token 'GET' ("/api/instances/" + $instId + "/" + $(if ($cat -eq 'shader') { 'shaderpacks' } else { 'resourcepacks' })) $null 60
    Log ("[$cat] 列表接口 -> " + $listApi.text.Substring(0, [Math]::Min(200, $listApi.text.Length)))
    $results[$cat] = [ordered]@{
        query = $case.query
        hits = $hits.Count
        projectId = $projId
        title = $title
        installResponse = $(if ($install) { $install.text } else { $null })
        taskStatus = [string](Prop $task 'status')
        taskMessage = [string](Prop $task 'message')
        taskSeconds = $taskSec
        targetDir = $targetDir
        files = @($files | ForEach-Object { [ordered]@{ name = $_.Name; bytes = $_.Length } })
        listResponse = $listApi.text
    }
}

$state2 = Api $port $token 'GET' '/api/state' $null 60
$inst2 = @(Prop $state2.body 'instances') | Where-Object { (Prop $_ 'id') -eq $instId } | Select-Object -First 1
$rpCount = Prop $inst2 'resourcePacksCount'; $spCount = Prop $inst2 'shaderPacksCount'
Log ("state 计数：resourcePacksCount=" + $rpCount + " shaderPacksCount=" + $spCount)

$checks = [ordered]@{
    resourcepack_search_hits = ($results['resourcepack'].hits -gt 0)
    resourcepack_installed = ($results['resourcepack'].files.Count -gt 0)
    resourcepack_task_completed = ($results['resourcepack'].taskStatus -eq 'completed')
    shader_search_hits = ($results['shader'].hits -gt 0)
    shader_installed = ($results['shader'].files.Count -gt 0)
    shader_task_completed = ($results['shader'].taskStatus -eq 'completed')
    state_counts_match = (([int]$rpCount -ge 1) -and ([int]$spCount -ge 1))
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：资源包与光影都能搜到、能装进对应目录、任务 completed、state 计数正确'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
$res = [ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    instance = [ordered]@{ id = $instId; gameDirectory = $gameDir; version = $Version }
    results = $results
    stateCounts = [ordered]@{ resourcePacksCount = $rpCount; shaderPacksCount = $spCount }
    checks = $checks
    verdict = $verdictText
}
$res | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
