param(
  [Parameter(Mandatory)][string]$Root,
  [Parameter(Mandatory)][string]$OutDir
)
# Round-30: add the regression test that pins defect #11 (sync-over-async deadlock on a thread that
# owns a SynchronizationContext). Kept separate from apply-deadlock-fix.ps1 so the test can be run
# against an UNPATCHED tree first (red) and a PATCHED tree second (green).
$ErrorActionPreference = 'Stop'
[void](New-Item -ItemType Directory -Path $OutDir -Force)

$selfBytes = [IO.File]::ReadAllBytes($PSCommandPath)
if (-not ($selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
  throw "本脚本缺少 UTF-8 BOM：PowerShell 5.1 会按 GBK 读取它，脚本里的中文锚点会静默失配。请先补回 BOM 再运行。"
}

$T = Join-Path $Root 'dev\PineglowLauncher.Tests'
$testFile = Join-Path $T 'SyncOverAsyncDeadlockTests.cs'

$src = @'
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 回归测试：在**拥有 SynchronizationContext 的线程**上同步等待 async 会死锁。
///
/// Avalonia UI 线程就是这种线程。<c>ProductionConfigStore.Load()</c> 用
/// <c>ConfigStore.LoadAsync().GetAwaiter().GetResult()</c> 阻塞等待；如果那条 async 链捕获了上下文，
/// 它的 continuation 会被投回**正在阻塞等待的同一个线程**——谁也动不了。
///
/// 本测试不依赖 Avalonia，而是复刻同样的形状：一个上下文，它自己的线程在阻塞等待的同时
/// 手动泵送被投递的回调。修复前该测试会超时失败（而不是把整个测试进程挂死）。
/// </summary>
[Collection("LocalApiService")]
public sealed class SyncOverAsyncDeadlockTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "pineglow-deadlock-" + Guid.NewGuid().ToString("N"));

	public SyncOverAsyncDeadlockTests()
	{
		Directory.CreateDirectory(_root);
		string configPath = Path.Combine(_root, "config.json");
		ConfigStore.ConfigPathOverrideForTest = configPath;
		// 必须先让配置文件存在：LoadAsync 在文件不存在时会走一条**纯同步**的早返回分支
		// （生成默认配置后直接 return），那样根本不会走到本测试关心的那个 await。
		// 第一版测试漏了这一步，于是 RED 也跑成了 PASS——一个测不出东西的"回归测试"。
		File.WriteAllText(configPath, "{}");
	}

	public void Dispose()
	{
		ConfigStore.ConfigPathOverrideForTest = null;
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch
		{
			// 临时目录清理失败不影响断言
		}
	}

	/// <summary>
	/// 模拟一个**正在阻塞、因此永远不会处理自己消息队列**的 UI 线程上下文。
	/// 关键点：Post 只记账、**不执行**。修复前，async 链的 continuation 会被投到这里再也出不来，
	/// 于是阻塞等待的那次调用永不返回——正是真实 UI 线程上发生的事。
	/// （我第一版测试自作聪明地"泵送"了队列，结果把死锁解开了，RED 跑成了 PASS。）
	/// </summary>
	private sealed class BlockedUiContext : SynchronizationContext
	{
		private int _posted;

		public int PostedCount => Volatile.Read(ref _posted);

		public override void Post(SendOrPostCallback d, object? state) => Interlocked.Increment(ref _posted);

		public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("本测试上下文不实现同步 Send");
	}

	[Fact]
	public void BlockingOnConfigLoad_FromAContextOwningThread_MustNotDeadlock()
	{
		BlockedUiContext context = new BlockedUiContext();
		AppConfig? loaded = null;
		Exception? failure = null;

		Thread worker = new Thread(new ThreadStart(delegate
		{
			SynchronizationContext.SetSynchronizationContext(context);
			try
			{
				loaded = ConfigStore.LoadAsync().GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				failure = ex;
			}
		}))
		{
			IsBackground = true
		};
		worker.Start();

		// 故意**不**泵送上下文：真实 UI 线程在阻塞等待时也不会处理自己的队列。
		DateTime deadline = DateTime.UtcNow.AddSeconds(30);
		while (worker.IsAlive && DateTime.UtcNow < deadline)
		{
			Thread.Sleep(20);
		}

		Assert.True(context.PostedCount == 0,
			$"async 链把 continuation 投回了调用者所在的 SynchronizationContext（Post 次数 = {context.PostedCount}）——"
			+ "这正是 UI 线程死锁的成因；每个 await 都必须 ConfigureAwait(false)。");
		Assert.False(worker.IsAlive,
			"ConfigStore.LoadAsync() 在拥有 SynchronizationContext 的线程上同步等待时死锁了："
			+ "async 链必须不捕获上下文（每个 await 都要 ConfigureAwait(false)）。");
		Assert.Null(failure);
		Assert.NotNull(loaded);
	}
}
'@

[IO.File]::WriteAllText($testFile, $src, (New-Object Text.UTF8Encoding($false)))
Write-Output ("[OK] wrote {0}" -f $testFile.Substring($Root.Length + 1))
Write-Output "P-TEST-SYNC-OVER-ASYNC added"
