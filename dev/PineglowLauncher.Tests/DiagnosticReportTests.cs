using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR08：诊断历史、脱敏与报告导出。
///
/// 本文件验证的契约：
///   1. 诊断结束会生成一份**冻结结构**的报告，历史默认只保存脱敏摘要；
///   2. 报告与导出物**不含** token / API Key / authorization / session / 服务器地址 / 用户路径 /
///      provider URL，但**保留**类名与 Mod 名（否则诊断线索全丢）；
///   3. 本地模式零外部请求（fake AI handler 一次都不会被调用）；
///   4. 同一 operationId 只记录一次；
///   5. 超量/超期只提示、**不自动删除**（避免静默丢掉待恢复证据）；
///   6. Markdown / JSON 两种导出字段与界面一致；证据摘录默认不包含，需显式勾选且有保留才出现。
/// </summary>
[Collection("LocalApiService")]
public class DiagnosticReportTests
{
	private readonly ITestOutputHelper _out;

	public DiagnosticReportTests(ITestOutputHelper output) => _out = output;

	private const string SecretToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.SECRETPART.SIGPART";
	private const string SecretApiKey = "sk-abcdef0123456789ABCDEF";
	private const string ProviderUrl = "https://api.deepseek.com/v1/chat/completions";

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string InstanceId { get; } = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		public AppConfig Config { get; }

