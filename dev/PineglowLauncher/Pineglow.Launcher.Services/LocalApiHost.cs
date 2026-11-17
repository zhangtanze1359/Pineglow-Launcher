using System;
using System.Diagnostics;
using System.Net;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 本地 API 的**宿主对象**：依赖 + listener + 端口 + UI 根 + 运行标志。
///
/// <para>
/// 架构评审候选 ③ 的两步走都在这里：第 1 步把宿主状态（listener / 端口 / UI 根 / 运行标志）
/// 从 <c>LocalApiService</c> 的静态字段搬进来；第 2 步把依赖
/// （<see cref="IConfigStore"/> / <see cref="StorageRoots"/> / <see cref="OutboundHttp"/>）
/// 变成构造参数，一起装进 <see cref="HostDependencies"/>。
/// </para>
/// <para>
/// <b>现在的实际收益</b>：<c>LocalApiService</c> 不再持有任何一个可以被"半改"的静态依赖字段，
/// 依赖只有一处默认值（<see cref="HostDependencies.Production"/>）、一处替换点
/// （<c>LocalApiService.OverrideHostDependenciesForTest</c>），
/// 测试想描述"我只换存储"就必须同时说清另外两块是什么。
/// </para>
/// <para>
/// <b>还不是什么</b>：仍然只有一个 host 能同时运行（<c>LocalApiService</c> 的静态成员被全库调用），
/// 所以它还没变成"多实例"。那一步（第 3 步）要连同域注册表与源码断言一起退役，
/// 见 <c>docs/architecture/local-api-instancing-2026-11-17.md</c>。
/// </para>
/// </summary>
internal sealed class LocalApiHost
{
	private readonly object _gate = new object();

	public LocalApiHost(HostDependencies dependencies)
	{
		Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
	}

	/// <summary>本宿主使用的依赖（配置存储 / 三个存储根 / 出站客户端）。</summary>
	public HostDependencies Dependencies { get; private set; }

	/// <summary>
	/// 替换依赖（<b>仅限测试</b>，且必须未运行）。
	///
	/// <para>
	/// 为什么“运行中不允许换”：换依赖等于换了 config store / 存储根 / 出站客户端，
	/// 而正在处理的请求已经拿到了旧的那一份——那会造出“一半旧一半新”的保存现象。
	/// 旧写法（四个独立静态 override）根本没挡这件事，只是没人试过。
	/// </para>
	/// </summary>
	internal void ReplaceDependencies(HostDependencies dependencies)
	{
		if (dependencies == null) throw new ArgumentNullException(nameof(dependencies));
		lock (_gate)
		{
			if (IsRunning)
			{
				throw new InvalidOperationException(
					"不能在运行中替换宿主依赖（config store / 存储根 / 出站客户端）；请先 Stop。");
			}

			Dependencies = dependencies;
		}
	}

	public int Port { get; private set; } = 48123;

	public string UiRoot { get; private set; } = string.Empty;

	public HttpListener? Listener { get; private set; }

	public bool IsRunning { get; private set; }

	/// <summary>绑定端口并开始监听。<paramref name="uiRoot"/> 在启动时解析一次并固化（与旧行为一致）。</summary>
	/// <exception cref="HttpListenerException">端口被占用时抛出，调用方（测试 harness）据此换端口重试。</exception>
	public void Start(int port, string uiRoot)
	{
		lock (_gate)
		{
			if (IsRunning)
			{
				return;
			}

			HttpListener listener = new HttpListener();
			listener.Prefixes.Add($"http://127.0.0.1:{port}/");
			listener.Start();

			Listener = listener;
			Port = port;
			UiRoot = uiRoot;
			IsRunning = true;
		}
	}

	/// <summary>停止并释放 listener。可重复调用；不抛。</summary>
	public void Stop()
	{
		lock (_gate)
		{
			if (!IsRunning)
			{
				return;
			}

			IsRunning = false;
			try
			{
				Listener?.Stop();
				Listener?.Close();
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[LocalApiHost] stop error: " + ex.Message);
			}

			Listener = null;
		}
	}
}
