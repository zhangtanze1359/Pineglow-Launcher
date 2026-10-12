using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 本轮（S1–S3）边界常驻回归：把独立复验码的 5 个探针转入正式测试工程，并补全矩阵。
///
/// 关闭项：
///   * RA-01：撤销 Mod 替换时，**记录根之上**的上层目录被换成 junction 仍能越界写入；
///   * RA-02：伪造/错位的恢复记录可以消耗**另一个实例或另一次操作**的隔离文件；
///   * RA-03：回滚失败字段没有穿过 HTTP 边界（客户端只能解析中文散文）。
///
/// 全部使用合成临时目录与合成字节；HTTP 走真实 <see cref="LocalApiService"/> 与真实 HttpClient。
/// **不启动真实游戏、不触碰真实配置/凭据/存档。**
/// </summary>
[Collection("LocalApiService")]
public sealed class RemediationBoundaryRegressionTests
{
	private readonly ITestOutputHelper _out;

	public RemediationBoundaryRegressionTests(ITestOutputHelper output) => _out = output;

	private sealed class Fixture : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), "pineglow-rbr-" + Guid.NewGuid().ToString("N"));
		public string Game => Path.Combine(Root, "game");
		public string Mods => Path.Combine(Game, "mods");
		public string Outside => Path.Combine(Root, "outside");
		public string Recovery { get; } = Path.Combine(Path.GetTempPath(), "pineglow-rbr-rec-" + Guid.NewGuid().ToString("N"));
		public GameInstance Instance { get; }
		public AppConfig Config { get; }

		public Fixture()
		{
			Directory.CreateDirectory(Mods);
			Directory.CreateDirectory(Outside);
			Directory.CreateDirectory(Recovery);
			Instance = new GameInstance
			{
				Id = "rbr-" + Guid.NewGuid().ToString("N"),
				Name = "rbr",
				GameDirectory = Game,
				MinecraftVersion = "1.20.4"
			};
			Config = new AppConfig
			{
				GameDirectory = Game,
				Instances = { Instance },
				SelectedInstanceId = Instance.Id
			};
			File.WriteAllText(Path.Combine(Mods, "a.jar"), "old-A");
		}

		public string Api(string suffix) => "/api/instances/" + Instance.Id + suffix;

		public void Dispose()
		{
			try { RunningInstanceRegistry.MarkExited(Instance.Id); } catch { }
			try { ModBisectService.Remove(Instance.Id); } catch { }
			BreakJunction(Mods);
			BreakJunction(Game);
			foreach (string dir in new[] { Root, Recovery })
			{
				try
				{
					if (!Directory.Exists(dir)) continue;
					foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
					{
						try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
					}

					Directory.Delete(dir, true);
				}
				catch
				{
				}
			}
		}
	}

	private static void BreakJunction(string path)
	{
		try
		{
			if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
			{
				Directory.Delete(path, false);
			}
		}
		catch
		{
		}
	}

	private static async Task<bool> TryCreateJunctionAsync(string link, string target)
	{
		if (Directory.Exists(link))
		{
			Directory.Delete(link, true);
		}

		var psi = new ProcessStartInfo("powershell.exe")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		psi.ArgumentList.Add("-NoProfile");
		psi.ArgumentList.Add("-NonInteractive");
		psi.ArgumentList.Add("-Command");
		psi.ArgumentList.Add($"New-Item -ItemType Junction -Path '{link}' -Target '{target}' -ErrorAction Stop | Out-Null");
		using Process p = Process.Start(psi)!;
		string stdout = await p.StandardOutput.ReadToEndAsync();
		string stderr = await p.StandardError.ReadToEndAsync();
		await p.WaitForExitAsync();
		if (p.ExitCode != 0)
		{
			Console.WriteLine("junction setup failed: " + stdout + stderr);
			return false;
		}

		return (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
	}

	private static string Sha(string path)
	{
		using FileStream fs = File.OpenRead(path);
		return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
	}

	/* ===================== RA-01：记录根之上的 junction ===================== */

	[Fact]
	public async Task ModReplaceUndo_RejectsJunctionAboveRecordedModsRoot_Http()
	{
		using var fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);

		string originalJarSha = Sha(Path.Combine(fx.Mods, "a.jar"));
		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
			new { newFileBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("new-A")) });
		Assert.Equal(200, status);
		string operation = JsonDocument.Parse(body).RootElement.GetProperty("operationId").GetString()!;

		// 把上层 game 换成指向 outside 的联接
		Directory.Move(fx.Game, Path.Combine(fx.Root, "original-game"));
		Directory.CreateDirectory(Path.Combine(fx.Outside, "mods"));
		Assert.True(await TryCreateJunctionAsync(fx.Game, fx.Outside), "本机应能创建目录联接");

		(status, body) = await h.SendAsync(HttpMethod.Post, fx.Api("/recovery/" + operation + "/restore"));
		bool outside = File.Exists(Path.Combine(fx.Outside, "mods", "a.jar"));
		_out.WriteLine($"[RA-01] restoreHTTP={status}; body={body}; outsideFileExists={outside}");

		Assert.False(outside, "撤销替换不得穿过记录根之上的联接写到实例目录之外");
		Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("ok").GetBoolean());
		Assert.Equal("failed", JsonDocument.Parse(body).RootElement.GetProperty("status").GetString());

		// 隔离源必须原样保留，用户还能再次恢复
		var lookup = new FileRecoveryService(h.RecoveryRoot);
		string stored = Path.Combine(lookup.GetOperationDirectory(fx.Instance.Id, operation), "files", "a.jar");
		Assert.True(File.Exists(stored), "拒绝时隔离副本必须保留：" + stored);
		Assert.Equal(originalJarSha, Sha(stored));
	}

	[Fact]
	public async Task Restore_RejectsJunctionAboveRecordedRoot_EvenWithoutTrustedRoot()
	{
		using var fx = new Fixture();
		Directory.Delete(fx.Mods, true);
		Directory.CreateDirectory(fx.Mods);
		File.WriteAllText(Path.Combine(fx.Mods, "a.jar"), "old-A");

		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord? record = await service.LoadAsync(
			fx.Instance.Id, (await service.QuarantineAsync(fx.Instance.Id, fx.Mods, "mod-replace",
				new[] { Path.Combine(fx.Mods, "a.jar") })).OperationId);

		// 上层 game 变成联接（mod-replace 记录的根是 game\mods，它自己仍然是普通目录）
		Directory.Move(fx.Game, Path.Combine(fx.Root, "original-game"));
		Directory.CreateDirectory(Path.Combine(fx.Outside, "mods"));
		Assert.True(await TryCreateJunctionAsync(fx.Game, fx.Outside), "本机应能创建目录联接");

		FileRecoveryRecord restored = await service.RestoreAsync(record!);
		_out.WriteLine($"[RA-01/service-no-trust] state={restored.Files[0].State} error={restored.Files[0].Error}");

		Assert.Equal("failed", restored.Files[0].State);
		Assert.False(File.Exists(Path.Combine(fx.Outside, "mods", "a.jar")));
		Assert.True(File.Exists(record!.Files[0].StoredPath!), "隔离副本必须保留");
	}

	[Fact]
	public async Task Restore_RejectsJunctionAboveRecordedRoot_WithTrustedRoot()
	{
		using var fx = new Fixture();
		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Mods, "mod-replace",
			new[] { Path.Combine(fx.Mods, "a.jar") });

		Directory.Move(fx.Game, Path.Combine(fx.Root, "original-game"));
		Directory.CreateDirectory(Path.Combine(fx.Outside, "mods"));
		Assert.True(await TryCreateJunctionAsync(fx.Game, fx.Outside), "本机应能创建目录联接");

		FileRecoveryRecord restored = await service.RestoreAsync(record, overwrite: false, trustedGameDirectory: fx.Game);
		_out.WriteLine($"[RA-01/service-trust] state={restored.Files[0].State} error={restored.Files[0].Error}");

		Assert.Equal("failed", restored.Files[0].State);
		Assert.False(File.Exists(Path.Combine(fx.Outside, "mods", "a.jar")));
		Assert.True(File.Exists(record.Files[0].StoredPath!));
	}

	[Fact]
	public async Task Restore_RejectsJunctionInsteadOfOperationFilesDirectory()
	{
		using var fx = new Fixture();
		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete",
			new[] { Path.Combine(fx.Mods, "a.jar") });
		string stored = record.Files[0].StoredPath!;
		string filesDir = Path.GetDirectoryName(stored)!;
		string realFilesDir = filesDir + "-real";

		Directory.Move(filesDir, realFilesDir);
		Directory.CreateDirectory(Path.Combine(fx.Outside, "files"));
		Assert.True(await TryCreateJunctionAsync(filesDir, Path.Combine(fx.Outside, "files")), "本机应能创建目录联接");

		FileRecoveryRecord restored = await service.RestoreAsync(record);
		_out.WriteLine($"[RA-01/stored-junction] state={restored.Files[0].State} error={restored.Files[0].Error}");

		Assert.Equal("failed", restored.Files[0].State);
		Assert.False(File.Exists(Path.Combine(fx.Mods, "a.jar")));
		// stored = <op>/files/mods/a.jar，因此 filesDir 是 <op>/files/mods；真实副本被移到 "<filesDir>-real"
		Assert.True(File.Exists(Path.Combine(realFilesDir, "a.jar")), "真实隔离副本必须原样保留");
	}

	/* ===================== RA-02：实例 / 操作绑定 ===================== */

	[Fact]
	public async Task StoredPath_MustBelongToSameInstanceAndOperation_Service()
	{
		using var fx = new Fixture();
		var service = new FileRecoveryService(fx.Recovery);

		FileRecoveryRecord a = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "A",
			new[] { Path.Combine(fx.Mods, "a.jar") });
		string otherGame = Path.Combine(fx.Root, "other-game");
		Directory.CreateDirectory(otherGame);
		string bfile = Path.Combine(otherGame, "b.jar");
		File.WriteAllText(bfile, "other-instance-B");
		FileRecoveryRecord b = await service.QuarantineAsync("other-instance", otherGame, "B", new[] { bfile });

		string originalAStored = a.Files[0].StoredPath!;
		a.Files[0].StoredPath = b.Files[0].StoredPath;
		a.Files[0].Sha256 = b.Files[0].Sha256;

		FileRecoveryRecord restored = await service.RestoreAsync(a);
		_out.WriteLine($"[RA-02/service] status={restored.Status} error={restored.Files[0].Error} " +
			$"BStoredExists={File.Exists(b.Files[0].StoredPath!)} AStoredExists={File.Exists(originalAStored)}");

		Assert.Equal("failed", restored.Files[0].State);
		Assert.True(File.Exists(b.Files[0].StoredPath!), "另一个实例的隔离文件不得被消耗");
		Assert.True(File.Exists(originalAStored), "本实例的隔离文件也不得被消耗");
		Assert.False(File.Exists(a.Files[0].OriginalPath));
		Assert.Contains("本实例", restored.Files[0].Error ?? string.Empty);
	}

	[Fact]
	public async Task HttpForgedStoredPath_MustNotConsumeOtherInstanceFile()
	{
		using var fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/delete"));
		Assert.Equal(200, status);
		string operation = JsonDocument.Parse(body).RootElement.GetProperty("operationId").GetString()!;

		var service = new FileRecoveryService(h.RecoveryRoot);
		FileRecoveryRecord a = (await service.LoadAsync(fx.Instance.Id, operation))!;
		string originalAStored = a.Files[0].StoredPath!;

		string otherGame = Path.Combine(fx.Root, "http-other-game");
		Directory.CreateDirectory(otherGame);
		string bfile = Path.Combine(otherGame, "b.jar");
		File.WriteAllText(bfile, "http-other-instance-B");
		FileRecoveryRecord b = await service.QuarantineAsync("other-instance", otherGame, "B", new[] { bfile });

		a.Files[0].StoredPath = b.Files[0].StoredPath;
		a.Files[0].Sha256 = b.Files[0].Sha256;
		await File.WriteAllTextAsync(
			Path.Combine(service.GetOperationDirectory(fx.Instance.Id, operation), "record.json"),
			JsonSerializer.Serialize(a, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

		(status, body) = await h.SendAsync(HttpMethod.Post, fx.Api("/recovery/" + operation + "/restore"));
		_out.WriteLine($"[RA-02/http] HTTP={status}; body={body}; BStoredExists={File.Exists(b.Files[0].StoredPath!)}");

		Assert.False(File.Exists(Path.Combine(fx.Mods, "a.jar")));
		Assert.True(File.Exists(b.Files[0].StoredPath!), "HTTP 不得接受指向另一个实例隔离文件的记录");
		Assert.True(File.Exists(originalAStored));
		Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("ok").GetBoolean());
	}

	[Fact]
	public async Task Restore_RejectsSourceFromAnotherOperationOfTheSameInstance()
	{
		using var fx = new Fixture();
		var service = new FileRecoveryService(fx.Recovery);
		File.WriteAllText(Path.Combine(fx.Mods, "b.jar"), "old-B");

		FileRecoveryRecord first = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete",
			new[] { Path.Combine(fx.Mods, "a.jar") });
		FileRecoveryRecord second = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete",
			new[] { Path.Combine(fx.Mods, "b.jar") });
		Assert.NotEqual(first.OperationId, second.OperationId);

		string firstStored = first.Files[0].StoredPath!;
		first.Files[0].StoredPath = second.Files[0].StoredPath;
		first.Files[0].Sha256 = second.Files[0].Sha256;

		FileRecoveryRecord restored = await service.RestoreAsync(first);
		_out.WriteLine($"[RA-02/op] state={restored.Files[0].State} error={restored.Files[0].Error}");

		Assert.Equal("failed", restored.Files[0].State);
		Assert.True(File.Exists(second.Files[0].StoredPath!), "另一次操作的隔离文件不得被消耗");
		Assert.True(File.Exists(firstStored));
	}

	[Fact]
	public async Task Restore_RejectsWhenStoredSourceShaDoesNotMatchRecord()
	{
		using var fx = new Fixture();
		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete",
			new[] { Path.Combine(fx.Mods, "a.jar") });

		// 记录里的 SHA 被篡改成别的值：消费源之前必须拒绝
		record.Files[0].Sha256 = new string('A', 64);

		FileRecoveryRecord restored = await service.RestoreAsync(record);
		_out.WriteLine($"[RA-02/sha] state={restored.Files[0].State} error={restored.Files[0].Error}");

		Assert.Equal("failed", restored.Files[0].State);
		Assert.Contains("SHA256", restored.Files[0].Error ?? string.Empty);
		Assert.True(File.Exists(record.Files[0].StoredPath!), "源不得被消费");
		Assert.False(File.Exists(Path.Combine(fx.Mods, "a.jar")));
	}

	[Fact]
	public async Task Restore_RejectsRecordWhoseRelativePathDoesNotMatchTheSource()
	{
		using var fx = new Fixture();
		var service = new FileRecoveryService(fx.Recovery);
		FileRecoveryRecord record = await service.QuarantineAsync(fx.Instance.Id, fx.Game, "mod-delete",
			new[] { Path.Combine(fx.Mods, "a.jar") });

		record.Files[0].RelativePath = "mods/other.jar";

		FileRecoveryRecord restored = await service.RestoreAsync(record);
		_out.WriteLine($"[RA-02/relative] state={restored.Files[0].State} error={restored.Files[0].Error}");

		Assert.Equal("failed", restored.Files[0].State);
		Assert.True(File.Exists(record.Files[0].StoredPath!));
	}

	[Fact]
	public async Task LoadAsync_RejectsRecordWhoseInstanceIdOnlyNormalizesToTheRequestedOne()
	{
		using var fx = new Fixture();
		var service = new FileRecoveryService(fx.Recovery);

		// `SafeSegment` 会剥掉 '-'，因此 "a-b" 与 "ab" 指向同一个目录。
		FileRecoveryRecord record = await service.QuarantineAsync("ab", fx.Game, "mod-delete",
			new[] { Path.Combine(fx.Mods, "a.jar") });

		FileRecoveryRecord? same = await service.LoadAsync("ab", record.OperationId);
		FileRecoveryRecord? collided = await service.LoadAsync("a-b", record.OperationId);

		_out.WriteLine($"[RA-02/id] exact={same != null}; normalizedCollision={collided != null}");

		Assert.NotNull(same);
		Assert.Null(collided);
	}

	/* ===================== RA-03：HTTP 结构化失败 ===================== */

	[Fact]
	public async Task RollbackFailure_HttpExposesStructuredRecoveryFields()
	{
		using var fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);

		ModReplacementService.BeforeCommitForTest = path =>
		{
			File.WriteAllText(path, "foreign-readonly");
			File.SetAttributes(path, FileAttributes.ReadOnly);
			throw new IOException("injected commit failure");
		};
		try
		{
			(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
				new { newFileBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("new-A")) });
			_out.WriteLine($"[RA-03/fail] HTTP={status}; body={body}");

			Assert.Equal(400, status);
			JsonElement json = JsonDocument.Parse(body).RootElement;
			Assert.True(json.TryGetProperty("needsRecovery", out JsonElement needs), "needsRecovery 必须是 JSON 字段");
			Assert.True(needs.GetBoolean());
			Assert.True(json.TryGetProperty("operationId", out JsonElement op));
			Assert.False(string.IsNullOrWhiteSpace(op.GetString()));
			Assert.True(json.TryGetProperty("recoveryStatus", out JsonElement recoveryStatus));
			Assert.Equal("failed", recoveryStatus.GetString());
			Assert.True(json.TryGetProperty("failedFiles", out JsonElement failedFiles));
			Assert.True(failedFiles.GetArrayLength() > 0);
			Assert.True(json.TryGetProperty("status", out JsonElement st));
			Assert.Equal("commit_failed", st.GetString());
			Assert.True(json.TryGetProperty("error", out _), "既有 error 字段必须保持兼容");
		}
		finally
		{
			ModReplacementService.BeforeCommitForTest = null;
			File.SetAttributes(Path.Combine(fx.Mods, "a.jar"), FileAttributes.Normal);
		}
	}

	[Fact]
	public async Task RollbackSuccess_HttpReportsNoRecoveryNeeded()
	{
		using var fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);
		string jarSha = Sha(Path.Combine(fx.Mods, "a.jar"));

		ModReplacementService.BeforeCommitForTest = _ => throw new IOException("injected commit failure");
		try
		{
			(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
				new { newFileBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("new-A")) });
			_out.WriteLine($"[RA-03/success] HTTP={status}; body={body}");

			Assert.Equal(400, status);
			JsonElement json = JsonDocument.Parse(body).RootElement;
			Assert.False(json.GetProperty("needsRecovery").GetBoolean(), "逐项恢复成功时不得报 needsRecovery");
			Assert.Equal("restored", json.GetProperty("recoveryStatus").GetString());
			Assert.Equal(0, json.GetProperty("failedFiles").GetArrayLength());
			Assert.Equal("commit_failed", json.GetProperty("status").GetString());
			Assert.Equal(jarSha, Sha(Path.Combine(fx.Mods, "a.jar")));
		}
		finally
		{
			ModReplacementService.BeforeCommitForTest = null;
		}
	}

	[Fact]
	public async Task MetadataFailure_HttpExposesStructuredFields()
	{
		using var fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);

		ModReplacementService.BeforeMetadataWriteForTest = _ => throw new IOException("injected metadata failure");
		try
		{
			(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
				new
				{
					newFileBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("new-A")),
					version = new { versionNumber = "9.9.9", versionId = "v-1", projectId = "p-1", source = "modrinth" }
				});
			_out.WriteLine($"[RA-03/metadata] HTTP={status}; body={body}");

			Assert.Equal(400, status);
			JsonElement json = JsonDocument.Parse(body).RootElement;
			Assert.Equal("metadata_failed", json.GetProperty("status").GetString());
			Assert.False(json.GetProperty("needsRecovery").GetBoolean());
			Assert.Equal("restored", json.GetProperty("recoveryStatus").GetString());
			Assert.Equal("old-A", File.ReadAllText(Path.Combine(fx.Mods, "a.jar")));
		}
		finally
		{
			ModReplacementService.BeforeMetadataWriteForTest = null;
		}
	}

	/* ===================== 正常路径不得退化 ===================== */

	[Fact]
	public async Task ModReplaceUndo_NormalRestore_StillWorksWithTrustedRoot()
	{
		using var fx = new Fixture();
		await using LocalApiTestHarness h = await LocalApiTestHarness.StartAsync(fx.Config);
		string jarSha = Sha(Path.Combine(fx.Mods, "a.jar"));

		(int status, string body) = await h.SendAsync(HttpMethod.Post, fx.Api("/mods/a.jar/replace"),
			new { newFileBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("new-A")) });
		Assert.Equal(200, status);
		Assert.Equal("new-A", File.ReadAllText(Path.Combine(fx.Mods, "a.jar")));
		string operation = JsonDocument.Parse(body).RootElement.GetProperty("operationId").GetString()!;

		(status, body) = await h.SendAsync(HttpMethod.Post, fx.Api("/recovery/" + operation + "/restore"),
			new { overwrite = true });
		_out.WriteLine($"[normal-undo] HTTP={status}; body={body}");

		Assert.Equal(200, status);
		Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("ok").GetBoolean());
		Assert.Equal(jarSha, Sha(Path.Combine(fx.Mods, "a.jar")));
	}

	/* ===================== RA-05：并发异步流的凭据隔离（常驻） ===================== */

	[Fact]
	public async Task SecretBackend_ConcurrentAsyncFlowsStayIsolated()
	{
		long before = SecretStore.ProductionBackendCallCount;
		var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int count = 0;

		async Task<string> Work(string value)
		{
			using IDisposable scope = SecretStore.OverrideBackendForTest(new InMemorySecretStoreBackend(value));
			SecretStore.Save(SecretStore.ProxyPassword, value);
			if (Interlocked.Increment(ref count) == 2)
			{
				ready.SetResult(true);
			}

			await ready.Task;
			await Task.Yield();
			return SecretStore.Load(SecretStore.ProxyPassword);
		}

		string[] values = await Task.WhenAll(Task.Run(() => Work("one")), Task.Run(() => Work("two")));

		Assert.Equal(new[] { "one", "two" }, values);
		Assert.Equal(before, SecretStore.ProductionBackendCallCount);
		_out.WriteLine("[RA-05] 两条重叠异步流保持隔离；原生后端调用增量为 0");
	}
}
