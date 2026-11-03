using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>MR21：测量必须串行，且不与其它测试类并行（否则计时不可比）。</summary>
[CollectionDefinition("LauncherPerfMeasurement", DisableParallelization = true)]
public sealed class LauncherPerfMeasurementCollection
{
}

/// <summary>
/// MR21：启动/列表/诊断等**热路径的服务层测量夹具**。
///
/// 本包**只做测量**：卡片明确"本包初始生产代码只读"、"只完成测量不写性能提升"。
/// 因此这里：
///   - **不对性能做任何断言**（只对"结果正确"做最小断言，避免测到空数据）；
///   - 每个指标先热身，再记录多次迭代的耗时；
///   - 以 `MR21 {...}` 前缀把原始数据打到 stdout，由 scripts/measure-mr21.ps1 采集，
///     并写到 MR21_EVIDENCE_DIR（未设置时写 %TEMP%）。
///
/// 明确**不覆盖**：真实 GUI 首屏可交互、WebView2 渲染、真实账号登录、真实启动游戏。
/// 这些需要人参与或有桌面会话，属 MR21 的后续观测或 MR22 内测范围。
/// </summary>
[Collection("LauncherPerfMeasurement")]
public class LauncherPerfMeasurementTests
{
	private readonly ITestOutputHelper _out;

	public LauncherPerfMeasurementTests(ITestOutputHelper output) => _out = output;

	private static int Iterations
	{
		get
		{
			// 默认 5 次：让日常 `dotnet test` 不被测量拖慢。
			// 冻结基线用的是 scripts/measure-mr21.ps1 显式设置的 20 次。
			string? raw = Environment.GetEnvironmentVariable("MR21_ITERATIONS");
			return int.TryParse(raw, out int n) && n > 0 ? n : 5;
		}
	}

	private static string? EvidenceDirOverride => Environment.GetEnvironmentVariable("MR21_EVIDENCE_DIR");

	private static string EvidenceDir
	{
		get
		{
			string? dir = EvidenceDirOverride;
			if (string.IsNullOrWhiteSpace(dir))
			{
				dir = Path.Combine(Path.GetTempPath(), "pineglow-mr21");
			}

			Directory.CreateDirectory(dir);
			return dir;
		}
	}

	/// <summary>把一次测量的原始数据同时打到 stdout 与证据目录。</summary>
	private void Report(string metric, string unit, IReadOnlyList<double> samples, long workingSetBytes, string note)
	{
		List<double> sorted = samples.OrderBy(x => x).ToList();
		double p50 = Percentile(sorted, 50);
		double p95 = Percentile(sorted, 95);
		var payload = new
		{
			metric,
			unit,
			iterations = sorted.Count,
			min = Math.Round(sorted.First(), 3),
			p50 = Math.Round(p50, 3),
			p95 = Math.Round(p95, 3),
			max = Math.Round(sorted.Last(), 3),
			workingSetMb = Math.Round(workingSetBytes / 1024.0 / 1024.0, 2),
			note,
			// 原始样本一并落盘：任何百分位都能被人工重新推导（"原始测量可复查"）
			samples = sorted.Select(x => Math.Round(x, 3)).ToArray(),
			utc = DateTimeOffset.UtcNow.ToString("o")
		};
		string line = JsonSerializer.Serialize(payload);
		_out.WriteLine("MR21 " + line);
		// 只有显式指定证据目录时才落盘（日常跑测试不留临时文件）
		if (!string.IsNullOrWhiteSpace(EvidenceDirOverride))
		{
			File.AppendAllText(Path.Combine(EvidenceDir, "raw-metrics.jsonl"), line + Environment.NewLine);
		}
	}

	/// <summary>最近秩百分位（不做插值，便于人工复查）。</summary>
	private static double Percentile(IReadOnlyList<double> sorted, int percent)
	{
		if (sorted.Count == 0)
		{
			return 0;
		}

		int rank = (int)Math.Ceiling(percent / 100.0 * sorted.Count);
		return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
	}

	private static long WorkingSet() => Process.GetCurrentProcess().WorkingSet64;

	/* ===================== 合成数据 ===================== */

	private sealed class TempDir : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-mr21-" + Guid.NewGuid().ToString("N"));

		public TempDir() => Directory.CreateDirectory(Path);

		public string File(string name) => System.IO.Path.Combine(Path, name);

