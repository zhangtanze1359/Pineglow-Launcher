using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class JavaRuntimeService
{
	private const string ApiBase = "https://api.adoptium.net/v3/assets/latest";

	private readonly HttpClient _http;

	private readonly Action<string> _log;

	public JavaRuntimeService(HttpClient http, Action<string> log)
	{
		_http = http;
		_log = log;
	}

	public async Task<JavaInstallation> InstallAsync(int majorVersion, IProgress<JavaRuntimeProgress>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		bool flag;
		switch (majorVersion)
		{
		case 8:
		case 16:
		case 17:
		case 21:
		case 25:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new InvalidOperationException($"暂不支持自动下载 Java {majorVersion}。");
		}
		JavaInstallation javaInstallation = await FindInstalledRuntimeAsync(majorVersion);
		if (javaInstallation != null)
		{
			return javaInstallation;
		}
		string os = (OperatingSystem.IsWindows() ? "windows" : (OperatingSystem.IsMacOS() ? "mac" : "linux"));
		string architecture = RuntimeInformation.OSArchitecture switch
		{
			Architecture.X64 => "x64", 
			Architecture.X86 => "x86", 
			Architecture.Arm64 => "aarch64", 
			Architecture.Arm => "arm", 
			_ => throw new PlatformNotSupportedException($"不支持的 CPU 架构：{RuntimeInformation.OSArchitecture}"), 
		};
		string requestUri = $"{"https://api.adoptium.net/v3/assets/latest"}/{majorVersion}/hotspot?architecture={architecture}&image_type=jre&os={os}&vendor=eclipse";
		progress?.Report(new JavaRuntimeProgress($"正在获取 Java {majorVersion} 下载信息...", 0.0));
		using HttpResponseMessage metadataResponse = await _http.GetAsync(requestUri, ct);
		metadataResponse.EnsureSuccessStatusCode();
		JavaInstallation result;
		await using (Stream metadataStream = await metadataResponse.Content.ReadAsStreamAsync(ct))
		{
			using JsonDocument metadata = await JsonDocument.ParseAsync(metadataStream, default(JsonDocumentOptions), ct);
			JsonElement jsonElement = ((metadata.RootElement.ValueKind == JsonValueKind.Array) ? metadata.RootElement.EnumerateArray().FirstOrDefault() : default(JsonElement));
			if (jsonElement.ValueKind == JsonValueKind.Undefined || !jsonElement.TryGetProperty("binary", out var value) || !value.TryGetProperty("package", out var value2))
			{
				throw new InvalidDataException($"Adoptium 没有返回 Java {majorVersion} 的可用 JRE。");
			}
			string text = JsonUtil.GetString(value2, "link");
			string checksum = JsonUtil.GetString(value2, "checksum");
			string packageName = Path.GetFileName(JsonUtil.GetString(value2, "name"));
			if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(packageName))
			{
				throw new InvalidDataException("Adoptium 返回的 Java 下载信息不完整。");
			}
			string text2 = Path.Combine(PlatformPaths.GetConfigDirectory(), "runtimes");
			Directory.CreateDirectory(text2);
			string installDirectory = Path.Combine(text2, $"java-{majorVersion}-{os}-{architecture}");
			string workDirectory = Path.Combine(text2, ".install-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(workDirectory);
			string archivePath = Path.Combine(workDirectory, packageName);
			string extractDirectory = Path.Combine(workDirectory, "extracted");
			try
			{
				await DownloadAsync(text, archivePath, majorVersion, progress, ct);
				flag = !string.IsNullOrWhiteSpace(checksum);
				if (flag)
				{
					flag = !(await Sha256MatchesAsync(archivePath, checksum, ct));
				}
				if (flag)
				{
					throw new InvalidDataException("Java 运行时 SHA-256 校验失败，文件可能损坏。");
				}
				progress?.Report(new JavaRuntimeProgress($"正在解压 Java {majorVersion}...", 0.92));
				Directory.CreateDirectory(extractDirectory);
				if (packageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				{
					ZipFile.ExtractToDirectory(archivePath, extractDirectory);
				}
				else
				{
					if (!packageName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) && !packageName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidDataException("不支持的 Java 压缩包格式：" + packageName);
					}
					await using FileStream archive = File.OpenRead(archivePath);
					await using GZipStream source = new GZipStream(archive, CompressionMode.Decompress);
					TarFile.ExtractToDirectory(source, extractDirectory, overwriteFiles: false);
				}
				string javaPath = FindJavaExecutable(extractDirectory) ?? throw new InvalidDataException("Java 压缩包内未找到 java 可执行文件。");
				if (Directory.Exists(installDirectory))
				{
					Directory.Delete(installDirectory, recursive: true);
				}
				Directory.Move(extractDirectory, installDirectory);
				javaPath = Path.Combine(installDirectory, Path.GetRelativePath(extractDirectory, javaPath));
				EnsureUnixExecutable(javaPath);
				JavaInstallation javaInstallation2 = (await JavaLocator.ValidateInstallationAsync(javaPath, _log)) ?? throw new InvalidDataException("下载的 Java 无法运行或版本无法识别。");
				if (javaInstallation2.VersionMajor != majorVersion)
				{
					throw new InvalidDataException($"下载的 Java 版本不正确：需要 {majorVersion}，实际 {javaInstallation2.VersionMajor}。");
				}
				progress?.Report(new JavaRuntimeProgress($"Java {majorVersion} 安装完成。", 1.0));
				_log($"Java {majorVersion} 自动安装完成：{javaPath}");
				result = javaInstallation2;
			}
			finally
			{
				if (Directory.Exists(workDirectory))
				{
					Directory.Delete(workDirectory, recursive: true);
				}
			}
		}
		return result;
	}

	private async Task<JavaInstallation?> FindInstalledRuntimeAsync(int majorVersion)
	{
		string path = Path.Combine(PlatformPaths.GetConfigDirectory(), "runtimes");
		if (!Directory.Exists(path))
		{
			return null;
		}
		foreach (string item in Directory.EnumerateDirectories(path, $"java-{majorVersion}-*"))
		{
			string text = FindJavaExecutable(item);
			if (text != null)
			{
				JavaInstallation javaInstallation = await JavaLocator.ValidateInstallationAsync(text);
				if (javaInstallation != null && javaInstallation.VersionMajor == majorVersion)
				{
					return javaInstallation;
				}
			}
		}
		return null;
	}

	private async Task DownloadAsync(string url, string outputPath, int majorVersion, IProgress<JavaRuntimeProgress>? progress, CancellationToken ct)
	{
		using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		long total = response.Content.Headers.ContentLength.GetValueOrDefault();
		await using Stream input = await response.Content.ReadAsStreamAsync(ct);
		await using FileStream output = File.Create(outputPath);
		byte[] buffer = new byte[131072];
		long received = 0L;
		while (true)
		{
			int num;
			int read = (num = await input.ReadAsync(buffer, ct));
			if (num <= 0)
			{
				break;
			}
			await output.WriteAsync(buffer.AsMemory(0, read), ct);
			received += read;
			double num2 = ((total > 0) ? Math.Min(1.0, (double)received / (double)total) : 0.0);
			progress?.Report(new JavaRuntimeProgress($"正在下载 Java {majorVersion}：{FormatBytes(received)} / {((total > 0) ? FormatBytes(total) : "未知大小")}", num2 * 0.9));
		}
	}

	private static async Task<bool> Sha256MatchesAsync(string path, string expected, CancellationToken ct)
	{
		bool result;
		await using (FileStream stream = File.OpenRead(path))
		{
			string text = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
			result = text.Equals(expected, StringComparison.OrdinalIgnoreCase);
		}
		return result;
	}

	private static string? FindJavaExecutable(string root)
	{
		string searchPattern = (OperatingSystem.IsWindows() ? "java.exe" : "java");
		return Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories).FirstOrDefault((string path) => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "bin", StringComparison.OrdinalIgnoreCase));
	}

	private static void EnsureUnixExecutable(string javaPath)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}
		try
		{
			UnixFileMode unixFileMode = File.GetUnixFileMode(javaPath);
			File.SetUnixFileMode(javaPath, unixFileMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
		}
		catch (PlatformNotSupportedException)
		{
		}
	}

	private static string FormatBytes(long bytes)
	{
		string[] array = new string[4] { "B", "KB", "MB", "GB" };
		double num = bytes;
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return $"{num:0.#} {array[num2]}";
	}
}
