using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// MR06：Bisect 文件操作失败必须阻止错误推进。
///
/// 改动前的缺陷（已逐行确认）：
///   - <c>ApplyBisectStep</c>/<c>RestoreBisectMods</c> 捕获异常后只写日志；
///   - 禁用半区失败仍然返回 200 且 <c>waitingForReport=true</c>，用户据此继续收窄；
///   - 完成/放弃路径在做完恢复后**无条件** <c>ModBisectService.Remove</c>，
///     恢复失败时恢复依据随会话一起消失，而且"成功"是假的。
///
/// 本文件验证的契约：
///   1. 一轮里任一文件禁用失败 → API 不得报告"本轮就绪"，而是 409 + needsRecovery + 逐文件原因；
///   2. 进入 needsRecovery 后不接受测试反馈，必须先「放弃」并确认恢复；
///   3. 恢复未核对通过 → 会话保留（恢复上下文不丢），并且可以修好磁盘后重试成功；
///   4. 用户原本禁用的 Mod 不会被误启用；
///   5. 同一轮的重复提交不会推进两轮；同一实例的并发请求被显式拒绝；
///   6. 定位进行中禁止手动改 Mod。
/// </summary>
[Collection("LocalApiService")]
public class BisectFileOperationTests
{
	private readonly ITestOutputHelper _out;

	public BisectFileOperationTests(ITestOutputHelper output) => _out = output;

	/* ===================== 夹具 ===================== */

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string ModsDir => Path.Combine(Root, "mods");
		public string InstanceId { get; } = "inst-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		public AppConfig Config { get; }

		public Fixture(params string[] modFileNames)
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-bisect-io-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(ModsDir);

			foreach (string name in modFileNames)
			{
				string path = Path.Combine(ModsDir, name);
				if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
				{
					File.WriteAllText(path, "DISABLED:" + name);
				}
				else
				{
					File.WriteAllText(path, "ENABLED:" + name);
				}
			}

