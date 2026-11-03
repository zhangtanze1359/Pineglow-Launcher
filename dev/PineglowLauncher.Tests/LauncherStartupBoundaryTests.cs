using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR21 补测（缺口登记表 A12）：**启动/环境边界的健壮性** —— 端口冲突、UI 根目录缺失、端口覆盖一致性。
///
/// MR21 报告 §六 原话："目录不可写 / 端口冲突 | 未在本包重新测量"、"WebView2 缺失时的降级文案 | 未观测"。
///
/// 本类覆盖**本机可测**的那部分：
///   ① 端口被占用时 `Start` 的行为，以及**失败后服务必须没有半启动状态**；
///   ② 失败后在空闲端口上必须能**重新启动成功**；
///   ③ `PINEGLOW_UI_DIR` 指向不存在的目录时仍能启动（回退是设计意图）；
///   ④ **静态守卫**：外壳窗口必须使用 `LocalApiService.Port`，**不得硬编码端口**
///      —— 因为 `PINEGLOW_API_PORT` 是受支持的覆盖机制，硬编码会让界面**静默降级**。
///
/// **未覆盖**：真实 WebView2 缺失路径（本机已安装）、真实桌面会话下的渲染。
/// </summary>
[Collection("LocalApiService")]
public class LauncherStartupBoundaryTests : IDisposable
{
	private readonly ITestOutputHelper _out;
	private readonly List<IDisposable> _isolation = new List<IDisposable>();

	public LauncherStartupBoundaryTests(ITestOutputHelper output)
	{
		_out = output;

		// A21（第 48 轮清点发现）：本类**直接** Start 服务，而修复前**一个根都没隔离** ——
		// 配置读取会落到真实 %APPDATA%\MCMiniLauncher\config.json。
		// xUnit 每个用例都会新建一次实例，所以在构造函数里装、在 Dispose 里卸，覆盖本类全部用例。
		_isolation.Add(LocalApiService.OverrideConfigStoreForTest(
			new RouteTestMemoryConfigStore(new Pineglow.Launcher.Models.AppConfig())));
		_isolation.Add(LocalApiService.OverrideRecoveryRootForTest(NewTempRoot("rec")));
		_isolation.Add(LocalApiService.OverrideBisectStoreRootForTest(NewTempRoot("bis")));
		_isolation.Add(LocalApiService.OverrideDiagnosticStoreRootForTest(NewTempRoot("dia")));
	}

