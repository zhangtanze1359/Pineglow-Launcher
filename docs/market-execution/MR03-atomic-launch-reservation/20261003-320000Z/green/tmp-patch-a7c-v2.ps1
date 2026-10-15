#requires -Version 5.1
# A7 剩余项（v2）：纯字面替换，避免索引运算吃掉不该删的行。
$ErrorActionPreference = 'Stop'
$root = 'D:\松光启动器'
Set-Location -LiteralPath $root

$ev = 'C:\Users\Administrator\.codex\worktrees\ed41\松光启动器\docs\market-execution'
$run = Join-Path $ev 'MR03-atomic-launch-reservation\20261003-320000Z'
New-Item -ItemType Directory -Force -Path (Join-Path $run 'green'), (Join-Path $run 'red') | Out-Null

$reg = Join-Path $root 'dev\PineglowLauncher\Pineglow.Launcher.Services\RunningInstanceRegistry.cs'
$api = Join-Path $root 'dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs'

# 0) 先还原到"本轮之前"（= 第 42 轮已修好守卫的状态），再干净地打补丁
Copy-Item (Join-Path $run 'red\RunningInstanceRegistry.cs.before') $reg -Force
Copy-Item (Join-Path $run 'red\LocalApiService.cs.before') $api -Force
Write-Host '  已还原到本轮之前的状态'

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

$anchor = "`t`ttry`n`t`t{`n`t`t`tif (entry.Process != null)"
if (-not $r.Contains($anchor)) { throw 'IsRunning 锚点未找到' }
$repl = "`t`t// 预约中（已占位但进程还没起来）：同样视为占用，否则第二次启动会挤进来。`n" +
        "`t`tif (entry.Launching)`n`t`t{`n`t`t`tpid = 0;`n`t`t`treturn true;`n`t`t}`n`n" + $anchor
$r = $r.Replace($anchor, $repl)

$tail = 'public static bool IsRunning(string instanceId) => IsRunning(instanceId, out _);'
if (-not $r.Contains($tail)) { throw '尾部锚点未找到' }
$members = @"
`t/// <summary>
`t/// 原子预约：把「检查该实例是否在运行」与「登记一个启动中占位」合并成一次原子操作。
`t/// 原先 HandleLaunchAsync 是「先 IsRunning 再 MarkRunning」，两次并发请求之间存在窗口，
`t/// 可能都通过检查 → 同一实例起两个游戏进程同时写同一份存档。现在只有一个请求能 TryAdd 成功。
`t/// 返回 false 时 existingPid 给出占用者 PID（0 表示正在启动中、还没有 PID）。
`t/// 调用方必须在启动失败时调用 AbortLaunch，否则该实例会被永久挡住。
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

`t`t// TryAdd 是原子的：只有当前没有条目才写入成功
`t`tif (!Running.TryAdd(instanceId, new Entry(0, DateTimeOffset.Now, null, Launching: true)))
`t`t{
`t`t`tIsRunning(instanceId, out existingPid);
`t`t`treturn false;
`t`t}

`t`treturn true;
`t}

`t/// <summary>
`t/// 启动失败时释放预约。只释放「预约中」的条目：若已升级成真实进程登记，则不动它。
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
$r = $r.Replace($tail, $members + $tail)
Save-NoBom $reg $r
Write-Host ('  注册表已改 = ' + ([IO.File]::ReadAllText($reg).Contains('TryBeginLaunch')))

# ---------- 2) 处理器：字面替换 ----------
$a = [IO.File]::ReadAllText($api, (New-Object Text.UTF8Encoding($false)))

# 2a) 移除第 42 轮的守卫块（逐字匹配）
$oldGuard = "`t`t// MR03 收尾之二（缺口登记表 A7）：同一实例的**并发启动守卫**。`n" +
            "`t`t// 修复前这里从不检查运行态，``MarkRunning`` 会`"以最后一次为准`"覆盖注册表 ——`n" +
            "`t`t// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），`n" +
            "`t`t// 而删除守卫此后只跟踪最新的那个 PID。`n" +
            "`t`t// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。`n" +
            "`t`tif (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))`n" +
            "`t`t{`n" +
            "`t`t`tthrow new InvalidOperationException(``$`"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。`");`n" +
            "`t`t}`n`n"
if (-not $a.Contains($oldGuard)) { throw '第 42 轮守卫块未逐字匹配到' }
$a = $a.Replace($oldGuard, '')

# 2b) 插入"预约壳"并把原方法改名为核心
$sig = "`tprivate static async Task HandleLaunchAsync(HttpListenerResponse response, string instanceId)`n`t{`n"
if (-not $a.Contains($sig)) { throw '签名未找到' }
$wrapper = @"
`tprivate static async Task HandleLaunchAsync(HttpListenerResponse response, string instanceId)
`t{
`t`t// MR03 收尾之三（A7 剩余项：严格原子性）：
`t`t// 把「检查是否在运行」与「登记启动中占位」合并为一次原子预约。
`t`t// 修复前是「先 IsRunning 再 MarkRunning」，两次并发请求之间存在窗口，可能都通过检查。
`t`tif (!RunningInstanceRegistry.TryBeginLaunch(instanceId, out int alreadyRunningPid))
`t`t{
`t`t`tthrow new InvalidOperationException(alreadyRunningPid > 0
`t`t`t`t? `$"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。"
`t`t`t`t: "该实例正在启动中，请勿重复启动。");
`t`t}

`t`t// 无论抛异常还是提前返回，只要进程没真正起来，就必须释放预约，
`t`t// 否则该实例会被永久挡住（这比漏挡更糟）。
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
$a = $a.Replace($sig, $wrapper)

# 2c) 进程起来后通知外壳
$mark = 'RunningInstanceRegistry.MarkRunning(instanceId, process);'
$mi = $a.IndexOf($mark)
if ($mi -lt 0) { throw 'MarkRunning 未找到' }
$a = $a.Insert($mi + $mark.Length, "`n`t`tonProcessStarted(); // 预约已升级为真实进程登记，不要再释放")

Save-NoBom $api $a
Write-Host ('  处理器已改 = ' + ([IO.File]::ReadAllText($api).Contains('LaunchCoreAsync')))
