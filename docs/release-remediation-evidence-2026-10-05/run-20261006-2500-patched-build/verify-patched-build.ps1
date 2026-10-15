#Requires -Version 5.1
<#
.SYNOPSIS
    **用打过补丁的构建跑三个 blocker 的复现测试**（第 74 轮）。
.DESCRIPTION
    B0：启动打过补丁的构建（便携）→ 连续三次 `/api/state` 的默认实例 id 是否**稳定**；`/launch` 是否不再是"实例不存在"。
    B1/B2：编译一个引用**打过补丁的 DLL** 的探针，反射调用 `GetInstallerUrl` 并对该 URL 发请求；再用 `VersionResolver.ResolveAsync`
        验证"加载器 profile 的 arguments 与父版本合并"（合并后应含 `-Djava.library.path=${natives_directory}`）。
#>
param(
    [Parameter(Mandatory)][string] $PatchedRoot,
    [Parameter(Mandatory)][string] $ProbeSource,
    [Parameter(Mandatory)][string] $JsonPath,
    [int] $StartupTimeoutSec = 120
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
$outDir = Split-Path -Parent $JsonPath
$logPath = Join-Path $outDir 'verify-patched-step.log'
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
        return [pscustomobject]@{ ok = $true; status = 200; body = $r; text = ($r | ConvertTo-Json -Compress -Depth 5) }
    } catch {
        $code = 0; $detail = ''
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        try { $detail = [string]$_.ErrorDetails.Message } catch { }
        if (-not $detail) { $detail = $_.Exception.Message }
        return [pscustomobject]@{ ok = $false; status = $code; body = $null; text = $detail }
    }
}
function Prop($obj, [string]$name) { if ($null -eq $obj) { return $null }; foreach ($p in $obj.PSObject.Properties) { if ($p.Name -ieq $name) { return $p.Value } }; return $null }

$root = (Resolve-Path -LiteralPath $PatchedRoot).Path
$exe = Join-Path $root 'dev\PineglowLauncher\bin\Release\net10.0\Pineglow.Launcher.exe'
$outDirName = Split-Path $exe -Parent
if (-not (Test-Path $exe)) { Log '❌ 找不到打过补丁的构建产物'; exit 2 }
Log ("打过补丁的构建: " + $exe + "  " + (Get-Item $exe).Length + " B")

# ---------- B0：默认实例 id 是否稳定
$port = Get-FreePort
$env:PINEGLOW_API_PORT = "$port"
$proc = Start-Process -FilePath $exe -WorkingDirectory $outDirName -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $outDirName 'p-stdout.log') -RedirectStandardError (Join-Path $outDirName 'p-stderr.log')
$token = $null; $deadline = (Get-Date).AddSeconds($StartupTimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try { $r = Invoke-WebRequest "http://127.0.0.1:$port/api/health" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $h = (Invoke-WebRequest "http://127.0.0.1:$port/" -UseBasicParsing -TimeoutSec 10).Content; if ($h -match 'name="pineglow-session-token"\s+content="([0-9A-Fa-f]+)"') { $token = $Matches[1] }; break } } catch { }
}
if (-not $token) { Log '❌ 打过补丁的构建没起来'; exit 2 }
Log ("启动器 pid=" + $proc.Id + " apiPort=" + $port)
$ids = @()
for ($i = 1; $i -le 3; $i++) {
    $st = Api $port $token 'GET' '/api/state' $null 60
    $arr = @(Prop $st.body 'instances')
    $id = ''
    if ($arr.Count -gt 0) { $id = [string](Prop $arr[0] 'id') }
    $ids += $id
    Log ("  第 $i 次 /api/state 默认实例 id = " + $id)
}
$stable = (($ids[0] -ne '') -and ($ids[0] -eq $ids[1]) -and ($ids[1] -eq $ids[2]))
$launch = Api $port $token 'POST' ("/api/instances/" + $ids[0] + "/launch") '{}' 60
$notMissing = -not ($launch.text -match '实例不存在')
Log ("  /launch -> status=" + $launch.status + " " + $launch.text.Substring(0, [Math]::Min(180, $launch.text.Length)))
Log ("  B0 判定：id 稳定=" + $stable + " ；launch 不再报'实例不存在'=" + $notMissing)
try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }

