using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR04 恢复区的**边界情形**覆盖（缺口登记表 A2/A3）。
///
/// MR04 报告 §六 的原话：
///   * "符号链接/重解析点拒绝：代码已实现（文件自身与祖先目录都检查），但**没有测试覆盖** ——
///      在 Windows 上创建符号链接需要特权或开发者模式，本机未验证"；
///   * "保留期到期行为：只实现'提示'（expiresAt + retentionDays），**没有任何自动清理**"；
///   * "文件被占用：会走 IOException 回退并逐项报错，但没有真实占用用例"。
///
/// 本组用**真实文件系统操作**关掉这三项：
///   * 重解析点：用**目录联接（junction）**覆盖"祖先目录"检查 —— junction 不需要特权，
///     因此本机可以真实验证（这正是 MR04 当时认为做不了的地方）；
///   * 文件被占用：用 `FileShare.None` 真实占用，断言"隔离失败但**源文件不丢**"且记录可查；
///   * 保留期：把"**没有自动清理**"这一决策**钉进测试**，防止以后有人悄悄加上静默删除。
/// </summary>
public class FileRecoveryEdgeCaseTests
{
	private readonly ITestOutputHelper _out;

	public FileRecoveryEdgeCaseTests(ITestOutputHelper output) => _out = output;

	private sealed class Fixture : IDisposable
	{
		public string Root { get; }
		public string GameDir { get; }
		public string ModsDir { get; }
		public string RecoveryRoot { get; }
		public FileRecoveryService Recovery { get; }
		public string InstanceId { get; } = "inst-edge";

		public Fixture()
		{
			Root = Path.Combine(Path.GetTempPath(), "pineglow-a2-" + Guid.NewGuid().ToString("N"));
			GameDir = Path.Combine(Root, "game");
			ModsDir = Path.Combine(GameDir, "mods");
			RecoveryRoot = Path.Combine(Root, "recovery");
			Directory.CreateDirectory(ModsDir);
			Recovery = new FileRecoveryService(RecoveryRoot);
		}

		public string WriteMod(string name, string content)
		{
			string path = Path.Combine(ModsDir, name);
			File.WriteAllText(path, content);
			return path;
		}

		public void Dispose()
		{
			try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
		}
	}

	/// <summary>用 `mklink /J` 建目录联接（Windows 上不需要管理员权限；符号链接才需要）。</summary>
	private static bool TryCreateJunction(string linkPath, string targetPath)
	{
		var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		using Process? p = Process.Start(psi);
		if (p == null)
		{
			return false;
		}

		p.WaitForExit(15000);
		return p.ExitCode == 0 && Directory.Exists(linkPath);
	}

	/* ===================== 1. 重解析点：祖先目录是联接 → 拒绝 ===================== */

	[Fact]
	public async Task Quarantine_RejectsFileReachedThroughJunctionAncestor()
	{
		using var fx = new Fixture();

		// 目标目录在游戏目录之外，用联接把它"挂"进游戏目录
		string outside = Path.Combine(fx.Root, "outside-mods");
		Directory.CreateDirectory(outside);
		string outsideJar = Path.Combine(outside, "linked-mod.jar");
		File.WriteAllText(outsideJar, "LINKED-CONTENT");

		string link = Path.Combine(fx.ModsDir, "linked");
		bool created = TryCreateJunction(link, outside);
		_out.WriteLine($"[A2] 创建目录联接 = {created}（{link} → {outside}）");
		Assert.True(created, "本机应能创建目录联接（mklink /J 不需要管理员权限）；若失败说明环境异常");

		// 经由联接访问那个 jar：其**祖先目录**含重解析点
		string viaLink = Path.Combine(link, "linked-mod.jar");
		Assert.True(File.Exists(viaLink), "经由联接应能看到目标文件");

		FileRecoveryRecord record = await fx.Recovery.QuarantineAsync(
			fx.InstanceId, fx.GameDir, "test", new[] { viaLink });

		FileRecoveryEntry entry = record.Files.Single();
		_out.WriteLine("[A2] entry.State = " + entry.State + " / Error = " + entry.Error);

		Assert.True(entry.State == "failed", $"经由重解析点祖先的文件必须被拒绝，实际状态 {entry.State}");
		Assert.Contains("重解析点", entry.Error ?? string.Empty, StringComparison.Ordinal);

		// 关键：源文件必须**原样还在**（拒绝 = 什么都不做）
		Assert.True(File.Exists(outsideJar), "被拒绝的文件必须原样保留");
		Assert.True(File.ReadAllText(outsideJar) == "LINKED-CONTENT", "被拒绝的文件内容不得改动");
		Assert.True(record.Status == "failed", "整条记录应标记为 failed，而不是假装成功");
	}

