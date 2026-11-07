using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR11：`.mrpack` 规范兼容、路径安全与文件哈希回退。
///
/// 为什么这些用例不跑"真实导入"：`ImportModrinthPackAsync` 一定会联网安装 Minecraft 与加载器，
/// 单测里跑不通也不该跑。所以本文件验证的是导入流程里**可以在离线环境判定**的三件事：
///   1. 规范字段校验（formatVersion / dependencies / files / env / downloads / name）；
///   2. 路径安全（相对路径不得逃出实例目录）；
///   3. 文件下载的哈希校验与**镜像回退**（某个镜像内容不对时换下一个，而不是整次导入失败）。
///
/// 明确不覆盖：真实网络导入、加载器安装、真实 .mrpack 端到端（属 MR22 内测范围）。
/// </summary>
public class InstancePackTests
{
	private readonly ITestOutputHelper _out;

	public InstancePackTests(ITestOutputHelper output) => _out = output;

	/* ===================== 夹具 ===================== */

	private sealed class TempDir : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-mrpack-" + Guid.NewGuid().ToString("N"));

		public TempDir() => Directory.CreateDirectory(Path);

		public string File(string name) => System.IO.Path.Combine(Path, name);

		public void Dispose()
		{
			try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { }
		}
	}

	/// <summary>按 URL 返回不同内容的假 handler，用于验证镜像回退。</summary>
	private sealed class PerUrlHandler : HttpMessageHandler
	{
		private readonly Dictionary<string, byte[]> _payloads;
		public List<string> Requested { get; } = new List<string>();

		public PerUrlHandler(Dictionary<string, byte[]> payloads) => _payloads = payloads;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri!.ToString();
			Requested.Add(url);
			if (!_payloads.TryGetValue(url, out byte[]? body))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(body)
			});
		}
	}

	private static string Sha1Hex(byte[] data) => Convert.ToHexString(SHA1.HashData(data));
	private static string Sha512Hex(byte[] data) => Convert.ToHexString(SHA512.HashData(data));

	private static InstancePackService NewService(HttpMessageHandler? handler = null)
	{
		var http = handler == null ? new HttpClient() : new HttpClient(handler);
		return new InstancePackService(http, System.IO.Path.GetTempPath(), _ => { });
	}

	private static void WriteZip(string path, params (string Name, string Content)[] entries)
	{
		using FileStream fs = File.Create(path);
		using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
		foreach ((string name, string content) in entries)
		{
			ZipArchiveEntry entry = zip.CreateEntry(name);
			using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
			writer.Write(content);
		}
	}

	/* ===================== 1. 格式探测 ===================== */

	[Fact]
	public void DetectModpackFormat_RecognizesTheThreeFormats_AndRejectsUnknown()
	{
		using var dir = new TempDir();
		string mrpack = dir.File("a.mrpack");
		string curse = dir.File("b.zip");
		string mcm = dir.File("c.zip");
		string unknown = dir.File("d.zip");

		WriteZip(mrpack, ("modrinth.index.json", "{}"));
		WriteZip(curse, ("manifest.json", "{}"));
		WriteZip(mcm, ("instance.json", "{}"));
		WriteZip(unknown, ("readme.txt", "hello"));

		Assert.Equal(ModpackArchiveFormat.Modrinth, InstancePackService.DetectModpackFormat(mrpack));
		Assert.Equal(ModpackArchiveFormat.CurseForge, InstancePackService.DetectModpackFormat(curse));
		Assert.Equal(ModpackArchiveFormat.Mcm, InstancePackService.DetectModpackFormat(mcm));
		Assert.Equal(ModpackArchiveFormat.Unknown, InstancePackService.DetectModpackFormat(unknown));
	}

	/* ===================== 2. 规范字段校验 ===================== */

	private const string MinimalIndex = """
		{
		  "formatVersion": 1,
		  "name": "测试整合包",
		  "dependencies": { "minecraft": "1.20.4", "fabric-loader": "0.15.0" },
		  "files": []
		}
		""";

	[Fact]
	public void ParseModrinthIndex_AcceptsASpecCompliantMinimalPack()
	{
		InstancePackService.ModrinthPackManifest manifest = InstancePackService.ParseModrinthIndex(MinimalIndex);

		Assert.Equal(1, manifest.FormatVersion);
		Assert.Equal("测试整合包", manifest.PackName);
		Assert.Equal("1.20.4", manifest.MinecraftVersion);
		Assert.Equal("Fabric", manifest.LoaderKind);
		Assert.Equal("0.15.0", manifest.LoaderVersion);
		Assert.Empty(manifest.Files);
	}

	[Fact]
	public void ParseModrinthIndex_RejectsUnsupportedFormatVersion()
	{
		string json = MinimalIndex.Replace("\"formatVersion\": 1", "\"formatVersion\": 2");
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => InstancePackService.ParseModrinthIndex(json));
		_out.WriteLine("[formatVersion=2] " + ex.Message);
		Assert.Contains("formatVersion=2", ex.Message);
		Assert.Contains("只支持 1", ex.Message);
	}

	[Fact]
	public void ParseModrinthIndex_DefaultsMissingFormatVersionToOne()
	{
		// CV-03（本轮修复）：C# 原始字符串字面量**原样保留源文件换行**。本文件被 core.autocrlf=true
		// 检出后夹具里是 CRLF，旧写法 Replace("  \"formatVersion\": 1,\n", "") 匹配不到，
		// 字段根本没被删掉，失败发生在**调用产品解析器之前**（是夹具构造问题，不是产品行为问题）。
		// 现改为按 JSON 结构删除属性：与源码换行无关，且「字段确实缺失」有显式断言守住。
		JsonObject root = JsonNode.Parse(MinimalIndex)?.AsObject()
			?? throw new InvalidOperationException("夹具 JSON 无法解析为对象。");
		Assert.True(root.Remove("formatVersion"), "夹具必须含 formatVersion，否则本用例无法证明「缺字段取默认值」。");

		string json = root.ToJsonString();
		Assert.DoesNotContain("formatVersion", json);
		InstancePackService.ModrinthPackManifest manifest = InstancePackService.ParseModrinthIndex(json);
		Assert.Equal(1, manifest.FormatVersion);
	}

	[Fact]
	public void ParseModrinthIndex_ReportsMissingDependenciesClearly()
	{
		string json = """{ "formatVersion": 1, "name": "x", "files": [] }""";
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => InstancePackService.ParseModrinthIndex(json));
		_out.WriteLine("[no dependencies] " + ex.Message);
		Assert.Contains("dependencies", ex.Message);
	}

	[Fact]
	public void ParseModrinthIndex_ReportsMissingMinecraftDependencyClearly()
	{
		string json = """{ "formatVersion": 1, "name": "x", "dependencies": { "fabric-loader": "0.15.0" }, "files": [] }""";
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => InstancePackService.ParseModrinthIndex(json));
		_out.WriteLine("[no minecraft] " + ex.Message);
		Assert.Contains("minecraft", ex.Message);
	}

	[Fact]
	public void ParseModrinthIndex_ReportsMissingFilesClearly()
	{
		string json = """{ "formatVersion": 1, "name": "x", "dependencies": { "minecraft": "1.20.4" } }""";
		InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => InstancePackService.ParseModrinthIndex(json));
		_out.WriteLine("[no files] " + ex.Message);
		Assert.Contains("files", ex.Message);
	}

	[Fact]
	public void ParseModrinthIndex_ToleratesUnknownExtraFields()
	{
		string json = """
			{
			  "formatVersion": 1,
			  "name": "带未知字段的包",
			  "futureField": { "nested": [1, 2, 3] },
			  "dependencies": { "minecraft": "1.20.4", "quilt-loader": "0.20.0", "unknown-loader": "9" },
			  "files": [],
			  "game": "minecraft"
			}
			""";

		InstancePackService.ModrinthPackManifest manifest = InstancePackService.ParseModrinthIndex(json);
		Assert.Equal("带未知字段的包", manifest.PackName);
		Assert.Equal("Quilt", manifest.LoaderKind);
		Assert.Equal("0.20.0", manifest.LoaderVersion);
	}

	[Fact]
	public void ParseModrinthIndex_SkipsClientUnsupportedFiles_AndFilesWithoutDownloads()
	{
		string json = """
			{
			  "formatVersion": 1,
			  "name": "过滤测试",
			  "dependencies": { "minecraft": "1.20.4" },
			  "files": [
			    { "path": "mods/keep.jar", "downloads": ["https://cdn.example.com/keep.jar"], "hashes": { "sha1": "aa" } },
			    { "path": "mods/server-only.jar", "downloads": ["https://cdn.example.com/s.jar"], "env": { "client": "unsupported" } },
			    { "path": "mods/no-download.jar", "hashes": { "sha1": "bb" } },
			    { "path": "mods/server-required.jar", "downloads": ["https://cdn.example.com/r.jar"], "env": { "client": "required" } }
			  ]
			}
			""";

		InstancePackService.ModrinthPackManifest manifest = InstancePackService.ParseModrinthIndex(json);
		string[] kept = manifest.Files.Select(f => f.Path).ToArray();
		_out.WriteLine("[kept] " + string.Join(", ", kept));

		Assert.Equal(2, kept.Length);
		Assert.Contains("mods/keep.jar", kept);
		Assert.Contains("mods/server-required.jar", kept);
		Assert.DoesNotContain("mods/server-only.jar", kept);
		Assert.DoesNotContain("mods/no-download.jar", kept);
	}

	[Fact]
	public void ParseModrinthIndex_FallsBackToASafeName_WhenNameIsMissing()
	{
		string json = """{ "formatVersion": 1, "dependencies": { "minecraft": "1.20.4" }, "files": [] }""";
		InstancePackService.ModrinthPackManifest manifest = InstancePackService.ParseModrinthIndex(json);
		Assert.False(string.IsNullOrWhiteSpace(manifest.PackName));
		Assert.Equal("Modrinth 整合包", manifest.PackName);
	}

	/* ===================== 3. 路径安全 ===================== */

	[Fact]
	public void GetSafeTargetPath_RejectsTraversal_AndAcceptsNestedPaths()
	{
		using var dir = new TempDir();

		string ok = InstancePackService.GetSafeTargetPath(dir.Path, "mods/example.jar");
		Assert.StartsWith(Path.GetFullPath(dir.Path), ok, StringComparison.OrdinalIgnoreCase);
		Assert.EndsWith("example.jar", ok, StringComparison.OrdinalIgnoreCase);

		string nested = InstancePackService.GetSafeTargetPath(dir.Path, "config/deep/nested/file.json");
		Assert.StartsWith(Path.GetFullPath(dir.Path), nested, StringComparison.OrdinalIgnoreCase);

		// 穿越写法必须被拒：'/' 是两边都认的分隔符，所以这两种在 Windows 与 POSIX 上都逃逸。
		foreach (string evil in new[] { "../escaped.txt", "mods/../../escaped.txt" })
		{
			Assert.Throws<InvalidDataException>(() => InstancePackService.GetSafeTargetPath(dir.Path, evil));
		}

		// 反斜杠写法是**平台相关**的：
		//   - Windows：'\' 是目录分隔符，`..\escaped.txt` 同样逃逸，必须拒绝；
		//   - POSIX：'\' 只是普通文件名字符，`..\escaped.txt` 是目录内一个合法文件名（不逃逸），
		//     GetSafeTargetPath 按"不越界即放行"的契约原样返回它。若这里也抛异常，说明产品
		//     硬编码了 Windows 的分隔符规则，反而会误拒合法文件名。
		if (OperatingSystem.IsWindows())
		{
			Assert.Throws<InvalidDataException>(() => InstancePackService.GetSafeTargetPath(dir.Path, "..\\escaped.txt"));
		}
		else
		{
			string backslashName = InstancePackService.GetSafeTargetPath(dir.Path, "..\\escaped.txt");
			Assert.StartsWith(Path.GetFullPath(dir.Path) + Path.DirectorySeparatorChar, backslashName);
			Assert.Equal("..\\escaped.txt", Path.GetFileName(backslashName));
		}

		// 逃逸文件绝不能真的被写出去
		Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dir.Path))!, "escaped.txt")));
	}

	/* ===================== 4. 哈希校验与镜像回退 ===================== */

	[Fact]
	public async Task Download_FallsBackToNextMirror_WhenHashesMismatch()
	{
		using var dir = new TempDir();
		byte[] good = Encoding.UTF8.GetBytes("REAL-CONTENT");
		byte[] bad = Encoding.UTF8.GetBytes("TAMPERED-CONTENT");

		var handler = new PerUrlHandler(new Dictionary<string, byte[]>
		{
			["https://mirror1.example.com/a.jar"] = bad,
			["https://mirror2.example.com/a.jar"] = good,
		});

		InstancePackService service = NewService(handler);
		var file = new InstancePackService.ModrinthPackFile(
			"mods/a.jar",
			new List<string> { "https://mirror1.example.com/a.jar", "https://mirror2.example.com/a.jar" },
			Sha1Hex(good),
			Sha512Hex(good));

		await service.DownloadModrinthPackFileAsync(dir.Path, file, CancellationToken.None);

		string target = Path.Combine(dir.Path, "mods", "a.jar");
		_out.WriteLine("[mirrors requested] " + string.Join(", ", handler.Requested));
		Assert.True(File.Exists(target), "第二个镜像的内容是对的，必须落盘");
		Assert.Equal(good, File.ReadAllBytes(target));
		Assert.Contains("https://mirror1.example.com/a.jar", handler.Requested);
		Assert.Contains("https://mirror2.example.com/a.jar", handler.Requested);
		Assert.False(File.Exists(target + ".download"), "临时文件必须被清理");
	}

	[Fact]
	public async Task Download_FailsOnlyAfterEveryMirrorHasWrongHashes()
	{
		using var dir = new TempDir();
		byte[] good = Encoding.UTF8.GetBytes("REAL");
		byte[] bad1 = Encoding.UTF8.GetBytes("BAD-1");
		byte[] bad2 = Encoding.UTF8.GetBytes("BAD-2");

		var handler = new PerUrlHandler(new Dictionary<string, byte[]>
		{
			["https://m1.example.com/a.jar"] = bad1,
			["https://m2.example.com/a.jar"] = bad2,
		});

		InstancePackService service = NewService(handler);
		var file = new InstancePackService.ModrinthPackFile(
			"mods/a.jar",
			new List<string> { "https://m1.example.com/a.jar", "https://m2.example.com/a.jar" },
			Sha1Hex(good),
			Sha512Hex(good));

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => service.DownloadModrinthPackFileAsync(dir.Path, file, CancellationToken.None));
		_out.WriteLine("[all mirrors bad] " + ex.Message);

		Assert.Contains("校验不通过", ex.Message);
		Assert.False(File.Exists(Path.Combine(dir.Path, "mods", "a.jar")), "校验没过就绝不能让文件落盘");
		Assert.False(File.Exists(Path.Combine(dir.Path, "mods", "a.jar.download")));
	}

	[Fact]
	public async Task Download_UsesOnlyTheFirstMirror_WhenItsHashIsCorrect()
	{
		using var dir = new TempDir();
		byte[] good = Encoding.UTF8.GetBytes("CORRECT");

		var handler = new PerUrlHandler(new Dictionary<string, byte[]>
		{
			["https://fast.example.com/a.jar"] = good,
			["https://slow.example.com/a.jar"] = good,
		});

		InstancePackService service = NewService(handler);
		var file = new InstancePackService.ModrinthPackFile(
			"mods/a.jar",
			new List<string> { "https://fast.example.com/a.jar", "https://slow.example.com/a.jar" },
			Sha1Hex(good),
			Sha512Hex(good));

		await service.DownloadModrinthPackFileAsync(dir.Path, file, CancellationToken.None);

		Assert.Equal(good, File.ReadAllBytes(Path.Combine(dir.Path, "mods", "a.jar")));
		Assert.Single(handler.Requested);
		Assert.Equal("https://fast.example.com/a.jar", handler.Requested[0]);
	}

	[Fact]
	public async Task Download_RejectsAPathThatEscapesTheInstanceDirectory()
	{
		using var dir = new TempDir();
		byte[] good = Encoding.UTF8.GetBytes("X");
		var handler = new PerUrlHandler(new Dictionary<string, byte[]> { ["https://m/a.jar"] = good });
		InstancePackService service = NewService(handler);

		var file = new InstancePackService.ModrinthPackFile(
			"../escaped.jar",
			new List<string> { "https://m/a.jar" },
			Sha1Hex(good),
			Sha512Hex(good));

		await Assert.ThrowsAsync<InvalidDataException>(
			() => service.DownloadModrinthPackFileAsync(dir.Path, file, CancellationToken.None));

		Assert.Empty(handler.Requested); // 路径不合法就不该发出任何请求
	}
}