# ---------- B1/B2：探针（引用打过补丁的 DLL）
$probeDir = Join-Path $env:TEMP ('pg-probe-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $probeDir -Force)
Copy-Item -LiteralPath $ProbeSource -Destination (Join-Path $probeDir 'Program.cs') -Force
$csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>pgprobe</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Pineglow.Launcher"><HintPath>$outDirName\Pineglow.Launcher.dll</HintPath></Reference>
  </ItemGroup>
</Project>
"@
[IO.File]::WriteAllText((Join-Path $probeDir 'pgprobe.csproj'), $csproj, (New-Object Text.UTF8Encoding($false)))
Push-Location $probeDir
$b = & dotnet build -c Release --nologo 2>&1; $bcode = $LASTEXITCODE
Pop-Location
Log ("探针 build exit=" + $bcode)
if ($bcode -ne 0) { ($b | Select-Object -Last 8) | ForEach-Object { Log ("  " + $_) } }
$probeDll = Join-Path $probeDir 'bin\Release\net10.0\pgprobe.dll'
$probeOut = ''
if (Test-Path $probeDll) {
    Copy-Item (Join-Path $probeDir 'bin\Release\net10.0\*') $outDirName -Force -Recurse -EA SilentlyContinue
    $gameDirForB1 = Join-Path $probeDir 'game'
    Push-Location $outDirName
    $probeOut = (& dotnet exec (Join-Path $outDirName 'pgprobe.dll') $gameDirForB1 2>&1) -join "`n"
    Pop-Location
    $probeOut -split "`n" | ForEach-Object { Log ("  " + $_) }
    @($probeOut) | Set-Content (Join-Path $outDir 'probe-output.txt') -Encoding utf8
}
$b2ok = ($probeOut -match '\[B2\] 段数检查：''/forge/forge/'' 出现=False') -and ($probeOut -match '\[B2\] HEAD/GET 状态 = 200')
$b1ok = ($probeOut -match '\[B1\] 含 -Djava\.library\.path=True') -and ($probeOut -match '\[B1\] 含 Fabric 自己的条目=True')

$checks = [ordered]@{
    B0_ids_stable = $stable
    B0_launch_no_longer_missing = $notMissing
    B1_merged_keeps_library_path = $b1ok
    B2_single_forge_segment = $b2ok
}
$fails = @($checks.GetEnumerator() | Where-Object { -not $_.Value })
Log ("逐项: " + (($checks.GetEnumerator() | ForEach-Object { $_.Key.ToString() + "=" + $_.Value.ToString() }) -join '  '))
$verdictText = 'PASS：打过补丁的构建里，B0（id 稳定 + 不再报实例不存在）、B1（合并保留库路径）、B2（URL 单段且 200）全部成立'
if ($fails.Count -gt 0) {
    $fk = ($fails | ForEach-Object { $_.Key.ToString() }) -join ','
    $verdictText = 'FAIL/需解释：' + $fails.Count + ' 项（' + $fk + '）'
}
Log ('判定: ' + $verdictText)
[ordered]@{
    generatedLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz')
    patchedExe = $exe
    b0 = [ordered]@{ ids = $ids; stable = $stable; launchStatus = $launch.status; launchResponse = $launch.text }
    probeOutput = $probeOut
    checks = $checks
    verdict = $verdictText
} | ConvertTo-Json -Depth 6 | Set-Content -Path $JsonPath -Encoding utf8
if ($fails.Count -eq 0) { exit 0 } else { exit 1 }
