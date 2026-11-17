using System;
using System.IO;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 构造 <see cref="HostDependencies"/> 的小工厂，供不启动服务、只想拿一个宿主对象的用例使用。
///
/// <para>
/// 为什么不直接用 <see cref="HostDependencies.Production"/>：那会把配置存储指向真实的
/// <c>%APPDATA%\MCMiniLauncher\config.json</c>。测试里任何一次 <c>LoadConfig</c> 都可能写隔离副本，
/// 而"我只是测 listener 能不能绑定端口"不该冒这个险。
/// </para>
/// </summary>
internal static class TestHostDependencies
{
	/// <summary>内存 store + 三个临时目录 + 一组不带代理的出站客户端（用完自己删目录）。</summary>
	public static HostDependencies TempRoots(string? tag = null)
	{
		string prefix = "pineglow-deps-" + (tag ?? "x") + "-" + Guid.NewGuid().ToString("N");
		string recovery = Path.Combine(Path.GetTempPath(), prefix + "-rec");
		string bisect = Path.Combine(Path.GetTempPath(), prefix + "-bis");
		string diagnostics = Path.Combine(Path.GetTempPath(), prefix + "-dia");
		Directory.CreateDirectory(recovery);
		Directory.CreateDirectory(bisect);
		Directory.CreateDirectory(diagnostics);

		return new HostDependencies(
			new RouteTestMemoryConfigStore(new AppConfig()),
			new StorageRoots(recovery, bisect, diagnostics),
			new OutboundHttp());
	}
}
