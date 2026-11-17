using System;
using System.Diagnostics;
using System.Net;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 本地 API 的**宿主状态**：listener、端口、UI 根目录、运行标志。
///
/// 这是架构评审候选 ③（LocalApiService 实例化）的第 1 步：先把"宿主状态"从
/// <c>LocalApiService</c> 的静态字段里搬出来，装进一个对象。行为不变——
/// <c>LocalApiService</c> 目前只持有**一个** host（<c>_host</c>），对外 API 全部原样委托。
///
/// 第 2 步才会把依赖（config store / 三个 HttpClient / 三个根目录）从这里变成构造参数；
/// 那一步做完，测试才能 `new LocalApiHost(...)` 起第二个独立服务。
/// 计划与分类见 <c>docs/architecture/local-api-instancing-2026-11-17.md</c>。
/// </summary>
internal sealed class LocalApiHost
{
	private readonly object _gate = new object();

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
