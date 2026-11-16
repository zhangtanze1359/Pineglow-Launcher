using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// D3③「Mod 批量更新」的直测。判定走纯函数（毫秒级），执行走真实文件系统 + 真实的
/// `ModReplacementService`（零 mock）。
///
/// 三条不变量：
///   1. **失败即停**：失败项之后的条目一律 Skipped，已成功的保持 Applied；
///   2. **失败项不动源文件**：expectedSha 不匹配时，原 jar 一个字节都不许变；
///   3. **撤销有牙齿**：逐项撤销回到旧版本；目标文件被外部改过时**拒绝撤销**并报冲突。
/// </summary>
public sealed class BatchUpdateTests : IDisposable
{
	private readonly string _root;
	private readonly string _gameDir;
	private readonly string _modsDir;
	private readonly string _recoveryRoot;
	private readonly GameInstance _instance;
	private readonly ModService _mods;
	private readonly FileRecoveryService _recovery;

	public BatchUpdateTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "pg-batch-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		_gameDir = Path.Combine(_root, "game");
		_modsDir = Path.Combine(_gameDir, "mods");
		_recoveryRoot = Path.Combine(_root, "recovery");
		Directory.CreateDirectory(_modsDir);
		Directory.CreateDirectory(_recoveryRoot);
		_instance = new GameInstance
		{
			Id = "inst-batch",
			Name = "batch",
			GameDirectory = _gameDir,
			MinecraftVersion = "1.20.4"
		};
		_mods = new ModService(_gameDir);
		_recovery = new FileRecoveryService(_recoveryRoot);
		BatchUpdateRegistry.Clear();
	}

	public void Dispose()
	{
		BatchUpdateRegistry.Clear();
		try
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}
		catch
		{
		}
	}

	private void WriteMod(string fileName, string content) => File.WriteAllText(Path.Combine(_modsDir, fileName), content);

	private static string Sha256Of(string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
	}

	private BatchUpdateInput Input(string fileName, string newContent, bool correctSha = true)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(newContent);
		string sha = correctSha ? Sha256Of(newContent) : Sha256Of(newContent + "-wrong");
		return new BatchUpdateInput(fileName, bytes, sha, null);
	}

	private BatchUpdateJob NewJob(params string[] fileNames)
	{
		BatchUpdateJob job = new BatchUpdateJob { InstanceId = _instance.Id };
		foreach (string name in fileNames)
		{
			job.FileNames.Add(name);
			job.Statuses.Add(BatchItemStatus.Pending);
				}
		BatchUpdateRegistry.Add(job);
		return job;
	}

	/* ===================== 纯状态机 ===================== */

	[Fact]
	public void Planner_AfterFailure_SkipsTheRest_ButKeepsApplied()
	{
		List<BatchItemStatus> statuses = new()
		{
			BatchItemStatus.Applied, BatchItemStatus.Pending, BatchItemStatus.Pending
		};

		IReadOnlyList<BatchItemStatus> after = BatchUpdatePlanner.AfterFailure(statuses, 1);

		Assert.Equal(BatchItemStatus.Applied, after[0]);
		Assert.Equal(BatchItemStatus.Failed, after[1]);
		Assert.Equal(BatchItemStatus.Skipped, after[2]);
		Assert.Equal("partial", BatchUpdatePlanner.Summarize(after, cancelled: false));
	}

	[Fact]
	public void Planner_Cancel_TurnsPendingIntoSkipped()
	{
		List<BatchItemStatus> statuses = new()
		{
			BatchItemStatus.Applied, BatchItemStatus.Pending, BatchItemStatus.Pending
		};

		IReadOnlyList<BatchItemStatus> after = BatchUpdatePlanner.AfterCancel(statuses);

		Assert.Equal(BatchItemStatus.Applied, after[0]);
		Assert.Equal(BatchItemStatus.Skipped, after[1]);
		Assert.Equal(BatchItemStatus.Skipped, after[2]);
		Assert.Equal("cancelled", BatchUpdatePlanner.Summarize(after, cancelled: true));
	}

	[Fact]
	public void Planner_NextReturnsFirstPending_ThenComplete()
	{
		List<BatchItemStatus> statuses = new() { BatchItemStatus.Applied, BatchItemStatus.Pending };

		BatchUpdateStep step = BatchUpdatePlanner.Next(statuses, cancelRequested: false);
		Assert.True(step.HasWork);
		Assert.Equal(1, step.Index);

		BatchUpdateStep done = BatchUpdatePlanner.Next(new[] { BatchItemStatus.Applied, BatchItemStatus.Applied }, false);
		Assert.False(done.HasWork);
		Assert.Equal("complete", done.Reason);
	}

	/* ===================== 执行 + 失败 + 撤销 ===================== */

	[Fact]
	public async Task Runner_SecondItemFails_StopsAndKeepsFirst_ThenUndoRestoresIt()
	{
		WriteMod("a.jar", "OLD-A");
		WriteMod("b.jar", "OLD-B");
		WriteMod("c.jar", "OLD-C");

		BatchUpdateJob job = NewJob("a.jar", "b.jar", "c.jar");
		List<BatchUpdateInput> inputs = new()
		{
			Input("a.jar", "NEW-A"),
			Input("b.jar", "NEW-B", correctSha: false),   // ← 第 2 项必失败
			Input("c.jar", "NEW-C"),
		};

		await BatchUpdateRunner.RunAsync(job, inputs, _instance, _mods, _recovery);

		Assert.True(job.Finished);
		Assert.Equal(BatchItemStatus.Applied, job.Statuses[0]);
		Assert.Equal(BatchItemStatus.Failed, job.Statuses[1]);
		Assert.Equal(BatchItemStatus.Skipped, job.Statuses[2]);
		Assert.Equal("b.jar", job.FailedFile);
		Assert.Equal("partial", job.State);
		Assert.Single(job.Applied);

		Assert.Equal("NEW-A", File.ReadAllText(Path.Combine(_modsDir, "a.jar")));

		IReadOnlyList<BatchUndoResult> undo = await BatchUpdateRunner.UndoAsync(job, _instance, _mods, _recovery, _gameDir);

		Assert.True(undo.Single().Ok);
		Assert.Equal("OLD-A", File.ReadAllText(Path.Combine(_modsDir, "a.jar")));
		// 撤销只把 Applied → Undone；本批次仍有 Failed，所以整体状态仍是 partial（不是"全撤销"）
		Assert.Equal(BatchItemStatus.Undone, job.Statuses[0]);
		Assert.Equal("partial", job.State);
		Assert.Empty(job.Applied);
	}

	/// <summary>失败路径 A —— **源未损坏**：SHA 不匹配的那一项，原 jar 内容与 mtime 都不变。</summary>
	[Fact]
	public async Task Runner_FailedItem_LeavesSourceJarUntouched()
	{
		WriteMod("a.jar", "OLD-A");
		WriteMod("b.jar", "OLD-B");
		string bPath = Path.Combine(_modsDir, "b.jar");
		DateTime mtimeBefore = File.GetLastWriteTimeUtc(bPath);
		byte[] bytesBefore = File.ReadAllBytes(bPath);

		BatchUpdateJob job = NewJob("a.jar", "b.jar");
		List<BatchUpdateInput> inputs = new()
		{
			Input("a.jar", "NEW-A"),
			Input("b.jar", "NEW-B", correctSha: false)
		};

		await BatchUpdateRunner.RunAsync(job, inputs, _instance, _mods, _recovery);

		Assert.Equal(bytesBefore, File.ReadAllBytes(bPath));
		Assert.Equal(mtimeBefore, File.GetLastWriteTimeUtc(bPath));
	}

	/// <summary>失败路径 B —— **失败可回滚**：失败项在 mods 目录里不留临时文件/半成品。</summary>
	[Fact]
	public async Task Runner_FailedItem_LeavesNoTemporaryArtifact()
	{
		WriteMod("a.jar", "OLD-A");
		WriteMod("b.jar", "OLD-B");

		BatchUpdateJob job = NewJob("a.jar", "b.jar");
		List<BatchUpdateInput> inputs = new()
		{
			Input("a.jar", "NEW-A"),
			Input("b.jar", "NEW-B", correctSha: false)
		};

		await BatchUpdateRunner.RunAsync(job, inputs, _instance, _mods, _recovery);

		List<string> files = Directory.EnumerateFiles(_modsDir).Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToList();
		Assert.DoesNotContain(files, f => f.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));
		Assert.Contains("a.jar", files);
		Assert.Contains("b.jar", files);
	}

	/// <summary>撤销冲突：目标文件被外部改成别的版本 → 拒绝撤销该项，并如实回报冲突。</summary>
	[Fact]
	public async Task Undo_TargetModifiedExternally_IsRefused()
	{
		WriteMod("a.jar", "OLD-A");

		BatchUpdateJob job = NewJob("a.jar");
		await BatchUpdateRunner.RunAsync(job, new List<BatchUpdateInput> { Input("a.jar", "NEW-A") },
			_instance, _mods, _recovery);
		Assert.Equal(BatchItemStatus.Applied, job.Statuses[0]);

		// 外部改回一个"别人的版本"
		File.WriteAllText(Path.Combine(_modsDir, "a.jar"), "EXTERNAL");

		IReadOnlyList<BatchUndoResult> undo = await BatchUpdateRunner.UndoAsync(job, _instance, _mods, _recovery, _gameDir);

		Assert.False(undo.Single().Ok);
		Assert.Equal("target-modified", undo.Single().Reason);
		Assert.Equal("EXTERNAL", File.ReadAllText(Path.Combine(_modsDir, "a.jar")));
		Assert.Equal(BatchItemStatus.Applied, job.Statuses[0]);
	}
}
