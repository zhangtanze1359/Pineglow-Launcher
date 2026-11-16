using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// D3③ 的**离线**验证：更新检查的有界并发 / 缓存，以及服务端下载的白名单与字节校验。
///
/// 这两块上一轮只做到"代码层"，没有自动化测试。这里不伪造 Modrinth 的 JSON、
/// 也不发真实网络请求 —— 用注入的查询委托与固定响应的 <see cref="HttpMessageHandler"/>，
/// 让规则本身可断言。
/// </summary>
public sealed class ModUpdateAndDownloadTests
{
	private sealed class StubHandler : HttpMessageHandler
	{
		private readonly byte[] _bytes;
		private readonly HttpStatusCode _status;

		public StubHandler(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK)
		{
			_bytes = bytes;
			_status = status;
		}

		public int Calls { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls++;
			HttpResponseMessage response = new HttpResponseMessage(_status);
			if (_status == HttpStatusCode.OK)
			{
				response.Content = new ByteArrayContent(_bytes);
			}
			return Task.FromResult(response);
		}
	}

	private static ModFile OnlineMod(string name, string versionId) => new ModFile
	{
		FileName = name,
		OnlineSource = "modrinth",
		ProjectId = "proj-" + name,
		VersionId = versionId
	};

	/* ===================== 缓存（纯） ===================== */

	[Fact]
	public void Cache_FreshEntryIsServed_AndExpiresAfterTtl()
	{
		ModUpdateService.ClearCache();
		string key = "inst|a.jar|v1";
		ModVersion latest = new ModVersion { VersionId = "v2" };
		DateTimeOffset t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);

		ModUpdateService.Put(key, latest, t0);

		Assert.True(ModUpdateService.TryGetFresh(key, t0.AddMinutes(1), out ModVersion? hit));
		Assert.Equal("v2", hit!.VersionId);

