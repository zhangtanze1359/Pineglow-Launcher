using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-11-19（v1.0.5 **r2**）：自更新状态机的**并发/交错**回归。
///
/// r1 的用例只是「预设 applying，再发一次请求」，它证明不了**同时到达**的请求只启动一次替换
/// （Lead 独立审查指出：r1 里检查与置位之间隔着文件校验与 ApplyUpdate 调用，两个请求可同时通过）。
///
/// 本文件的用例全部用**生产 handler 本身**（走真实 HTTP → LocalApiService → UpdateGate），
/// 只替换三个受控委托：
///   · <c>FetchManifestInvokerForTest</c>：受控清单（不打网络，可阻塞以制造跨 await 交错）
///   · <c>DownloadUpdateInvokerForTest</c>：受控下载（可控延迟/结果，不打网络）
///   · <c>ApplyUpdateInvokerForTest</c>：只计数，**不写 .bat、不起 cmd、不碰任何用户 EXE**
///   · <c>ExitSchedulerForTest</c>：空操作，避免 testhost 被真实 Environment.Exit 杀掉
///
/// 覆盖：
///   1. 两个 apply 请求**同时**到达 → 只认领一次、只启动一次替换脚本；
///   2. 已认领后再来 apply → 回 applying，不启动第二次；
///   3. 认领后文件缺失 / 哈希不符 → 状态回落 failed 且**没有**启动脚本；
///   4. 启动脚本抛错 → 状态回落 failed，认领释放（下一次合法重试可以再认领）；
///   5. 下载与应用互斥（applying 时下载请求被 409 拒绝，不改当次下载结果）；
///   6. 下载的两个并发请求跨 await 交错 → 只启一个 worker，落伍的启动请求不覆盖新状态；
///   7. 落伍的下载 worker 不把状态写成 completed。
/// </summary>
[Collection("LocalApiService")]
public class UpdateConcurrencyTests
{
	private readonly ITestOutputHelper _out;

	public UpdateConcurrencyTests(ITestOutputHelper output) => _out = output;

