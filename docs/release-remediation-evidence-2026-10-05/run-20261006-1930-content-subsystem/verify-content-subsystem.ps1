#Requires -Version 5.1
<#
.SYNOPSIS
    **内容子系统**（搜索 → 安装）实测（第 62 轮）：Modrinth 的 mod/modpack/资源包/光影搜索是否真的返回数据；
    CurseForge 无 API Key 时是否给出确定性错误；`POST /api/content/install` 是否真的把 jar 落到实例的 mods/。
.DESCRIPTION
    路由与参数取自源码：`GET /api/content/search?q=&category=&gameVersion=&loader=&source=`（LocalApiService.cs:5033）、
    `POST /api/content/install {category,projectId,instanceId,source}`（:5270）、`GET /api/content/tasks`（:805）。
    这是本会话**还没碰过**的一块（此前只验过 `/api/content/icon`）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [int] $StartupTimeoutSec = 120,
    [int] $InstallTimeoutSec = 300
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
if (-not (Test-Path $outDir)) { [void](New-Item -ItemType Directory -Path $outDir -Force) }
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
$runDir = Join-Path $WorkRoot ('content-' + [guid]::NewGuid().ToString('N'))
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
Log ("启动器 pid=$($proc.Id) apiPort=$port")

$checks = [ordered]@{}
# ---- 1) 四类搜索
$searches = @(
    @{ name = 'modrinth-mod';      path = '/api/content/search?q=sodium&category=mod&gameVersion=1.18.2&loader=fabric&source=modrinth' },
    @{ name = 'modrinth-modpack';  path = '/api/content/search?q=fabulously&category=modpack&source=modrinth' },
    @{ name = 'modrinth-resource'; path = '/api/content/search?q=faithful&category=resourcepack&gameVersion=1.18.2&source=modrinth' },
    @{ name = 'modrinth-shader';   path = '/api/content/search?q=complementary&category=shader&gameVersion=1.18.2&loader=iris&source=modrinth' },
    @{ name = 'curseforge-mod';    path = '/api/content/search?q=jei&category=mod&gameVersion=1.18.2&source=curseforge' }
)
$searchResults = [ordered]@{}
$firstModId = ''
foreach ($s in $searches) {
    $r = Api $port $token 'GET' $s.path $null 120
    $n = 0; $first = ''
    if ($r.ok -and $r.body -and ($r.body.PSObject.Properties.Name -contains 'results')) {
        $n = @($r.body.results).Count
        if ($n -gt 0) {
            $f0 = $r.body.results[0]
            # 注意：`-contains` 是**大小写敏感**的，而接口可能序列化成 PascalCase（第 62 轮踩过）
            foreach ($nm in @('projectId', 'ProjectId', 'id', 'Id', 'slug', 'Slug', 'project_id')) {
                if ($f0.PSObject.Properties.Name -contains $nm) { $first = [string]$f0.$nm; break }
            }
        }
    }
    if ($s.name -eq 'modrinth-mod' -and $n -gt 0) { $firstModId = $first }
    Log ("  {0,-20} ok={1} 结果数={2} 首个 id={3}" -f $s.name, $r.ok, $n, $first)
    $searchResults[$s.name] = [ordered]@{ ok = $r.ok; status = $r.status; count = $n; firstId = $first; raw = $r.text.Substring(0, [Math]::Min(300, $r.text.Length)) }
}
$checks['modrinth_mod_has_results'] = ($searchResults['modrinth-mod'].count -gt 0)
$checks['modrinth_modpack_has_results'] = ($searchResults['modrinth-modpack'].count -gt 0)
$checks['modrinth_resource_has_results'] = ($searchResults['modrinth-resource'].count -gt 0)
$checks['modrinth_shader_has_results'] = ($searchResults['modrinth-shader'].count -gt 0)
$checks['curseforge_without_key_is_deterministic'] = ($searchResults['curseforge-mod'].ok -or $searchResults['curseforge-mod'].status -eq 400)

