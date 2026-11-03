using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR05 更新接线的覆盖（缺口登记表 A1）。
///
/// 为什么补这一组：`ModContentService` 会**写文件**（下载后导入或替换 Mod），
/// 生产调用点有 3 处（`InstancePackService`、`LocalApiService` ×2），
/// 但在此之前**测试项目里对它的引用数为 0**。
/// 它所属的 MR05 在 MG1（数据安全）里，而 MG1 的门禁正是"不可恢复误删、状态与磁盘不符"。
///
/// 本组聚焦 MR05 加的**更新接线**（`ModContentService.cs` 第 131–149 行）：
/// 已装过的同一在线项目要**事务化替换**（保留启用状态、旧版本进恢复区可撤销），
/// 而不是像以前那样在旧版本旁边再放一个 "xxx (1).jar"。
///
/// 全部用例**不发外网**：用假 HttpMessageHandler 供字节。
/// </summary>
public class ModContentUpdateWiringTests
{
	private readonly ITestOutputHelper _out;

	public ModContentUpdateWiringTests(ITestOutputHelper output) => _out = output;

	/* ===================== 夹具 ===================== */

	/// <summary>只回一个固定字节体的假处理器；不发外网。</summary>
	private sealed class BytesHandler : HttpMessageHandler
	{
		private readonly byte[] _payload;
		public int Calls;

