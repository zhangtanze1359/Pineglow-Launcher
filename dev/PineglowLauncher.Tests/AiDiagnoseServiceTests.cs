using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

public class AiDiagnoseServiceTests
{
	private sealed class FakeHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

		public HttpRequestMessage? LastRequest { get; private set; }

		public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			_respond = respond;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastRequest = request;
			return Task.FromResult(_respond(request));
		}
	}

	private static HttpClient CreateClient(FakeHandler handler) => new HttpClient(handler);

	private static CrashAnalysis Rule => new CrashAnalysis("RULE-SUMMARY", new[] { "RULE-SUGGESTION" }, null);

	private static ModHealthReport Mods => new ModHealthReport
	{
		Issues = new[] { new ModHealthIssue(ModHealthSeverity.Warning, "ISSUE-TITLE", "ISSUE-DESC") }
	};

	[Fact]
	public void ComposePrompt_ContainsTailRuleAndModIssues()
	{
		string prompt = AiDiagnoseService.ComposePrompt("TAIL-MARK", Rule, Mods);

		Assert.Contains("TAIL-MARK", prompt);
		Assert.Contains("RULE-SUMMARY", prompt);
		Assert.Contains("RULE-SUGGESTION", prompt);
		Assert.Contains("ISSUE-TITLE", prompt);
		Assert.Contains("ISSUE-DESC", prompt);
	}

	[Fact]
	public void ComposePrompt_EmptyTailDoesNotThrow()
	{
		string prompt = AiDiagnoseService.ComposePrompt(string.Empty, Rule, Mods);

		Assert.False(string.IsNullOrWhiteSpace(prompt));
	}

	[Fact]
	public async Task DiagnoseAsync_ReturnsContent_WhenSuccessful()
	{
		const string body = """{"choices":[{"message":{"content":"HELLO-AI"}}]}""";
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		});
		using HttpClient http = CreateClient(handler);

		string? result = await AiDiagnoseService.DiagnoseAsync(http, "https://example.test/v1", "sk", "m", "tail", Rule, Mods, CancellationToken.None);

		Assert.Equal("HELLO-AI", result);
	}

	[Fact]
	public async Task DiagnoseAsync_SendsBearerHeader_WhenKeyProvided()
	{
		const string body = """{"choices":[{"message":{"content":"OK"}}]}""";
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		});
		using HttpClient http = CreateClient(handler);

		await AiDiagnoseService.DiagnoseAsync(http, "https://example.test/v1", "secret", "m", "tail", Rule, Mods, CancellationToken.None);

		Assert.NotNull(handler.LastRequest);
		Assert.True(handler.LastRequest!.Headers.TryGetValues("Authorization", out var values));
		Assert.Equal("Bearer secret", values.Single());
	}

	[Fact]
	public async Task DiagnoseAsync_OmitsAuthorization_WhenKeyEmpty()
	{
		const string body = """{"choices":[{"message":{"content":"OK"}}]}""";
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		});
		using HttpClient http = CreateClient(handler);

		await AiDiagnoseService.DiagnoseAsync(http, "https://example.test/v1", string.Empty, "m", "tail", Rule, Mods, CancellationToken.None);

		Assert.NotNull(handler.LastRequest);
		Assert.False(handler.LastRequest!.Headers.Contains("Authorization"));
	}

	[Fact]
	public async Task DiagnoseAsync_ReturnsNull_OnNonSuccessStatus()
	{
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
		using HttpClient http = CreateClient(handler);

		string? result = await AiDiagnoseService.DiagnoseAsync(http, "https://example.test/v1", "sk", "m", "tail", Rule, Mods, CancellationToken.None);

		Assert.Null(result);
	}
}

public class AiSuspectExtractionTests
{
	[Fact]
	public void Extract_ReturnsNames_WhenJsonAtEnd()
	{
		string content = "解释文本。{\"explain\":\"原因\",\"fixes\":[\"步骤\"],\"suspectMods\":[\"sodium-0.6.8.jar\",\"bad-mod.jar\"]}";
		var names = AiDiagnoseService.ExtractSuspectModFileNames(content);
		Assert.Equal(new[] { "sodium-0.6.8", "bad-mod" }, names);
	}

	[Fact]
	public void Extract_ReturnsEmpty_WhenNoJson()
	{
		Assert.Empty(AiDiagnoseService.ExtractSuspectModFileNames("普通的解释文本"));
		Assert.Empty(AiDiagnoseService.ExtractSuspectModFileNames(null));
	}

	[Fact]
	public void Normalize_MatchesInstalledFiles_IgnoringCaseAndSuffix()
	{
		var installed = new List<string> { "Sodium-0.6.8.jar", "Iris-1.8.1.jar" };
		var suspects = new List<string> { "sodium-0.6.8", "iris-1.8.1.jar" };
		var result = AiDiagnoseService.NormalizeSuspectModFileNames(suspects, installed);
		Assert.Equal(new[] { "Sodium-0.6.8.jar", "Iris-1.8.1.jar" }, result);
	}

	[Fact]
	public void Normalize_FallsBackToCandidate_WhenNoMatch()
	{
		var result = AiDiagnoseService.NormalizeSuspectModFileNames(new List<string> { "unknown-mod.jar" }, new List<string> { "other.jar" });
		Assert.Equal(new[] { "unknown-mod.jar" }, result);
	}
}
public class CrashAnalyzerServiceTests
{
	[Fact]
	public void ReadNewestDiagnosticTail_ReturnsCrashReport_WhenNewer()
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-crash-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(root, "logs"));
		Directory.CreateDirectory(Path.Combine(root, "crash-reports"));
		string log = Path.Combine(root, "logs", "latest.log");
		string crash = Path.Combine(root, "crash-reports", "crash.txt");
		File.WriteAllText(log, "LOG-TAIL");
		File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddMinutes(-5));
		File.WriteAllText(crash, "CRASH-TAIL");
		File.SetLastWriteTimeUtc(crash, DateTime.UtcNow);

		try
		{
			Assert.Equal("CRASH-TAIL", CrashAnalyzerService.ReadNewestDiagnosticTail(root));
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void ReadNewestDiagnosticTail_ReturnsEmpty_WhenNoDiagnostics()
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-crash-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			Assert.Equal(string.Empty, CrashAnalyzerService.ReadNewestDiagnosticTail(root));
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
		}
	}
}