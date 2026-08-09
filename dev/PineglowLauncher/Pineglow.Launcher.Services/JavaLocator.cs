using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public static class JavaLocator
{
	public static async Task<JavaInfo?> ValidateAsync(string javaPath)
	{
		JavaInstallation javaInstallation = await ValidateInstallationAsync(javaPath);
		if (javaInstallation == null)
		{
			return null;
		}
		return new JavaInfo
		{
			Path = javaInstallation.JavaPath,
			VersionText = javaInstallation.VersionText,
			VersionMajor = javaInstallation.VersionMajor
		};
	}

	public static async Task<JavaInstallation?> ValidateInstallationAsync(string javaPath, Action<string>? log = null)
	{
		if (string.IsNullOrWhiteSpace(javaPath))
		{
			return null;
		}
		if (!File.Exists(javaPath))
		{
			if (!javaPath.Equals("java", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			javaPath = FindOnPath().FirstOrDefault() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
			{
				return null;
			}
		}
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = javaPath,
				Arguments = "-version",
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = (Path.GetDirectoryName(Path.GetFullPath(javaPath)) ?? Environment.CurrentDirectory)
			};
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				return null;
			}
			string stderr = await process.StandardError.ReadToEndAsync();
			string stdout = await process.StandardOutput.ReadToEndAsync();
			await process.WaitForExitAsync();
			string text = (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
			string text2 = FirstLine(text);
			int num = ParseMajorVersion(text);
			string vendor = ParseVendor(text, javaPath);
			log?.Invoke($"Java 版本解析结果：{javaPath} => {text2} (Major={num})");
			return (process.ExitCode == 0 && num > 0) ? new JavaInstallation
			{
				JavaPath = javaPath,
				VersionText = text2,
				VersionMajor = num,
				Vendor = vendor,
				IsValid = true
			} : null;
		}
		catch (Exception ex)
		{
			log?.Invoke("Java 校验失败：" + javaPath + " - " + ex.Message);
			return null;
		}
	}

	public static async Task<JavaInfo?> FindAsync()
	{
		JavaInstallation javaInstallation = (await ScanAsync()).FirstOrDefault((JavaInstallation x) => x.IsValid);
		return (javaInstallation == null) ? null : new JavaInfo
		{
			Path = javaInstallation.JavaPath,
			VersionText = javaInstallation.VersionText,
			VersionMajor = javaInstallation.VersionMajor
		};
	}

	public static async Task<List<JavaInstallation>> ScanAsync(Action<string>? log = null)
	{
		log?.Invoke("扫描 Java 开始");
		List<string> source = EnumerateCandidates().Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		List<JavaInstallation> results = new List<JavaInstallation>();
		object lockObj = new object();
		SemaphoreSlim semaphore = new SemaphoreSlim(4);
		await Parallel.ForEachAsync(source, async delegate(string candidate, CancellationToken ct)
		{
			await semaphore.WaitAsync(ct);
			try
			{
				log?.Invoke("检测到 Java 路径：" + candidate);
				JavaInstallation info = await ValidateInstallationAsync(candidate, log);
				if (info != null)
				{
					lock (lockObj)
					{
						if (results.All((JavaInstallation x) => !x.JavaPath.Equals(info.JavaPath, StringComparison.OrdinalIgnoreCase)))
						{
							results.Add(info);
						}
					}
				}
			}
			finally
			{
				semaphore.Release();
			}
		});
		return (from x in results
			orderby x.VersionMajor, x.JavaPath
			select x).ToList();
	}

	public static JavaInstallation? MatchForMinecraftVersion(IEnumerable<JavaInstallation> installations, string minecraftVersion, Action<string>? log = null)
	{
		int requiredJavaMajor = JavaVersionMatcher.GetRequiredJavaMajor(minecraftVersion, log);
		return MatchForRequiredMajor(installations, requiredJavaMajor, minecraftVersion, log);
	}

	public static JavaInstallation? MatchForMinecraftVersion(IEnumerable<JavaInstallation> installations, string minecraftVersion, string gameDirectory, Action<string>? log = null)
	{
		int requiredJavaMajor = JavaVersionMatcher.GetRequiredJavaMajor(minecraftVersion, gameDirectory, log);
		return MatchForRequiredMajor(installations, requiredJavaMajor, minecraftVersion, log);
	}

	private static JavaInstallation? MatchForRequiredMajor(IEnumerable<JavaInstallation> installations, int required, string minecraftVersion, Action<string>? log)
	{
		log?.Invoke($"当前 Minecraft 版本 {minecraftVersion} 需要 Java {required}");
		JavaInstallation javaInstallation = (from x in installations
			where x.IsValid && JavaVersionMatcher.IsCompatible(x.VersionMajor, required)
			orderby x.VersionMajor
			select x).FirstOrDefault();
		if (javaInstallation != null)
		{
			log?.Invoke($"自动匹配到 Java {javaInstallation.VersionMajor}：{javaInstallation.JavaPath}");
		}
		else
		{
			log?.Invoke($"未找到合适 Java：需要 Java {required}");
		}
		return javaInstallation;
	}

	public static int ParseMajorVersion(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return 0;
		}
		Match match = Regex.Match(text, "(?:java|openjdk) version \\\"(?<version>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			match = Regex.Match(text, "version \\\"(?<version>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
		}
		if (!match.Success)
		{
			return 0;
		}
		string value = match.Groups["version"].Value;
		if (value.StartsWith("1.", StringComparison.Ordinal))
		{
			Match match2 = Regex.Match(value, "^1\\.(\\d+)");
			if (!match2.Success)
			{
				return 0;
			}
			return int.Parse(match2.Groups[1].Value);
		}
		Match match3 = Regex.Match(value, "^(\\d+)");
		if (!match3.Success)
		{
			return 0;
		}
		return int.Parse(match3.Groups[1].Value);
	}

	private static IEnumerable<string> EnumerateCandidates()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("JAVA_HOME");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			yield return Path.Combine(environmentVariable, "bin", JavaExeName());
		}
		foreach (string item in FindOnPath())
		{
			yield return item;
		}
		string text = Path.Combine(PlatformPaths.GetConfigDirectory(), "runtimes");
		if (Directory.Exists(text))
		{
			foreach (string item2 in SafeEnumerateFiles(text, JavaExeName()))
			{
				yield return item2;
			}
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			List<string?> roots = new List<string>
			{
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
			};
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			string[] source = new string[7]
			{
				Path.Combine(folderPath, "Java"),
				Path.Combine(folderPath, "Eclipse Adoptium"),
				Path.Combine(folderPath, "Microsoft"),
				Path.Combine(folderPath, "Zulu"),
				Path.Combine(folderPath, "BellSoft"),
				Path.Combine(folderPath, "Amazon Corretto"),
				Path.Combine(folderPath2, "Java")
			};
			foreach (string item3 in source.Where((string x) => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x)))
			{
				foreach (string item4 in SafeEnumerateDirectories(item3, "*", SearchOption.TopDirectoryOnly))
				{
					yield return Path.Combine(item4, "bin", JavaExeName());
				}
			}
			foreach (string item5 in roots.Where((string x) => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x)))
			{
				foreach (string item6 in SafeEnumerateDirectories(item5, "*jdk*", SearchOption.AllDirectories).Concat(SafeEnumerateDirectories(item5, "*jre*", SearchOption.AllDirectories)))
				{
					yield return Path.Combine(item6, "bin", JavaExeName());
				}
			}
		}
		else
		{
			yield return "/usr/bin/java";
			yield return "/usr/local/bin/java";
			yield return "/opt/homebrew/bin/java";
		}
	}

	private static IEnumerable<string> FindOnPath()
	{
		string text = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		char separator = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':');
		string[] array = text.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = Path.Combine(array[i], JavaExeName());
			if (File.Exists(text2))
			{
				yield return text2;
			}
		}
	}

	private static IEnumerable<string> SafeEnumerateDirectories(string root, string pattern, SearchOption option)
	{
		try
		{
			return Directory.EnumerateDirectories(root, pattern, option).ToList();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static IEnumerable<string> SafeEnumerateFiles(string root, string fileName)
	{
		try
		{
			return (from path in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
				where string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase)
				select path).ToList();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static string JavaExeName()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			return "java";
		}
		return "java.exe";
	}

	private static string FirstLine(string text)
	{
		using StringReader stringReader = new StringReader(text.Trim());
		return stringReader.ReadLine() ?? text.Trim();
	}

	private static string ParseVendor(string text, string path)
	{
		string text2 = (text + " " + path).ToLowerInvariant();
		if (text2.Contains("adoptium") || text2.Contains("temurin"))
		{
			return "Eclipse Adoptium";
		}
		if (text2.Contains("microsoft"))
		{
			return "Microsoft";
		}
		if (text2.Contains("zulu"))
		{
			return "Zulu";
		}
		if (text2.Contains("bellsoft") || text2.Contains("liberica"))
		{
			return "BellSoft";
		}
		if (text2.Contains("corretto") || text2.Contains("amazon"))
		{
			return "Amazon Corretto";
		}
		if (text2.Contains("oracle") || text2.Contains("java(tm)"))
		{
			return "Oracle";
		}
		if (text2.Contains("openjdk"))
		{
			return "OpenJDK";
		}
		return string.Empty;
	}
}