		public BytesHandler(byte[] payload) => _payload = payload;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref Calls);
			var response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				RequestMessage = request,
				Content = new ByteArrayContent(_payload)
			};
			response.Content.Headers.ContentLength = _payload.Length;
			return Task.FromResult(response);
		}
	}

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string ModsDir { get; }
		public string RecoveryRoot { get; }
		public GameInstance Instance { get; }
		public ModService ModService { get; }
		public FileRecoveryService Recovery { get; }

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-a1-" + Guid.NewGuid().ToString("N"));
			ModsDir = Path.Combine(Root, "mods");
			RecoveryRoot = Path.Combine(Root, "recovery");
			Directory.CreateDirectory(ModsDir);
			Instance = new GameInstance { Id = "inst-a1", Name = "a1", GameDirectory = Root, MinecraftVersion = "1.20.4" };
			ModService = new ModService(Root);
			Recovery = new FileRecoveryService(RecoveryRoot);
		}

		/// <summary>预置一个"已装过的同一在线项目"：默认**禁用**（`.jar.disabled`），用于验证启用状态被保留。</summary>
		public string SeedInstalledMod(string projectId, byte[] content, bool disabled = true)
		{
			string path = Path.Combine(ModsDir, projectId + "-1.0.jar" + (disabled ? ".disabled" : string.Empty));
			File.WriteAllBytes(path, content);
			ModService.WriteOnlineMetadata(path, new ModVersion
			{
				Source = ModSource.Modrinth,
				ProjectId = projectId,
				VersionId = "v1",
				VersionNumber = "1.0",
				FileName = Path.GetFileName(path)
			});
			return path;
		}

		public List<string> ModEntries() =>
			Directory.EnumerateFiles(ModsDir).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
				|| f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	private static ModVersion NewVersion(string projectId, byte[] payload, string fileName, string downloadUrl = "https://example.invalid/mod.jar")
	{
		return new ModVersion
		{
			Source = ModSource.Modrinth,
			ProjectId = projectId,
			VersionId = "v2",
			VersionNumber = "2.0",
			FileName = fileName,
			DownloadUrl = downloadUrl,
			FileSize = payload.Length,
			Sha1 = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant()
		};
	}

	private static bool AnyFileContains(string root, byte[] needle)
	{
		foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			try
			{
				if (File.ReadAllBytes(file).SequenceEqual(needle))
				{
					return true;
				}
			}
			catch (IOException) { /* 占用中的文件跳过 */ }
		}

		return false;
	}

	/* ===================== 1. 核心：事务化替换 ===================== */

	[Fact]
	public async Task Update_ExistingProject_ReplacesInPlace_KeepsDisabledState_AndOldVersionStaysRecoverable()
	{
		using var fx = new Fixture();
		byte[] oldBytes = Encoding.UTF8.GetBytes("OLD-MOD-CONTENT");
		byte[] newBytes = Encoding.UTF8.GetBytes("NEW-MOD-CONTENT-v2");
		fx.SeedInstalledMod("example", oldBytes, disabled: true);

		var handler = new BytesHandler(newBytes);
		var content = new ModContentService(new HttpClient(handler), fx.Recovery);

		string result = await content.DownloadAndInstallAsync(fx.Instance, NewVersion("example", newBytes, "example-2.0.jar"), fx.ModService);

		// ① 没有并列副本：该项目的 jar 仍然只有 1 个
		List<string> entries = fx.ModEntries();
		_out.WriteLine("[A1] mods 目录条目：" + string.Join(" | ", entries.Select(Path.GetFileName)));
		Assert.True(entries.Count == 1, $"同一项目更新后不应出现并列副本，实际 {entries.Count} 个：{string.Join(", ", entries)}");

		// ② 内容换成了新版本
		Assert.True(File.Exists(result), "返回路径必须存在：" + result);
		Assert.True(File.ReadAllBytes(result).SequenceEqual(newBytes), "返回路径的内容应是新下载的字节");

		// ③ 启用状态被保留（旧的是 disabled，新的仍应 disabled）
		Assert.True(result.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase),
			"旧版本是禁用状态，更新后必须仍是禁用（MR05 契约：保留启用状态）。实际：" + Path.GetFileName(result));

		// ④ 旧版本进了恢复区，可撤销
		List<FileRecoveryRecord> records = await fx.Recovery.ListAsync(fx.Instance.Id);
		_out.WriteLine($"[A1] 恢复记录数 = {records.Count}");
		Assert.True(records.Count >= 1, "旧版本必须留下恢复记录，否则更新不可撤销");
		Assert.True(AnyFileContains(fx.RecoveryRoot, oldBytes), "恢复区里必须能找到旧版本的原字节");
	}

	/* ===================== 2. 替换失败不丢数据 ===================== */

	[Fact]
	public async Task Update_WhenReplacementFails_Throws_AndLeavesOldVersionIntact()
	{
		using var fx = new Fixture();
		byte[] oldBytes = Encoding.UTF8.GetBytes("OLD-MUST-SURVIVE");
		byte[] newBytes = Encoding.UTF8.GetBytes("NEW-WILL-FAIL");
		string installedPath = fx.SeedInstalledMod("example", oldBytes, disabled: false);

		var content = new ModContentService(new HttpClient(new BytesHandler(newBytes)), fx.Recovery);

		// 制造替换失败：把目标文件独占打开，替换阶段的文件操作会失败
		Exception? thrown = null;
		using (FileStream hold = new FileStream(installedPath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			try
			{
				await content.DownloadAndInstallAsync(fx.Instance, NewVersion("example", newBytes, "example-2.0.jar"), fx.ModService);
			}
			catch (Exception ex)
			{
				thrown = ex;
				_out.WriteLine("[A1] 预期失败：" + ex.GetType().Name + " / " + ex.Message);
			}
		}

		Assert.NotNull(thrown);

		// 关键：旧文件必须还在、内容不变（不能出现"更新失败把旧版本弄丢"）
		Assert.True(File.Exists(installedPath), "替换失败后旧文件必须仍然存在");
		Assert.True(File.ReadAllBytes(installedPath).SequenceEqual(oldBytes), "替换失败后旧文件内容必须不变");
	}

	/* ===================== 3. SHA1 不符 → 不安装任何东西 ===================== */

	[Fact]
	public async Task Download_Sha1Mismatch_Throws_AndInstallsNothing()
	{
		using var fx = new Fixture();
		byte[] oldBytes = Encoding.UTF8.GetBytes("OLD");
		fx.SeedInstalledMod("example", oldBytes, disabled: false);

		byte[] served = Encoding.UTF8.GetBytes("SERVED-BYTES");
		var handler = new BytesHandler(served);
		var content = new ModContentService(new HttpClient(handler), fx.Recovery);

		ModVersion version = NewVersion("example", served, "example-2.0.jar");
		version.Sha1 = new string('a', 40); // 故意与真实 SHA1 不符

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			content.DownloadAndInstallAsync(fx.Instance, version, fx.ModService));

		// 只有原来那一个文件，没有新装的
		Assert.True(fx.ModEntries().Count == 1, "校验失败不应安装任何文件");
		Assert.True(File.ReadAllBytes(fx.ModEntries()[0]).SequenceEqual(oldBytes), "校验失败不应改动已装文件");
	}

	/* ===================== 4. 未接线时的回退（把 seam 写进测试） ===================== */

	[Fact]
	public async Task WithoutRecoveryService_UpdateFallsBackToSideBySideImport()
	{
		using var fx = new Fixture();
		byte[] oldBytes = Encoding.UTF8.GetBytes("OLD");
		byte[] newBytes = Encoding.UTF8.GetBytes("NEW");
		fx.SeedInstalledMod("example", oldBytes, disabled: false);

		// 不传 recovery → 走旧的并列导入分支（这正是 MR05 之前的行为）
		var content = new ModContentService(new HttpClient(new BytesHandler(newBytes)));

		await content.DownloadAndInstallAsync(fx.Instance, NewVersion("example", newBytes, "example-2.0.jar"), fx.ModService);

		List<string> entries = fx.ModEntries();
		_out.WriteLine("[A1] 无 recovery 时的条目：" + string.Join(" | ", entries.Select(Path.GetFileName)));
		Assert.True(entries.Count == 2, $"未接线时应是并列导入（2 个），实际 {entries.Count} 个");
		Assert.True(entries.Any(f => File.ReadAllBytes(f).SequenceEqual(oldBytes)), "旧版本应仍在（并列）");
		Assert.True(entries.Any(f => File.ReadAllBytes(f).SequenceEqual(newBytes)), "新版本应已导入");
	}
}