	private sealed class TempDir : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-upd-conc-" + Guid.NewGuid().ToString("N"));
		public TempDir() => Directory.CreateDirectory(Path);
		public void Dispose() { try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { } }
	}

	private sealed class TestScope : IDisposable
	{
		private readonly Action<string, string, string> _apply;
		private readonly Func<UpdateManifest, IProgress<double>?, CancellationToken, Task<string>> _download;
		private readonly Func<IReadOnlyList<string>, CancellationToken, Task<UpdateService.ManifestFetchResult>> _fetch;
		private readonly Action<int, TimeSpan> _exit;
		private readonly Func<string?, IReadOnlyList<string>>? _chain;

		public TestScope(
			Action<string, string, string>? apply = null,
			Func<UpdateManifest, IProgress<double>?, CancellationToken, Task<string>>? download = null,
			Func<IReadOnlyList<string>, CancellationToken, Task<UpdateService.ManifestFetchResult>>? fetch = null,
			Action<int, TimeSpan>? exit = null)
		{
			_apply = LocalApiService.ApplyUpdateInvokerForTest;
			_download = LocalApiService.DownloadUpdateInvokerForTest;
			_fetch = LocalApiService.FetchManifestInvokerForTest;
			_exit = LocalApiService.ExitSchedulerForTest;
			_chain = LocalApiService.UpdateUrlChainOverrideForTest;
			// 清单地址链也要受控：生产链是真实 Gitee/GitHub；这里给一个占位地址，
			// 真正取清单由 FetchManifestInvokerForTest 决定（不打网络）。
			LocalApiService.UpdateUrlChainOverrideForTest = _ => new[] { "https://updates.invalid/r2-fixture.json" };
			// 自更新状态是进程级静态字段：每个用例从确定的 idle 起点开始，不依赖上一条用例的残留。
			LocalApiService.ResetUpdateStateForTest();
			if (apply != null) LocalApiService.ApplyUpdateInvokerForTest = apply;
			if (download != null) LocalApiService.DownloadUpdateInvokerForTest = download;
			if (fetch != null) LocalApiService.FetchManifestInvokerForTest = fetch;
			// 默认空操作：测试里绝不真的退出进程
			LocalApiService.ExitSchedulerForTest = exit ?? ((_, _) => { });
		}

		public void Dispose()
		{
			LocalApiService.ApplyUpdateInvokerForTest = _apply;
			LocalApiService.DownloadUpdateInvokerForTest = _download;
			LocalApiService.FetchManifestInvokerForTest = _fetch;
			LocalApiService.ExitSchedulerForTest = _exit;
			LocalApiService.UpdateUrlChainOverrideForTest = _chain;
		}
	}

	private static string Sha256Hex(byte[] data) =>
		Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

	private static UpdateService.ManifestFetchResult OkManifest(string version, string url, long size, string sha)
	{
		var manifest = new UpdateManifest
		{
			Version = version,
			Url = url,
			Size = size,
			Sha256 = sha,
			Notes = "r2-concurrency-fixture",
		};
		return new UpdateService.ManifestFetchResult(manifest, url, false, new[] { new UpdateService.ManifestAttempt(url, true, "ok") });
	}

	/* ============ 1. 两个 apply 同时到达 → 只认领一次、只启动一次 ============ */

	[Fact]
	public async Task TwoApplyRequestsArrivingTogether_LaunchReplacementExactlyOnce()
	{
		string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-apply-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(gameDir);
		string pkgDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-pkg-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(pkgDir);
		string pkg = Path.Combine(pkgDir, "Pineglow-Launcher-1.0.5-win-x64-single-file.exe");
		byte[] payload = System.Text.Encoding.UTF8.GetBytes("R2-CONCURRENCY-PAYLOAD");
		await File.WriteAllBytesAsync(pkg, payload);
		string sha = Sha256Hex(payload);

		int launches = 0;
		using var scope = new TestScope(
			apply: (_, _, _) =>
			{
				// 故意放慢「启动脚本」这一步：r1 的竞态窗口就在这里。
				Interlocked.Increment(ref launches);
				Thread.Sleep(120);
			});

		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });
			using (LocalApiService.OverrideUpdateStateForTest("completed", pkg, "1.0.5", sha))
			{
				// 两个请求真的同时发出（不是先 await 一个再发第二个）
				Task<(int Status, string Body)> first = h.SendAsync(HttpMethod.Post, "/api/update/apply");
				Task<(int Status, string Body)> second = h.SendAsync(HttpMethod.Post, "/api/update/apply");
				var results = await Task.WhenAll(first, second);

				_out.WriteLine("[r2][apply-concurrent] launchCount=" + launches);
				foreach (var r in results) _out.WriteLine("[r2][apply-concurrent] " + r.Status + " " + r.Body);

				Assert.Equal(1, Volatile.Read(ref launches));
				Assert.Equal(1, LocalApiService.ApplyClaimSnapshotForTest.Count);
				// 一个拿到 200 applying，另一个也必须是 applying 语义（200 + state=applying），不能是“又启动一次”
				foreach (var r in results)
				{
					Assert.Equal(200, r.Status);
					using JsonDocument doc = JsonDocument.Parse(r.Body);
					Assert.Equal("applying", doc.RootElement.GetProperty("state").GetString());
				}
			}
		}
		finally
		{
			try { Directory.Delete(gameDir, true); } catch { }
			try { Directory.Delete(pkgDir, true); } catch { }
		}
	}

	/* ============ 2. 已认领后再来 apply → 不启动第二次 ============ */

	[Fact]
	public async Task ApplyAfterClaim_DoesNotLaunchAgain()
	{
		string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-apply2-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(gameDir);
		string pkgDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-pkg2-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(pkgDir);
		string pkg = Path.Combine(pkgDir, "pkg.exe");
		byte[] payload = { 1, 2, 3, 4, 5 };
		await File.WriteAllBytesAsync(pkg, payload);
		string sha = Sha256Hex(payload);

		int launches = 0;
		using var scope = new TestScope(apply: (_, _, _) => Interlocked.Increment(ref launches));
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });
			using (LocalApiService.OverrideUpdateStateForTest("completed", pkg, "1.0.5", sha))
			{
				var (s1, b1) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
				var (s2, b2) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
				_out.WriteLine("[r2][apply-twice] " + s1 + " then " + s2 + " launches=" + launches);
				Assert.Equal(1, launches);
				Assert.Equal(200, s1);
				Assert.Equal(200, s2);
				using JsonDocument d2 = JsonDocument.Parse(b2);
				Assert.Equal("applying", d2.RootElement.GetProperty("state").GetString());
			}
		}
		finally
		{
			try { Directory.Delete(gameDir, true); } catch { }
			try { Directory.Delete(pkgDir, true); } catch { }
		}
	}

	/* ============ 3. 认领后文件缺失 / 哈希不符 → 不启动，状态 failed ============ */

	[Fact]
	public async Task ClaimedButPackageMissing_FailsWithoutLaunching()
	{
		string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-miss-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(gameDir);
		string missing = Path.Combine(Path.GetTempPath(), "pineglow-conc-missing-" + Guid.NewGuid().ToString("N") + ".exe");
		int launches = 0;
		using var scope = new TestScope(apply: (_, _, _) => Interlocked.Increment(ref launches));
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });
			using (LocalApiService.OverrideUpdateStateForTest("completed", missing, "1.0.5", new string('a', 64)))
			{
				(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
				_out.WriteLine("[r2][apply-missing] " + status + " " + body);
				Assert.Equal(400, status);
				Assert.Equal(0, launches);
				using JsonDocument doc = JsonDocument.Parse(body);
				Assert.Equal("failed", doc.RootElement.GetProperty("state").GetString());
				// 认领必须已释放（否则合法重试永远被拒）
				Assert.Equal(0, LocalApiService.ApplyClaimSnapshotForTest.Claim);
			}
		}
		finally
		{
			try { Directory.Delete(gameDir, true); } catch { }
		}
	}

	[Fact]
	public async Task ClaimedButHashMismatch_FailsWithoutLaunching()
	{
		string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-sha-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(gameDir);
		string pkgDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-sha-pkg-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(pkgDir);
		string pkg = Path.Combine(pkgDir, "pkg.exe");
		await File.WriteAllBytesAsync(pkg, new byte[] { 7, 7, 7 });
		int launches = 0;
		using var scope = new TestScope(apply: (_, _, _) => Interlocked.Increment(ref launches));
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });
			using (LocalApiService.OverrideUpdateStateForTest("completed", pkg, "1.0.5", new string('b', 64)))
			{
				(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
				_out.WriteLine("[r2][apply-badsha] " + status + " " + body);
				Assert.Equal(400, status);
				Assert.Equal(0, launches);
				using JsonDocument doc = JsonDocument.Parse(body);
				Assert.True(doc.RootElement.GetProperty("sha256Mismatch").GetBoolean());
				Assert.Equal(0, LocalApiService.ApplyClaimSnapshotForTest.Claim);
			}
		}
		finally
		{
			try { Directory.Delete(gameDir, true); } catch { }
			try { Directory.Delete(pkgDir, true); } catch { }
		}
	}

	/* ============ 4. 启动脚本抛错 → failed 且认领释放（下一次可重试） ============ */

	[Fact]
	public async Task LaunchFailure_ReleasesClaimAndAllowsLegitimateRetry()
	{
		string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-launchfail-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(gameDir);
		string pkgDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-launchfail-pkg-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(pkgDir);
		string pkg = Path.Combine(pkgDir, "pkg.exe");
		byte[] payload = { 4, 4, 4, 4 };
		await File.WriteAllBytesAsync(pkg, payload);
		string sha = Sha256Hex(payload);

		int launches = 0;
		bool fail = true;
		using var scope = new TestScope(apply: (_, _, _) =>
		{
			Interlocked.Increment(ref launches);
			if (fail) throw new InvalidOperationException("simulated launch failure");
		});
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });
			using (LocalApiService.OverrideUpdateStateForTest("completed", pkg, "1.0.5", sha))
			{
				(int s1, string b1) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
				_out.WriteLine("[r2][apply-launchfail] " + s1 + " " + b1);
				Assert.Equal(500, s1);
				using (JsonDocument d1 = JsonDocument.Parse(b1))
				{
					Assert.Equal("failed", d1.RootElement.GetProperty("state").GetString());
				}
				Assert.Equal(0, LocalApiService.ApplyClaimSnapshotForTest.Claim);

				// 合法重试：状态是 failed → 允许再认领（这正是 r1 缺失的语义）
				fail = false;
				(int s2, string b2) = await h.SendAsync(HttpMethod.Post, "/api/update/apply");
				_out.WriteLine("[r2][apply-retry] " + s2 + " " + b2 + " launches=" + launches);
				Assert.Equal(200, s2);
				Assert.Equal(2, launches);
				using JsonDocument d2 = JsonDocument.Parse(b2);
				Assert.Equal("applying", d2.RootElement.GetProperty("state").GetString());
			}
		}
		finally
		{
			try { Directory.Delete(gameDir, true); } catch { }
			try { Directory.Delete(pkgDir, true); } catch { }
		}
	}

	/* ============ 5. 下载与应用互斥 ============ */

	[Fact]
	public async Task ApplyingUpdate_RejectsDownloadRequestAndKeepsPackageState()
	{
		string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-mutex-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(gameDir);
		string pkgDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-mutex-pkg-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(pkgDir);
		string pkg = Path.Combine(pkgDir, "pkg.exe");
		byte[] payload = { 6, 6, 6 };
		await File.WriteAllBytesAsync(pkg, payload);
		string sha = Sha256Hex(payload);

		int manifestsFetched = 0;
		using var scope = new TestScope(
			apply: (_, _, _) => { },
			fetch: (_, _) =>
			{
				Interlocked.Increment(ref manifestsFetched);
				return Task.FromResult(OkManifest("9.9.9", "https://example.com/x.exe", 3, new string('c', 64)));
			});
		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });
			using (LocalApiService.OverrideUpdateStateForTest("applying", pkg, "1.0.5", sha))
			{
				(int status, string body) = await h.SendAsync(HttpMethod.Post, "/api/update/download");
				_out.WriteLine("[r2][download-while-applying] " + status + " " + body);
				Assert.Equal(409, status);
				Assert.Equal(0, manifestsFetched); // 连清单都不去取
				using JsonDocument doc = JsonDocument.Parse(body);
				Assert.Equal("applying", doc.RootElement.GetProperty("state").GetString());

				// 当次下载结果没被改掉
				var snap = LocalApiService.UpdateStateSnapshotForTest;
				Assert.Equal("applying", snap.State);
				Assert.Equal(pkg, snap.Path);
				Assert.Equal(sha, snap.Sha);
			}
		}
		finally
		{
			try { Directory.Delete(gameDir, true); } catch { }
			try { Directory.Delete(pkgDir, true); } catch { }
		}
	}

	/* ============ 6. 落伍的启动请求不覆盖新状态（跨 await 的时序） ============ */

	[Fact]
	public async Task StaleStarterRequest_DoesNotOverwriteNewerClaim()
	{
		string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-stale-start-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(gameDir);

		var fetchEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFetch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int downloads = 0;

		using var scope = new TestScope(
			apply: (_, _, _) => { },
			fetch: async (_, _) =>
			{
				fetchEntered.TrySetResult(true);
				await releaseFetch.Task;
				// 落伍的那次故意给一个更低的版本：一旦被采用，状态里的 version 就是旧的
				return OkManifest("0.0.1-stale", "https://example.com/old.exe", 3, new string('d', 64));
			},
			download: async (manifest, _, ct) =>
			{
				Interlocked.Increment(ref downloads);
				await Task.Yield();
				string p = Path.Combine(Path.GetTempPath(), "pineglow-conc-stale-start-" + Guid.NewGuid().ToString("N") + ".exe");
				await File.WriteAllBytesAsync(p, new byte[] { 1 });
				return p;
			});

		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });

			// 请求 A：已原子认领（状态 downloading、epoch=1），卡在取清单的 await 里
			Task<(int Status, string Body)> a = h.SendAsync(HttpMethod.Post, "/api/update/download");
			await fetchEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

			// 在 A 还挂着的时候，模拟「另一个更晚的下载已经认领」：代次推进到 2
			int newEpoch = LocalApiService.BumpUpdateEpochForTest();
			_out.WriteLine("[r2][stale-start] bumped epoch to " + newEpoch);

			// 放 A 回来：它必须发现自己的代次已过期，丢弃结果
			releaseFetch.TrySetResult(true);
			(int sa, string ba) = await a;
			_out.WriteLine("[r2][stale-start] A -> " + sa + " " + ba);
			Assert.Equal(409, sa);
			using (JsonDocument da = JsonDocument.Parse(ba))
			{
				Assert.Equal("superseded", da.RootElement.GetProperty("state").GetString());
			}

			// A 不得起 worker，也不得把 version 写成它取到的那份
			await Task.Delay(200);
			var snap = LocalApiService.UpdateStateSnapshotForTest;
			_out.WriteLine("[r2][stale-start] final state=" + snap.State + " epoch=" + snap.Epoch + " downloads=" + downloads);
			Assert.Equal(0, downloads);
			Assert.Equal(newEpoch, snap.Epoch);
			Assert.NotEqual("0.0.1-stale", snap.State);
		}
		finally
		{
			try { Directory.Delete(gameDir, true); } catch { }
		}
	}

	/* ============ 7. 落伍的下载 worker 不写回 completed ============ */

	[Fact]
	public async Task StaleDownloadWorker_DoesNotMarkStateCompleted()
	{
		string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-conc-stale-worker-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(gameDir);

		var downloadEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseDownload = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var scope = new TestScope(
			apply: (_, _, _) => { },
			fetch: (_, _) => Task.FromResult(OkManifest("9.9.9", "https://example.com/a.exe", 3, new string('f', 64))),
			download: async (manifest, _, ct) =>
			{
				string p = Path.Combine(Path.GetTempPath(), "pineglow-conc-stale-worker-" + Guid.NewGuid().ToString("N") + ".exe");
				await File.WriteAllBytesAsync(p, new byte[] { 1 });
				downloadEntered.TrySetResult(true);
				await releaseDownload.Task;   // 卡在这里，模拟「下载迟迟不回来」
				return p;
			});

		try
		{
			await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(new AppConfig { GameDirectory = gameDir });

			(int s1, string b1) = await h.SendAsync(HttpMethod.Post, "/api/update/download");
			Assert.Equal(200, s1);
			await downloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

			// 在旧 worker 挂着时，代次被推进（= 已有更新的一次下载认领）
			int newEpoch = LocalApiService.BumpUpdateEpochForTest();
			_out.WriteLine("[r2][stale-worker] bumped epoch to " + newEpoch);

			// 放开旧 worker：它不得把状态写成 completed
			releaseDownload.TrySetResult(true);
			for (int i = 0; i < 60; i++) await Task.Delay(50);

			var snap = LocalApiService.UpdateStateSnapshotForTest;
			_out.WriteLine("[r2][stale-worker] final state=" + snap.State + " epoch=" + snap.Epoch);
			Assert.NotEqual("completed", snap.State);
			Assert.Equal(newEpoch, snap.Epoch);
		}
		finally
		{
			try { Directory.Delete(gameDir, true); } catch { }
		}
	}
}
