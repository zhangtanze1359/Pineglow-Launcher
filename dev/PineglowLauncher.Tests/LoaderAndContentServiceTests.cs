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
/// 零覆盖服务类的补齐：FabricService / QuiltService / ResourceContentService /
/// ModrinthModpackService / AuthlibInjectorService / NewsContentStateText。
///
/// 为什么要专门写这一组：覆盖率实测显示这 6 个类**一个测试都没提到过**（0/10 ~ 0/48 行）。
/// 它们的共同点是"只需要一个假的 HttpClient 就能测"——构造函数不碰磁盘、不弹 GUI、
/// 不依赖真实网络，因此没测没有任何技术上的借口，纯粹是漏了。
///
/// 这组测试刻意**不碰真实网络**：所有请求都走 <see cref="RoutingFakeHandler"/>，
/// 断言的是"发出去的请求长什么样"和"拿到某种响应时怎么解读"，而不是"服务端今天返回什么"。
/// 后者属于联网验收（scripts/api-smoke.cjs 那一层），放进单元测试只会变成偶发失败。
///
/// 断言口径尽量钉在**会静默出错**的地方：
///   * URL 拼接（少一段路径 / 漏转义 → 404 或打到别的资源）；
///   * JSON 字段缺失时的降级（读崩 vs 退化到空值）；
///   * 下载后的哈希校验（不校验 = 装进一个被篡改的包）；
///   * 临时目录清理（不清理 = 磁盘一点点被吃光）。
/// </summary>
public class LoaderAndContentServiceTests
{
	private readonly ITestOutputHelper _out;

	public LoaderAndContentServiceTests(ITestOutputHelper output) => _out = output;

	/* ============================ 假 HTTP 层 ============================ */

	/// <summary>
	/// 按 URL 片段路由的假处理器。没有匹配到路由时返回 404 并把"未预期请求"记下来，
	/// 这样断言失败时能直接看到实际打的是哪个地址，而不是只看到一句 NullReferenceException。
	/// </summary>
	private sealed class RoutingFakeHandler : HttpMessageHandler
	{
		private readonly List<(string Url, string Method, string? Body)> _requests = new();
		private readonly List<(Func<string, bool> Match, Func<HttpResponseMessage> Respond)> _routes = new();

		public IReadOnlyList<(string Url, string Method, string? Body)> Requests => _requests;

		public RoutingFakeHandler On(Func<string, bool> match, Func<HttpResponseMessage> respond)
		{
			_routes.Add((match, respond));
			return this;
		}

		public RoutingFakeHandler OnContains(string fragment, string body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json")
			=> On(url => url.Contains(fragment, StringComparison.OrdinalIgnoreCase),
				  () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) });