	private static string NewTempRoot(string tag)
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-boundary-" + tag + "-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		return root;
	}

	public void Dispose()
	{
		for (int i = _isolation.Count - 1; i >= 0; i--)
		{
			try { _isolation[i].Dispose(); } catch { }
		}
	}

	/// <summary>占住一个端口，返回 (监听器, 端口)。</summary>
	private static (TcpListener Listener, int Port) OccupyLoopbackPort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		return (listener, port);
	}

	private static int FreeLoopbackPort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	/* ===================== ① 端口被占用：必须抛出，且不留半启动状态 ===================== */

	[Fact]
	public void Start_OnBusyPort_Throws_AndLeavesTheServiceNotRunning()
	{
		Assert.False(LocalApiService.IsRunning, "前置：测试开始时服务不应在运行");

		(TcpListener listener, int port) = OccupyLoopbackPort();
		try
		{
			Exception? thrown = null;
			try
			{
				LocalApiService.Start(port);
			}
			catch (Exception ex)
			{
				thrown = ex;
			}

			_out.WriteLine($"[A12] 占用端口 {port} 上 Start 的结果：" + (thrown == null ? "未抛出（异常！）" : thrown.GetType().Name + " / " + thrown.Message));

			Assert.NotNull(thrown);
			Assert.False(LocalApiService.IsRunning,
				"启动失败后 `IsRunning` 必须为 false —— 否则调用方会以为服务在跑，后续请求全部失败且难以定位");
		}
		finally
		{
			listener.Stop();
			try { LocalApiService.Stop(); } catch { }
		}
	}

	/* ===================== ② 失败后必须能在空闲端口重新启动 ===================== */

	[Fact]
	public async Task Start_AfterBusyPortFailure_SucceedsOnAFreePort()
	{
		(TcpListener listener, int busyPort) = OccupyLoopbackPort();
		try
		{
			try { LocalApiService.Start(busyPort); } catch { /* 预期失败 */ }
			Assert.False(LocalApiService.IsRunning, "前置：忙碌端口上的启动应失败");
		}
		finally
		{
			listener.Stop();
		}

		int freePort = FreeLoopbackPort();
		LocalApiService.Start(freePort);
		try
		{
			Assert.True(LocalApiService.IsRunning, "失败一次之后必须仍能在空闲端口启动（不得残留脏状态）");
			Assert.True(LocalApiService.Port == freePort, $"Port 应为 {freePort}，实际 {LocalApiService.Port}");

			// 真的能应答（不只是标志位为真）
			using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			using System.Net.Http.HttpResponseMessage res = await http.GetAsync($"http://127.0.0.1:{freePort}/api/health");
			_out.WriteLine($"[A12] 重新启动后 /api/health -> {(int)res.StatusCode}");
			Assert.True(res.IsSuccessStatusCode, "重新启动后健康检查必须成功");
		}
		finally
		{
			LocalApiService.Stop();
		}
	}

	/* ===================== ③ UI 根目录不存在时仍能启动（回退是设计意图） ===================== */

	[Fact]
	public void Start_WithNonExistentUiDir_StillStarts()
	{
		string? original = Environment.GetEnvironmentVariable("PINEGLOW_UI_DIR");
		int port = FreeLoopbackPort();
		try
		{
			Environment.SetEnvironmentVariable("PINEGLOW_UI_DIR", Path.Combine(Path.GetTempPath(), "pineglow-does-not-exist-" + Guid.NewGuid().ToString("N")));
			LocalApiService.Start(port);
			Assert.True(LocalApiService.IsRunning,
				"PINEGLOW_UI_DIR 指向不存在的目录时，服务仍应启动（ResolveUiRoot 会回退到可执行目录旁的 UI/）");
			_out.WriteLine($"[A12] UI 目录不存在时仍启动成功，端口 {port}");
		}
		finally
		{
			Environment.SetEnvironmentVariable("PINEGLOW_UI_DIR", original);
			try { LocalApiService.Stop(); } catch { }
		}
	}

	/* ===================== ④ 静态守卫：外壳窗口不得硬编码端口 ===================== */

	private static string ShellWindowPath
	{
		get
		{
			string dir = AppContext.BaseDirectory;
			for (int i = 0; i < 8 && dir != null; i++)
			{
				string candidate = Path.Combine(dir, "dev", "PineglowLauncher", "Pineglow.Launcher.Views", "Shell", "WebShellWindow.axaml.cs");
				if (File.Exists(candidate))
				{
					return candidate;
				}

				dir = Path.GetDirectoryName(dir);
			}

			throw new FileNotFoundException("找不到 WebShellWindow.axaml.cs");
		}
	}

	[Fact]
	public void ShellWindow_MustUseTheConfiguredPort_NotAHardCodedOne()
	{
		string text = File.ReadAllText(ShellWindowPath);

		// `PINEGLOW_API_PORT` 是受支持的覆盖机制（LocalApiService.cs:169）。
		// 外壳窗口若硬编码端口，覆盖端口后健康检查会探错端口 → 走进 catch →
		// **静默**加载静态 UI/index.html，界面看起来"能用"但完全没有 API。
		var hardCoded = text.Split('\n')
			.Where(l => l.Contains("127.0.0.1:48123", StringComparison.Ordinal))
			.ToList();

		Assert.True(hardCoded.Count == 0,
			"外壳窗口**不得硬编码端口**（应为 LocalApiService.Port）：\n  " + string.Join("\n  ", hardCoded.Select(l => l.Trim())));

		Assert.True(text.Contains("LocalApiService.Port", StringComparison.Ordinal),
			"外壳窗口必须从 LocalApiService.Port 取端口（否则端口覆盖机制在界面上失效）");
		_out.WriteLine("[A12] 外壳窗口使用 LocalApiService.Port，未硬编码端口");
	}
}
