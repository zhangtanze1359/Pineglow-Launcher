using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>真实 HTTP 入口必须接通迁移，且迁移结果在重新加载后仍有效。</summary>
[Collection("LocalApiService")]
public sealed class DownloadSourceHttpMigrationTests
{
	[Theory]
	[InlineData("mcbbs", "bmclapi", 1)]
	[InlineData("MCBBS", "bmclapi", 1)]
	[InlineData(" mcbbs ", "bmclapi", 1)]
	[InlineData("official", "official", 0)]
	[InlineData("bmclapi", "bmclapi", 0)]
	[InlineData("Official", "official", 1)]
	[InlineData("BMCLAPI", "bmclapi", 1)]
	[InlineData(null, "official", 1)]
	[InlineData("", "official", 1)]
	[InlineData("   ", "official", 1)]
	public async Task State_MigratesSourceOnce_AndSurvivesReload(string? original, string expected, int expectedWrites)
	{
		string root = Path.Combine(Path.GetTempPath(), "pg-source-http-" + Guid.NewGuid().ToString("N"));
		string previousSource = MirrorHelper.SelectedKey;
		Directory.CreateDirectory(root);
		try
		{
			var initial = new AppConfig { GameDirectory = root, DownloadSource = original! };
			var store = new RecordingFileStore(Path.Combine(root, "config.json"), initial);
			await using (LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(store, initial))
			{
				await AssertStateAsync(harness, expected);
				Assert.Equal(expectedWrites, store.SaveCount);
				Assert.Equal(expected, store.Load().DownloadSource);
				await AssertStateAsync(harness, expected);
				Assert.Equal(expectedWrites, store.SaveCount);
			}

			// 新 store 从实际写回的 JSON 重载，不复用原 AppConfig 引用。
			var reloaded = new RecordingFileStore(store.Path);
			await using (LocalApiTestHarness harness = await LocalApiTestHarness.StartAsync(reloaded, reloaded.Load()))
			{
				await AssertStateAsync(harness, expected);
				Assert.Equal(0, reloaded.SaveCount);
			}
		}
		finally
		{
			MirrorHelper.SelectedKey = previousSource;
			Directory.Delete(root, recursive: true);
		}
	}

	private static async Task AssertStateAsync(LocalApiTestHarness harness, string expected)
	{
		(int status, string body) = await harness.SendAsync(HttpMethod.Get, "/api/state");
		Assert.Equal(200, status);
		using JsonDocument document = JsonDocument.Parse(body);
		Assert.Equal(expected, document.RootElement.GetProperty("config").GetProperty("downloadSource").GetString());
		Assert.Equal(expected, MirrorHelper.SelectedKey);
		const string url = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
		Assert.Equal(expected == "bmclapi"
			? "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"
			: url, MirrorHelper.Rewrite(url));
	}

	private sealed class RecordingFileStore : IConfigStore
	{
		public string Path { get; }
		public int SaveCount { get; private set; }

		public RecordingFileStore(string path) { Path = path; }
		public RecordingFileStore(string path, AppConfig initial) : this(path)
		{
			File.WriteAllText(Path, JsonSerializer.Serialize(initial));
		}

		public AppConfig Load() => JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path))
			?? throw new InvalidOperationException("隔离测试配置为空");

		public void Save(AppConfig config)
		{
			File.WriteAllText(Path, JsonSerializer.Serialize(config));
			SaveCount++;
		}
	}
}
