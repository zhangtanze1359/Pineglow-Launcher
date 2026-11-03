using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR05 补测（缺口登记表 A4）：**文件被占用 / 只读**时的替换行为。
///
/// 为什么先"测出行为"再断言：
/// 第 41 轮实测过一个 Windows 细节 —— **用 <c>FileShare.None</c> 打开的文件仍然可以被重命名**。
/// 因此"锁住目标文件"**不一定**会让替换失败（隔离阶段是移动，可能照样成功）。
/// 所以本类不预设结论，而是断言**无论成功与否都必须成立的不变量**：
///   1. **不崩**：返回明确的成功或带可读错误的失败；
///   2. **旧版本永远可恢复**：要么原地还在（逐字节不变），要么完整进了恢复区；
///   3. **不留垃圾**：mods 目录里不出现 `(1)` 副本、`.tmp`、`.staging` 之类残留。
///
/// 磁盘不足**无法可靠模拟**，本类不覆盖（如实记录）。
/// </summary>
[Collection("LocalApiService")]
public class ModReplaceFileInUseTests
{
	private readonly ITestOutputHelper _out;

	public ModReplaceFileInUseTests(ITestOutputHelper output) => _out = output;

	private const string OldJar = "OLD-JAR-CONTENT-v1";
	private const string NewJar = "NEW-JAR-CONTENT-v2-longer";
	private const string OldSidecar = "{\"source\":\"Modrinth\",\"projectId\":\"proj-1\",\"versionId\":\"ver-1\",\"versionNumber\":\"1.0\"}";

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string ModsDir => Path.Combine(Root, "mods");
		public string RecoveryRoot { get; }
		public string InstanceId { get; } = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		public AppConfig Config { get; }

		public string JarPath(string name) => Path.Combine(ModsDir, name);

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-a4-" + Guid.NewGuid().ToString("N"));
			RecoveryRoot = Path.Combine(Path.GetTempPath(), "pineglow-a4-rec-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(ModsDir);
			Directory.CreateDirectory(RecoveryRoot);
			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "a4", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		public string Api(string suffix) => "/api/instances/" + InstanceId + suffix;

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
			try { if (Directory.Exists(RecoveryRoot)) Directory.Delete(RecoveryRoot, true); } catch { }
		}
	}

	private static string Sha(string path)
	{
		using FileStream fs = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(fs));
	}

	private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

	private static bool RecoveryContains(string recoveryRoot, string content)
	{
		foreach (string file in Directory.EnumerateFiles(recoveryRoot, "*", SearchOption.AllDirectories))
		{
			try
			{
				if (File.ReadAllText(file) == content)
				{
					return true;
				}
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}

		return false;
	}

	/// <summary>不变量：不崩 + 旧版本可恢复 + 无垃圾。</summary>
	private void AssertInvariants(Fixture fx, string recoveryRoot, string target, string oldSha, int status, string body)
	{
		Assert.True(status == 200 || (status >= 400 && status < 600),
			$"状态码应是成功或明确的失败，实际 {status}");

		if (status != 200)
		{
			string? error = null;
			try
			{
				using JsonDocument doc = JsonDocument.Parse(body);
				if (doc.RootElement.TryGetProperty("error", out JsonElement el) && el.ValueKind == JsonValueKind.String)
				{
					error = el.GetString();
				}
			}
			catch { }
			Assert.False(string.IsNullOrWhiteSpace(error), "失败必须带可读的 error（不能是空体）");
			_out.WriteLine($"[A4] 失败信息：{error}");
		}

		// 旧版本必须可恢复：要么原地逐字节不变，要么完整躺在恢复区
		bool stillInPlace = File.Exists(target) && Sha(target) == oldSha;
		bool inRecovery = RecoveryContains(recoveryRoot, OldJar);
		_out.WriteLine($"[A4] 旧版本：原地={stillInPlace}  恢复区={inRecovery}  status={status}");
		Assert.True(stillInPlace || inRecovery,
			"旧版本必须要么原地逐字节不变、要么完整进了恢复区 —— 否则就是数据丢失");

		// 不留垃圾
		Assert.Empty(Directory.GetFiles(fx.ModsDir, "* (1)*"));
		Assert.Empty(Directory.GetFiles(fx.ModsDir, "*.tmp"));
		Assert.Empty(Directory.GetDirectories(fx.ModsDir, "*staging*"));
	}

	/* ===================== ① 目标 jar 被独占打开 ===================== */

	[Fact]
	public async Task Replace_WhenTargetJarIsLocked_StillHonoursTheInvariants()
	{
		using var fx = new Fixture();
		string target = fx.JarPath("a.jar");
		File.WriteAllText(target, OldJar);
		File.WriteAllText(target + ".mcmini.json", OldSidecar);
		string oldSha = Sha(target);

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		// 独占打开目标文件：别的进程/线程拿不到它。
		// 注意：断言必须在**释放锁之后**做 —— 否则连"算旧文件 SHA"都会因占用而抛 IOException（测试自身的坑）。
		int status;
		string body;
		using (var hold = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			(status, body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
				new { newFileBase64 = Base64(NewJar) });
			_out.WriteLine($"[A4] 目标被独占打开时 replace -> {status} body={body.Substring(0, Math.Min(160, body.Length))}");
		}

		AssertInvariants(fx, h.RecoveryRoot, target, oldSha, status, body);

		// 释放锁之后必须能成功（瞬时占用不应留下永久阻塞）
		(int retryStatus, string retryBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
			new { newFileBase64 = Base64(NewJar) });
		_out.WriteLine($"[A4] 释放锁后重试 -> {retryStatus}");
		Assert.Equal(200, retryStatus);
		Assert.Equal(NewJar, File.ReadAllText(target));
		Assert.True(RecoveryContains(h.RecoveryRoot, OldJar), "成功替换后旧版本应在恢复区");
		Assert.False(string.IsNullOrWhiteSpace(retryBody));
	}

	/* ===================== ② 侧车被独占打开（元数据写入阶段） ===================== */

	[Fact]
	public async Task Replace_WhenSidecarIsLocked_StillHonoursTheInvariants()
	{
		using var fx = new Fixture();
		string target = fx.JarPath("b.jar");
		string sidecar = target + ".mcmini.json";
		File.WriteAllText(target, OldJar);
		File.WriteAllText(sidecar, OldSidecar);
		string oldSha = Sha(target);

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		using (var hold = new FileStream(sidecar, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/b.jar/replace"),
				new { newFileBase64 = Base64(NewJar) });
			_out.WriteLine($"[A4] 侧车被独占打开时 replace -> {status}");
			AssertInvariants(fx, h.RecoveryRoot, target, oldSha, status, body);
		}
	}

	/* ===================== ③ 目标文件只读 ===================== */

	[Fact]
	public async Task Replace_WhenTargetIsReadOnly_StillHonoursTheInvariants()
	{
		using var fx = new Fixture();
		string target = fx.JarPath("c.jar");
		File.WriteAllText(target, OldJar);
		File.WriteAllText(target + ".mcmini.json", OldSidecar);
		string oldSha = Sha(target);

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		try
		{
			File.SetAttributes(target, FileAttributes.ReadOnly);
			(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/c.jar/replace"),
				new { newFileBase64 = Base64(NewJar) });
			_out.WriteLine($"[A4] 目标只读时 replace -> {status}");
			AssertInvariants(fx, h.RecoveryRoot, target, oldSha, status, body);
		}
		finally
		{
			try { File.SetAttributes(target, FileAttributes.Normal); } catch { }
		}
	}
}