		public RoutingFakeHandler OnContainsBytes(string fragment, byte[] bytes, HttpStatusCode status = HttpStatusCode.OK)
			=> On(url => url.Contains(fragment, StringComparison.OrdinalIgnoreCase),
				  () => new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) });

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri?.ToString() ?? string.Empty;
			string body = string.Empty;
			if (request.Content != null)
			{
				body = await request.Content.ReadAsStringAsync(cancellationToken);
			}

			_requests.Add((url, request.Method.Method, body));

			foreach ((Func<string, bool> match, Func<HttpResponseMessage> respond) in _routes)
			{
				if (match(url))
				{
					return respond();
				}
			}

			return new HttpResponseMessage(HttpStatusCode.NotFound)
			{
				Content = new StringContent("no route for " + url, Encoding.UTF8, "text/plain"),
			};
		}
	}

	private static HttpClient Client(RoutingFakeHandler handler) => new HttpClient(handler, disposeHandler: false);

	private static MinecraftDownloader Downloader(HttpClient http) => new MinecraftDownloader(http, _ => { });

	/// <summary>测试期间用的临时目录；Dispose 时整棵删掉。</summary>
	private sealed class TempDir : IDisposable
	{
		public string Path { get; }

		public TempDir(string label = "pg")
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), label + "-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

		public void Dispose()
		{
			try
			{
				if (Directory.Exists(Path))
				{
					Directory.Delete(Path, recursive: true);
				}
			}
			catch
			{
				// 清理失败不影响断言结论
			}
		}
	}

	/* ============================ FabricService ============================ */

	[Fact]
	public async Task Fabric_GetSupportedGameVersions_HitsGameEndpointAndMapsFields()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"/v2/versions/game",
			"[{\"version\":\"1.21.4\",\"stable\":true},{\"version\":\"1.20.1\",\"stable\":false}]");

		FabricService service = new FabricService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });
		List<FabricGameVersion> versions = await service.GetSupportedGameVersionsAsync();

		Assert.Equal(2, versions.Count);
		Assert.Equal("1.21.4", versions[0].Version);
		Assert.True(versions[0].Stable);
		// 第二项 stable=false 必须被如实读出来：前端靠它决定是否标"实验性"。
		Assert.False(versions[1].Stable);

		// 端点必须是 /v2/versions/game，少了 /game 会拿到另一个列表（loader 列表），
		// 数量对不上但不会报错——属于典型的静默错位。
		Assert.Contains("/v2/versions/game", handler.Requests[0].Url);
	}

	[Fact]
	public async Task Fabric_GetLoaderGameVersions_ProjectsOntoSharedLoaderModel()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"/v2/versions/game",
			"[{\"version\":\"1.21.4\",\"stable\":true}]");

		FabricService service = new FabricService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });
		List<LoaderGameVersion> versions = await service.GetLoaderGameVersionsAsync();

		LoaderGameVersion single = Assert.Single(versions);
		Assert.Equal("1.21.4", single.Version);
		Assert.True(single.Stable);
	}

	[Fact]
	public async Task Fabric_GetCompatibleLoaders_EscapesMinecraftVersionIntoTemplatePath()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"/v2/versions/loader/",
			"[{\"loader\":{\"version\":\"0.16.9\",\"stable\":true,\"build\":9},\"intermediary\":{\"version\":\"1.21.4\"}}]");

		FabricService service = new FabricService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });
		List<FabricCompatibleLoaderVersion> loaders = await service.GetCompatibleLoadersAsync("1.21.4");

		Assert.Single(loaders);
		// Version / Stable 是转发自嵌套 loader 对象的投影属性，不是顶层字段。
		Assert.Equal("0.16.9", loaders[0].Version);
		Assert.True(loaders[0].Stable);
		Assert.Contains("/v2/versions/loader/1.21.4", handler.Requests[0].Url);
	}

	[Fact]
	public async Task Fabric_GetCompatibleLoaders_RejectsBlankMinecraftVersionBeforeAnyRequest()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler();
		FabricService service = new FabricService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCompatibleLoadersAsync("   "));
		// 参数校验必须发生在发请求之前，否则会打出一个 /loader/ 的空版本请求。
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task Fabric_GetProfileJson_RejectsNonObjectRoot()
	{
		// 数组根（例如误打了列表接口）必须被当成格式异常，而不是被当成 profile 往下传。
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/profile/json", "[]");
		FabricService service = new FabricService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.GetFabricProfileJsonAsync("1.21.4", "0.16.9"));
		Assert.Contains("格式异常", ex.Message);
	}

	[Fact]
	public async Task Fabric_GetProfileJson_TranslatesHttpErrorIntoReadableChineseMessage()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler()
			.OnContains("/profile/json", "boom", HttpStatusCode.InternalServerError, "text/plain");
		FabricService service = new FabricService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.GetFabricProfileJsonAsync("1.21.4", "0.16.9"));
		// 用户看到的必须是"返回 500，请稍后重试"这种话，而不是原始 JSON 或英文堆栈。
		Assert.Contains("500", ex.Message);
	}

	[Fact]
	public async Task Fabric_GetProfileJson_RejectsBlankLoaderVersion()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler();
		FabricService service = new FabricService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.GetFabricProfileJsonAsync("1.21.4", "  "));
		Assert.Empty(handler.Requests);
	}

	/* ============================ QuiltService ============================ */

	[Fact]
	public async Task Quilt_GetLoaderGameVersions_HitsQuiltHostAndMapsFields()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"meta.quiltmc.org/v3/versions/game",
			"[{\"version\":\"1.21.4\",\"stable\":true}]");

		QuiltService service = new QuiltService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });
		List<LoaderGameVersion> versions = await service.GetLoaderGameVersionsAsync();

		Assert.Single(versions);
		Assert.Equal("1.21.4", versions[0].Version);
		// Quilt 走的是 v3，而 Fabric 走 v2：串了会 404。
		Assert.Contains("meta.quiltmc.org/v3/", handler.Requests[0].Url);
	}

	[Fact]
	public async Task Quilt_GetLoaderVersionEntries_MapsNestedVersionsAndKeepsOrder()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"/v3/versions/loader/1.21.4",
			"[{\"version\":\"0.27.0\",\"stable\":true},{\"version\":\"0.28.0-beta.1\",\"stable\":false}]");

		QuiltService service = new QuiltService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });
		List<LoaderVersionEntry> entries = await service.GetLoaderVersionEntriesAsync("1.21.4");

		Assert.Equal(2, entries.Count);
		Assert.Equal("0.27.0", entries[0].Version);
		Assert.True(entries[0].Stable);
		Assert.Equal("0.28.0-beta.1", entries[1].Version);
		Assert.False(entries[1].Stable);
	}

	[Fact]
	public async Task Quilt_GetLoaderVersionEntries_RejectsBlankVersionBeforeRequest()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler();
		QuiltService service = new QuiltService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLoaderVersionEntriesAsync(""));
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task Quilt_GetProfileJson_RejectsNonObjectRoot()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/profile/json", "\"just-a-string\"");
		QuiltService service = new QuiltService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.GetQuiltProfileJsonAsync("1.21.4", "0.27.0"));
	}

	[Fact]
	public async Task Quilt_GetProfileJson_SurfacesStatusInMessage()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler()
			.OnContains("/profile/json", "nope", HttpStatusCode.NotFound, "text/plain");
		QuiltService service = new QuiltService(Client(handler), System.IO.Path.GetTempPath(), Downloader(Client(handler)), _ => { });

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.GetQuiltProfileJsonAsync("1.21.4", "0.27.0"));
		Assert.Contains("404", ex.Message);
	}

	/* ============================ ResourceContentService ============================ */

	[Fact]
	public async Task ResourceContent_Search_BuildsProjectTypeAndVersionFacets()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"api.modrinth.com/v2/search",
			"{\"hits\":[{\"project_id\":\"abc\",\"slug\":\"faithful\",\"title\":\"Faithful 32x\",\"description\":\"d\",\"author\":\"a\",\"downloads\":42,\"icon_url\":\"http://i\"}]}");

		ResourceContentService service = new ResourceContentService(Client(handler));
		List<ResourceContentProject> projects = await service.SearchAsync("faithful", "1.21.4", ResourceContentKind.ResourcePack);

		ResourceContentProject single = Assert.Single(projects);
		Assert.Equal("abc", single.ProjectId);
		Assert.Equal(ResourceContentKind.ResourcePack, single.Kind);
		Assert.Equal(42, single.Downloads);
		Assert.Equal("https://modrinth.com/resourcepack/faithful", single.WebsiteUrl);

		string url = handler.Requests[0].Url;
		// facets 是 UrlEncode 后的 JSON 数组串，未被转义会让 Modrinth 直接 400。
		Assert.Contains("project_type%3Aresourcepack", url);
		Assert.Contains("versions%3A1.21.4", url);
		Assert.DoesNotContain("project_type:resourcepack", url);
	}

	[Fact]
	public async Task ResourceContent_Search_ShaderKindMapsToShaderProjectTypeAndUrl()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"api.modrinth.com/v2/search",
			"{\"hits\":[{\"slug\":\"bsl\",\"title\":\"BSL\"}]}");

		ResourceContentService service = new ResourceContentService(Client(handler));
		List<ResourceContentProject> projects = await service.SearchAsync("bsl", string.Empty, ResourceContentKind.Shader);

		Assert.Equal("https://modrinth.com/shader/bsl", projects[0].WebsiteUrl);
		string url = handler.Requests[0].Url;
		Assert.Contains("project_type%3Ashader", url);
		// 没给游戏版本时不能拼出空的 versions facet（Modrinth 会当成"匹配空集"）。
		Assert.DoesNotContain("versions", url);
	}

	[Fact]
	public async Task ResourceContent_Search_RejectsBlankQueryBeforeRequest()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler();
		ResourceContentService service = new ResourceContentService(Client(handler));

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.SearchAsync("   ", "1.21.4", ResourceContentKind.ResourcePack));
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task ResourceContent_GetVersions_PrefersPrimaryZipOverFirstZip()
	{
		// 关键点：Modrinth 的 files 数组里可能同时有 sources jar、dev zip、正式包。
		// 取"第一个 .zip"会取错；必须优先取 primary=true 且以 .zip 结尾的那一个。
		const string body = """
		[
		  {
		    "id": "v1",
		    "version_number": "1.2.3",
		    "files": [
		      {"filename": "notes.txt", "url": "http://x/notes.txt", "primary": true},
		      {"filename": "secondary.zip", "url": "http://x/secondary.zip", "primary": false, "size": 10},
		      {"filename": "primary.zip", "url": "http://x/primary.zip", "primary": true, "size": 2048, "hashes": {"sha1": "AA"}}
		    ]
		  }
		]
		""";

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/version", body);
		ResourceContentService service = new ResourceContentService(Client(handler));
		List<ResourceContentVersion> versions = await service.GetVersionsAsync(
			new ResourceContentProject { ProjectId = "abc" }, "1.21.4");

		ResourceContentVersion single = Assert.Single(versions);
		Assert.Equal("primary.zip", single.FileName);
		Assert.Equal("http://x/primary.zip", single.DownloadUrl);
		Assert.Equal(2048, single.FileSize);
		Assert.Equal("AA", single.Sha1);
		Assert.Contains("game_versions=", handler.Requests[0].Url);
	}

	[Fact]
	public async Task ResourceContent_GetVersions_FallsBackToAnyZipWhenNoPrimary()
	{
		const string body = """
		[{ "id": "v1", "version_number": "1.0.0",
		   "files": [ {"filename": "pack.zip", "url": "http://x/pack.zip", "primary": false} ] }]
		""";

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/version", body);
		ResourceContentService service = new ResourceContentService(Client(handler));
		List<ResourceContentVersion> versions = await service.GetVersionsAsync(
			new ResourceContentProject { ProjectId = "abc" }, string.Empty);

		Assert.Equal("pack.zip", Assert.Single(versions).FileName);
	}

	[Fact]
	public async Task ResourceContent_GetVersions_SkipsVersionsWithoutAnyZip()
	{
		// 只有 jar 的版本对资源包页没有意义，直接跳过；若不过滤，前端会显示一条点不动的记录。
		const string body = """
		[{ "id": "v1", "files": [ {"filename": "mod.jar", "url": "http://x/mod.jar"} ] },
		 { "id": "v2", "files": [ {"filename": "pack.zip", "url": "http://x/pack.zip"} ] }]
		""";

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/version", body);
		ResourceContentService service = new ResourceContentService(Client(handler));
		List<ResourceContentVersion> versions = await service.GetVersionsAsync(
			new ResourceContentProject { ProjectId = "abc" }, string.Empty);

		Assert.Equal("v2", Assert.Single(versions).VersionId);
	}

	/// <summary>
	/// 走完整的下载 → SHA1 校验 → 落到 resourcepacks/ 的链路。
	/// 用真实 zip 字节（内容不必是合法资源包，导入只做文件复制），因此能验证落盘与清理。
	/// </summary>
	[Fact]
	public async Task ResourceContent_Download_VerifiesSha1AndImportsIntoResourcePacks()
	{
		using TempDir game = new TempDir("mcgame");
		byte[] zipBytes = Encoding.UTF8.GetBytes("PK\u0003\u0004fake-zip-payload-for-sha1");
		string sha1 = Convert.ToHexString(SHA1.HashData(zipBytes));

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContainsBytes("/cdn/pack.zip", zipBytes);
		ResourceContentService service = new ResourceContentService(Client(handler));

		string imported = await service.DownloadAsync(
			new GameInstance { GameDirectory = game.Path },
			new ResourceContentProject { Kind = ResourceContentKind.ResourcePack, ProjectId = "abc" },
			new ResourceContentVersion { FileName = "My Pack.zip", DownloadUrl = "https://cdn.example/cdn/pack.zip", Sha1 = sha1 },
			game.Path);

		Assert.True(File.Exists(imported), "应把资源包复制进实例的 resourcepacks 目录：" + imported);
		Assert.Equal("resourcepacks", System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(imported)));
		Assert.Equal(zipBytes, File.ReadAllBytes(imported));
	}

	[Fact]
	public async Task ResourceContent_Download_RejectsWhenSha1Mismatches_AndLeavesNothingBehind()
	{
		using TempDir game = new TempDir("mcgame");
		byte[] zipBytes = Encoding.UTF8.GetBytes("PK\u0003\u0004tampered");

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContainsBytes("/cdn/pack.zip", zipBytes);
		ResourceContentService service = new ResourceContentService(Client(handler));

		await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(
			new GameInstance { GameDirectory = game.Path },
			new ResourceContentProject { Kind = ResourceContentKind.ResourcePack, ProjectId = "abc" },
			new ResourceContentVersion { FileName = "pack.zip", DownloadUrl = "https://cdn.example/cdn/pack.zip", Sha1 = "0000000000000000000000000000000000000000" },
			game.Path));

		// 校验失败必须什么都不留下：既不能有半成品，也不能污染实例目录。
		string packs = System.IO.Path.Combine(game.Path, "resourcepacks");
		Assert.False(Directory.Exists(packs) && Directory.EnumerateFiles(packs).Any(),
			"校验失败后 resourcepacks 目录里不应出现文件");
	}

	[Fact]
	public async Task ResourceContent_SafeZipName_StripsDirectoryTraversalFromServerFileName()
	{
		// 服务端给的 filename 是不可信输入。若直接拼进临时目录，../../x.zip 会写到临时目录之外。
		// 这里断言的正是"路径被压平成文件名"。
		using TempDir game = new TempDir("mcgame");
		byte[] zipBytes = Encoding.UTF8.GetBytes("PK\u0003\u0004traversal");

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContainsBytes("/cdn/evil.zip", zipBytes);
		ResourceContentService service = new ResourceContentService(Client(handler));

		string imported = await service.DownloadAsync(
			new GameInstance { GameDirectory = game.Path },
			new ResourceContentProject { Kind = ResourceContentKind.ResourcePack, ProjectId = "abc" },
			new ResourceContentVersion { FileName = "../../../evil.zip", DownloadUrl = "https://cdn.example/cdn/evil.zip" },
			game.Path);

		Assert.Equal("evil.zip", System.IO.Path.GetFileName(imported));
		Assert.StartsWith(System.IO.Path.Combine(game.Path, "resourcepacks"), imported, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ResourceContent_SafeZipName_AppendsZipExtensionWhenMissing()
	{
		using TempDir game = new TempDir("mcgame");
		byte[] zipBytes = Encoding.UTF8.GetBytes("PK\u0003\u0004noext");

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContainsBytes("/cdn/noext", zipBytes);
		ResourceContentService service = new ResourceContentService(Client(handler));

		string imported = await service.DownloadAsync(
			new GameInstance { GameDirectory = game.Path },
			new ResourceContentProject { Kind = ResourceContentKind.ResourcePack, ProjectId = "abc" },
			new ResourceContentVersion { FileName = "pack", DownloadUrl = "https://cdn.example/cdn/noext" },
			game.Path);

		Assert.EndsWith(".zip", imported, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ResourceContent_Constructor_AddsUserAgentOnce()
	{
		// Modrinth 会对无 User-Agent 的请求限流；重复添加会产生 "Pineglow-Launcher/1.0 Pineglow-Launcher/1.0"。
		RoutingFakeHandler handler = new RoutingFakeHandler();
		HttpClient http = Client(handler);

		_ = new ResourceContentService(http);
		_ = new ResourceContentService(http);

		Assert.Single(http.DefaultRequestHeaders.UserAgent);
		Assert.Contains("Pineglow-Launcher", http.DefaultRequestHeaders.UserAgent.ToString());
	}

	/* ============================ ModrinthModpackService ============================ */

	[Fact]
	public async Task Modpack_Search_ForcesModpackFacetAndMapsHits()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"api.modrinth.com/v2/search",
			"{\"hits\":[{\"project_id\":\"p1\",\"slug\":\"fabulously-optimized\",\"title\":\"Fabulously Optimized\",\"description\":\"d\",\"author\":\"a\",\"downloads\":10,\"categories\":[\"optimization\"],\"versions\":[\"1.21.4\"]}]}");

		ModrinthModpackService service = new ModrinthModpackService(Client(handler));
		List<ModpackProject> projects = await service.SearchAsync("fabulously");

		ModpackProject single = Assert.Single(projects);
		Assert.Equal("https://modrinth.com/modpack/fabulously-optimized", single.WebsiteUrl);
		Assert.Equal("optimization", Assert.Single(single.Categories));
		Assert.Equal("1.21.4", Assert.Single(single.GameVersions));

		// 少了 project_type:modpack 这个 facet，搜出来的会是 mod，整合包页会显示成"搜索不到"。
		Assert.Contains("project_type%3Amodpack", handler.Requests[0].Url);
		Assert.Contains("limit=20", handler.Requests[0].Url);
	}

	[Fact]
	public async Task Modpack_Search_RejectsBlankQuery()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler();
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchAsync("  "));
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task Modpack_GetVersions_SortsReleaseFirstThenNewest()
	{
		const string body = """
		[
		  {"id":"v-beta","version_number":"2.0.0-beta","version_type":"beta","date_published":"2026-01-05T00:00:00Z",
		   "files":[{"filename":"pack.mrpack","url":"http://x/beta.mrpack","hashes":{"sha1":"B"}}]},
		  {"id":"v-rel-old","version_number":"1.0.0","version_type":"release","date_published":"2025-01-01T00:00:00Z",
		   "files":[{"filename":"pack.mrpack","url":"http://x/rel-old.mrpack","hashes":{"sha1":"A"}}]},
		  {"id":"v-rel-new","version_number":"1.1.0","version_type":"release","date_published":"2025-06-01T00:00:00Z",
		   "files":[{"filename":"pack.mrpack","url":"http://x/rel-new.mrpack","hashes":{"sha512":"C"}}]}
		]
		""";

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/version", body);
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));
		List<ModpackVersion> versions = await service.GetVersionsAsync(new ModpackProject { ProjectId = "p1" });

		Assert.Equal(3, versions.Count);
		// 稳定版在前，同是稳定版则新的在前；beta 必须排到最后（否则用户默认装到测试版）。
		Assert.Equal("v-rel-new", versions[0].VersionId);
		Assert.Equal("v-rel-old", versions[1].VersionId);
		Assert.Equal("v-beta", versions[2].VersionId);
		Assert.Equal("C", versions[0].Sha512);
	}

	[Fact]
	public async Task Modpack_GetVersions_IgnoresVersionsWithoutMrpackFile()
	{
		const string body = """
		[{"id":"v1","files":[{"filename":"readme.txt","url":"http://x/readme.txt"}]},
		 {"id":"v2","files":[{"filename":"pack.mrpack","url":"http://x/pack.mrpack"}]}]
		""";

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/version", body);
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));
		List<ModpackVersion> versions = await service.GetVersionsAsync(new ModpackProject { ProjectId = "p1" });

		Assert.Equal("v2", Assert.Single(versions).VersionId);
	}

	[Fact]
	public async Task Modpack_GetVersions_RejectsProjectWithoutId()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler();
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.GetVersionsAsync(new ModpackProject()));
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task Modpack_Download_VerifiesSha512AndWritesIntoTempDirectory()
	{
		byte[] bytes = Encoding.UTF8.GetBytes("PK\u0003\u0004mrpack-body");
		string sha512 = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContainsBytes("/cdn/pack.mrpack", bytes);
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));

		string path = await service.DownloadAsync(
			new ModpackProject { Title = "T" },
			new ModpackVersion { FileName = "pack.mrpack", DownloadUrl = "https://cdn.example/cdn/pack.mrpack", Sha512 = sha512 });

		try
		{
			Assert.True(File.Exists(path));
			Assert.Equal(bytes, File.ReadAllBytes(path));
			// 必须落在系统临时目录下：调用方下载完立刻导入，随后按"父目录"整棵删除。
			Assert.StartsWith(System.IO.Path.GetTempPath(), path, StringComparison.OrdinalIgnoreCase);
			Assert.StartsWith("pineglow-modpack-", System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)));
		}
		finally
		{
			ModrinthModpackService.DeleteDownload(path);
		}
	}

	[Fact]
	public async Task Modpack_Download_DeletesTempDirectoryWhenHashMismatches()
	{
		byte[] bytes = Encoding.UTF8.GetBytes("PK\u0003\u0004tampered");
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContainsBytes("/cdn/pack.mrpack", bytes);
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));

		await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(
			new ModpackProject { Title = "T" },
			new ModpackVersion
			{
				FileName = "pack.mrpack",
				DownloadUrl = "https://cdn.example/cdn/pack.mrpack",
				Sha1 = new string('0', 40),
			}));

		// 失败分支必须把临时目录删干净；否则每次换包失败都会留下一个几十 MB 的残留目录。
		string tempRoot = System.IO.Path.GetTempPath();
		string[] leftovers = Directory.EnumerateDirectories(tempRoot, "pineglow-modpack-*")
			.Where(d => (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(d)).TotalMinutes < 2)
			.ToArray();
		_out.WriteLine("近 2 分钟内残留的 pineglow-modpack-* 目录数：" + leftovers.Length);
		Assert.Empty(leftovers);
	}

	[Fact]
	public async Task Modpack_Download_SkipsHashingWhenNoHashProvided()
	{
		// 没有哈希时"不校验"是当前设计（Modrinth 老版本可能缺 hashes）。
		// 把这条钉住，是为了让"以后想改成强制校验"的人看到需要同步改这里。
		byte[] bytes = Encoding.UTF8.GetBytes("PK\u0003\u0004no-hash");
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContainsBytes("/cdn/pack.mrpack", bytes);
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));

		string path = await service.DownloadAsync(
			new ModpackProject { Title = "T" },
			new ModpackVersion { FileName = "pack.mrpack", DownloadUrl = "https://cdn.example/cdn/pack.mrpack" });

		try
		{
			Assert.True(File.Exists(path));
			Assert.Equal(bytes, File.ReadAllBytes(path));
		}
		finally
		{
			ModrinthModpackService.DeleteDownload(path);
		}
	}

	[Fact]
	public async Task Modpack_Download_RejectsWhenNoDownloadUrl()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler();
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.DownloadAsync(new ModpackProject { Title = "X" }, new ModpackVersion()));
		Assert.Contains("X", ex.Message);
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task Modpack_Download_UsesFallbackFileNameWhenServerProvidesNone()
	{
		byte[] bytes = Encoding.UTF8.GetBytes("PK\u0003\u0004fallback");
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContainsBytes("/cdn/anon", bytes);
		ModrinthModpackService service = new ModrinthModpackService(Client(handler));

		string path = await service.DownloadAsync(
			new ModpackProject { Title = "T" },
			new ModpackVersion { DownloadUrl = "https://cdn.example/cdn/anon" });

		try
		{
			Assert.Equal("modpack.mrpack", System.IO.Path.GetFileName(path));
		}
		finally
		{
			ModrinthModpackService.DeleteDownload(path);
		}
	}

	[Fact]
	public void Modpack_DeleteDownload_RemovesWholeTempDirectory()
	{
		using TempDir scratch = new TempDir("pineglow-modpack-manual");
		string nested = System.IO.Path.Combine(scratch.Path, "sub");
		Directory.CreateDirectory(nested);
		File.WriteAllText(System.IO.Path.Combine(nested, "extra.bin"), "x");
		string zip = System.IO.Path.Combine(scratch.Path, "pack.mrpack");
		File.WriteAllText(zip, "PK");

		ModrinthModpackService.DeleteDownload(zip);

		Assert.False(Directory.Exists(scratch.Path), "DeleteDownload 会整棵删除传入路径的父目录");
	}

	[Fact]
	public void Modpack_DeleteDownload_IgnoresBlankAndMissingPaths()
	{
		// 幂等：被调用两次、或传 null，都不能抛。调用点在 finally 里，抛了会盖掉真正的业务异常。
		ModrinthModpackService.DeleteDownload(null);
		ModrinthModpackService.DeleteDownload("   ");
		ModrinthModpackService.DeleteDownload(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pg-not-exist-" + Guid.NewGuid().ToString("N"), "a.mrpack"));
	}

	/// <summary>
	/// **已知行为，非缺陷但值得记录**：<see cref="ModrinthModpackService.DeleteDownload"/>
	/// 不校验传入路径，直接对 <c>Path.GetDirectoryName(path)</c> 做递归删除。
	///
	/// 也就是说：谁把这个方法传进一个"不是 DownloadAsync 返回值"的路径，谁就在删任意目录。
	/// 当前只有一个调用点（LocalApiService.Content.cs:510），参数是
	/// <c>await modrinth.DownloadAsync(...)</c> 的直接返回值，且该返回值总是
	/// <c>%TEMP%\pineglow-modpack-&lt;guid&gt;\...</c>，因此**当前不可被外部输入触达**。
	///
	/// 本用例把"破坏力"钉成可执行事实：改天有人把它接上用户可控路径，这里会提醒他先加白名单。
	/// </summary>
	[Fact]
	public void Modpack_DeleteDownload_IsRecursiveOnParent_SoCallersMustPassOnlyServiceOwnedPaths()
	{
		using TempDir victim = new TempDir("pg-victim");
		File.WriteAllText(System.IO.Path.Combine(victim.Path, "important.txt"), "should be gone");
		string inside = System.IO.Path.Combine(victim.Path, "whatever.mrpack");
		File.WriteAllText(inside, "PK");

		ModrinthModpackService.DeleteDownload(inside);

		Assert.False(Directory.Exists(victim.Path),
			"确认：DeleteDownload 会连同父目录里的无关文件一起删掉");
	}

	/* ============================ AuthlibInjectorService ============================ */

	[Fact]
	public async Task Authlib_Login_PostsYggdrasilPayloadAndParsesProfile()
	{
		const string reply = """
		{ "accessToken": "tok-123456",
		  "selectedProfile": { "id": "uuid-abc", "name": "Steve" } }
		""";

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/authserver/authenticate", reply);
		AuthlibInjectorService service = new AuthlibInjectorService(Client(handler));

		AuthlibAccount account = await service.LoginAsync("https://littleskin.cn/", "  Steve  ", "pw", CancellationToken.None);

		Assert.Equal("Steve", account.UserName);
		Assert.Equal("uuid-abc", account.Uuid);
		Assert.Equal("tok-123456", account.AccessToken);
		// 末尾斜杠必须被剥掉，否则会拼出 //authserver/authenticate。
		Assert.Equal("https://littleskin.cn", account.ServerUrl);
		Assert.Equal("https://littleskin.cn/authserver/authenticate", handler.Requests[0].Url);

		// 请求体是 Yggdrasil 协议的一部分，字段名写错服务端直接 400。
		using JsonDocument body = JsonDocument.Parse(handler.Requests[0].Body!);
		JsonElement root = body.RootElement;
		Assert.Equal("Minecraft", root.GetProperty("agent").GetProperty("name").GetString());
		Assert.Equal(1, root.GetProperty("agent").GetProperty("version").GetInt32());
		Assert.Equal("Steve", root.GetProperty("username").GetString());
		Assert.True(root.GetProperty("requestUser").GetBoolean());
	}

	[Fact]
	public async Task Authlib_Login_ParsesTexturesIntoSkinCapeAndSlimFlag()
	{
		string textures = """
		{"textures":{"SKIN":{"url":"https://tex/skin.png","metadata":{"model":"slim"}},
		             "CAPE":{"url":"https://tex/cape.png"}}}
		""";
		string reply = "{ \"accessToken\": \"tok\", \"selectedProfile\": {\"id\":\"u\",\"name\":\"Alex\"," +
			"\"properties\":[{\"name\":\"textures\",\"value\":\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(textures)) + "\"}]}}";

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/authserver/authenticate", reply);
		AuthlibInjectorService service = new AuthlibInjectorService(Client(handler));

		AuthlibAccount account = await service.LoginAsync("https://littleskin.cn", "Alex", "pw", CancellationToken.None);

		Assert.Equal("https://tex/skin.png", account.SkinUrl);
		Assert.Equal("https://tex/cape.png", account.CapeUrl);
		// slim 决定用 3px 手臂模型；判错会让皮肤显示成"宽手臂拉伸"。
		Assert.True(account.IsSlim);
	}

	[Fact]
	public async Task Authlib_Login_ToleratesUndecodableTexturePayload()
	{
		// textures 是 base64 的 JSON，第三方服务端可能返回垃圾值。
		// 结论：皮肤拿不到就留空，**登录必须成功**——不能因为皮肤异常让人登不进去。
		const string reply = """
		{ "accessToken": "tok", "selectedProfile": { "id": "u", "name": "N",
		  "properties": [ {"name": "textures", "value": "!!!not-base64!!!"} ] } }
		""";

		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains("/authserver/authenticate", reply);
		AuthlibInjectorService service = new AuthlibInjectorService(Client(handler));

		AuthlibAccount account = await service.LoginAsync("https://littleskin.cn", "N", "pw", CancellationToken.None);

		Assert.Equal("tok", account.AccessToken);
		Assert.Equal(string.Empty, account.SkinUrl);
		Assert.Equal(string.Empty, account.CapeUrl);
		Assert.False(account.IsSlim);
	}

	[Fact]
	public async Task Authlib_Login_RejectsMissingInputsBeforeRequest()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler();
		AuthlibInjectorService service = new AuthlibInjectorService(Client(handler));

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync("", "u", "p", CancellationToken.None));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync("https://x", "", "p", CancellationToken.None));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync("https://x", "u", "  ", CancellationToken.None));
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task Authlib_Login_RejectsNonHttpServerUrl()
	{
		// 地址会拼成真实请求目标。允许 ftp:// 或裸域名会让 requestUri 变成相对路径，
		// 请求打到配置里的 BaseAddress 上——凭据就发给了另一个主机。
		RoutingFakeHandler handler = new RoutingFakeHandler();
		AuthlibInjectorService service = new AuthlibInjectorService(Client(handler));

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.LoginAsync("ftp://evil.example", "u", "p", CancellationToken.None));
		await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.LoginAsync("littleskin.cn", "u", "p", CancellationToken.None));
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public async Task Authlib_Login_SurfacesUpstreamErrorBodyAndKeepsItShort()
	{
		string spam = new string('x', 500);
		RoutingFakeHandler handler = new RoutingFakeHandler()
			.OnContains("/authserver/authenticate", spam, HttpStatusCode.Unauthorized, "text/plain");
		AuthlibInjectorService service = new AuthlibInjectorService(Client(handler));

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.LoginAsync("https://littleskin.cn", "u", "p", CancellationToken.None));

		// 保留前 200 字符便于用户看清"密码错误"，但不能把整段响应体塞进错误框。
		Assert.Contains("第三方登录失败", ex.Message);
		Assert.True(ex.Message.Length < 300, "错误消息不应把整个响应体带出来，实际长度 " + ex.Message.Length);
	}

	[Fact]
	public async Task Authlib_Login_RejectsResponseWithoutUsableProfile()
	{
		RoutingFakeHandler handler = new RoutingFakeHandler().OnContains(
			"/authserver/authenticate",
			"{ \"accessToken\": \"tok\", \"selectedProfile\": { \"id\": \"\", \"name\": \"N\" } }");
		AuthlibInjectorService service = new AuthlibInjectorService(Client(handler));

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.LoginAsync("https://littleskin.cn", "u", "p", CancellationToken.None));
		Assert.Contains("角色", ex.Message);
	}

	/// <summary>
	/// **验证能力缺口（如实记录）**：<see cref="AuthlibInjectorService.EnsureAgentAsync"/>
	/// 把目标目录写死为 <c>PlatformPaths.GetConfigDirectory()/authlib-injector</c>，
	/// 没有注入点。测试它就会在**运行者真实的应用数据目录**里建目录、写 jar，
	/// 这属于"测试污染用户环境"，不能接受。
	///
	/// 因此这里只断言"缺口本身"：构造签名里没有可注入的路径参数。
	/// 将来若加入注入点（例如可选 string targetDirectory），本用例会失败并提醒补上真正的下载用例。
	/// </summary>
	[Fact]
	public void Authlib_EnsureAgent_HasNoInjectableTargetDirectory_GapRecorded()
	{
		_ = typeof(AuthlibInjectorService);

		System.Reflection.ConstructorInfo[] ctors = typeof(AuthlibInjectorService).GetConstructors();
		Assert.Single(ctors);
		// 主构造函数只有一个 HttpClient 参数：证明确实没有路径注入点。
		Assert.Equal(new[] { typeof(HttpClient) }, ctors[0].GetParameters().Select(p => p.ParameterType).ToArray());

		System.Reflection.MethodInfo ensure = typeof(AuthlibInjectorService).GetMethod("EnsureAgentAsync")
			?? throw new InvalidOperationException("EnsureAgentAsync 应存在");
		Assert.Single(ensure.GetParameters());
		Assert.Equal(typeof(CancellationToken), ensure.GetParameters()[0].ParameterType);
	}

	/* ============================ NewsContentStateText ============================ */

	[Theory]
	[InlineData(NewsContentState.Live, "live")]
	[InlineData(NewsContentState.Revalidated, "revalidated")]
	[InlineData(NewsContentState.Memory, "memory")]
	[InlineData(NewsContentState.Cached, "cached")]
	[InlineData(NewsContentState.Empty, "empty")]
	[InlineData(NewsContentState.Error, "error")]
	public void NewsContentState_ToWire_MatchesFrontendContract(NewsContentState state, string expected)
	{
		// 这几个串是**前端契约**：ui/js 按 live/cached/empty 分支决定显示"已是最新/来自缓存/暂无内容"。
		// 改一个字母前端就会静默落进 default 分支显示成"加载失败"。
		Assert.Equal(expected, NewsContentStateText.ToWire(state));
	}

	[Fact]
	public void NewsContentState_ToWire_FallsBackToErrorForUnknownEnumValue()
	{
		// 防御性分支：反序列化出越界枚举值（服务端加了新状态而后端没同步）时不能返回 null，
		// 否则前端拿到 undefined 会走"成功但没有内容"的路径。
		Assert.Equal("error", NewsContentStateText.ToWire((NewsContentState)99));
	}

	/* ============================ 收尾：确认没有真实网络请求 ============================ */

	[Fact]
	public void FakeHandler_RecordsUnroutedRequestsInsteadOfThrowing()
	{
		// 自检：路由没匹配时返回 404 而非抛异常，保证"漏配路由"会以断言失败的形式暴露，
		// 而不是变成一个看不出原因的异常。
		RoutingFakeHandler handler = new RoutingFakeHandler();
		using HttpClient http = Client(handler);
		HttpResponseMessage response = http.GetAsync("https://nowhere.example/x").GetAwaiter().GetResult();

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
		Assert.Single(handler.Requests);
	}
}
