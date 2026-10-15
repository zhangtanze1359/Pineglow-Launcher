#Requires -Version 5.1
<#
.SYNOPSIS
    辅助脚本：**在一个独立进程里**发起一次真实整合包导入（`POST /api/instances/import` 是同步长调用），
    把结果写到文件，供主脚本同时去做"取消"。
#>
param(
    [Parameter(Mandatory)][string] $Port,
    [Parameter(Mandatory)][string] $Token,
    [Parameter(Mandatory)][string] $MrpackPath,
    [Parameter(Mandatory)][string] $OperationId,
    [Parameter(Mandatory)][string] $OutFile,
    [int] $TimeoutSec = 1200
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$sw = [Diagnostics.Stopwatch]::StartNew()
$pack = (Resolve-Path -LiteralPath $MrpackPath).Path
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pack))
$body = @{ fileName = (Split-Path $pack -Leaf); fileBase64 = $b64; operationId = $OperationId } | ConvertTo-Json -Compress
$status = 0; $text = ''
try {
    $r = Invoke-RestMethod "http://127.0.0.1:$Port/api/instances/import" -Method Post -Headers @{ 'X-Pineglow-Token' = $Token } `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec $TimeoutSec
    $status = 200; $text = ($r | ConvertTo-Json -Compress -Depth 5)
} catch {
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    try { $text = [string]$_.ErrorDetails.Message } catch { }
    if (-not $text) { $text = $_.Exception.Message }
}
$sw.Stop()
@{
    operationId = $OperationId
    status = $status
    elapsedSec = [int]$sw.Elapsed.TotalSeconds
    response = $text
} | ConvertTo-Json -Depth 5 | Set-Content -Path $OutFile -Encoding utf8
exit 0
