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

	private static string Sha1Of(string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		return Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
	}

	/// <summary>只带下载地址的输入（字节由执行器逐项下载）。</summary>
	private static BatchUpdateInput DownloadInput(string fileName, string newContent, bool correctSha1 = true)
	{
		string sha1 = correctSha1 ? Sha1Of(newContent) : Sha1Of(newContent + "-wrong");
		return new BatchUpdateInput(fileName, null, null, null, "https://cdn.modrinth.com/" + fileName, sha1);
	}

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

	/* ===================== 更新检查（纯函数） ===================== */

	[Fact]
	public void ModUpdates_NoOnlineMetadata_IsSkipped()
	{
		List<ModFile> mods = new()
		{
			new ModFile { FileName = "local-only.jar" },
			new ModFile { FileName = "a.jar", OnlineSource = "modrinth", ProjectId = "p1", VersionId = "v1" }
		};
		List<ModVersion?> latest = new() { null, new ModVersion { VersionId = "v2", DownloadUrl = "https://cdn.modrinth.com/x.jar" } };

		ModUpdatePlan plan = ModUpdatePlanner.Plan(mods, latest);

		Assert.Single(plan.Candidates);
		Assert.Equal("a.jar", plan.Candidates[0].FileName);
		Assert.Equal(1, plan.SkippedNoMetadata);
		Assert.Equal(0, plan.SkippedUpToDate);
	}

	[Fact]
	public void ModUpdates_SameVersionId_IsUpToDate()
	{
		List<ModFile> mods = new()
		{
			new ModFile { FileName = "a.jar", OnlineSource = "modrinth", ProjectId = "p1", VersionId = "v2" }
		};
		List<ModVersion?> latest = new() { new ModVersion { VersionId = "v2", DownloadUrl = "https://cdn.modrinth.com/x.jar" } };

		ModUpdatePlan plan = ModUpdatePlanner.Plan(mods, latest);

		Assert.Empty(plan.Candidates);
		Assert.Equal(1, plan.SkippedUpToDate);
	}

	[Fact]
	public void ModUpdates_NewerVersionWithoutDownloadUrl_IsSkippedNotCrashed()
	{
		List<ModFile> mods = new()
		{
			new ModFile { FileName = "a.jar", OnlineSource = "modrinth", ProjectId = "p1", VersionId = "v1" },
			new ModFile { FileName = "b.jar", OnlineSource = "curseforge", ProjectId = "p2", VersionId = "v1" }
		};
		List<ModVersion?> latest = new()
		{
			new ModVersion { VersionId = "v2", DownloadUrl = "" },
			new ModVersion { VersionId = "v9", DownloadUrl = "https://mediafilez.forgecdn.net/y.jar", VersionNumber = "9.0" }
		};

		ModUpdatePlan plan = ModUpdatePlanner.Plan(mods, latest);

		Assert.Single(plan.Candidates);
		Assert.Equal("b.jar", plan.Candidates[0].FileName);
		Assert.Equal("9.0", plan.Candidates[0].Latest.VersionNumber);
		Assert.Equal(1, plan.SkippedNoDownload);
		Assert.Equal(2, plan.Checked);
	}

	/* ===================== 取消与逐项下载 ===================== */

	/// <summary>取消发生在开始前 → 一条都不下载、一条都不替换。</summary>
	[Fact]
	public async Task Runner_CancelBeforeStart_DownloadsNothing()
	{
		WriteMod("a.jar", "OLD-A");
		WriteMod("b.jar", "OLD-B");

		BatchUpdateJob job = NewJob("a.jar", "b.jar");
		job.CancelRequested = true;
		int downloads = 0;
		List<BatchUpdateInput> inputs = new() { DownloadInput("a.jar", "NEW-A"), DownloadInput("b.jar", "NEW-B") };

		await BatchUpdateRunner.RunAsync(job, inputs, _instance, _mods, _recovery, (url, ct) =>
		{
			downloads++;
			return Task.FromResult(Encoding.UTF8.GetBytes("NEW-A"));
		});

		Assert.Equal(0, downloads);
		Assert.All(job.Statuses, s => Assert.Equal(BatchItemStatus.Skipped, s));
		Assert.Equal("cancelled", job.State);
	}

	/// <summary>
	/// 取消发生在第一项提交时 → 第一项跑完（含它的下载），其后各项**连下载都不发生**。
	/// 这正是把下载挪进执行器要解决的问题：旧实现把全部下载塞在建任务之前，最慢的一段不可取消。
	/// </summary>
	[Fact]
	public async Task Runner_CancelDuringFirstItem_StopsLaterDownloads()
	{
		WriteMod("a.jar", "OLD-A");
		WriteMod("b.jar", "OLD-B");
		WriteMod("c.jar", "OLD-C");

		BatchUpdateJob job = NewJob("a.jar", "b.jar", "c.jar");
		int downloads = 0;
		Dictionary<string, string> contents = new()
		{
			["https://cdn.modrinth.com/a.jar"] = "NEW-A",
			["https://cdn.modrinth.com/b.jar"] = "NEW-B",
			["https://cdn.modrinth.com/c.jar"] = "NEW-C"
		};
		List<BatchUpdateInput> inputs = new()
		{
			DownloadInput("a.jar", "NEW-A"), DownloadInput("b.jar", "NEW-B"), DownloadInput("c.jar", "NEW-C")
		};

		await BatchUpdateRunner.RunAsync(job, inputs, _instance, _mods, _recovery, (url, ct) =>
		{
			Interlocked.Increment(ref downloads);
			// 第一项下载完成、替换之前就把取消旗标立起来 —— 确定、无竞态，
			// 而且不碰 BeforeCommitForTest 那种全局钩子（那会和并行跑的其他用例互相干扰）。
			if (url.EndsWith("a.jar", StringComparison.Ordinal))
			{
				job.CancelRequested = true;
			}
			return Task.FromResult(Encoding.UTF8.GetBytes(contents[url]));
		});

		Assert.Equal(1, downloads);
		Assert.Equal(BatchItemStatus.Applied, job.Statuses[0]);
		Assert.Equal(BatchItemStatus.Skipped, job.Statuses[1]);
		Assert.Equal(BatchItemStatus.Skipped, job.Statuses[2]);
		Assert.Equal("cancelled", job.State);
		Assert.Equal("NEW-A", File.ReadAllText(Path.Combine(_modsDir, "a.jar")));
		Assert.Equal("OLD-B", File.ReadAllText(Path.Combine(_modsDir, "b.jar")));
	}

	/// <summary>下载内容 sha1 与期望不符 → 该项失败，源文件一个字节不变。</summary>
	[Fact]
	public async Task Runner_DownloadSha1Mismatch_FailsWithoutTouchingSource()
	{
		WriteMod("a.jar", "OLD-A");
		byte[] before = File.ReadAllBytes(Path.Combine(_modsDir, "a.jar"));

		BatchUpdateJob job = NewJob("a.jar");
		List<BatchUpdateInput> inputs = new() { DownloadInput("a.jar", "NEW-A", correctSha1: false) };

		await BatchUpdateRunner.RunAsync(job, inputs, _instance, _mods, _recovery, (url, ct) =>
			Task.FromResult(Encoding.UTF8.GetBytes("NEW-A")));

		Assert.Equal(BatchItemStatus.Failed, job.Statuses[0]);
		Assert.Equal("partial", job.State);
		Assert.Equal(before, File.ReadAllBytes(Path.Combine(_modsDir, "a.jar")));
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

	/// <summary>
	/// 取消（一）：批次还没开始就被取消 → 一条都不跑，全部 Skipped，状态 cancelled。
	/// </summary>
	[Fact]
	public async Task Runner_CancelBeforeStart_SkipsEverything()
	{
		WriteMod("a.jar", "OLD-A");
		WriteMod("b.jar", "OLD-B");

		BatchUpdateJob job = NewJob("a.jar", "b.jar");
		job.CancelRequested = true;
		List<BatchUpdateInput> inputs = new()
		{
			Input("a.jar", "NEW-A"),
			Input("b.jar", "NEW-B")
		};

		await BatchUpdateRunner.RunAsync(job, inputs, _instance, _mods, _recovery);

		Assert.True(job.Finished);
		Assert.All(job.Statuses, s => Assert.Equal(BatchItemStatus.Skipped, s));
		Assert.Empty(job.Applied);
		Assert.Equal("cancelled", job.State);
		Assert.Equal("OLD-A", File.ReadAllText(Path.Combine(_modsDir, "a.jar")));
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
