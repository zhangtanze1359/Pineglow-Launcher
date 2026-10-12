using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR05 更新接线的**剩余部分**（缺口登记表 A1 的余项）：`InstallWithDependenciesAsync` 的依赖递归安装。
///
/// 为什么值得测：这条路径有三处容易出错的地方 ——
///   1. **递归**：依赖还有依赖（`InstallRecursiveAsync`）；
///   2. **去重**：`visited` 按 `source:versionId` 去重（菱形依赖下同一个库只能装一次）；
///   3. **已装跳过**：`installedProjects` 来自当前实例已有的在线元数据。
/// 它同样会**写文件**，属于 MG1（数据安全）的范围。
///
/// 全部用例**不发外网**：用按 URL 路由的假 HttpMessageHandler
/// （`/v2/version/{id}` 回版本 JSON；下载 URL 回 jar 字节）。
/// </summary>
public class ModContentDependencyInstallTests
{
	private readonly ITestOutputHelper _out;

	public ModContentDependencyInstallTests(ITestOutputHelper output) => _out = output;

	/* ===================== 夹具 ===================== */

	/// <summary>按 URL 路由的假处理器：API 路径回 JSON，其它路径回 jar 字节。</summary>
	private sealed class RoutingHandler : HttpMessageHandler
	{
		private readonly Dictionary<string, string> _versionsByUrl = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, byte[]> _bytesByUrl = new(StringComparer.OrdinalIgnoreCase);
		public List<string> Requested { get; } = new();

		public void AddVersion(string versionId, string json) =>
			_versionsByUrl["https://api.modrinth.com/v2/version/" + versionId] = json;

		public void AddBytes(string url, byte[] payload) => _bytesByUrl[url] = payload;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri!.AbsoluteUri;
			lock (Requested) { Requested.Add(url); }