		// 超过 TTL（5 分钟）就不再算新鲜
		Assert.False(ModUpdateService.TryGetFresh(key, t0.AddMinutes(6), out _));
	}

	[Fact]
	public void Cache_NullResultIsAlsoCached_AndKeyFollowsInstalledVersion()
	{
		ModUpdateService.ClearCache();
		ModFile mod = OnlineMod("a.jar", "v1");
		DateTimeOffset t0 = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);

		string keyV1 = ModUpdateService.CacheKey("inst", mod);
		ModUpdateService.Put(keyV1, null, t0);
		Assert.True(ModUpdateService.TryGetFresh(keyV1, t0.AddMinutes(1), out ModVersion? cached));
		Assert.Null(cached);

		// 装上升级后 VersionId 变了 → 键变化 → 自动失效（不需要额外的失效逻辑）
		ModFile upgraded = OnlineMod("a.jar", "v2");
		Assert.NotEqual(keyV1, ModUpdateService.CacheKey("inst", upgraded));
	}

	/* ===================== 有界并发（注入查询委托，离线） ===================== */

	[Fact]
	public async Task QueryLatest_BoundsConcurrency_AndKeepsOrder()
	{
		ModUpdateService.ClearCache();
		List<ModFile> mods = Enumerable.Range(0, 20).Select(i => OnlineMod($"m{i}.jar", "v1")).ToList();
		int inFlight = 0;
		int maxInFlight = 0;
		var service = new ModUpdateService();

		List<ModVersion?> result = await service.QueryLatestAsync(
			mods, "inst", DateTimeOffset.Now,
			async (mod, ct) =>
			{
				int now = Interlocked.Increment(ref inFlight);
				int seen;
				do
				{
					seen = Volatile.Read(ref maxInFlight);
					if (now <= seen) break;
				}
				while (Interlocked.CompareExchange(ref maxInFlight, now, seen) != seen);

				await Task.Delay(40, ct);
				Interlocked.Decrement(ref inFlight);
				return new ModVersion { VersionId = "v2-" + mod.FileName, DownloadUrl = "https://cdn.modrinth.com/x.jar" };
			});

		Assert.Equal(20, result.Count);
		Assert.True(maxInFlight <= ModUpdateService.MaxConcurrency, $"在途请求峰值 {maxInFlight} 超过了上限 {ModUpdateService.MaxConcurrency}");
		Assert.True(maxInFlight > 1, "并发上限没有生效（实际仍是串行）");
		// 顺序与输入对齐（按下标回填，而不是按完成先后）
		for (int i = 0; i < mods.Count; i++)
		{
			Assert.Equal("v2-" + mods[i].FileName, result[i]!.VersionId);
		}
	}

	[Fact]
	public async Task QueryLatest_SecondCall_IsServedFromCache()
	{
		ModUpdateService.ClearCache();
		List<ModFile> mods = new List<ModFile> { OnlineMod("a.jar", "v1") };
		int calls = 0;
		var service = new ModUpdateService();
		Func<ModFile, CancellationToken, Task<ModVersion?>> query = (mod, ct) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult<ModVersion?>(new ModVersion { VersionId = "v2", DownloadUrl = "https://cdn.modrinth.com/x.jar" });
		};

		await service.QueryLatestAsync(mods, "inst", DateTimeOffset.Now, query);
		await service.QueryLatestAsync(mods, "inst", DateTimeOffset.Now, query);

		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task QueryLatest_ModsWithoutMetadata_NeverHitTheNetwork()
	{
		ModUpdateService.ClearCache();
		List<ModFile> mods = new List<ModFile> { new ModFile { FileName = "local.jar" } };
		int calls = 0;
		var service = new ModUpdateService();

		List<ModVersion?> result = await service.QueryLatestAsync(mods, "inst", DateTimeOffset.Now, (mod, ct) =>
		{
			Interlocked.Increment(ref calls);
			return Task.FromResult<ModVersion?>(null);
		});

		Assert.Equal(0, calls);
		Assert.Null(result[0]);
	}

	/* ===================== 下载白名单与字节 ===================== */

	[Fact]
	public void DownloadUrl_Validation_RejectsHttpAndUnknownHost()
	{
		Assert.False(ModContentService.TryValidateDownloadUrl("http://cdn.modrinth.com/x.jar", out _, out string? error1));
		Assert.Contains("https", error1);

		Assert.False(ModContentService.TryValidateDownloadUrl("https://evil.example.com/x.jar", out _, out string? error2));
		Assert.Contains("白名单", error2);

		Assert.True(ModContentService.TryValidateDownloadUrl("https://cdn.modrinth.com/x.jar", out Uri? ok, out _));
		Assert.Equal("cdn.modrinth.com", ok!.Host);
	}

	[Fact]
	public async Task Download_ReturnsBytesFromWhitelistedHost()
	{
		byte[] payload = Encoding.UTF8.GetBytes("JAR-BYTES");
		StubHandler handler = new StubHandler(payload);
		ModContentService service = new ModContentService(new HttpClient(handler));

		byte[] bytes = await service.DownloadBytesAsync("https://cdn.modrinth.com/x.jar");

		Assert.Equal(1, handler.Calls);
		Assert.Equal(payload, bytes);
		Assert.Equal(Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant(), Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant());
	}

	[Fact]
	public async Task Download_EmptyBody_Throws()
	{
		StubHandler handler = new StubHandler(Array.Empty<byte>());
		ModContentService service = new ModContentService(new HttpClient(handler));

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadBytesAsync("https://cdn.modrinth.com/x.jar"));
	}

	[Fact]
	public async Task Download_NonSuccess_Throws()
	{
		StubHandler handler = new StubHandler(Array.Empty<byte>(), HttpStatusCode.NotFound);
		ModContentService service = new ModContentService(new HttpClient(handler));

		await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadBytesAsync("https://cdn.modrinth.com/x.jar"));
	}
}