			Config = new AppConfig
			{
				GameDirectory = Root,
				SelectedInstanceId = InstanceId,
				Instances = new List<GameInstance>
				{
					new GameInstance { Id = InstanceId, Name = "bisect", GameDirectory = Root, MinecraftVersion = "1.20.4" }
				}
			};
		}

		public string Api(string suffix) => "/api/instances/" + InstanceId + suffix;
		public string Mod(string name) => Path.Combine(ModsDir, name);

		public void Dispose()
		{
			try { ModBisectService.Remove(InstanceId); } catch { }
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static string Sha(string path)
	{
		using FileStream fs = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(fs));
	}

	private static (int Status, JsonElement Root) Parse(string body) =>
		(0, JsonDocument.Parse(body).RootElement.Clone());

	private static string? Error(string body)
	{
		try
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			return doc.RootElement.TryGetProperty("error", out JsonElement el) ? el.GetString() : null;
		}
		catch { }
		return null;
	}

	private static bool Flag(string body, string name)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		return doc.RootElement.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.True;
	}

	private static JsonElement FileEntry(string body, string fileName)
	{
		using JsonDocument doc = JsonDocument.Parse(body);
		foreach (JsonElement f in doc.RootElement.GetProperty("files").EnumerateArray())
		{
			if (f.GetProperty("fileName").GetString() == fileName)
			{
				return f.Clone();
			}
		}
		throw new Xunit.Sdk.XunitException($"响应里没有 {fileName} 的结果项：{body}");
	}

	/* ===================== 1. 禁用失败 → 不得报告本轮就绪 ===================== */

	[Fact]
	public async Task Start_WhenDisableFails_Reports409WithNeedsRecovery_NotRoundReady()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		// 故障注入：把禁用目标占成目录，File.Move 必然失败。
		Directory.CreateDirectory(fx.Mod("a.jar.disabled"));

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		_out.WriteLine($"[start-disable-fail] -> {status} body={body}");

		Assert.Equal(409, status);
		Assert.False(Flag(body, "ok"), "失败时不得报 ok");
		Assert.True(Flag(body, "needsRecovery"), "必须进入 needsRecovery");
		Assert.False(Flag(body, "waitingForReport"), "禁用失败时绝不能声称本轮已就绪、等待反馈");
		Assert.Equal("failed", FileEntry(body, "a.jar").GetProperty("state").GetString());

		// 文件没有被"半禁用"：a.jar 仍在启用态
		Assert.True(File.Exists(fx.Mod("a.jar")), "失败的禁用不得改动源文件");
	}

	/* ===================== 2. needsRecovery 期间拒绝推进 ===================== */

	[Fact]
	public async Task Test_DuringNeedsRecovery_IsRejected_AndDoesNotNarrowCandidates()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		Directory.CreateDirectory(fx.Mod("a.jar.disabled"));

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		(_, string startBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.True(Flag(startBody, "needsRecovery"));

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/test"), new { crashed = false });
		_out.WriteLine($"[test-during-recovery] -> {status} error={Error(body)}");

		Assert.Equal(409, status);
		Assert.Contains("未完成", Error(body) ?? string.Empty);
		Assert.False(Flag(body, "finished"));
	}

	/* ===================== 3. 恢复失败 → 会话必须保留 ===================== */

	[Fact]
	public async Task GiveUp_WhenRestoreFails_KeepsSessionAndRecoveryContext()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, string startBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);
		Assert.True(File.Exists(fx.Mod("a.jar.disabled")), "start 应当禁用一半");

		// 故障注入：在启用态路径上放一个目录，EnableMod 的改名必然失败。
		Directory.CreateDirectory(fx.Mod("a.jar"));

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/giveup"), new { });
		_out.WriteLine($"[giveup-restore-fail] -> {status} body={body}");

		Assert.Equal(409, status);
		Assert.True(Flag(body, "needsRecovery"), "恢复失败必须标 needsRecovery");
		Assert.Equal("failed", FileEntry(body, "a.jar").GetProperty("state").GetString());

		// 会话必须还在：再发一次 test 应当是"文件操作未完成"，而不是"没有进行中的会话"
		(int testStatus, string testBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/test"), new { crashed = false });
		Assert.Equal(409, testStatus);
		Assert.Contains("未完成", Error(testBody) ?? string.Empty);
		Assert.DoesNotContain("没有进行中的二分定位会话", Error(testBody) ?? string.Empty);
	}

	/* ===================== 4. 修好磁盘后重试恢复应当成功 ===================== */

	[Fact]
	public async Task GiveUp_RetryAfterFixingDiskState_RestoresAndRemovesSession()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		string originalSha = Sha(fx.Mod("a.jar"));

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });

		Directory.CreateDirectory(fx.Mod("a.jar"));
		(int failStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/giveup"), new { });
		Assert.Equal(409, failStatus);

		// 修好磁盘状态后重试
		Directory.Delete(fx.Mod("a.jar"));
		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/giveup"), new { });
		_out.WriteLine($"[giveup-retry] -> {status} body={body}");

		Assert.Equal(200, status);
		Assert.False(Flag(body, "needsRecovery"));
		Assert.True(File.Exists(fx.Mod("a.jar")), "重试后应恢复到启用态");
		Assert.False(File.Exists(fx.Mod("a.jar.disabled")));
		Assert.Equal(originalSha, Sha(fx.Mod("a.jar")));

		// 恢复核对通过后会话才被移除
		(int afterStatus, string afterBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/giveup"), new { });
		Assert.Equal(400, afterStatus);
		Assert.Contains("没有进行中的二分定位会话", Error(afterBody) ?? string.Empty);
	}

	/* ===================== 5. 用户原本禁用的 Mod 不被误启用 ===================== */

	[Fact]
	public async Task GiveUp_DoesNotReEnableModsTheUserHadDisabled()
	{
		using var fx = new Fixture("a.jar", "b.jar.disabled");
		string userDisabledSha = Sha(fx.Mod("b.jar.disabled"));

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);
		(_, string startBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });

		using (JsonDocument doc = JsonDocument.Parse(startBody))
		{
			string[] enabledAtStart = doc.RootElement.GetProperty("enabledAtStart")
				.EnumerateArray().Select(x => x.GetString()!).ToArray();
			Assert.Equal(new[] { "a.jar" }, enabledAtStart);
		}

		(int status, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/giveup"), new { });
		Assert.Equal(200, status);

		Assert.True(File.Exists(fx.Mod("b.jar.disabled")), "用户原本禁用的 Mod 必须保持禁用");
		Assert.False(File.Exists(fx.Mod("b.jar")));
		Assert.Equal(userDisabledSha, Sha(fx.Mod("b.jar.disabled")));
	}

	/* ===================== 6. 同一轮的重复提交不推进两轮 ===================== */

	[Fact]
	public async Task Test_WithStaleStep_IsRejectedInsteadOfAdvancingWrongRound()
	{
		using var fx = new Fixture("a.jar", "b.jar", "c.jar", "d.jar");
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, string startBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);
		int step = JsonDocument.Parse(startBody).RootElement.GetProperty("step").GetInt32();

		(int okStatus, string okBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/test"), new { crashed = true, step });
		_out.WriteLine($"[test-round-ok] -> {okStatus} body={okBody}");
		Assert.Equal(200, okStatus);
		int newStep = JsonDocument.Parse(okBody).RootElement.GetProperty("step").GetInt32();
		Assert.True(newStep > step, "正常提交应当推进轮次");

		// 用**过期轮次**重复提交（模拟双击）：必须拒绝，且不得推进
		(int staleStatus, string staleBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/test"), new { crashed = true, step });
		_out.WriteLine($"[test-stale-step] -> {staleStatus} error={Error(staleBody)}");

		Assert.Equal(409, staleStatus);
		Assert.Contains("已经推进", Error(staleBody) ?? string.Empty);
		Assert.Equal(newStep, JsonDocument.Parse(staleBody).RootElement.GetProperty("step").GetInt32());
	}

	/* ===================== 7. 并发原语 ===================== */

	[Fact]
	public void ConcurrentGuard_RejectsSecondOperationOnSameInstance()
	{
		string instanceId = "inst-guard-" + Guid.NewGuid().ToString("N").Substring(0, 8);

		Assert.False(LocalApiService.IsBisectOperationInFlightForTest(instanceId));
		Assert.True(LocalApiService.TryBeginBisectOperationForTest(instanceId), "第一次应当拿到占位");
		try
		{
			Assert.True(LocalApiService.IsBisectOperationInFlightForTest(instanceId));
			Assert.False(LocalApiService.TryBeginBisectOperationForTest(instanceId), "同一实例不得并发");
		}
		finally
		{
			LocalApiService.EndBisectOperationForTest(instanceId);
		}

		Assert.False(LocalApiService.IsBisectOperationInFlightForTest(instanceId));
		Assert.True(LocalApiService.TryBeginBisectOperationForTest(instanceId), "释放后应当可以再取");
		LocalApiService.EndBisectOperationForTest(instanceId);
	}

	/* ===================== 8. 定位进行中禁止手动改 Mod ===================== */

	[Fact]
	public async Task ManualModEdits_AreRejectedWhileBisectIsActive()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, _) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);

		(int toggleStatus, string toggleBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/b.jar/toggle"), new { });
		_out.WriteLine($"[manual-toggle-during-bisect] -> {toggleStatus} error={Error(toggleBody)}");
		Assert.Equal(400, toggleStatus);
		Assert.Contains("二分定位", Error(toggleBody) ?? string.Empty);

		(int deleteStatus, string deleteBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/b.jar/delete"), new { });
		Assert.Equal(400, deleteStatus);
		Assert.Contains("二分定位", Error(deleteBody) ?? string.Empty);

		(int replaceStatus, string replaceBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/b.jar/replace"),
			new { newFileBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("NEW")) });
		Assert.Equal(400, replaceStatus);
		Assert.Contains("二分定位", Error(replaceBody) ?? string.Empty);

		// b.jar 一个字节都没被动过
		Assert.True(File.Exists(fx.Mod("b.jar")));
	}

	/* ===================== 9. 正常路径不回归 ===================== */

	[Fact]
	public async Task HappyPath_StartTestGiveUp_StillWorksWithVerifiedRestore()
	{
		using var fx = new Fixture("a.jar", "b.jar");
		string jarSha = Sha(fx.Mod("a.jar"));
		string otherSha = Sha(fx.Mod("b.jar"));

		await using var h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int startStatus, string startBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/start"), new { });
		Assert.Equal(200, startStatus);
		Assert.False(Flag(startBody, "needsRecovery"));
		int step = JsonDocument.Parse(startBody).RootElement.GetProperty("step").GetInt32();

		(int testStatus, string testBody) = await h.SendAsync(HttpMethod.Post, fx.Api("/bisect/test"), new { crashed = false, step });
		_out.WriteLine($"[happy-test] -> {testStatus} body={testBody}");
		Assert.Equal(200, testStatus);
		using (JsonDocument doc = JsonDocument.Parse(testBody))
		{
			Assert.True(doc.RootElement.GetProperty("finished").GetBoolean());
			Assert.Equal("a.jar", doc.RootElement.GetProperty("culprit").GetString());
		}

		// 结束路径已核对恢复：两个 jar 都回到启用态且 SHA 不变
		Assert.True(File.Exists(fx.Mod("a.jar")));
		Assert.True(File.Exists(fx.Mod("b.jar")));
		Assert.Equal(jarSha, Sha(fx.Mod("a.jar")));
		Assert.Equal(otherSha, Sha(fx.Mod("b.jar")));
		Assert.False(File.Exists(fx.Mod("a.jar.disabled")));
	}
}
