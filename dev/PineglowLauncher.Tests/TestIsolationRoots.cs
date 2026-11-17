using System;
using System.Collections.Generic;
using System.IO;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 共享的**测试隔离根**助手（缺口登记表 A21）。
///
/// 背景（第 47–48 轮实测）：仓库里有若干**自建**测试夹具（不走 <see cref="LocalApiTestHarness"/>），
/// 它们大多只重定向了**配置存储**，而把**恢复区 / 二分落盘 / 诊断报告**写进真实
/// <c>%APPDATA%\MCMiniLauncher</c>。第 47 轮已确证其中一个（诊断报告）真的一直在写。
///
/// 候选 ③ 第 2 步之后，三个根 + config store 是<b>一个</b>依赖值（<see cref="HostDependencies"/>），
/// 所以这里只需要一次替换，而不是四次。缺一个根就往真实目录写的隐患从结构上消失了。
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

	/// <summary>本次为三个存储根分配的临时目录（夹具据此清理）。</summary>
	public string Recovery { get; }

	public string Bisect { get; }

	public string Diagnostics { get; }

	private bool _disposed;

	private TestIsolationRoots(bool includeDiagnostic = true)
	{
		Recovery = NewRoot("rec");
		Bisect = NewRoot("bis");
		Diagnostics = includeDiagnostic ? NewRoot("dia") : string.Empty;
	}

	/// <summary>安装依赖替换（配置存储保持当前值；三个根指向临时目录），并返回可释放句柄。</summary>
	public static TestIsolationRoots Install() => new TestIsolationRoots();

	/// <summary>
	/// 安装到**当前夹具的静态槽位**。用于那些已经写死 `StartAsync` / `DisposeAsync` 结构、
	/// 不想再加字段与构造函数参数的夹具。返回值与该槽位**同一个**作用域（重复 Dispose 安全），
	/// 所以既有的 <c>_configScope.Dispose()</c> 照样成立。
	///
	/// <paramref name="store"/>：夹具自己的内存 store（需要对它做断言时传）。
	/// <paramref name="includeConfigStore"/>：夹具**完全没有**隔离配置时传 <c>true</c>，
	/// 这里给它一个空的内存 store；否则保持调用方已经装好的那一份。
	///
	/// **为什么静态槽位在这里可以接受**：这些夹具所在的测试类都在
	/// <c>[Collection("LocalApiService")]</c> 里**严格串行**执行，同一时刻只有一个夹具活着；
	/// 而且释放动作写在夹具自己的 `DisposeAsync` / `finally` 里，不会泄漏到别的测试类。
	/// </summary>
	private static TestIsolationRoots? _current;

	public static IDisposable InstallCurrent(IConfigStore? store = null, bool includeConfigStore = false)
	{
		ReleaseCurrent();

		TestIsolationRoots installed = new TestIsolationRoots();
		StorageRoots roots = new StorageRoots(installed.Recovery, installed.Bisect, installed.Diagnostics);

		// 只换三个根 + （可选的）store：其余依赖保持调用方当前的份。
		IConfigStore? target = store ?? (includeConfigStore ? new RouteTestMemoryConfigStore(new AppConfig()) : null);
		IDisposable scope = LocalApiService.OverrideHostDependenciesForTest(dependencies =>
			target == null ? dependencies.WithRoots(roots) : dependencies.WithRoots(roots).WithStore(target));
		installed._scopes.Add(scope);
		_current = installed;
		return scope;
	}

	/// <summary>
	/// 只把**恢复区 + 二分落盘**换成临时目录，诊断根保持当前值。
	///
	/// 用途：某个夹具想自己数真实诊断目录的文件数做守卫时（`AiCancelAndRedirectTests` 就是），
	/// 它要自己控制诊断根；这个入口保证另外两个根不会漏掉。
	/// </summary>
	public static void InstallRecoveryAndBisectCurrent()
	{
		ReleaseCurrent();

		string recovery = NewRoot("rec");
		string bisect = NewRoot("bis");
		// 诊断根沿用当前值 —— 变形函数的输入就是"当前依赖"，天然做到这一点。
		TestIsolationRoots installed = new TestIsolationRoots(includeDiagnostic: false);
		installed._scopes.Add(LocalApiService.OverrideHostDependenciesForTest(dependencies =>
			dependencies.WithRoots(new StorageRoots(recovery, bisect, dependencies.Roots.Diagnostics))));
		_current = installed;
	}

	public static void ReleaseCurrent()
	{
		TestIsolationRoots? current = _current;
		_current = null;
		current?.Dispose();
	}

	private static string NewRoot(string tag)
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-iso-" + tag + "-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		return root;
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

		foreach (string root in new[] { Recovery, Bisect, Diagnostics })
		{
			try { if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) Directory.Delete(root, true); } catch { }
		}
	}
}