		public void Dispose()
		{
			try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { }
		}
	}

	/// <summary>写一个带最小 fabric.mod.json 的 jar（比空 zip 更接近真实扫描成本）。</summary>
	private static void WriteJar(string path, int index)
	{
		using FileStream fs = File.Create(path);
		using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
		ZipArchiveEntry entry = zip.CreateEntry("fabric.mod.json");
		using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
		writer.Write($"{{\"schemaVersion\":1,\"id\":\"mod{index:D4}\",\"version\":\"1.0.0\",\"name\":\"Mod {index:D4}\"}}");
	}

	private static string BuildLongLog(int lines)
	{
		var sb = new StringBuilder(lines * 90);
		for (int i = 0; i < lines; i++)
		{
			sb.Append("[12:00:").Append(i % 60).Append("] [main/INFO]: tick ").Append(i)
				.Append(" chunk loaded at ").Append(i % 512).Append(", 64, ").Append(i % 512).Append('\n');
			if (i % 500 == 0)
			{
				sb.Append("[12:00:00] [main/ERROR]: java.lang.NoClassDefFoundError: example/mod/Class").Append(i).Append('\n');
			}
		}

		return sb.ToString();
	}

	private static string BuildPackIndex(int files)
	{
		var sb = new StringBuilder(files * 180);
		sb.Append("{\"formatVersion\":1,\"name\":\"perf\",\"dependencies\":{\"minecraft\":\"1.20.4\",\"fabric-loader\":\"0.15.0\"},\"files\":[");
		for (int i = 0; i < files; i++)
		{
			if (i > 0)
			{
				sb.Append(',');
			}

			sb.Append("{\"path\":\"mods/mod").Append(i.ToString("D4"))
				.Append(".jar\",\"downloads\":[\"https://cdn.example.com/mods/mod").Append(i.ToString("D4"))
				.Append(".jar\"],\"hashes\":{\"sha1\":\"").Append(new string('a', 40)).Append("\"}}");
		}

		sb.Append("]}");
		return sb.ToString();
	}

	/* ===================== 1. Mod 列表（100 / 500） ===================== */

	[Theory]
	[InlineData(100)]
	[InlineData(500)]
	public async Task Measure_ModListLoad(int modCount)
	{
		using var dir = new TempDir();
		string modsDir = System.IO.Path.Combine(dir.Path, "mods");
		Directory.CreateDirectory(modsDir);
		for (int i = 0; i < modCount; i++)
		{
			WriteJar(System.IO.Path.Combine(modsDir, "mod" + i.ToString("D4") + ".jar"), i);
		}

		var instance = new GameInstance { Id = "perf", Name = "perf", GameDirectory = dir.Path, MinecraftVersion = "1.20.4" };
		var service = new ModService(dir.Path);

		// 热身（JIT + 首次 IO），不计入样本
		List<ModFile> warm = await service.GetModsAsync(instance);
		Assert.Equal(modCount, warm.Count);

		var samples = new List<double>(Iterations);
		for (int i = 0; i < Iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			List<ModFile> mods = await service.GetModsAsync(instance);
			sw.Stop();
			Assert.Equal(modCount, mods.Count);
			samples.Add(sw.Elapsed.TotalMilliseconds);
		}

		Report($"mods.list.{modCount}", "ms", samples, WorkingSet(), $"扫描 {modCount} 个 jar 并解析 manifest");
	}

	/* ===================== 2. 诊断长文本 ===================== */

	[Fact]
	public void Measure_DiagnosticAnalyze_LongLog()
	{
		using var dir = new TempDir();
		string logsDir = System.IO.Path.Combine(dir.Path, "logs");
		Directory.CreateDirectory(logsDir);
		string log = BuildLongLog(20000); // ≈ 1.8 MB
		File.WriteAllText(System.IO.Path.Combine(logsDir, "latest.log"), log);

		CrashAnalysis warm = CrashAnalyzerService.Analyze(dir.Path, 1);
		Assert.False(string.IsNullOrWhiteSpace(warm.Summary));

		var samples = new List<double>(Iterations);
		for (int i = 0; i < Iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			CrashAnalysis analysis = CrashAnalyzerService.Analyze(dir.Path, 1);
			sw.Stop();
			Assert.False(string.IsNullOrWhiteSpace(analysis.Summary));
			samples.Add(sw.Elapsed.TotalMilliseconds);
		}

		Report("diagnostic.analyze.longlog", "ms", samples, WorkingSet(), $"读 {log.Length / 1024} KB 日志并跑规则引擎");

		// 脱敏/证据挑选（诊断面板的另一段热路径）
		var sanitizeSamples = new List<double>(Iterations);
		for (int i = 0; i < Iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			string evidence = DiagnosticLogSanitizer.SelectEvidence(log, 4000);
			sw.Stop();
			Assert.False(string.IsNullOrEmpty(evidence));
			sanitizeSamples.Add(sw.Elapsed.TotalMilliseconds);
		}

		Report("diagnostic.sanitize.longlog", "ms", sanitizeSamples, WorkingSet(), "长日志脱敏 + 证据挑选（当前实现：先脱敏整段再挑行）");

		// ------------------------------------------------------------------
		// MR21 候选修复的**测量实验**（只在本测量夹具里实现，未进入产品代码）：
		// 当前 SelectEvidence 先对整段日志跑 13 条正则，再挑相关行；
		// 候选做法是**先挑行、只脱敏被选中的那几行**。
		// 本包初始生产代码只读，因此这里只量化收益，不改产品实现。
		// ------------------------------------------------------------------
		var candidateSamples = new List<double>(Iterations);
		for (int i = 0; i < Iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			string candidate = SelectEvidenceCandidate(log, 4000);
			sw.Stop();
			Assert.False(string.IsNullOrEmpty(candidate));
			candidateSamples.Add(sw.Elapsed.TotalMilliseconds);
		}

		Report("diagnostic.sanitize.selectedOnly", "ms", candidateSamples, WorkingSet(),
			"候选修复（测量实验，未进产品代码）：先挑相关行、只脱敏选中行");
	}

	/// <summary>
	/// MR21 候选修复的**测量用**实现：先按相关性挑行，再只对选中的行脱敏。
	/// 与产品实现的差异（必须写进变化风险）：
	///   - 产品实现先脱敏整段，再按**脱敏后**的文本判相关性；
	///   - 候选实现按**原始**文本判相关性，再脱敏。
	/// 因此当某行的关键词本身会被脱敏吃掉时（例如 `Authorization: Bearer Error123`），
	/// 两者选中的行集合可能略有不同。
	/// </summary>
	private static string SelectEvidenceCandidate(string input, int maxCharacters)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		string[] lines = input.Split('\n');
		string[] relevant = lines
			.Where(l => !string.IsNullOrWhiteSpace(l))
			.Where(l => l.Contains("Caused by", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("Error", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("OutOfMemory", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("UnsupportedClass", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("NoClassDefFound", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("Missing", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("Mixin", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("OpenGL", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("EXCEPTION_ACCESS_VIOLATION", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("hs_err", StringComparison.OrdinalIgnoreCase)
				|| l.Contains("ModResolution", StringComparison.OrdinalIgnoreCase))
			.ToArray();
		if (relevant.Length == 0)
		{
			relevant = lines.TakeLast(80).ToArray();
		}

		string joined = string.Join("\n", relevant);
		string sanitized = DiagnosticLogSanitizer.Sanitize(joined);
		return sanitized.Length <= maxCharacters ? sanitized : sanitized.Substring(sanitized.Length - maxCharacters);
	}

	/* ===================== 3. .mrpack 清单解析（500 文件） ===================== */

	[Fact]
	public void Measure_MrpackIndexParse_500Files()
	{
		string json = BuildPackIndex(500);

		InstancePackService.ModrinthPackManifest warm = InstancePackService.ParseModrinthIndex(json);
		Assert.Equal(500, warm.Files.Count);

		var samples = new List<double>(Iterations);
		for (int i = 0; i < Iterations; i++)
		{
			var sw = Stopwatch.StartNew();
			InstancePackService.ModrinthPackManifest manifest = InstancePackService.ParseModrinthIndex(json);
			sw.Stop();
			Assert.Equal(500, manifest.Files.Count);
			samples.Add(sw.Elapsed.TotalMilliseconds);
		}

		Report("mrpack.parse.500files", "ms", samples, WorkingSet(), $"解析 {json.Length / 1024} KB 的 modrinth.index.json");
	}

	/* ===================== 4. 环境事实（供报告冻结） ===================== */

	[Fact]
	public void Measure_EnvironmentFacts()
	{
		bool webView2Present = false;
		try
		{
			// 以 Edge WebView2 运行时的标准安装目录是否存在为准（不读用户数据）
			string[] candidates =
			{
				System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application"),
				System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "EdgeWebView", "Application")
			};
			webView2Present = candidates.Any(c => Directory.Exists(c) && Directory.EnumerateDirectories(c).Any());
		}
		catch
		{
		}

		var facts = new
		{
			osVersion = Environment.OSVersion.VersionString,
			is64BitOs = Environment.Is64BitOperatingSystem,
			logicalCores = Environment.ProcessorCount,
			dotnetVersion = Environment.Version.ToString(),
			webView2Present,
			processArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
			utc = DateTimeOffset.UtcNow.ToString("o")
		};
		string line = JsonSerializer.Serialize(facts);
		_out.WriteLine("MR21-ENV " + line);
		File.WriteAllText(System.IO.Path.Combine(EvidenceDir, "env-facts.json"), line);

		Assert.True(facts.logicalCores > 0);
	}
}
