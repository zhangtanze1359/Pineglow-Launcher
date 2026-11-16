using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// S9 之后最要紧的用户可见后果：**程序里不再内置 CurseForge Key，那模组还能不能下？**
///
/// 答案必须可解释：
///   * Modrinth 搜索**完全不受影响**（那条链路本来就不需要 Key）；
///   * CurseForge 搜索返回 **400 + `curseforge_key_required`**（不是 500、不是静默空列表），
///     界面据此引导"去设置页填 Key，或切回 Modrinth"。
/// </summary>
[Collection("LocalApiService")]
public sealed class CurseForgeKeyRequiredTests : IAsyncLifetime
{
	private LocalApiTestHarness _harness = null!;
	private string _root = string.Empty;

	public async Task InitializeAsync()
	{
		_root = Path.Combine(Path.GetTempPath(), "pg-cf-key-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(_root);
		AppConfig config = new AppConfig
		{
			GameDirectory = _root,
			CurseForgeApiKey = string.Empty,
			AiProviders = new List<AiProviderProfile>(),
			Instances = new List<GameInstance>()
		};
		_harness = await LocalApiTestHarness.StartAsync(config);
	}

	public async Task DisposeAsync()
	{
		await _harness.DisposeAsync();
		try
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, true);
			}
		}
		catch
		{
		}
	}

	[Fact]
	public async Task CurseForgeSearch_WithoutKey_Returns400WithStableCode()
	{
		string? before = Environment.GetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable);
		try
		{
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, null);
			// 本版本有内置回退（有截止日期的临时豁免）：把它关掉才能模拟"完全没有 Key"。
			// 下个版本删掉豁免文件后，这一行可以去掉。
			ModContentService.EmbeddedKeyFallbackForTest = string.Empty;

			(int status, string body) = await _harness.SendAsync(HttpMethod.Get,
				"/api/content/search?q=sodium&category=mod&source=curseforge", null);

			Assert.Equal(400, status);
			JsonElement root = JsonDocument.Parse(body).RootElement;
			Assert.False(root.GetProperty("ok").GetBoolean());
			Assert.Equal(CurseForgeKeyRequiredException.ErrorCode, root.GetProperty("code").GetString());
			Assert.Contains("设置页", root.GetProperty("error").GetString());
		}
		finally
		{
			ModContentService.EmbeddedKeyFallbackForTest = null;
			Environment.SetEnvironmentVariable(ModContentService.CurseForgeApiKeyEnvironmentVariable, before);
		}
	}

	[Fact]
	public async Task ModrinthSearch_DoesNotNeedAnyKey()
	{
		// 这条是"去掉内置 Key 之后模组还能不能下"的直接回答：
		// Modrinth 不需要 Key，所以请求**必须正常走通**（发请求拿真实网络结果）。
		(int status, string body) = await _harness.SendAsync(HttpMethod.Get,
			"/api/content/search?q=sodium&category=mod&source=modrinth", null);

		// 这条用例**不依赖网络成功**：上游挂了给 502 也算"不需要 Key"，
		// 唯一不能出现的是 400 + curseforge_key_required（那说明我们又把 Key 当成必需了）。
		Assert.NotEqual(400, status);
		JsonElement root = JsonDocument.Parse(body).RootElement;
		Assert.False(root.TryGetProperty("code", out JsonElement code)
			&& code.GetString() == CurseForgeKeyRequiredException.ErrorCode);
		if (status == 200)
		{
			Assert.True(root.TryGetProperty("results", out JsonElement results));
			Assert.True(results.ValueKind == JsonValueKind.Array);
		}
	}
}
