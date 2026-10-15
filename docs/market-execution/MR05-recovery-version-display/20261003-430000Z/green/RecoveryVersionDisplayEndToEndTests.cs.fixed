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
/// A14 的**生产路径**端到端验证。
///
/// 为什么需要它（第 54 轮澄清的一处误判）：
/// 第 53 轮我曾把"前端 replace 调用点尚未传 version"记为剩余项 —— **那条是错的**：
///   * 前端的 Mod 操作只有 <c>getMods</c>/<c>toggleMod</c>/<c>deleteMod</c>，**根本没有更新入口**；
///   * 生产里的更新走 <c>ModContentService.DownloadAndInstallAsync</c> → <c>ModReplacementService.ReplaceAsync(..., version)</c>
///     （<c>ModContentService.cs:140-141</c>），**本来就传了真实版本** → 新侧车本来就会被写。
///   * HTTP 的 <c>/mods/{file}/replace</c> 端点**没有 UI 调用方**，它是测试/自动化缝；A22 让这条缝与生产路径一致（仍有价值）。
///
/// 因此本用例直接跑**生产路径**：内容服务更新 → 恢复区（API 列表）必须给出「旧版本 → 新版本」。
/// 这比"调 HTTP 端点"更接近真实使用。
/// </summary>
[Collection("LocalApiService")]
public class RecoveryVersionDisplayEndToEndTests
{
	private readonly ITestOutputHelper _out;

	public RecoveryVersionDisplayEndToEndTests(ITestOutputHelper output) => _out = output;

	/// <summary>假下载：按需返回固定字节，绝不发外网。</summary>
	private sealed class BytesHandler : HttpMessageHandler
	{
		private readonly byte[] _payload;
		public BytesHandler(byte[] payload) => _payload = payload;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
			=> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_payload) });
	}

	private static ModVersion NewVersion(byte[] payload) => new ModVersion
	{
		Source = ModSource.Modrinth,
		ProjectId = "example",
		VersionId = "v2",
		VersionNumber = "2.0",
		FileName = "example-2.0.jar",
		DownloadUrl = "https://example.invalid/mod.jar",
		FileSize = payload.Length,
		Sha1 = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant()
	};

	private static string? Str(JsonElement obj, string name)
		=> obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

	[Fact]
	public async Task ProductionUpdateFlow_ShowsOldAndNewVersionInTheRecoveryListing()
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-a14-e2e-" + Guid.NewGuid().ToString("N"));
		string modsDir = Path.Combine(root, "mods");
		Directory.CreateDirectory(modsDir);

		string instanceId = "a14-e2e";
		var config = new AppConfig
		{
			GameDirectory = root,
			SelectedInstanceId = instanceId,
			Instances = new List<GameInstance>
			{
				new GameInstance { Id = instanceId, Name = "a14", GameDirectory = root, MinecraftVersion = "1.20.4" }
			}
		};

		byte[] oldBytes = Encoding.UTF8.GetBytes("OLD-MOD-CONTENT-v1");
		byte[] newBytes = Encoding.UTF8.GetBytes("NEW-MOD-CONTENT-v2-longer");

		// 先"装"一个带在线元数据的旧版本（真实形状的侧车：VersionNumber=1.0）
		string oldJar = Path.Combine(modsDir, "example-1.0.jar");
		File.WriteAllBytes(oldJar, oldBytes);
		File.WriteAllText(oldJar + ".mcmini.json",
			"{\"source\":\"Modrinth\",\"projectId\":\"example\",\"versionId\":\"v1\",\"versionNumber\":\"1.0\"}");

		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(config);

			// **生产路径**：内容服务直接做事务化替换（与 ModContentService.cs:140 同一调用形状），
			// 恢复区根**必须**用夹具那一份，否则 API 列表看不到这些记录。
			var content = new ModContentService(new HttpClient(new BytesHandler(newBytes)),
				new FileRecoveryService(h.RecoveryRoot));
			string installed = await content.DownloadAndInstallAsync(config.Instances[0], NewVersion(newBytes),
				new ModService(root));
			_out.WriteLine("[A14-e2e] 更新结果路径 = " + installed);

			(int listStatus, string listBody) = await h.SendAsync(HttpMethod.Get, "/api/instances/" + instanceId + "/recovery");
			Assert.Equal(200, listStatus);

			using JsonDocument doc = JsonDocument.Parse(listBody);
			JsonElement rec = doc.RootElement.GetProperty("recoveries").EnumerateArray()
				.First(x => x.GetProperty("reason").GetString() == "mod-replace");

			string? oldVersion = Str(rec, "oldVersion");
			string? newVersion = Str(rec, "newVersion");
			_out.WriteLine($"[A14-e2e] 生产路径更新后：oldVersion={oldVersion}  newVersion={newVersion}");

			Assert.Equal("1.0", oldVersion);
			Assert.Equal("2.0", newVersion);
		}
		finally
		{
			try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
		}
	}
}
