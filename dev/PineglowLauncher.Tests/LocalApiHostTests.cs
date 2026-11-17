using System;
using System.Net;
using System.Net.Sockets;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 候选 ③ 第 1 步的接口测试：宿主状态（端口 / UI 根 / 运行标志 / listener）已经是一个对象，
/// 不再是 <c>LocalApiService</c> 的静态字段。
///
/// 这一层现在能**脱离 LocalApiService 单独测**：绑定、重复停止、端口被占的失败路径
/// 都不需要起整个 API，也不需要夹具。
/// </summary>
[Collection("LocalApiService")]
public sealed class LocalApiHostTests
{
	private static int FreePort()
	{
		using TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		int port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}

	[Fact]
	public void Start_BindsThePortAndReportsTheUiRoot()
	{
		LocalApiHost host = new LocalApiHost(TestHostDependencies.TempRoots());
		int port = FreePort();

		host.Start(port, @"C:\ui-root");
		try
		{
			Assert.True(host.IsRunning);
			Assert.Equal(port, host.Port);
			Assert.Equal(@"C:\ui-root", host.UiRoot);
			Assert.NotNull(host.Listener);
		}
		finally
		{
			host.Stop();
		}

		Assert.False(host.IsRunning);
		Assert.Null(host.Listener);
		// 端口与 UI 根是"最后一次绑定过的值"，停止后仍可读（旧行为：Port 不变）
		Assert.Equal(port, host.Port);
	}

	[Fact]
	public void Stop_IsIdempotent()
	{
		LocalApiHost host = new LocalApiHost(TestHostDependencies.TempRoots());
		host.Stop();          // 从未启动
		host.Start(FreePort(), "ui");
		host.Stop();
		host.Stop();          // 重复停止
		Assert.False(host.IsRunning);
	}

	[Fact]
	public void Start_OnAnOccupiedPort_ThrowsHttpListenerException()
	{
		// harness 的"换端口重试"就是靠这个失败信号工作的 —— 这里把前提钉住。
		using TcpListener occupant = new TcpListener(IPAddress.Loopback, 0);
		occupant.Start();
		int port = ((IPEndPoint)occupant.LocalEndpoint).Port;

		LocalApiHost host = new LocalApiHost(TestHostDependencies.TempRoots());
		try
		{
			Assert.Throws<HttpListenerException>(() => host.Start(port, "ui"));
			Assert.False(host.IsRunning);
		}
		finally
		{
			occupant.Stop();
			host.Stop();
		}
	}
}