# ---- 2) 真实安装一个 Mod 到实例
$create = Api $port $token 'POST' '/api/instances' '{"name":"content-test","version":"1.18.2"}'
$instId = ''; if ($create.ok -and $create.body -and ($create.body.instance.PSObject.Properties.Name -contains 'id')) { $instId = [string]$create.body.instance.id }
Log ("  创建实例 id=$instId")
$installOk = $false; $installText = ''; $jarCount = 0; $jarBytes = 0; $gameDir = ''
if ($instId -and $firstModId) {
    $ins = Api $port $token 'POST' '/api/content/install' (@{ category = 'mod'; projectId = $firstModId; instanceId = $instId; source = 'modrinth' }) $InstallTimeoutSec
    $installOk = $ins.ok; $installText = $ins.text
    Log ("  安装 mod '$firstModId' -> ok={0} {1}" -f $ins.ok, $ins.text.Substring(0, [Math]::Min(200, $ins.text.Length)))
    # 安装可能是后台任务 ⇒ 轮询 /api/content/tasks 到没有进行中的任务（第 62 轮）
    $swT = [Diagnostics.Stopwatch]::StartNew()
    while ($swT.Elapsed.TotalSeconds -lt 150) {
        Start-Sleep -Seconds 3
        $tk = Api $port $token 'GET' '/api/content/tasks' $null 60
        $active = 0
        if ($tk.ok -and $tk.body -and ($tk.body.PSObject.Properties.Name -contains 'tasks')) { $active = @($tk.body.tasks).Count }
        if ($active -eq 0 -and $swT.Elapsed.TotalSeconds -gt 6) { break }
    }
    Log ("  内容任务轮询用时=" + [int]$swT.Elapsed.TotalSeconds + "s")
    $st = Api $port $token 'GET' '/api/state' $null
    $inst = @($st.body.instances) | Where-Object { $_.id -eq $instId } | Select-Object -First 1
    if ($inst -and ($inst.PSObject.Properties.Name -contains 'gameDirectory')) { $gameDir = [string]$inst.gameDirectory }
    if ($gameDir) {
        $modsDir = Join-Path $gameDir 'mods'
        if (Test-Path $modsDir) {
            $jars = @(Get-ChildItem $modsDir -File -Filter '*.jar' -EA SilentlyContinue)
            $jarCount = $jars.Count; $jarBytes = [int64](($jars | Measure-Object -Property Length -Sum).Sum)
            foreach ($j in $jars) { Log ("    mods/" + $j.Name + "  " + $j.Length + " B") }
        }
    }
}
$checks['mod_installed_to_mods'] = ($installOk -and $jarCount -ge 1 -and $jarBytes -gt 0)

# ---- 3) 任务列表
$tasks = Api $port $token 'GET' '/api/content/tasks' $null 60
Log ("  /api/content/tasks -> ok={0} {1}" -f $tasks.ok, $tasks.text.Substring(0, [Math]::Min(200, $tasks.text.Length)))
$checks['content_tasks_endpoint_ok'] = $tasks.ok

$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '  '))
Log ('判定: ' + $(if ($fails.Count -eq 0) { 'PASS：内容子系统（搜索/安装/任务）全部符合预期' } else { "FAIL：$($fails.Count) 项不符（" + (($fails | ForEach-Object { $_.Key }) -join ',') + '）' }))
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    searches = $searchResults
    install = [ordered]@{ instanceId = $instId; projectId = $firstModId; ok = $installOk; response = $installText; jarCount = $jarCount; jarBytes = $jarBytes; gameDirectory = $gameDir }
    tasks = $tasks.text
    checks = $checks
    verdict = $(if ($fails.Count -eq 0) { 'PASS' } else { 'FAIL' })
} | ConvertTo-Json -Depth 7 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit $(if ($fails.Count -eq 0) { 0 } else { 1 })
