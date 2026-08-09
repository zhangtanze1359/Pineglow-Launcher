using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public sealed class DiagnosticBundleService
{
	public async Task ExportAsync(string outputPath, string gameDirectory, GameInstance? instance, IEnumerable<string> launcherLogs, CancellationToken ct)
	{
		using ZipArchive archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
		await WriteTextAsync(archive, "environment.json", JsonSerializer.Serialize(new
		{
			exportedAt = DateTimeOffset.Now,
			os = Environment.OSVersion.ToString(),
			is64Bit = Environment.Is64BitOperatingSystem,
			instance = ((instance == null) ? null : new { instance.Name, instance.MinecraftVersion, instance.BaseMinecraftVersion, instance.LoaderType, instance.LoaderVersion, instance.MaxMemoryMb, instance.UseAutoMemory })
		}, JsonUtil.Options), ct);
		await WriteTextAsync(archive, "launcher-log.txt", Redact(string.Join(Environment.NewLine, launcherLogs)), ct);
		await AddTailAsync(archive, Path.Combine(gameDirectory, "logs", "latest.log"), "game/latest.log", ct);
		string path = Path.Combine(gameDirectory, "crash-reports");
		if (!Directory.Exists(path))
		{
			return;
		}
		foreach (string item in Directory.EnumerateFiles(path, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
		{
			await AddTailAsync(archive, item, "game/crash-reports/" + Path.GetFileName(item), ct);
		}
	}

	private static async Task AddTailAsync(ZipArchive archive, string source, string entryName, CancellationToken ct)
	{
		if (!File.Exists(source))
		{
			return;
		}
		await using FileStream input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (input.Length > 524288)
		{
			input.Seek(-524288L, SeekOrigin.End);
		}
		using StreamReader reader = new StreamReader(input);
		await WriteTextAsync(archive, entryName, Redact(await reader.ReadToEndAsync(ct)), ct);
	}

	private static async Task WriteTextAsync(ZipArchive archive, string name, string text, CancellationToken ct)
	{
		ZipArchiveEntry zipArchiveEntry = archive.CreateEntry(name, CompressionLevel.Fastest);
		await using Stream stream = zipArchiveEntry.Open();
		await using StreamWriter writer = new StreamWriter(stream);
		await writer.WriteAsync(text.AsMemory(), ct);
	}

	private static string Redact(string text)
	{
		return Regex.Replace(text, "(?i)(access[_-]?token|refresh[_-]?token|x-api-key|api[_-]?key|网页登录代码|device[_-]?code)\\s*[:=]\\s*[^\\s,;]+", "$1=<redacted>");
	}
}
