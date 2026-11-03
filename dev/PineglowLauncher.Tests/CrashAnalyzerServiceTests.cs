using System.IO;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>CrashAnalyzerService.Analyze 规则分支测试（规则引擎对日志关键字的归因分支）。</summary>
public class CrashAnalyzerRuleBranchTests
{
	private static string NewRoot()
	{
		return Path.Combine(Path.GetTempPath(), "pineglow-analyze-" + Guid.NewGuid().ToString("N"));
	}

	private static string WriteLog(string root, string content)
	{
		string dir = Path.Combine(root, "logs");
		Directory.CreateDirectory(dir);
		string log = Path.Combine(dir, "latest.log");
		File.WriteAllText(log, content);
		return log;
	}

	private static CrashAnalysis AnalyzeWith(string content, int exitCode = 1)
	{
		string root = NewRoot();
		try
		{
			string log = WriteLog(root, content);
			CrashAnalysis analysis = CrashAnalyzerService.Analyze(root, exitCode);
			Assert.NotNull(analysis.Summary);
			Assert.NotNull(analysis.Suggestions);
			Assert.NotNull(analysis.SourceFile);
			Assert.Equal(log, analysis.SourceFile);
			return analysis;
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void Analyze_OomKeyword_AttributesToMemory()
	{
		CrashAnalysis analysis = AnalyzeWith("java.lang.OutOfMemoryError: Java heap space");

		Assert.Contains("内存", analysis.Summary);
		Assert.Equal(2, analysis.Suggestions.Count);
	}

	[Fact]
	public void Analyze_JavaVersionKeyword_AttributesToJava()
	{
		CrashAnalysis analysis = AnalyzeWith("java.lang.UnsupportedClassVersionError: class file version 61.0");

		Assert.Contains("Java", analysis.Summary);
		Assert.Contains("Java", analysis.Suggestions[0]);
	}

	[Fact]
	public void Analyze_ModDependencyKeyword_AttributesToMods()
	{
		CrashAnalysis analysis = AnalyzeWith("net.fabricmc.loader.impl.discovery.ModResolutionException: Missing mandatory dependencies");

		Assert.Contains("Mod", analysis.Summary);
		Assert.Equal(2, analysis.Suggestions.Count);
	}

	[Fact]
	public void Analyze_NativeGraphicsKeyword_AttributesToGfx()
	{
		CrashAnalysis analysis = AnalyzeWith("EXCEPTION_ACCESS_VIOLATION (0xc0000005) at nvoglv64.dll");

		Assert.Contains("显卡", analysis.Summary);
		Assert.Equal(2, analysis.Suggestions.Count);
	}

	[Fact]
	public void Analyze_NoMatch_UsesExitCodeFallback()
	{
		CrashAnalysis analysis = AnalyzeWith("unknown random crash line", exitCode: 7);

		Assert.Contains("7", analysis.Summary);
		Assert.Single(analysis.Suggestions);
	}

	[Fact]
	public void Analyze_NewerCrashReportWinsOverLog()
	{
		string root = NewRoot();
		try
		{
			WriteLog(root, "OutOfMemoryError");
			string crashDir = Path.Combine(root, "crash-reports");
			Directory.CreateDirectory(crashDir);
			string crash = Path.Combine(crashDir, "crash-2026.txt");
			File.WriteAllText(crash, "UnsupportedClassVersionError");
			File.SetLastWriteTimeUtc(crash, DateTime.UtcNow);

			CrashAnalysis analysis = CrashAnalyzerService.Analyze(root, 1);

			Assert.Equal(crash, analysis.SourceFile);
			Assert.Contains("Java", analysis.Summary);
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
		}
	}
}