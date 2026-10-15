#requires -Version 5.1
# A7 剩余项（v3）：只做两处**短**字面替换。
# 关键性质：AbortLaunch 只删「预约中(Launching=true)」的条目，
# 因此 finally 里可以无条件调用 —— 进程起来后 MarkRunning 已把它升级成 Launching:false，AbortLaunch 是 no-op。
$ErrorActionPreference = 'Stop'
$root = 'D:\松光启动器'
Set-Location -LiteralPath $root

$api = Join-Path $root 'dev\PineglowLauncher\Pineglow.Launcher.Services\LocalApiService.cs'
$a = [IO.File]::ReadAllText($api, (New-Object Text.UTF8Encoding($false)))

# 1) 移除第 42 轮的守卫**代码块**（不含注释，短匹配更稳）
$old = "`t`tif (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))`n" +
       "`t`t{`n" +
       "`t`t`tthrow new InvalidOperationException(``$`"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。`");`n" +
       "`t`t}`n"
if (-not $a.Contains($old)) { throw '守卫代码块未匹配到' }
$a = $a.Replace($old, '')

# 2) 插入预约壳，并把原方法改名为核心
$sig = "`tprivate static async Task HandleLaunchAsync(HttpListenerResponse response, string instanceId)`n`t{`n"
if (-not $a.Contains($sig)) { throw '签名未匹配到' }
$wrapper = @"
`tprivate static async Task HandleLaunchAsync(HttpListenerResponse response, string instanceId)
`t{
`t`t// MR03 收尾之三（A7 剩余项：严格原子性）：
`t`t// 把「检查是否在运行」与「登记启动中占位」合并为一次原子预约（TryBeginLaunch 用 TryAdd）。
`t`t// 修复前是「先 IsRunning 再 MarkRunning」，两次并发请求之间存在窗口，可能都通过检查
`t`t// → 同一实例起两个游戏进程同时写同一份存档。
`t`tif (!RunningInstanceRegistry.TryBeginLaunch(instanceId, out int alreadyRunningPid))
`t`t{
`t`t`tthrow new InvalidOperationException(alreadyRunningPid > 0
`t`t`t`t? `$"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。"
`t`t`t`t: "该实例正在启动中，请勿重复启动。");
`t`t}

`t`ttry
`t`t{
`t`t`tawait LaunchBodyAsync(response, instanceId);
`t`t}
`t`tfinally
`t`t{
`t`t`t// 无条件调用即可：AbortLaunch 只删「预约中」的条目；
`t`t`t// 启动成功后 MarkRunning 已把它升级为真实进程登记（Launching=false），这里是 no-op。
`t`t`t// 而**任何失败路径**（抛异常、提前返回）都会把预约释放掉，不会永久挡住该实例。
`t`t`tRunningInstanceRegistry.AbortLaunch(instanceId);
`t`t}
`t}

`tprivate static async Task LaunchBodyAsync(HttpListenerResponse response, string instanceId)
`t{
"@
$a = $a.Replace($sig, $wrapper)

[IO.File]::WriteAllText($api, $a, (New-Object Text.UTF8Encoding($false)))
Write-Host ('  处理器已改 = ' + ([IO.File]::ReadAllText($api).Contains('LaunchBodyAsync')))
Write-Host ('  仍含 TryBeginLaunch = ' + ([IO.File]::ReadAllText($api).Contains('TryBeginLaunch')))