	/* ===================== 2. 重解析点：文件自身是符号链接 → 拒绝（环境依赖） ===================== */

	[Fact]
	public async Task Quarantine_RejectsSymlinkFile_WhenEnvironmentAllowsCreatingOne()
	{
		using var fx = new Fixture();
		string realJar = fx.WriteMod("real.jar", "REAL");
		string symlinkPath = Path.Combine(fx.ModsDir, "link.jar");

		try
		{
			File.CreateSymbolicLink(symlinkPath, realJar);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
		{
			// 本机没有开发者模式/特权时无法创建文件符号链接。
			// 这里**如实报告该子项未能在本环境验证**，而不是把它写成通过。
			_out.WriteLine("[A2] 本机无法创建文件符号链接（" + ex.GetType().Name + "）→ 该子项未验证；" +
				"祖先目录用 junction 已在另一用例覆盖");
			return;
		}

		FileRecoveryRecord record = await fx.Recovery.QuarantineAsync(
			fx.InstanceId, fx.GameDir, "test", new[] { symlinkPath });

		FileRecoveryEntry entry = record.Files.Single();
		_out.WriteLine("[A2] 符号链接 entry.State = " + entry.State + " / Error = " + entry.Error);
		Assert.True(entry.State == "failed", "符号链接本身必须被拒绝");
		Assert.Contains("重解析点", entry.Error ?? string.Empty, StringComparison.Ordinal);
		Assert.True(File.Exists(realJar), "真实文件不得被移动");
	}

	/* ===================== 3. 文件被占用（A16 修复后：逐项报错，不再抛出） ===================== */

	/// <summary>
	/// **本组曾经实测出的缺陷（A16）**：把文件以 `FileShare.None` 打开后调用 `QuarantineAsync`，
	/// 异常不是被逐项捕获，而是在**哈希阶段**（`HashFile`，第 399 行 `File.OpenRead`）就抛出并冲出
	/// `QuarantineAsync` —— 与 MR04 报告所述「走 IOException 回退并逐项报错」不符（那只覆盖了移动阶段）。
	///
	/// **本用例现在是修复后的回归守卫**：加锁文件必须被**逐项记为失败**，同时守住两条安全底线：
	/// 源文件不丢、操作记录已落盘（`QuarantineAsync` 先落记录再处理文件）。
	/// 修复前的"抛出"行为由 `red/` 下的日志留证（见本包 deliverable）。
	/// </summary>
	[Fact]
	public async Task Quarantine_LockedForReading_RecordsPerItemError_WithoutLosingSourceOrRecord()
	{
		using var fx = new Fixture();
		string jar = fx.WriteMod("locked.jar", "LOCKED-CONTENT");

		FileRecoveryRecord record;
		using (new FileStream(jar, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			record = await fx.Recovery.QuarantineAsync(fx.InstanceId, fx.GameDir, "test", new[] { jar });
		}

		FileRecoveryEntry entry = record.Files.Single();
		_out.WriteLine("[A2/A16] 加锁文件 entry.State = " + entry.State + " / Error = " + entry.Error);

		// ① 逐项记为失败（修复前这里会抛异常，根本走不到）
		Assert.True(entry.State == "failed", $"加锁文件应被逐项记为失败，实际 {entry.State}");
		Assert.False(string.IsNullOrWhiteSpace(entry.Error), "失败必须带可读原因");
		Assert.Contains("无法读取文件", entry.Error ?? string.Empty, StringComparison.Ordinal);
		Assert.True(record.Status == "failed", "整条记录应为 failed，而不是假装成功");

		// ② 安全底线：源文件必须还在、内容不变
		Assert.True(File.Exists(jar), "隔离失败后源文件必须仍在");
		Assert.True(File.ReadAllText(jar) == "LOCKED-CONTENT", "隔离失败不得改动源文件内容");

		// ③ 安全底线：操作记录不能丢
		List<FileRecoveryRecord> listed = await fx.Recovery.ListAsync(fx.InstanceId);
		Assert.True(listed.Count == 1, "即使逐项失败，操作记录也必须已经落盘（否则操作会彻底消失）");
		FileRecoveryRecord? loaded = await fx.Recovery.LoadAsync(fx.InstanceId, record.OperationId);
		Assert.NotNull(loaded);
		Assert.True(loaded!.Files.Single().State == "failed", "重新读出的记录应保留失败状态");
	}
	/* ===================== 4. 保留期：把"没有自动清理"钉进测试 ===================== */

	[Fact]
	public async Task Retention_ExpiredRecordsAreStillListed_NoSilentCleanup()
	{
		using var fx = new Fixture();
		string jar = fx.WriteMod("old.jar", "OLD");
		FileRecoveryRecord record = await fx.Recovery.QuarantineAsync(fx.InstanceId, fx.GameDir, "test", new[] { jar });

		// 策略：GetExpiresAt = CreatedAt + DefaultRetentionDays(30)
		Assert.True(FileRecoveryService.DefaultRetentionDays == 30, "保留期默认值应仍为 30 天");
		DateTimeOffset expires = FileRecoveryService.GetExpiresAt(record);
		Assert.True(expires == record.CreatedAt.AddDays(FileRecoveryService.DefaultRetentionDays),
			$"GetExpiresAt 应等于 CreatedAt + {FileRecoveryService.DefaultRetentionDays} 天");

		// 把记录改成"早就过期"（直接改盘上的记录文件），再列举
		string recordPath = Path.Combine(fx.Recovery.GetOperationDirectory(fx.InstanceId, record.OperationId), "record.json");
		Assert.True(File.Exists(recordPath), "记录文件应存在：" + recordPath);
		// 注意（第 36 轮修正）：**不要**用字符串替换 `CreatedAt.ToString("O")` 来伪造过期。
		// 序列化会省略末尾的零（例如 `.1234560` → `.123456`），因此当 CreatedAt 的
		// 第 7 位小数恰为 0 时替换会**静默失配** —— 那会让本用例约 10% 概率假失败（flaky）。
		// 改为在 JSON 层面确定性地改写 createdAt。
		using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(recordPath)))
		{
			var patched = new Dictionary<string, object?>();
			foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
			{
				patched[prop.Name] = prop.Name == "createdAt"
					? record.CreatedAt.AddDays(-90).ToString("O")
					: (object?)JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
			}

			patched["createdAt"] = record.CreatedAt.AddDays(-90).ToString("O");
			string rewritten = JsonSerializer.Serialize(patched);
			Assert.NotEqual(File.ReadAllText(recordPath), rewritten);
			File.WriteAllText(recordPath, rewritten);
		}

		FileRecoveryRecord? reloaded = await fx.Recovery.LoadAsync(fx.InstanceId, record.OperationId);
		Assert.NotNull(reloaded);
		Assert.True(FileRecoveryService.GetExpiresAt(reloaded!) < DateTimeOffset.UtcNow, "这条记录此刻应已过期");

		// 关键断言：**过期不等于被删除**
		List<FileRecoveryRecord> listed = await fx.Recovery.ListAsync(fx.InstanceId);
		Assert.True(listed.Any(r => r.OperationId == record.OperationId),
			"已过期的记录**仍必须被列出**：本项目的决策是只提示、不自动清理（永久清理需单独授权）。" +
			"若这条断言失败，说明有人加了静默清理 —— 那需要先改策略并补用例。");

		// 恢复能力也不应因为过期而消失
		FileRecoveryRecord restored = await fx.Recovery.RestoreAsync(reloaded!);
		Assert.True(restored.Status != "failed", "过期记录的恢复能力不应被静默取消");
		Assert.True(File.Exists(jar), "恢复后文件应回到原位");
	}
}