		public Fixture(bool keepEvidence = false)
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-diag-report-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(Root, "logs"));
			Directory.CreateDirectory(Path.Combine(Root, "mods"));
			File.WriteAllText(Path.Combine(Root, "mods", "examplemod-1.0.0.jar"), "JAR");

			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				DiagnosticKeepEvidence = keepEvidence,
				AiDiagnosisEnabled = true,
				AiProviders = new List<AiProviderProfile>
				{
					new AiProviderProfile
					{
						Id = "provider-1",
						Name = "DeepSeek",
						BaseUrl = "https://api.deepseek.com/v1",
						Model = "deepseek-chat",
						ApiKey = "fake-key"
					}
				},
				SelectedAiProviderId = "provider-1",
				Instances = new List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "演示实例", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		/// <summary>
		/// 写一份含真实敏感串与诊断线索的日志。
		/// 敏感串刻意放在 **ERROR** 行：证据摘录只挑选与错误相关的行，
		/// 而这正是真实场景（异常里带完整 URL / Key）——脱敏必须在那条路径上生效。
		/// </summary>
		public void WriteLog()
		{
			string log = string.Join("\n", new[]
			{
				"[12:00:00] [main/INFO]: Loading Minecraft 1.20.4 with Fabric Loader 0.15.0",
				"[12:00:01] [main/ERROR]: auth failed access_token=" + SecretToken,
				"[12:00:02] [main/ERROR]: api_key=" + SecretApiKey + " rejected",
				"[12:00:02] [main/ERROR]: Authorization: Bearer " + SecretToken,
				"[12:00:03] [main/ERROR]: server=play.example.com:25565 unreachable",
				@"[12:00:03] [main/ERROR]: game dir C:\Users\Administrator\AppData\Roaming\.minecraft missing",
				"[12:00:04] [main/ERROR]: provider " + ProviderUrl + " returned 401",
				"[12:00:04] [main/ERROR]: java.lang.NoClassDefFoundError: net/fabricmc/api/EnvType",
				"[12:00:05] [main/ERROR]: Caused by: java.lang.ClassNotFoundException: examplemod.MixinHooks",
				"[12:00:06] [main/ERROR]: Mod resolution failed for examplemod-1.0.0.jar"
			});
			File.WriteAllText(Path.Combine(Root, "logs", "latest.log"), log);
		}

		public string Api(string suffix = "") => "/api/instances/" + InstanceId + suffix;

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static string? Error(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			return doc.RootElement.TryGetProperty("error", out JsonElement el) ? el.GetString() : null;
		}
		catch { }
		return null;
	}

	private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement.Clone();

	private static async Task<(int Status, string Body)> DiagnoseAsync(LocalApiTestHarness h, Fixture fx, string operationId, bool localOnly)
	{
		return await h.SendAsync(HttpMethod.Post, fx.Api("/diagnose"), new
		{
			operationId,
			localOnly,
			confirmAiSend = false,
			locale = "zh"
		});
	}

	/* ===================== 1. 本地模式：零外部请求 + rule_only 报告 ===================== */

	[Fact]
	public async Task LocalOnlyDiagnosis_RecordsRuleOnlyReport_WithoutCallingAi()
	{
		using var fx = new Fixture();
		fx.WriteLog();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		int sendCount = 0;
		AiDiagnoseService.SendWithRetryOverrideForTest = (http, baseUrl, apiKey, model, prompt, useJson, ct) =>
		{
			sendCount++;
			return Task.FromResult<string?>("{}");
		};

		try
		{
			(string operationId, int status, string body) = await RunDiagnoseAsync(h, fx, localOnly: true);
			_out.WriteLine($"[local-only] -> {status} body={body.Substring(0, Math.Min(200, body.Length))}");
			Assert.Equal(200, status);
			Assert.Equal(0, sendCount); // 本地模式绝不外发

			(_, string historyBody) = await GetHistoryAsync(h, fx);
			JsonElement reports = Root(historyBody).GetProperty("reports");
			Assert.Equal(1, reports.GetArrayLength());
			Assert.Equal("rule_only", reports[0].GetProperty("status").GetString());
			Assert.Equal(operationId, reports[0].GetProperty("reportId").GetString());
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	/* ===================== 2. 脱敏：删凭据、留线索 ===================== */

	[Fact]
	public async Task ExportedReport_RedactsSecrets_ButKeepsClassNamesAndModNames()
	{
		using var fx = new Fixture(keepEvidence: true);
		fx.WriteLog();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(string operationId, int status, _) = await RunDiagnoseAsync(h, fx, localOnly: true);
		Assert.Equal(200, status);

		(_, string md) = await ExportAsync(h, fx, operationId, "md", includeEvidence: true);
		_out.WriteLine("[export-md]\n" + md.Substring(0, Math.Min(1200, md.Length)));

		Assert.DoesNotContain(SecretToken, md);
		Assert.DoesNotContain(SecretApiKey, md);
		Assert.DoesNotContain("Bearer " + SecretToken, md);
		Assert.DoesNotContain("play.example.com", md);
		Assert.DoesNotContain(@"C:\Users\Administrator", md);
		Assert.DoesNotContain(ProviderUrl, md);
		Assert.DoesNotContain("sk-abcdef", md);

		// 诊断线索必须保留
		Assert.Contains("NoClassDefFoundError", md);
		Assert.Contains("examplemod", md);

		// 脱敏统计必须出现在导出物里
		Assert.Contains("脱敏说明", md);
		Assert.Contains("api_key", md);

		// JSON 导出同样干净
		(_, string json) = await ExportAsync(h, fx, operationId, "json", includeEvidence: true);
		Assert.DoesNotContain(SecretToken, json);
		Assert.DoesNotContain(SecretApiKey, json);
		Assert.DoesNotContain(ProviderUrl, json);
		using JsonDocument doc = JsonDocument.Parse(json);
		Assert.True(doc.RootElement.GetProperty("redaction").GetProperty("totalRedactions").GetInt32() > 0);
	}

	/* ===================== 3. 同一操作只记录一次 ===================== */

	[Fact]
	public async Task SameOperationId_IsRecordedOnlyOnce()
	{
		using var fx = new Fixture();
		fx.WriteLog();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		string operationId = Guid.NewGuid().ToString("N");
		(int firstStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/diagnose"), new { operationId, localOnly = true, locale = "zh" });
		(int secondStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/diagnose"), new { operationId, localOnly = true, locale = "zh" });
		Assert.Equal(200, firstStatus);
		Assert.Equal(200, secondStatus);

		(_, string historyBody) = await GetHistoryAsync(h, fx);
		Assert.Equal(1, Root(historyBody).GetProperty("reports").GetArrayLength());
	}

	/* ===================== 4. 超量/超期只提示，不删除 ===================== */

	[Fact]
	public async Task HistoryOverLimit_IsReportedButNothingIsDeleted()
	{
		using var fx = new Fixture();
		fx.WriteLog();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		// 直接往历史根里塞 55 份报告（含 1 份 60 天前的）
		var service = new DiagnosticReportService(h.DiagnosticStoreRoot);
		for (int i = 0; i < 54; i++)
		{
			await service.SaveAsync(new DiagnosticReport
			{
				ReportId = "seed-" + i.ToString("D3"),
				InstanceId = fx.InstanceId,
				InstanceName = "seed",
				CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
				Summary = "seed"
			});
		}

		await service.SaveAsync(new DiagnosticReport
		{
			ReportId = "seed-old",
			InstanceId = fx.InstanceId,
			InstanceName = "seed",
			CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
			Summary = "old"
		});

		(_, string historyBody) = await GetHistoryAsync(h, fx);
		JsonElement root = Root(historyBody);
		Assert.Equal(55, root.GetProperty("reports").GetArrayLength());
		Assert.True(root.GetProperty("overLimitCount").GetInt32() >= 5, "必须报出超量条数");
		Assert.True(root.GetProperty("olderThanRetentionCount").GetInt32() >= 1, "必须报出超期条数");
		Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("notice").GetString()), "必须给出清理提示");

		// 关键：一个文件都没被删
		int files = Directory.GetFiles(Path.Combine(h.DiagnosticStoreRoot, fx.InstanceId), "*.json").Length;
		Assert.Equal(55, files);
	}

	/* ===================== 5. 导出缺失/证据缺省 ===================== */

	[Fact]
	public async Task Export_WithoutKeptEvidence_SaysSoInsteadOfPretending()
	{
		using var fx = new Fixture(keepEvidence: false);
		fx.WriteLog();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(string operationId, int status, _) = await RunDiagnoseAsync(h, fx, localOnly: true);
		Assert.Equal(200, status);

		(_, string md) = await ExportAsync(h, fx, operationId, "md", includeEvidence: true);
		Assert.Contains("未保留证据摘录", md);

		(_, string json) = await ExportAsync(h, fx, operationId, "json", includeEvidence: false);
		using JsonDocument doc = JsonDocument.Parse(json);
		Assert.False(doc.RootElement.TryGetProperty("evidenceExcerpt", out _), "未勾选时不得带证据字段");
		Assert.False(doc.RootElement.GetProperty("keptEvidence").GetBoolean());
	}

	[Fact]
	public async Task Export_UnknownReport_Returns400()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await ExportAsync(h, fx, "no-such-report", "md", includeEvidence: false);
		_out.WriteLine($"[export-missing] -> {status} error={Error(body)}");
		Assert.Equal(400, status);
		Assert.Contains("不存在", Error(body) ?? string.Empty);
	}

	/* ===================== 6. 脱敏器单元级 ===================== */

	[Fact]
	public void Redactor_CountsAndReplacesEachCategory()
	{
		string input = "api_key=" + SecretApiKey + " token=abcdef123456 server=play.example.com:25565 "
			+ @"path C:\Users\Bob\AppData url " + ProviderUrl + " class java.lang.NoClassDefFoundError mod examplemod-1.0.0.jar";

		string output = DiagnosticRedactor.Redact(input, out Dictionary<string, int> counts);

		Assert.DoesNotContain(SecretApiKey, output);
		Assert.DoesNotContain("play.example.com", output);
		Assert.DoesNotContain(@"C:\Users\Bob", output);
		Assert.DoesNotContain(ProviderUrl, output);
		Assert.Contains("NoClassDefFoundError", output);
		Assert.Contains("examplemod-1.0.0.jar", output);
		Assert.True(counts.Count >= 3, "应当统计到多个类别：" + string.Join(",", counts.Keys));
		Assert.True(counts.Values.Sum() >= 3);
	}

	/* ===================== 7. AI 失败 / 取消分支 ===================== */

	[Fact]
	public async Task AiFailure_IsRecordedAsFailed_AndRuleResultIsKept()
	{
		using var fx = new Fixture();
		fx.WriteLog();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		AiDiagnoseService.SendWithRetryOverrideForTest = (http, baseUrl, apiKey, model, prompt, useJson, ct) =>
			throw new HttpRequestException("rate limited");

		try
		{
			string operationId = Guid.NewGuid().ToString("N");
			(int status, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/diagnose"),
				new { operationId, confirmAiSend = true, locale = "zh" });
			_out.WriteLine($"[ai-fail] -> {status}");

			(_, string historyBody) = await GetHistoryAsync(h, fx);
			JsonElement reports = Root(historyBody).GetProperty("reports");
			Assert.Equal(1, reports.GetArrayLength());
			Assert.Equal("failed", reports[0].GetProperty("status").GetString());

			// 关键：AI 失败不影响本地规则结果与事实
			(_, string json) = await ExportAsync(h, fx, operationId, "json", includeEvidence: false);
			using JsonDocument doc = JsonDocument.Parse(json);
			Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("ruleSummary").GetString()));
			Assert.True(doc.RootElement.GetProperty("facts").GetArrayLength() > 0);
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	[Fact]
	public async Task CancelledDiagnosis_IsRecordedAsCancelled()
	{
		using var fx = new Fixture();
		fx.WriteLog();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		using var started = new System.Threading.ManualResetEventSlim(false);
		AiDiagnoseService.SendWithRetryOverrideForTest = async (http, baseUrl, apiKey, model, prompt, useJson, ct) =>
		{
			started.Set();
			await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
			return "{}";
		};

		try
		{
			string operationId = Guid.NewGuid().ToString("N");
			Task<(int Status, string Body)> diagnose = h.SendAsync(HttpMethod.Post, fx.Api("/diagnose"),
				new { operationId, confirmAiSend = true, locale = "zh" });

			Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "AI 发送应当已经开始");

			(int cancelStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/diagnose/cancel"), new { operationId });
			Assert.Equal(200, cancelStatus);

			(int status, _) = await diagnose;
			_out.WriteLine($"[cancelled] -> {status}");

			(_, string historyBody) = await GetHistoryAsync(h, fx);
			JsonElement reports = Root(historyBody).GetProperty("reports");
			Assert.Equal(1, reports.GetArrayLength());
			Assert.Equal("cancelled", reports[0].GetProperty("status").GetString());
		}
		finally
		{
			AiDiagnoseService.SendWithRetryOverrideForTest = null;
		}
	}

	/* ===================== 辅助 ===================== */

	private static async Task<(string OperationId, int Status, string Body)> RunDiagnoseAsync(
		LocalApiTestHarness h, Fixture fx, bool localOnly)
	{
		string operationId = Guid.NewGuid().ToString("N");
		(int status, string body) = await DiagnoseAsync(h, fx, operationId, localOnly);
		return (operationId, status, body);
	}

	private static Task<(int Status, string Body)> GetHistoryAsync(LocalApiTestHarness h, Fixture fx) =>
		h.SendAsync(HttpMethod.Get, fx.Api("/diagnose/history"));

	private static Task<(int Status, string Body)> ExportAsync(
		LocalApiTestHarness h, Fixture fx, string reportId, string format, bool includeEvidence) =>
		h.SendAsync(HttpMethod.Get,
			fx.Api("/diagnose/export?reportId=" + Uri.EscapeDataString(reportId)
				+ "&format=" + format + "&includeEvidence=" + (includeEvidence ? "1" : "0")));
}
