using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 自更新：从 manifest（GET updateUrl 返回 JSON）检查版本 → 下载新 exe 到 updates 目录 →
/// 校验 SHA256 → 生成替换脚本，待主进程退出后替换并重启。
/// </summary>
public static class UpdateService
{
	public static bool IsNewerVersion(string latest, string current)
	{
		if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(current))
		{
			return false;
		}
		string[] a = latest.Trim().Split('.');
		string[] b = current.Trim().Split('.');
		int len = Math.Max(a.Length, b.Length);
		for (int i = 0; i < len; i++)
		{
			int x = i < a.Length && int.TryParse(a[i], out int av) ? av : 0;
			int y = i < b.Length && int.TryParse(b[i], out int bv) ? bv : 0;
			if (x != y)
			{
				return x > y;
			}
		}
		return false;
	}

	public static async Task<UpdateManifest?> FetchManifestAsync(HttpClient http, string updateUrl, CancellationToken ct)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, updateUrl);
		request.Headers.UserAgent.ParseAdd("PineglowLauncher/" + LauncherService.LauncherVersion);
		using HttpResponseMessage response = await http.SendAsync(request, ct);
		response.EnsureSuccessStatusCode();
		string json = await response.Content.ReadAsStringAsync(ct);
		// manifest 协议为 camelCase，需忽略大小写映射到 PascalCase 模型
		UpdateManifest? manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOpts);
		return manifest;
	}

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	public static async Task<string> DownloadUpdateAsync(HttpClient http, UpdateManifest manifest, IProgress<double>? progress, CancellationToken ct)
	{
		string updatesDir = Path.Combine(PlatformPaths.GetConfigDirectory(), "updates");
		Directory.CreateDirectory(updatesDir);
		string dest = Path.Combine(updatesDir, "Pineglow-Launcher-" + manifest.Version + "-win-x64-single-file.exe");
		if (File.Exists(dest) && Sha256Matches(dest, manifest.Sha256))
		{
			progress?.Report(1.0);
			return dest;
		}

		string part = dest + ".part";
		using HttpResponseMessage response = await http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();
		long total = manifest.Size > 0 ? manifest.Size : (response.Content.Headers.ContentLength ?? 0L);
		await using FileStream fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
		await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(ct);
		byte[] buffer = new byte[81920];
		long received = 0;
		int read;
		while ((read = await stream.ReadAsync(buffer, ct)) > 0)
		{
			await fs.WriteAsync(buffer.AsMemory(0, read), ct);
			received += read;
			progress?.Report(total > 0 ? (double)received / total : 0.0);
		}
		await fs.FlushAsync(ct);
		fs.Close();

		if (!Sha256Matches(part, manifest.Sha256))
		{
			File.Delete(part);
			throw new InvalidOperationException("更新包校验失败（SHA256 不匹配），请重试。");
		}
		if (File.Exists(dest))
		{
			File.Delete(dest);
		}
		File.Move(part, dest);
		progress?.Report(1.0);
		return dest;
	}

	/// <summary>生成替换脚本并启动；脚本等待当前进程退出后替换 exe 并重启。</summary>
	public static void ApplyUpdate(string downloadedPath, string scriptExt)
	{
		string targetExe = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前可执行文件路径");
		string dir = Path.GetDirectoryName(downloadedPath) ?? PlatformPaths.GetConfigDirectory();
		string scriptPath = Path.Combine(dir, "apply-update" + scriptExt);
		string exeName = Path.GetFileName(targetExe);

		if (scriptExt == ".bat")
		{
			string script = "@echo off\r\n"
				+ "chcp 65001 >nul\r\n"
				+ ":wait\r\n"
				+ "tasklist /FI \"IMAGENAME eq " + exeName + "\" 2>nul | find /I \"" + exeName + "\" >nul\r\n"
				+ "if %errorlevel%==0 ( timeout /t 1 /nobreak >nul & goto wait )\r\n"
				+ "copy /Y \"" + downloadedPath + "\" \"" + targetExe + "\" >nul\r\n"
				+ "start \"\" \"" + targetExe + "\"\r\n"
				+ "del \"%~f0\"\r\n";
			File.WriteAllText(scriptPath, script);
			Process.Start(new ProcessStartInfo
			{
				FileName = "cmd.exe",
				Arguments = "/c \"" + scriptPath + "\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = AppContext.BaseDirectory
			});
		}
		else
		{
			string script = "#!/bin/sh\n"
				+ "while pgrep -f \"" + exeName + "\" >/dev/null 2>&1; do sleep 1; done\n"
				+ "cp \"" + downloadedPath + "\" \"" + targetExe + "\"\n"
				+ "chmod +x \"" + targetExe + "\"\n"
				+ "nohup \"" + targetExe + "\" >/dev/null 2>&1 &\n"
				+ "rm -f \"$0\"\n";
			File.WriteAllText(scriptPath, script);
			Process.Start(new ProcessStartInfo
			{
				FileName = "/bin/sh",
				Arguments = "\"" + scriptPath + "\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = AppContext.BaseDirectory
			});
		}
	}

	private static bool Sha256Matches(string file, string expected)
	{
		if (string.IsNullOrWhiteSpace(expected))
		{
			return true;
		}
		string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
		return string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
	}
}
