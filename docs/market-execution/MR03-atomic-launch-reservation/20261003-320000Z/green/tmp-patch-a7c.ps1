#requires -Version 5.1
# A7 剩余项：把"检查+登记"改为原子预约（注册表 + 处理器）。一次性补丁脚本。
$ErrorActionPreference = 'Stop'
$root = 'D:\松光启动器'
Set-Location -LiteralPath $root

$ev = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution'
$run = Join-Path $ev 'MR03-atomic-launch-reservation\20261003-320000Z'
New-Item -ItemType Directory -Force -Path (Join-Path $run 'green'), (Join-Path $run 'red') | Out-Null

$reg = Join-Path $root 'dev\PineglowLauncher\Pineglow.Launcher.Services\RunningInstanceRegistry.cs'
$api = Join-Path $root 'dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs'
Copy-Item $reg (Join-Path $run 'red\RunningInstanceRegistry.cs.before') -Force
Copy-Item $api (Join-Path $run 'red\LocalApiService.cs.before') -Force

function Save-NoBom([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
}

# ---------- 1) 注册表 ----------
$r = [IO.File]::ReadAllText($reg, (New-Object Text.UTF8Encoding($false)))
$r = $r.Replace(
    'private sealed record Entry(int Pid, DateTimeOffset StartedAt, Process? Process);',
    'private sealed record Entry(int Pid, DateTimeOffset StartedAt, Process? Process, bool Launching);')
$r = $r.Replace(
    'Running[instanceId] = new Entry(process.Id, DateTimeOffset.Now, process);',
    'Running[instanceId] = new Entry(process.Id, DateTimeOffset.Now, process, Launching: false);')
$r = $r.Replace(
    'Running[instanceId] = new Entry(pid, DateTimeOffset.Now, null);',
    'Running[instanceId] = new Entry(pid, DateTimeOffset.Now, null, Launching: false);')

$a1 = "`t`ttry`n`t`t{`n`t`t`tif (entry.Process != null)"
$i1 = "`t`t// 预约中（已占位但进程还没起来）：同样视为占用，否则第二次启动会挤进来。`n`t`tif (entry.Launching)`n`t`t{`n`t`t`tpid = 0;`n`t`t`treturn true;`n`t`t}`n`n" + $a1
if (-not $r.Contains($a1)) { throw 'IsRunning 锚点未找到' }
$r = $r.Replace($a1, $i1)

$tail = 'public static bool IsRunning(string instanceId) => IsRunning(instanceId, out _);'
$members = @"
`t/// <summary>
`t/// **原子预约**：把「检查该实例是否在运行」与「登记一个启动中占位」合并成一次原子操作。
`t///
`t/// 为什么需要它（缺口登记表 A7 的剩余项）：原先 HandleLaunchAsync 是
`t/// 「先 IsRunning 再 MarkRunning」，两次并发请求之间存在窗口，可能**都**通过检查
`t/// → 同一实例起两个游戏进程同时写同一份存档。现在只有一个请求能 TryAdd 成功。
`t///
`t/// 返回 false 时 existingPid 给出占用者的 PID（0 表示「正在启动中、还没有 PID」）。
`t/// **调用方必须在启动失败时调用 AbortLaunch**，否则该实例会被永久挡住。
`t/// </summary>
`tpublic static bool TryBeginLaunch(string instanceId, out int existingPid)
`t{
`t`texistingPid = 0;
`t`tif (string.IsNullOrWhiteSpace(instanceId))
`t`t{
`t`t`treturn false;
`t`t}

`t`t// 先自愈：进程已退出时 IsRunning 会顺手清掉条目，避免陈旧条目永久挡住启动
`t`tif (IsRunning(instanceId, out existingPid))
`t`t{
`t`t`treturn false;
`t`t}

`t`t// TryAdd 是原子的：只有「当前没有条目」才写入成功
`t`tif (!Running.TryAdd(instanceId, new Entry(0, DateTimeOffset.Now, null, Launching: true)))
`t`t{
`t`t`tIsRunning(instanceId, out existingPid);
`t`t`treturn false;
`t`t}

`t`treturn true;
`t}

`t/// <summary>
`t/// 启动失败时释放预约。**只释放「预约中」的条目**：若已经升级成真实进程登记，则不动它。
`t/// </summary>
`tpublic static void AbortLaunch(string instanceId)
`t{
`t`tif (string.IsNullOrWhiteSpace(instanceId))
`t`t{
`t`t`treturn;
`t`t}

`t`tif (Running.TryGetValue(instanceId, out Entry? entry) && entry.Launching)
`t`t{
`t`t`tRunning.TryRemove(instanceId, out _);
`t`t}
`t}

"@
if (-not $r.Contains($tail)) { throw '尾部锚点未找到' }
$r = $r.Replace($tail, $members + $tail)
Save-NoBom $reg $r
Write-Host ('  注册表已改 = ' + ([IO.File]::ReadAllText($reg).Contains('TryBeginLaunch')))

# ---------- 2) 处理器：预约壳 + 核心 ----------
$a = [IO.File]::ReadAllText($api, (New-Object Text.UTF8Encoding($false)))

$guardStart = 'if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))'
$gi = $a.IndexOf($guardStart)
if ($gi -lt 0) { throw '旧守卫未找到' }
# 往前吃掉注释块
$lineStart = $a.LastIndexOf("`n", $gi)
while ($true) {
    $prevStart = $a.LastIndexOf("`n", $lineStart - 1)
    if ($prevStart -lt 0) { break }
    $prevLine = $a.Substring($prevStart + 1, $lineStart - $prevStart - 1).Trim()
    if ($prevLine.StartsWith('//')) { $lineStart = $prevStart } else { break }
}
# 往后吃掉整个 if 块
$ge = $a.IndexOf('}', $a.IndexOf('{', $gi))
if ($ge -lt 0) { throw '旧守卫块结束未找到' }
$ge = $a.IndexOf("`n", $ge) + 1
# 再吃掉紧随其后的空行
while ($a.Substring($ge, 1) -eq "`n" -or $a.Substring($ge, 2) -eq "`r`n") {
    if ($a.Substring($ge, 2) -eq "`r`n") { $ge += 2 } else { $ge += 1 }
}
$a = $a.Remove($lineStart + 1, $ge - $lineStart - 1)

$sig = 'private static async Task HandleLaunchAsync(HttpListenerResponse response, string instanceId)'
$si = $a.IndexOf($sig)
if ($si -lt 0) { throw '签名未找到' }
$wrapper = @"
`tprivate static async Task HandleLaunchAsync(HttpListenerResponse response, string instanceId)
`t{
`t`t// MR03 收尾之三（A7 剩余项：**严格原子性**）：
`t`t// 把「检查是否在运行」与「登记启动中占位」合并为一次原子预约。
`t`t// 修复前是「先 IsRunning 再 MarkRunning」，两次并发请求之间存在窗口，可能都通过检查。
`t`tif (!RunningInstanceRegistry.TryBeginLaunch(instanceId, out int alreadyRunningPid))
`t`t{
`t`t`tthrow new InvalidOperationException(alreadyRunningPid > 0
`t`t`t`t? `$"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。"
`t`t`t`t: "该实例正在启动中，请勿重复启动。");
`t`t}

`t`t// 无论**抛异常**还是**提前返回**，只要进程没真正起来，就必须释放预约，
`t`t// 否则该实例会被永久挡住（这比「漏挡」更糟）。
`t`tbool upgraded = false;
`t`ttry
`t`t{
`t`t`tawait LaunchCoreAsync(response, instanceId, () => upgraded = true);
`t`t}
`t`tfinally
`t`t{
`t`t`tif (!upgraded)
`t`t`t{
`t`t`t`tRunningInstanceRegistry.AbortLaunch(instanceId);
`t`t`t}
`t`t}
`t}

`tprivate static async Task LaunchCoreAsync(HttpListenerResponse response, string instanceId, Action onProcessStarted)
`t{
"@
$a = $a.Remove($si, $sig.Length).Insert($si, $wrapper.TrimEnd())

$mark = 'RunningInstanceRegistry.MarkRunning(instanceId, process);'
$mi = $a.IndexOf($mark)
if ($mi -lt 0) { throw 'MarkRunning 未找到' }
$miEnd = $mi + $mark.Length
$a = $a.Insert($miEnd, "`n`t`tonProcessStarted(); // 通知外壳：预约已升级为真实进程登记，不要再释放")

Save-NoBom $api $a
Write-Host ('  处理器已改 = ' + ([IO.File]::ReadAllText($api).Contains('LaunchCoreAsync')))
