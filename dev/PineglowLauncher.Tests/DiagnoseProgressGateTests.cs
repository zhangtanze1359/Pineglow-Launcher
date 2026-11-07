using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR10 门槛判定的证据夹具：**诊断到底要等多久**，以及界面在等待时说了什么。
///
/// MR10（真实进度协议）是**条件任务**，门槛是"确有等待体验问题"。
/// 卡片明确："先用 MR09 延迟观察判定是否需要轮询；**若延迟可接受，维持明确的等待/取消状态并记"无需实现"**"。
///
/// 本夹具只产出判定所需的证据，**不实现任何进度协议**：
///   1. 本地模式（不发外网）诊断的**端到端**往返延迟：p50 / p95 / max；
///   2. 本地模式的阶段构成（规则引擎 / Mod 扫描 / 快照），定位时间花在哪；
///   3. AI 路径的**等待上界**（来自 HttpClient 超时配置）与界面在等待时的状态。
///
/// 明确不覆盖：真实 provider 的 AI 往返延迟（需要另行授权的外发与费用）。
/// </summary>
[Collection("LauncherPerfMeasurement")]
public class DiagnoseProgressGateTests
{
	private readonly ITestOutputHelper _out;

	public DiagnoseProgressGateTests(ITestOutputHelper output) => _out = output;

	private static int Iterations
	{
		get
		{
			string? raw = Environment.GetEnvironmentVariable("MR10_ITERATIONS");
			return int.TryParse(raw, out int n) && n > 0 ? n : 10;
		}
	}

	private static string? EvidenceDirOverride => Environment.GetEnvironmentVariable("MR10_EVIDENCE_DIR");

