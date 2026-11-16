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
/// S8「免 Key 的本地诊断」的验收用例：**没有任何 AI 服务商**时，
///   * 「仅本地分析」必须能出结论（规则 + Mod 健康），且**不发外网**；
///   * 即使客户端误点「深度分析」，也必须返回 200 + `configured=false` 的可解释结果，**不能 500**。
/// </summary>
public sealed class DiagnoseWithoutAiTests : IAsyncLifetime
{
	private string _root = string.Empty;
	private LocalApiTestHarness _harness = null!;
	private GameInstance _instance = null!;

	public async Task InitializeAsync()
	{
		_root = Path.Combine(Path.GetTempPath(), "pg-diag-noai-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(Path.Combine(_root, "game", "mods"));
		Directory.CreateDirectory(Path.Combine(_root, "game", "logs"));
		_instance = new GameInstance
		{
			Id = "inst-noai",
			Name = "no-ai",
			GameDirectory = Path.Combine(_root, "game"),
			MinecraftVersion = "1.20.4"
		};

		AppConfig config = new AppConfig
		{
			GameDirectory = Path.Combine(_root, "game"),
			SelectedInstanceId = _instance.Id,
			Instances = new List<GameInstance> { _instance },
			AiDiagnosisEnabled = false,
			AiProviders = new List<AiProviderProfile>(),
			SelectedAiProviderId = string.Empty
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
				Directory.Delete(_root, recursive: true);
			}
		}
		catch
		{
		}
	}

	private string Api(string suffix = "") => "/api/instances/" + _instance.Id + "/diagnose" + suffix;

	private static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement;

	[Fact]
	public async Task LocalOnly_WithoutAnyProvider_ReturnsRuleConclusion()
	{
		(int status, string body) = await _harness.SendAsync(HttpMethod.Post, Api(), new
		{
			operationId = "op-s8-local-" + Guid.NewGuid().ToString("N"),
			confirmAiSend = false,
			localOnly = true,
			locale = "zh"
		});

		Assert.Equal(200, status);
		JsonElement root = Parse(body);
		Assert.True(root.GetProperty("ok").GetBoolean());

		// 没有 AI 服务商 → configured 必须是 false，而且本地规则块仍然存在
		Assert.False(root.GetProperty("configured").GetBoolean());
		Assert.True(root.TryGetProperty("rule", out JsonElement rule));
		Assert.True(rule.TryGetProperty("summary", out _));
		Assert.True(rule.TryGetProperty("suggestions", out JsonElement suggestions));
		Assert.True(suggestions.GetArrayLength() >= 0);

		// 本地模式绝不发外网 → ai 必须为 null，且 aiDispatch 为空
		Assert.True(root.GetProperty("ai").ValueKind == JsonValueKind.Null);
		Assert.True(root.GetProperty("aiDispatch").ValueKind == JsonValueKind.Null);
	}

	[Fact]
	public async Task AiMode_WithoutProvider_StillReturns200_AndExplainsWhy()
	{
		(int status, string body) = await _harness.SendAsync(HttpMethod.Post, Api(), new
		{
			operationId = "op-s8-ai-" + Guid.NewGuid().ToString("N"),
			confirmAiSend = true,
			localOnly = false,
			locale = "zh"
		});

		// 关键：未配置 AI 不能变成 500（那是"功能坏了"，而不是"用不了深度分析"）
		Assert.Equal(200, status);
		JsonElement root = Parse(body);
		Assert.False(root.GetProperty("configured").GetBoolean());
		Assert.True(root.GetProperty("ai").ValueKind == JsonValueKind.Null);

		// 本地结论照旧给出来，用户不会对着一片空白
		Assert.True(root.TryGetProperty("rule", out JsonElement rule));
		Assert.True(rule.GetProperty("summary").GetString() is not null);
	}
}
