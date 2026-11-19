using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-11-19（v1.0.5）聚焦测试：**替换动作的并发/重复触发守卫**。
///
/// 原始缺口（静态可核）：<c>/api/update/apply</c> 的守卫只有「更新包存在」。
/// 同一份已下载包可以被连续 POST：每次都会覆写 <c>apply-update.bat</c> 并再拉一个 cmd 进程，
/// 两个脚本同时 <c>copy /Y</c> 同一个 TARGET —— 而替换是**不可逆且会动用户正在运行的 exe** 的动作。
/// 下载侧原本就有 <c>alreadyDownloading</c> 判定，apply 侧没有对应守卫。
///
/// 修复方式：进入 applying 后再次 POST 直接返回同一结果，不再启动第二个替换脚本；
/// 且 apply 失败时把状态退回 failed（否则界面会永远停在"正在替换"）。
///
/// 明确不覆盖：真实替换（那是 UpdateApplyScriptPathTests 与 UpdateRecoveryTests 的事），
/// 真实重启，真实发布端点。
/// </summary>
[Collection("LocalApiService")]
public class UpdateApplyGuardTests
{
	private readonly ITestOutputHelper _out;

	public UpdateApplyGuardTests(ITestOutputHelper output) => _out = output;

	private sealed class FnTempDir : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-apply-guard-fn-" + Guid.NewGuid().ToString("N"));
		public FnTempDir() => Directory.CreateDirectory(Path);
		public void Dispose() { try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { } }
	}

	private static string TempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "pineglow-apply-guard-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	[Fact]
	public async Task Apply_WhenAlreadyApplying_SecondRequestIsIgnoredAndNoSecondScriptStarts()
	{
		string gameDir = TempDir();
		string updatesDir = Path.Combine(Path.GetTempPath(), "pineglow-apply-guard-updates-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(updatesDir);
		string downloaded = Path.Combine(updatesDir, "Pineglow-Launcher-1.0.5-win-x64-single-file.exe");
		await File.WriteAllTextAsync(downloaded, "NEW-CANDIDATE-BYTES");

		AppConfig config = new AppConfig { GameDirectory = gameDir };
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(config);

			// 把状态机设到 applying：模拟"第一次 apply 已经启动替换脚本、进程即将退出"。
			using (LocalApiService.OverrideUpdateStateForTest("applying", downloaded, "1.0.5"))
			{
				(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
				_out.WriteLine("[v1.0.5][apply-guard] status=" + status + " body=" + body);

				// 现状（修复前）：这里会再去写脚本、再拉一个 cmd，并返回 ok=true 无 state 字段。
				// 修复后：立刻返回 state=applying，且**不**启动第二个脚本。
				Assert.Equal(200, status);
				using JsonDocument doc = JsonDocument.Parse(body);
				JsonElement root = doc.RootElement;
				Assert.True(root.GetProperty("ok").GetBoolean());
				Assert.True(root.TryGetProperty("state", out JsonElement st), "重复 apply 的响应必须带 state，便于界面区分");
				Assert.Equal("applying", st.GetString());
			}

			// 断言"没有再写替换脚本"：applying 分支应当在写脚本之前返回。
			// 脚本只会写到更新包同目录，因此直接看那目录里有没有 apply-update.bat。
			Assert.False(File.Exists(Path.Combine(updatesDir, "apply-update.bat")),
				"已在 applying 时不得再生成第二个替换脚本");
		}
		finally
		{
			try { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); } catch { }
			try { if (Directory.Exists(updatesDir)) Directory.Delete(updatesDir, true); } catch { }
		}
	}

	[Fact]
	public async Task Apply_WithoutDownloadedPackage_StillRejectsBeforeTouchingAnything()
	{
		string gameDir = TempDir();
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });
			(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
			_out.WriteLine("[v1.0.5][apply-guard] idle status=" + status + " body=" + body);
			Assert.Equal(400, status);
			using JsonDocument doc = JsonDocument.Parse(body);
			Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
			// 这条是既有行为（回归位）：新增 applying 守卫不得把"没下载"的拒绝弄丢。
			Assert.Contains("更新包未就绪", doc.RootElement.GetProperty("message").GetString());
		}
		finally
		{
			try { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); } catch { }
		}
	}

	[Fact]
	public async Task ApplyStateOverride_IsScopedAndRestoresPreviousState()
	{
		string gameDir = TempDir();
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });

			(int idleA, string _) = await h.SendAsync(HttpMethod.Get, "/api/update");
			using (LocalApiService.OverrideUpdateStateForTest("applying", @"C:\nope\nothing.exe", "9.9.9"))
			{
				(int _, string inside) = await h.SendAsync(HttpMethod.Get, "/api/update");
				using JsonDocument docInside = JsonDocument.Parse(inside);
				Assert.Equal("applying", docInside.RootElement.GetProperty("state").GetString());
				Assert.Equal("9.9.9", docInside.RootElement.GetProperty("version").GetString());
			}

			(int _, string after) = await h.SendAsync(HttpMethod.Get, "/api/update");
			using JsonDocument docAfter = JsonDocument.Parse(after);
			_out.WriteLine("[v1.0.5][apply-guard] restored state=" + docAfter.RootElement.GetProperty("state").GetString());
			Assert.Equal("idle", docAfter.RootElement.GetProperty("state").GetString());
			Assert.NotNull(idleA);
		}
		finally
		{
			try { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); } catch { }
		}
	}

	/// <summary>
	/// 2026-11-19（v1.0.5，RED 4 回归位）：**替换前必须再校一次 SHA256**。
	///
	/// 真机 E2E 实测的缺陷：下载阶段校验通过后，磁盘上的包被截断成 100,000 字节，
	/// apply 仍然写出 applied 并把可用旧 EXE 换成无法启动的坏文件。
	/// 根因是替换脚本只做 <c>fc /b</c>（拷贝完整性），不校清单声明的哈希。
	///
	/// 修复：<c>/api/update/apply</c> 在启动脚本**之前**用同一个 <c>Sha256Matches</c> 复核。
	/// 本用例断言：哈希不符 → 400 + sha256Mismatch=true + 状态置 failed，
	/// 且**不生成** apply-update[.bat]（不写脚本、不替换）。
	/// </summary>
	[Fact]
	public async Task Apply_WhenDownloadedPackageHashDoesNotMatch_RefusesBeforeWritingAnyScript()
	{
		string gameDir = TempDir();
		string updatesDir = Path.Combine(Path.GetTempPath(), "pineglow-apply-hash-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(updatesDir);
		string downloaded = Path.Combine(updatesDir, "Pineglow-Launcher-1.0.5-win-x64-single-file.exe");
		await File.WriteAllBytesAsync(downloaded, new byte[] { 1, 2, 3, 4 });

		AppConfig config = new AppConfig { GameDirectory = gameDir };
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(config);

			// 声明一个与磁盘内容不符的 SHA256（模拟"下载后被截断/改写"）
			string wrongSha = new string('a', 64);
			using (LocalApiService.OverrideUpdateStateForTest("completed", downloaded, "1.0.5", wrongSha))
			{
				(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
				_out.WriteLine("[v1.0.5][apply-sha] status=" + status + " body=" + body);

				Assert.Equal(400, status);
				using JsonDocument doc = JsonDocument.Parse(body);
				JsonElement root = doc.RootElement;
				Assert.False(root.GetProperty("ok").GetBoolean());
				Assert.True(root.GetProperty("sha256Mismatch").GetBoolean(), "响应必须能区分『哈希不符』这类拒绝");
				Assert.Contains("SHA256", root.GetProperty("message").GetString());

				// 状态必须置 failed（而不是留在 completed 让界面反复重试）。
				// 注意：这个断言必须在 using 作用域**内**做 —— override 作用域释放会把状态还原成 idle，
				// 放到外面就会误判成“没有置 failed”。
				(int _, string st) = await h.SendAsync(HttpMethod.Get, "/api/update");
				using JsonDocument sdoc = JsonDocument.Parse(st);
				Assert.Equal("failed", sdoc.RootElement.GetProperty("state").GetString());
			}

			// 不写脚本、不替换
			Assert.False(File.Exists(Path.Combine(updatesDir, "apply-update.bat")), "哈希不符时不得生成替换脚本");
			Assert.False(File.Exists(Path.Combine(updatesDir, "apply-update.sh")), "哈希不符时不得生成替换脚本（unix 形态）");
		}
		finally
		{
			try { if (Directory.Exists(gameDir)) Directory.Delete(gameDir, true); } catch { }
			try { if (Directory.Exists(updatesDir)) Directory.Delete(updatesDir, true); } catch { }
		}
	}

	/// <summary>
	/// 与上一条配对的正向用例：**哈希相符时必须放行**（否则修完就变成"永远拒绝 apply"）。
	///
	/// 注意：这里**不能**真的走 <c>/api/update/apply</c>。产品在启动替换脚本后会
	/// <c>await Task.Delay(1200)</c> 然后 <c>Environment.Exit(0)</c> —— 在 testhost 里那就是
	/// "测试主机进程崩溃"（本轮实测：全量跑到 This test 时 vstest 报 host crashed）。
	/// 所以这条改成直接钉住那个纯函数判定；真实放行路径由隔离 E2E 覆盖
	/// （`logs/70-E2E-happy-path.txt`）。
	/// </summary>
	[Fact]
	public void DownloadedPackageMatchesExpectedHash_AllowsMatchingPackage()
	{
		using var dir = new FnTempDir();
		string file = Path.Combine(dir.Path, "pkg.exe");
		byte[] payload = { 9, 9, 9 };
		File.WriteAllBytes(file, payload);
		string good = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

		Assert.True(UpdateService.DownloadedPackageMatchesExpectedHash(file, good));
		Assert.True(UpdateService.DownloadedPackageMatchesExpectedHash(file, good.ToUpperInvariant()),
			"比较必须大小写不敏感（清单里可能是大写）");
	}

	/// <summary>没有期望值时放行（旧状态 / 未记录），交由“包未就绪”分支处理，不把功能锁死。</summary>
	[Fact]
	public void DownloadedPackageMatchesExpectedHash_AllowsWhenNoExpectationRecorded()
	{
		using var dir = new FnTempDir();
		string file = Path.Combine(dir.Path, "pkg.exe");
		File.WriteAllBytes(file, new byte[] { 1 });

		Assert.True(UpdateService.DownloadedPackageMatchesExpectedHash(file, string.Empty));
		Assert.True(UpdateService.DownloadedPackageMatchesExpectedHash(file, "   "));
	}

	/// <summary>期望值非法（不是 64 位 hex）或文件内容不符 → 拒绝。</summary>
	[Fact]
	public void DownloadedPackageMatchesExpectedHash_RejectsMismatchAndInvalidExpectation()
	{
		using var dir = new FnTempDir();
		string file = Path.Combine(dir.Path, "pkg.exe");
		File.WriteAllBytes(file, new byte[] { 1, 2, 3 });

		Assert.False(UpdateService.DownloadedPackageMatchesExpectedHash(file, new string('a', 64)),
			"内容不符必须拒绝");
		Assert.False(UpdateService.DownloadedPackageMatchesExpectedHash(file, "not-a-hash"),
			"非法期望值必须拒绝（不得当作“没填”放行）");
	}

}