	private void Report(string metric, string unit, IReadOnlyList<double> samples, string note)
	{
		List<double> sorted = samples.OrderBy(x => x).ToList();
		double P(int p)
		{
			int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
			return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
		}

		var payload = new
		{
			metric,
			unit,
			iterations = sorted.Count,
			min = Math.Round(sorted.First(), 3),
			p50 = Math.Round(P(50), 3),
			p95 = Math.Round(P(95), 3),
			max = Math.Round(sorted.Last(), 3),
			note,
			samples = sorted.Select(x => Math.Round(x, 3)).ToArray()
		};
		string line = JsonSerializer.Serialize(payload);
		_out.WriteLine("MR10 " + line);
		if (!string.IsNullOrWhiteSpace(EvidenceDirOverride))
		{
			File.AppendAllText(Path.Combine(EvidenceDirOverride, "diagnose-latency.jsonl"), line + Environment.NewLine);
		}
	}

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string InstanceId { get; } = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		public AppConfig Config { get; }

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-mr10-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path.Combine(Root, "mods"));
			Directory.CreateDirectory(Path.Combine(Root, "logs"));

			// 一个"像样"的实例：60 个 Mod + 1.2 MB 日志（贴近真实整合包的量级）
			for (int i = 0; i < 60; i++)
			{
				File.WriteAllText(Path.Combine(Root, "mods", "mod" + i.ToString("D3") + ".jar"), "JAR");
			}

			var sb = new StringBuilder();
			for (int i = 0; i < 20000; i++)
			{
				sb.Append("[12:00:00] [main/INFO]: tick ").Append(i).Append('\n');
				if (i % 500 == 0)
				{
					sb.Append("[12:00:00] [main/ERROR]: java.lang.NoClassDefFoundError: example/Cls").Append(i).Append('\n');
				}
			}

			File.WriteAllText(Path.Combine(Root, "logs", "latest.log"), sb.ToString());

			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "mr10", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		public string Api(string suffix = "") => "/api/instances/" + InstanceId + suffix;

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	/* ===================== 1. 本地模式端到端延迟 ===================== */

	[Fact]
	public async Task LocalDiagnose_EndToEndLatency_IsMeasured()
	{
		using var fx = new Fixture();
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		// 热身（JIT + 首次 IO）
		(string _, int warmStatus, _) = await RunOnce(h, fx);
		Assert.Equal(200, warmStatus);

		var samples = new List<double>(Iterations);
		for (int i = 0; i < Iterations; i++)
		{
			(string _, int status, string body) = await RunOnce(h, fx);
			Assert.Equal(200, status);
			using JsonDocument doc = JsonDocument.Parse(body);
			Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
			samples.Add(LastMs);
		}

		Report("diagnose.local.endToEnd", "ms", samples,
			"本地模式（不发外网）POST /diagnose 的端到端往返：60 个 Mod + 1.2 MB 日志");

		// 判定所需的量级：本地模式必须远低于"需要进度条"的阈值
		List<double> sorted = samples.OrderBy(x => x).ToList();
		double p95 = sorted[(int)Math.Ceiling(0.95 * sorted.Count) - 1];
		_out.WriteLine($"[gate] 本地模式 p95 = {p95:F1} ms");
		Assert.True(p95 < 5000, $"本地模式 p95 不应达到需要进度反馈的量级（实测 {p95:F1} ms）");
	}

	private double LastMs;

	private async Task<(string OperationId, int Status, string Body)> RunOnce(LocalApiTestHarness h, Fixture fx)
	{
		string operationId = Guid.NewGuid().ToString("N");
		var sw = Stopwatch.StartNew();
		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/diagnose"),
			new { operationId, localOnly = true, locale = "zh" });
		sw.Stop();
		LastMs = sw.Elapsed.TotalMilliseconds;
		return (operationId, status, body);
	}

	/* ===================== 2. 阶段构成 ===================== */

	[Fact]
	public async Task LocalDiagnose_StageBreakdown_IsMeasured()
	{
		using var fx = new Fixture();

		var service = new ModService(fx.Root);
		var instance = fx.Config.Instances[0];

		// 阶段 A：Mod 扫描（诊断要枚举 Mod 健康）
		var modSamples = new List<double>();
		for (int i = 0; i < Iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			List<ModFile> mods = await service.GetModsAsync(instance);
			sw.Stop();
			Assert.Equal(60, mods.Count);
			modSamples.Add(sw.Elapsed.TotalMilliseconds);
		}

		Report("diagnose.stage.modScan", "ms", modSamples, "阶段：枚举 60 个 Mod 并读 manifest");

		// 阶段 B：规则引擎（读日志尾部 + 分桶）
		var ruleSamples = new List<double>();
		for (int i = 0; i < Iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			CrashAnalysis analysis = CrashAnalyzerService.Analyze(fx.Root, 1);
			sw.Stop();
			Assert.False(string.IsNullOrWhiteSpace(analysis.Summary));
			ruleSamples.Add(sw.Elapsed.TotalMilliseconds);
		}

		Report("diagnose.stage.ruleEngine", "ms", ruleSamples, "阶段：读 1.2 MB 日志尾部并跑规则引擎");

		// 阶段 C：脱敏 + 证据挑选（MR21 已确认这是最慢的一段）
		var sanitizeSamples = new List<double>();
		string log = File.ReadAllText(Path.Combine(fx.Root, "logs", "latest.log"));
		for (int i = 0; i < Iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			string evidence = DiagnosticLogSanitizer.SelectEvidence(log, 4000);
			sw.Stop();
			Assert.False(string.IsNullOrEmpty(evidence));
			sanitizeSamples.Add(sw.Elapsed.TotalMilliseconds);
		}

		Report("diagnose.stage.sanitize", "ms", sanitizeSamples, "阶段：长日志脱敏 + 证据挑选");

		// 阶段 D：Java 查找。诊断 handler 会 `await JavaLocator.FindAsync()` 填 javaVersion；
		// 这是"看起来不属于诊断工作"的一段，实测发现它才是本地模式的主要成本。
		// 迭代次数刻意压小：这段可能很慢，不必测 10 次才能看出量级。
		var javaSamples = new List<double>();
		int javaIterations = Math.Min(Iterations, 3);
		for (int i = 0; i < javaIterations; i++)
		{
			var sw = Stopwatch.StartNew();
			JavaInfo? info = await JavaLocator.FindAsync();
			sw.Stop();
			javaSamples.Add(sw.Elapsed.TotalMilliseconds);
			_out.WriteLine($"[gate] JavaLocator.FindAsync #{i + 1} = {sw.Elapsed.TotalMilliseconds:F1} ms（found={info != null}）");
		}

		Report("diagnose.stage.javaFind", "ms", javaSamples,
			"阶段：JavaLocator.FindAsync（诊断用它填 javaVersion；本会话实测的主要成本）");
	}

	/* ===================== 3. 等待上界与界面状态（契约） ===================== */

	[Fact]
	public void AiWaitBound_IsSixtySeconds_AndUiShowsHonestWaitWithCancel()
	{
		// 等待上界：来自 HttpClient 超时配置（不是前端计时器推断）
		Assert.Equal(60, AiHttpClientFactory.DefaultTimeoutSeconds);
		_out.WriteLine($"[gate] AI 等待上界 = {AiHttpClientFactory.DefaultTimeoutSeconds} s（客户端超时）");

		// 界面在等待时的状态：必须只有"等待中"文案 + 取消按钮，**不得**有虚假百分比。
		string launcher = File.ReadAllText(FindUiFile("launcher.js"));
		int idx = launcher.IndexOf("diag.analyzing", StringComparison.Ordinal);
		Assert.True(idx > 0, "诊断等待状态必须存在");
		string block = launcher.Substring(Math.Max(0, idx - 200), Math.Min(600, launcher.Length - Math.Max(0, idx - 200)));
		Assert.Contains("dCancelRequest", block);
		// 诊断等待块里不得出现百分比插值（虚假进度）
		Assert.DoesNotContain("%", block.Replace("100%", "").Replace("width", ""));
		_out.WriteLine("[gate] 诊断等待状态 = 文案 + 取消按钮；未发现虚假百分比");
	}

	private static string FindUiFile(string name)
	{
		// AppContext.BaseDirectory **末尾带分隔符**，而 Path.GetDirectoryName(".../net10.0/")
		// 返回的是同一个目录（只去掉末尾分隔符），所以旧写法（固定 6 次循环）实际只向上走了
		// 4~5 层。Windows 上碰巧在 i=0 就命中 —— 主项目会把 ui 复制到输出目录的 UI/，
		// 而 NTFS 大小写不敏感，Path.Combine(dir, "ui", "js", name) 能匹配 "UI/js/..."。
		// Linux 上两者都不成立：文件系统大小写敏感（ui ≠ UI），再加上少走一层，
		// 于是仓库根下的 ui/js/launcher.js 永远找不到。
		// 正确写法：先剥掉末尾分隔符，再按"仓库根标记"向上找，不依赖层数。
		string dir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
		for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
		{
			string candidate = Path.Combine(dir, "ui", "js", name);
			if (File.Exists(candidate))
			{
				return candidate;
			}

			dir = Path.GetDirectoryName(dir) ?? string.Empty;
		}

		throw new FileNotFoundException("找不到 UI 文件：" + name + "（起点 " + AppContext.BaseDirectory + "）");
	}
}
