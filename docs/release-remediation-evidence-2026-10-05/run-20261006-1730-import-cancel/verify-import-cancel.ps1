#Requires -Version 5.1
<#
.SYNOPSIS
    **整合包导入"取消"与清理**验收（第 58 轮）：
    A) 导入进行到一半时取消 → 取消返回确定性结果、**不留半成品实例/目录**、启动器仍可用；
    B) "取消先到"（tombstone）：先取消一个 operationId，再用同一个 id 发起导入 → 该导入应被直接取消且不建实例。
.DESCRIPTION
    代码事实：`operationId` 由**客户端**生成并随导入 body 传入（`ui/js/api.js:872`），
    取消接口 `POST /api/instances/import/cancel {operationId}`（`LocalApiService.cs:1507`）返回
    `{ok, operationId, cancelled, pendingCancel, message}`；导入失败/取消时会 `Directory.Delete(instance.GameDirectory, recursive:true)`
    清理半成品（`InstancePackService.cs:349`）。
#>
param(
    [Parameter(Mandatory)][string] $ExePath,
    [Parameter(Mandatory)][string] $MrpackPath,
    [Parameter(Mandatory)][string] $HelperScript,
    [Parameter(Mandatory)][string] $JsonPath,
    [string] $WorkRoot,
    [int] $CancelAfterSec = 35,
    [int] $StartupTimeoutSec = 120
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
function Snapshot([string]$gameDir) {
    $inst = @(); $idir = Join-Path $gameDir 'instances'
    if (Test-Path $idir) { $inst = @(Get-ChildItem $idir -Directory -EA SilentlyContinue | ForEach-Object { $_.Name }) }
    $tmp = @(Get-ChildItem ([System.IO.Path]::GetTempPath()) -Directory -Filter 'pineglow-import-*' -EA SilentlyContinue | ForEach-Object { $_.Name })
    return [pscustomobject]@{ instances = $inst; tempUploadDirs = $tmp }
}

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path
$helper = (Resolve-Path -LiteralPath $HelperScript).Path
if (-not $WorkRoot) { $WorkRoot = [System.IO.Path]::GetTempPath() }
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$runDir = Join-Path $WorkRoot ('impcancel-' + [guid]::NewGuid().ToString('N'))
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
$state0 = Api $port $token 'GET' '/api/state' $null
# 注意（第 58 轮踩过，本会话第 5 次同类）：StrictMode 下访问不存在的属性会**直接中断脚本**。
# /api/state 里没有 gameDirectory，所以改为读盘上的 config.json（产品写入的是 PascalCase）。
$cfgPathNow = Join-Path $dataDir 'config.json'
$gameDir = ''
if (Test-Path $cfgPathNow) {
    $cfgNow = [IO.File]::ReadAllText($cfgPathNow, [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($cfgNow.PSObject.Properties.Name -contains 'GameDirectory') { $gameDir = [string]$cfgNow.GameDirectory }
}
# 产品可能还没写过 config.json（懒写），此时用它的默认位置
if ([string]::IsNullOrWhiteSpace($gameDir)) { $gameDir = Join-Path $dataDir '.minecraft' }
Log ("启动器 pid=$($proc.Id) port=$port gameDir=$gameDir")
$before = Snapshot $gameDir
Log ("取消前：instances=[" + ($before.instances -join ',') + "] 临时上传目录=" + $before.tempUploadDirs.Count + " 个")

# ---------------- A) 导入到一半取消
$opA = 'cancel-a-' + [guid]::NewGuid().ToString('N')
$outA = Join-Path $outDir 'import-A-result.json'
$helperLogA = Join-Path $outDir 'helper-A.log'
Log ("A) 发起导入 operationId=$opA（独立进程）")
$hp = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $helper, '-Port', "$port", '-Token', $token, '-MrpackPath', $pack, '-OperationId', $opA, '-OutFile', $outA) -PassThru -WindowStyle Hidden -RedirectStandardOutput $helperLogA -RedirectStandardError (Join-Path $outDir 'helper-A-err.log')
Start-Sleep -Seconds $CancelAfterSec
$mid = Snapshot $gameDir
$midDirBytes = 0
$newMid = @($mid.instances | Where-Object { $before.instances -notcontains $_ })
foreach ($nm in $newMid) { $midDirBytes += [int64]((Get-ChildItem (Join-Path $gameDir ('instances\' + $nm)) -Recurse -File -EA SilentlyContinue | Measure-Object -Property Length -Sum).Sum) }
Log ("  取消时（{0}s）：新增实例目录=[" -f $CancelAfterSec) 
Log ("     " + ($newMid -join ',') + "] 已落盘=" + [math]::Round($midDirBytes / 1MB, 1) + " MB")
$cancelA = Api $port $token 'POST' '/api/instances/import/cancel' ('{"operationId":"' + $opA + '"}') 60
Log ("  取消响应: " + $cancelA.text)
# 等辅助进程结束（导入调用会返回）
$hp.WaitForExit(180000) | Out-Null
Start-Sleep -Seconds 5
$resultA = $null; if (Test-Path $outA) { $resultA = [IO.File]::ReadAllText($outA, [Text.Encoding]::UTF8) | ConvertFrom-Json }
if ($resultA) { Log ("  导入调用返回：status=" + $resultA.status + " 用时=" + $resultA.elapsedSec + "s 响应=" + ([string]$resultA.response).Substring(0, [Math]::Min(160, ([string]$resultA.response).Length))) }
$after = Snapshot $gameDir
Log ("取消后：instances=[" + ($after.instances -join ',') + "] 临时上传目录=" + $after.tempUploadDirs.Count + " 个")
$leftoverInstances = @($after.instances | Where-Object { $before.instances -notcontains $_ })
$leftoverBytes = 0
foreach ($nm in $leftoverInstances) { $leftoverBytes += [int64]((Get-ChildItem (Join-Path $gameDir ('instances\' + $nm)) -Recurse -File -EA SilentlyContinue | Measure-Object -Property Length -Sum).Sum) }
Log ("  残留新增实例=" + $leftoverInstances.Count + " 个，共 " + [math]::Round($leftoverBytes / 1MB, 1) + " MB")
$stateA = Api $port $token 'GET' '/api/state' $null
$instanceCountUnchanged = (@($stateA.body.instances).Count -eq @($state0.body.instances).Count)
Log ("  取消后 /api/state 实例数=" + @($stateA.body.instances).Count + "（原 " + @($state0.body.instances).Count + "）")

# ---------------- B) "取消先到"（tombstone）
$opB = 'cancel-b-' + [guid]::NewGuid().ToString('N')
$pre = Api $port $token 'POST' '/api/instances/import/cancel' ('{"operationId":"' + $opB + '"}') 60
Log ("B) 先取消（尚无在途操作）operationId=$opB → " + $pre.text)
$outB = Join-Path $outDir 'import-B-result.json'
$helperLogB = Join-Path $outDir 'helper-B.log'
$hp2 = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $helper, '-Port', "$port", '-Token', $token, '-MrpackPath', $pack, '-OperationId', $opB, '-OutFile', $outB) -PassThru -WindowStyle Hidden -RedirectStandardOutput $helperLogB -RedirectStandardError (Join-Path $outDir 'helper-B-err.log')
$hp2.WaitForExit(180000) | Out-Null
Start-Sleep -Seconds 3
$resultB = $null; if (Test-Path $outB) { $resultB = [IO.File]::ReadAllText($outB, [Text.Encoding]::UTF8) | ConvertFrom-Json }
if ($resultB) { Log ("  导入调用返回：status=" + $resultB.status + " 用时=" + $resultB.elapsedSec + "s 响应=" + ([string]$resultB.response).Substring(0, [Math]::Min(200, ([string]$resultB.response).Length))) }
$afterB = Snapshot $gameDir
$leftoverB = @($afterB.instances | Where-Object { $before.instances -notcontains $_ })
Log ("  B 之后新增实例=" + $leftoverB.Count + " 个")

$alive = Api $port $token 'GET' '/api/health' $null
$verdicts = [ordered]@{
    A_cancelAccepted = ($cancelA.ok -and $cancelA.body.cancelled -eq $true)
    A_importAborted = ($resultB -ne $null -and $resultA -ne $null -and $resultA.status -ne 200)
    A_noLeftoverInstance = ($leftoverInstances.Count -eq 0)
    A_stateCountUnchanged = $instanceCountUnchanged
    B_pendingCancelRecorded = ($pre.ok -and $pre.body.pendingCancel -eq $true)
    B_importCancelled = ($resultB -ne $null -and $resultB.status -ne 200)
    B_noInstance = ($leftoverB.Count -eq 0)
    launcherStillHealthy = $alive.ok
}
$pass = -not (@($verdicts.Values) -contains $false)
Log ("逐项: " + (($verdicts.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '  '))
Log ('判定: ' + $(if ($pass) { 'PASS：取消有确定性返回、无半成品残留、tombstone 生效、启动器仍可用' } else { 'FAIL：见逐项' }))
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    gameDirectory = $gameDir
    cancelAfterSec = $CancelAfterSec
    phaseA = [ordered]@{
        operationId = $opA
        newInstanceDirsAtCancel = $newMid
        bytesAtCancel = $midDirBytes
        cancelResponse = $cancelA.text
        importCallResult = $(if ($resultA) { $resultA } else { $null })
        leftoverInstanceDirs = $leftoverInstances
        leftoverBytes = $leftoverBytes
        tempUploadDirsBefore = $before.tempUploadDirs.Count
        tempUploadDirsAfter = $after.tempUploadDirs.Count
    }
    phaseB = [ordered]@{
        operationId = $opB
        preCancelResponse = $pre.text
        importCallResult = $(if ($resultB) { $resultB } else { $null })
        leftoverInstanceDirs = $leftoverB
    }
    checks = $verdicts
    verdict = $(if ($pass) { 'PASS' } else { 'FAIL' })
} | ConvertTo-Json -Depth 8 | Set-Content -Path $JsonPath -Encoding utf8
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
exit $(if ($pass) { 0 } else { 1 })