			if (_versionsByUrl.TryGetValue(url, out string? json))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					RequestMessage = request,
					Content = new StringContent(json, Encoding.UTF8, "application/json")
				});
			}

			if (_bytesByUrl.TryGetValue(url, out byte[]? payload))
			{
				var response = new HttpResponseMessage(HttpStatusCode.OK)
				{
					RequestMessage = request,
					Content = new ByteArrayContent(payload)
				};
				response.Content.Headers.ContentLength = payload.Length;
				return Task.FromResult(response);
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
		}
	}

	/// <summary>构造一个 Modrinth 版本 JSON（含可选依赖声明）。</summary>
	private static string VersionJson(string projectId, string versionId, string fileName, string downloadUrl, byte[] payload,
		params (string ProjectId, string VersionId, string Type)[] dependencies)
	{
		string sha1 = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();
		var deps = dependencies.Select(d => new
		{
			project_id = d.ProjectId,
			version_id = d.VersionId,
			dependency_type = d.Type
		}).ToArray();

		return JsonSerializer.Serialize(new
		{
			id = versionId,
			project_id = projectId,
			name = projectId + " " + versionId,
			version_number = "1.0",
			version_type = "release",
			files = new[]
			{
				new
				{
					primary = true,
					filename = fileName,
					url = downloadUrl,
					size = payload.Length,
					hashes = new { sha1 }
				}
			},
			game_versions = new[] { "1.20.4" },
			loaders = new[] { "fabric" },
			dependencies = deps
		});
	}

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string ModsDir { get; }
		public GameInstance Instance { get; }
		public ModService ModService { get; }

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-a1d-" + Guid.NewGuid().ToString("N"));
			ModsDir = Path.Combine(Root, "mods");
			Directory.CreateDirectory(ModsDir);
			Instance = new GameInstance { Id = "inst-a1d", Name = "a1d", GameDirectory = Root, MinecraftVersion = "1.20.4" };
			ModService = new ModService(Root);
		}

		public List<string> ModEntries() =>
			Directory.EnumerateFiles(ModsDir).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
				|| f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static ModVersion MainVersion(string projectId, string versionId, string fileName, string downloadUrl, byte[] payload,
		params (string ProjectId, string VersionId, string Type)[] dependencies)
	{
		var version = new ModVersion
		{
			Source = ModSource.Modrinth,
			ProjectId = projectId,
			VersionId = versionId,
			VersionNumber = "1.0",
			FileName = fileName,
			DownloadUrl = downloadUrl,
			FileSize = payload.Length,
			Sha1 = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant()
		};
		foreach (var d in dependencies)
		{
			version.Dependencies.Add(new ModDependency
			{
				Source = ModSource.Modrinth,
				ProjectId = d.ProjectId,
				VersionId = d.VersionId,
				Required = d.Type.Equals("required", StringComparison.OrdinalIgnoreCase)
			});
		}

		return version;
	}

	/* ===================== 1. 必需依赖被递归安装 ===================== */

	[Fact]
	public async Task InstallWithDependencies_InstallsRequiredDependencyRecursively()
	{
		using var fx = new Fixture();
		byte[] mainBytes = Encoding.UTF8.GetBytes("MAIN");
		byte[] depBytes = Encoding.UTF8.GetBytes("DEP");

		var handler = new RoutingHandler();
		handler.AddBytes("https://example.invalid/main.jar", mainBytes);
		handler.AddBytes("https://example.invalid/dep.jar", depBytes);
		handler.AddVersion("depv1", VersionJson("dep", "depv1", "dep.jar", "https://example.invalid/dep.jar", depBytes));

		var content = new ModContentService(new HttpClient(handler));
		ModInstallResult result = await content.InstallWithDependenciesAsync(
			fx.Instance,
			MainVersion("main", "mainv1", "main.jar", "https://example.invalid/main.jar", mainBytes, ("dep", "depv1", "required")),
			fx.ModService, "1.20.4", "fabric");

		_out.WriteLine("[A1d] 安装文件：" + string.Join(" | ", result.InstalledFiles.Select(Path.GetFileName)));
		Assert.True(result.InstalledFiles.Count == 2, $"应安装主 Mod + 1 个必需依赖，实际 {result.InstalledFiles.Count}");
		Assert.True(result.DependencyCount == 1, $"DependencyCount 应为 1，实际 {result.DependencyCount}");
		Assert.True(result.MainFilePath.EndsWith("main.jar", StringComparison.OrdinalIgnoreCase), "MainFilePath 应指向主 Mod");
		Assert.True(fx.ModEntries().Count == 2, "磁盘上应有 2 个 jar");
	}

	/* ===================== 2. 可选依赖不装 ===================== */

	[Fact]
	public async Task InstallWithDependencies_SkipsOptionalDependencies()
	{
		using var fx = new Fixture();
		byte[] mainBytes = Encoding.UTF8.GetBytes("MAIN-OPT");
		var handler = new RoutingHandler();
		handler.AddBytes("https://example.invalid/main.jar", mainBytes);

		var content = new ModContentService(new HttpClient(handler));
		ModInstallResult result = await content.InstallWithDependenciesAsync(
			fx.Instance,
			MainVersion("main", "mainv1", "main.jar", "https://example.invalid/main.jar", mainBytes, ("optional", "optv1", "optional")),
			fx.ModService, "1.20.4", "fabric");

		Assert.True(result.InstalledFiles.Count == 1, $"可选依赖不应被安装，实际装了 {result.InstalledFiles.Count} 个");
		// 而且不应该为可选依赖发任何 API 请求
		Assert.DoesNotContain(handler.Requested, u => u.Contains("/version/optv1", StringComparison.OrdinalIgnoreCase));
	}

	/* ===================== 3. 已装的依赖被跳过 ===================== */

	[Fact]
	public async Task InstallWithDependencies_SkipsAlreadyInstalledDependency()
	{
		using var fx = new Fixture();
		byte[] mainBytes = Encoding.UTF8.GetBytes("MAIN-2");
		byte[] depBytes = Encoding.UTF8.GetBytes("DEP-ALREADY");

		// 预置：依赖已经装好（且带在线元数据）
		string installed = Path.Combine(fx.ModsDir, "dep.jar");
		File.WriteAllBytes(installed, depBytes);
		fx.ModService.WriteOnlineMetadata(installed, new ModVersion
		{
			Source = ModSource.Modrinth,
			ProjectId = "dep",
			VersionId = "depv1",
			FileName = "dep.jar"
		});

		var handler = new RoutingHandler();
		handler.AddBytes("https://example.invalid/main.jar", mainBytes);
		handler.AddBytes("https://example.invalid/dep.jar", depBytes);
		handler.AddVersion("depv1", VersionJson("dep", "depv1", "dep.jar", "https://example.invalid/dep.jar", depBytes));

		var content = new ModContentService(new HttpClient(handler));
		ModInstallResult result = await content.InstallWithDependenciesAsync(
			fx.Instance,
			MainVersion("main", "mainv1", "main.jar", "https://example.invalid/main.jar", mainBytes, ("dep", "depv1", "required")),
			fx.ModService, "1.20.4", "fabric");

		Assert.True(result.InstalledFiles.Count == 1, $"已装的依赖应被跳过，实际装了 {result.InstalledFiles.Count} 个");
		Assert.DoesNotContain(handler.Requested, u => u.Contains("/version/depv1", StringComparison.OrdinalIgnoreCase));
		Assert.True(fx.ModEntries().Count == 2, "磁盘上仍是主 Mod + 原有依赖");
	}

	/* ===================== 4. 菱形依赖：共享库只装一次 ===================== */

	[Fact]
	public async Task InstallWithDependencies_DiamondGraph_InstallsSharedDependencyOnce()
	{
		using var fx = new Fixture();
		byte[] mainBytes = Encoding.UTF8.GetBytes("MAIN-D");
		byte[] bBytes = Encoding.UTF8.GetBytes("B");
		byte[] cBytes = Encoding.UTF8.GetBytes("C");
		byte[] dBytes = Encoding.UTF8.GetBytes("SHARED-D");

		var handler = new RoutingHandler();
		handler.AddBytes("https://example.invalid/main.jar", mainBytes);
		handler.AddBytes("https://example.invalid/b.jar", bBytes);
		handler.AddBytes("https://example.invalid/c.jar", cBytes);
		handler.AddBytes("https://example.invalid/d.jar", dBytes);
		// main → B, main → C（都必需）；B → D, C → D（同一个库，必需）
		handler.AddVersion("bv1", VersionJson("b", "bv1", "b.jar", "https://example.invalid/b.jar", bBytes, ("d", "dv1", "required")));
		handler.AddVersion("cv1", VersionJson("c", "cv1", "c.jar", "https://example.invalid/c.jar", cBytes, ("d", "dv1", "required")));
		handler.AddVersion("dv1", VersionJson("d", "dv1", "d.jar", "https://example.invalid/d.jar", dBytes));

		var content = new ModContentService(new HttpClient(handler));
		ModInstallResult result = await content.InstallWithDependenciesAsync(
			fx.Instance,
			MainVersion("main", "mainv1", "main.jar", "https://example.invalid/main.jar", mainBytes,
				("b", "bv1", "required"), ("c", "cv1", "required")),
			fx.ModService, "1.20.4", "fabric");

		_out.WriteLine("[A1d] 菱形安装：" + string.Join(" | ", result.InstalledFiles.Select(Path.GetFileName)));
		Assert.True(result.InstalledFiles.Count == 4, $"应为 main+B+C+D 共 4 个，实际 {result.InstalledFiles.Count}");
		Assert.True(result.InstalledFiles.Count(f => f.EndsWith("d.jar", StringComparison.OrdinalIgnoreCase)) == 1,
			"共享库 D 只能安装一次（visited 去重）");
		// D 的版本 JSON 只应被请求一次
		Assert.True(handler.Requested.Count(u => u.Contains("/version/dv1", StringComparison.OrdinalIgnoreCase)) == 1,
			"共享库 D 的版本查询应只发生一次");
	}

	/* ===================== 5. 必需依赖解析不了 → 明确报错 ===================== */

	[Fact]
	public async Task InstallWithDependencies_UnresolvableRequiredDependency_ThrowsClearly()
	{
		using var fx = new Fixture();
		byte[] mainBytes = Encoding.UTF8.GetBytes("MAIN-X");
		var handler = new RoutingHandler();
		handler.AddBytes("https://example.invalid/main.jar", mainBytes);

		var content = new ModContentService(new HttpClient(handler));
		// 必需依赖既没有 ProjectId 也没有 VersionId → 无法解析
		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			content.InstallWithDependenciesAsync(
				fx.Instance,
				MainVersion("main", "mainv1", "main.jar", "https://example.invalid/main.jar", mainBytes, (string.Empty, string.Empty, "required")),
				fx.ModService, "1.20.4", "fabric"));

		_out.WriteLine("[A1d] 报错原文：" + ex.Message);
		Assert.Contains("无法找到必需依赖", ex.Message, StringComparison.Ordinal);
	}

	/* ===================== 6. 循环依赖：必须终止（visited 真正防的场景） ===================== */

	/// <summary>
	/// 为什么单列这一条：**菱形依赖测不到 `visited`**。
	/// 菱形的去重其实由"依赖层"的 `!installedProjects.Contains(item2)` 保证
	/// （D 装完后进了 `installedProjects`，C 再引用 D 时在解析之前就被跳过）。
	/// `visited` 真正防的是**循环**：A→B→A 时，A 尚未安装（主 Mod 在依赖之后才装），
	/// 所以 `installedProjects` 拦不住；只有 `visited` 能让递归停下来。
	/// 本用例断言：**必须终止**，且 A、B 各只装一次。
	/// </summary>
	[Fact]
	public async Task InstallWithDependencies_CyclicGraph_TerminatesAndInstallsEachOnce()
	{
		using var fx = new Fixture();
		byte[] aBytes = Encoding.UTF8.GetBytes("A-CYCLE");
		byte[] bBytes = Encoding.UTF8.GetBytes("B-CYCLE");

		var handler = new RoutingHandler();
		handler.AddBytes("https://example.invalid/a.jar", aBytes);
		handler.AddBytes("https://example.invalid/b.jar", bBytes);
		// A(主) → B(必需)；B → A(必需)  ← 环
		handler.AddVersion("bv1", VersionJson("b", "bv1", "b.jar", "https://example.invalid/b.jar", bBytes, ("a", "av1", "required")));
		handler.AddVersion("av1", VersionJson("a", "av1", "a.jar", "https://example.invalid/a.jar", aBytes, ("b", "bv1", "required")));

		var content = new ModContentService(new HttpClient(handler));

		// 若 visited 失效，这里会无限递归（最终 StackOverflow 杀掉测试进程）。
		// 给一个宽松但有限的上限：正常应当在毫秒级完成。
		Task<ModInstallResult> task = content.InstallWithDependenciesAsync(
			fx.Instance,
			MainVersion("a", "av1", "a.jar", "https://example.invalid/a.jar", aBytes, ("b", "bv1", "required")),
			fx.ModService, "1.20.4", "fabric");
		Task done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
		Assert.True(done == task, "循环依赖必须在有限时间内终止（visited 去重失效会无限递归）");

		ModInstallResult result = await task;
		_out.WriteLine("[A1d] 循环安装：" + string.Join(" | ", result.InstalledFiles.Select(Path.GetFileName)));
		Assert.True(result.InstalledFiles.Count == 2, $"A 与 B 各装一次，共 2 个，实际 {result.InstalledFiles.Count}");
		Assert.True(result.InstalledFiles.Count(f => f.EndsWith("a.jar", StringComparison.OrdinalIgnoreCase)) == 1, "A 只能装一次");
		Assert.True(result.InstalledFiles.Count(f => f.EndsWith("b.jar", StringComparison.OrdinalIgnoreCase)) == 1, "B 只能装一次");
	}
}