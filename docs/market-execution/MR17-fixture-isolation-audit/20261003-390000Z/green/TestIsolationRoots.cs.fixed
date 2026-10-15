using System;
using System.Collections.Generic;
using System.IO;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 共享的**测试隔离根**助手（缺口登记表 A21）。
///
/// 背景（第 47–48 轮实测）：仓库里有若干**自建**测试夹具（不走 <see cref="LocalApiTestHarness"/>），
/// 它们大多只重定向了**配置存储**，而把**恢复区 / 二分落盘 / 诊断报告**写进真实
/// <c>%APPDATA%\MCMiniLauncher</c>。第 47 轮已确证其中一个（诊断报告）真的一直在写。
///
/// 用法（每个夹具一次）：
/// <code>
/// _roots = TestIsolationRoots.Install();   // 启动服务之前
/// ...
/// _roots.Dispose();                        // 夹具 DisposeAsync / finally 里
/// </code>
///
/// **为什么不做成静态自注册**：那会把重定向泄漏到**其它**测试类（全局状态），
/// 让"默认诊断根"之类的断言失真。因此它必须由每个夹具**显式持有并释放**。
/// </summary>
internal sealed class TestIsolationRoots : IDisposable
{
	private readonly List<IDisposable> _scopes = new List<IDisposable>();
	private readonly List<string> _roots = new List<string>();
	private bool _disposed;

	private TestIsolationRoots(bool includeDiagnostic = true)
	{
		Add("rec", root => LocalApiService.OverrideRecoveryRootForTest(root));
		Add("bis", root => LocalApiService.OverrideBisectStoreRootForTest(root));
		if (includeDiagnostic)
		{
			Add("dia", root => LocalApiService.OverrideDiagnosticStoreRootForTest(root));
		}
	}

	/// <summary>安装三个根的重定向（恢复区 / 二分落盘 / 诊断报告），并返回可释放句柄。</summary>
	public static TestIsolationRoots Install() => new TestIsolationRoots();

	/// <summary>
	/// 安装到**当前夹具的静态槽位**。用于那些已经写死 `StartAsync` / `DisposeAsync` 结构、
	/// 不想再加字段与构造函数参数的夹具：`StartAsync` 里调 <see cref="InstallCurrent"/>，
	/// `DisposeAsync` 里调 <see cref="ReleaseCurrent"/>。
	///
	/// <paramref name="includeConfigStore"/>：是否**同时**把配置存储也换成内存 store。
	/// 默认 <c>false</c> —— 因为已有夹具（News 两个）会**先**装自己的内存 store，
	/// 若这里再覆盖一次会把它们的配置冲掉。只有那些**完全没有**隔离配置的用例才该传 <c>true</c>。
	///
	/// **为什么静态槽位在这里可以接受**：这些夹具所在的测试类都在
	/// <c>[Collection("LocalApiService")]</c> 里**严格串行**执行，同一时刻只有一个夹具活着；
	/// 而且释放动作写在夹具自己的 `DisposeAsync` / `finally` 里，不会泄漏到别的测试类。
	/// </summary>
	private static TestIsolationRoots? _current;
	private static IDisposable? _currentConfigScope;

	public static void InstallCurrent(bool includeConfigStore = false)
	{
		ReleaseCurrent();
		_current = Install();
		if (includeConfigStore)
		{
			_currentConfigScope = LocalApiService.OverrideConfigStoreForTest(
				new RouteTestMemoryConfigStore(new Pineglow.Launcher.Models.AppConfig()));
		}
	}

	/// <summary>
	/// 只安装**恢复区 + 二分落盘**两个根（不碰诊断报告）。
	///
	/// 用途：某些夹具（`AiCancelAndRedirectTests`）已经**自己**持有诊断根的重定向字段与构造参数，
	/// 若这里再装一次诊断根会造成**重复重定向**、把那个字段的作用域打乱。
	/// 于是给它一个只补 rec/bisect 的入口 —— 补缺口而不干扰既有结构。
	/// </summary>
	public static void InstallRecoveryAndBisectCurrent()
	{
		ReleaseCurrent();
		_current = new TestIsolationRoots(includeDiagnostic: false);
	}

	public static void ReleaseCurrent()
	{
		TestIsolationRoots? current = _current;
		_current = null;
		IDisposable? configScope = _currentConfigScope;
		_currentConfigScope = null;
		configScope?.Dispose();
		current?.Dispose();
	}

	private void Add(string tag, Func<string, IDisposable> install)
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-iso-" + tag + "-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		_roots.Add(root);
		_scopes.Add(install(root));
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		for (int i = _scopes.Count - 1; i >= 0; i--)
		{
			try { _scopes[i].Dispose(); } catch { }
		}

		foreach (string root in _roots)
		{
			try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
		}
	}
}
