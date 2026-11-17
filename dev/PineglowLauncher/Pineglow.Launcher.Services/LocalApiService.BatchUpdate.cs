using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// D3③「Mod 批量更新」的 HTTP 层。
///
/// 为什么是**任务式**而不是一个长请求：交接书要求「某项失败后停止未执行项、保留已成功结果并支持逐项撤销；
/// 取消停止后续项」——这三条都需要在一个批次**还在跑的时候**从外面看见状态并打断它，
/// 一个同步 POST 做不到（前端只能等到超时）。
///
/// 于是：`POST …/batch-update` 建任务并立刻返回 jobId；`GET` 看状态；`POST …/cancel` 停后续项；
/// `POST …/undo` 逐项撤销。判定在 <see cref="BatchUpdatePlanner"/>（纯），执行在 <see cref="BatchUpdateRunner"/>。
/// </summary>
public static partial class LocalApiService
{
	/// <summary>
	/// D3③ 的"哪几个能更新"：逐项查最新兼容版本，再用纯函数 <see cref="ModUpdatePlanner"/> 判定。
	/// 网络失败 **不整体报 500**——某个项目查不到就按"无元数据/查不到"跳过，让其余项目照常给出结果。
	/// </summary>
	private static async Task HandleListModUpdatesAsync(HttpListenerResponse response, string instanceId)
	{
		RequestScope scope = BeginWriteScope(instanceId, requireNoBisect: true);
		AppConfig config = scope.Config;
		GameInstance instance = scope.Instance!;
		string gameDir = scope.GameDirectory;
		ModService modService = new ModService(gameDir);
		List<ModFile> mods = await modService.GetModsAsync(instance);

		ModContentService content = new ModContentService(Outbound.Api, CreateRecoveryService());
		string mcVersion = instance.MinecraftVersion ?? string.Empty;
		string loader = instance.LoaderType ?? string.Empty;
		string apiKey = string.IsNullOrWhiteSpace(config.CurseForgeApiKey) ? string.Empty : config.CurseForgeApiKey;

		// 有界并发 + 5 分钟缓存（见 ModUpdateService）：200 Mod 的整合包不再一条条串行等。
		// 用 QueryOutcomesAsync 而不是 QueryLatestAsync：**"查失败"与"没元数据"必须分开报**
		// （前者重试可能好，后者重试也不会变），否则用户看到的是半个真相。
		List<ModQueryOutcome> outcomes = await new ModUpdateService().QueryOutcomesAsync(
			mods, mcVersion, loader, apiKey, instanceId, content, DateTimeOffset.Now,
			message => Debug.WriteLine(message));

		ModUpdatePlan plan = ModUpdatePlanner.Plan(mods, outcomes);
		await WriteJsonAsync(response, 200, new
		{
			ok = true,
			instanceId,
			minecraftVersion = mcVersion,
			loader,
			checkedCount = plan.Checked,
			skipped = new
			{
				noMetadata = plan.SkippedNoMetadata,
				upToDate = plan.SkippedUpToDate,
				noDownload = plan.SkippedNoDownload,
				// 新增：与上面三种本质不同——查询失败**重试可能变**，无兼容版本换 MC/加载器才可能变
				queryFailed = plan.SkippedQueryFailed,
				noCompatibleVersion = plan.SkippedNoCompatible,
				// 逐项明细：前端不用再从计数反推"为什么只有 N 个可更新"
				items = plan.SkipList.Select(x => new
				{
					fileName = x.FileName,
					status = x.Status == ModQueryStatus.QueryFailed ? "queryFailed"
						: x.Status == ModQueryStatus.NoCompatibleVersion ? "noCompatibleVersion"
						: x.Status == ModQueryStatus.NoMetadata ? "noMetadata"
						: "upToDateOrNoDownload",
					detail = x.Detail ?? string.Empty,
					retryable = x.Retryable
				}).ToArray()
			},
			candidates = plan.Candidates.Select(c => new
			{
				fileName = c.FileName,
				projectId = c.ProjectId,
				installedVersionId = c.InstalledVersionId,
				installedVersionNumber = c.InstalledVersionNumber,
				latestVersionId = c.Latest.VersionId,
				latestVersionNumber = c.Latest.VersionNumber,
				downloadUrl = c.Latest.DownloadUrl,
				sha1 = c.Latest.Sha1,
				version = new
				{
					source = c.Latest.Source == ModSource.CurseForge ? "curseforge" : "modrinth",
					projectId = c.Latest.ProjectId,
					versionId = c.Latest.VersionId,
					versionNumber = c.Latest.VersionNumber,
					fileName = c.Latest.FileName,
					displayName = c.Latest.DisplayName
				}
			}).ToArray()
		});
	}

	private static async Task HandleStartBatchUpdateAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
	{
		using JsonDocument body = await ReadBodyAsync(request);
		JsonElement root = body.RootElement;

		if (!root.TryGetProperty("items", out JsonElement itemsEl) || itemsEl.ValueKind != JsonValueKind.Array
			|| itemsEl.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("items 不能为空");
		}

		RequestScope scope = BeginWriteScope(instanceId, requireNoBisect: true);
		AppConfig config = scope.Config;
		GameInstance instance = scope.Instance!;
		string gameDir = scope.GameDirectory;

		// D3③：体积主要来自 jar 本身。前端可以二选一：
		//   * newFileBase64 —— 调用方自己拿到字节；
		//   * downloadUrl   —— 由服务端下载（白名单主机，见 ModContentService.AllowedDownloadHosts），
		//     避免浏览器侧 CORS 与"把整包塞进本地 API 请求体"的双重代价。
		// 无论哪种，写入时的 expectedSha256 都用**服务端算出来的值**；调用方给的是 sha1，
		// 只用于"确认拿到的确实是那个版本"，不参与替换判定。
		ModContentService contentService = new ModContentService(Outbound.Api, CreateRecoveryService());
		List<BatchUpdateInput> inputs = new List<BatchUpdateInput>();
		foreach (JsonElement item in itemsEl.EnumerateArray())
		{
			string fileName = JsonUtil.GetString(item, "fileName")?.Trim() ?? string.Empty;
			string base64 = JsonUtil.GetString(item, "newFileBase64")?.Trim() ?? string.Empty;
			string downloadUrl = JsonUtil.GetString(item, "downloadUrl")?.Trim() ?? string.Empty;
			string expectedSha1 = JsonUtil.GetString(item, "expectedSha1")?.Trim() ?? string.Empty;

			if (string.IsNullOrWhiteSpace(fileName) || (string.IsNullOrWhiteSpace(base64) && string.IsNullOrWhiteSpace(downloadUrl)))
			{
				throw new InvalidOperationException("items 里每一项都需要 fileName，以及 newFileBase64 或 downloadUrl 之一");
			}

			// 有 base64 就现在解开；只有地址的话**不在这一步下载** ——
			// 下载交给 BatchUpdateRunner，逐项进行，这样"取消停止后续项"才能真的省掉后面的下载。
			// （旧实现把全部下载塞在建任务之前，结果最慢的一段不可取消。）
			byte[]? bytes = null;
			string? sha256 = null;
			if (!string.IsNullOrWhiteSpace(base64))
			{
				try
				{
					bytes = Convert.FromBase64String(base64);
				}
				catch (FormatException)
				{
					throw new InvalidOperationException("newFileBase64 不是合法 base64：" + fileName);
				}
				// 服务端自己算 sha256：避免把"客户端声称的哈希"当成替换判据
				sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
			}

			inputs.Add(new BatchUpdateInput(
				fileName,
				bytes,
				sha256,
				ParseVersion(item),
				string.IsNullOrWhiteSpace(downloadUrl) ? null : downloadUrl,
				string.IsNullOrWhiteSpace(expectedSha1) ? null : expectedSha1));
		}

		ModService modService = new ModService(gameDir);
		FileRecoveryService recovery = CreateRecoveryService();

		BatchUpdateJob job = new BatchUpdateJob { InstanceId = instanceId };
		foreach (BatchUpdateInput input in inputs)
		{
			job.FileNames.Add(input.FileName);
			job.Statuses.Add(BatchItemStatus.Pending);
		}
		BatchUpdateRegistry.Add(job);

		_ = Task.Run(() => BatchUpdateRunner.RunAsync(job, inputs, instance, modService, recovery,
			(url, ct) => contentService.DownloadBytesAsync(url, ct),
			message => Debug.WriteLine(message)));

		await WriteJsonAsync(response, 202, Snapshot(job));
	}

	private static async Task HandleGetBatchUpdateAsync(HttpListenerResponse response, string instanceId, string jobId)
	{
		BatchUpdateJob job = RequireJob(instanceId, jobId);
		await WriteJsonAsync(response, 200, Snapshot(job));
	}

	private static async Task HandleCancelBatchUpdateAsync(HttpListenerResponse response, string instanceId, string jobId)
	{
		BatchUpdateJob job = RequireJob(instanceId, jobId);
		job.CancelRequested = true;
		await WriteJsonAsync(response, 200, Snapshot(job));
	}

	private static async Task HandleUndoBatchUpdateAsync(HttpListenerResponse response, string instanceId, string jobId)
	{
		BatchUpdateJob job = RequireJob(instanceId, jobId);
		if (!job.Finished)
		{
			throw new InvalidOperationException("批次尚未结束，不能撤销");
		}

		RequestScope scope = BeginWriteScope(instanceId, requireNoBisect: true);
		AppConfig config = scope.Config;
		GameInstance instance = scope.Instance!;
		string gameDir = scope.GameDirectory;
		FileRecoveryService recovery = CreateRecoveryService();

		IReadOnlyList<BatchUndoResult> results = await BatchUpdateRunner.UndoAsync(job, instance, new ModService(gameDir), recovery, gameDir);
		await WriteJsonAsync(response, 200, new
		{
			ok = results.All(r => r.Ok),
			jobId = job.JobId,
			undoed = results.Where(r => r.Ok).Select(r => r.FileName).ToArray(),
			conflicts = results.Where(r => !r.Ok).Select(r => new { r.FileName, r.Reason }).ToArray(),
			state = job.State,
			items = ItemSnapshots(job)
		});
	}

	private static BatchUpdateJob RequireJob(string instanceId, string jobId)
	{
		BatchUpdateJob? job = BatchUpdateRegistry.Get(jobId);
		if (job == null || !string.Equals(job.InstanceId, instanceId, StringComparison.Ordinal))
		{
			throw new InvalidOperationException("批量更新任务不存在或不属于该实例：" + jobId);
		}
		return job;
	}

	private static object Snapshot(BatchUpdateJob job) => new
	{
		ok = true,
		jobId = job.JobId,
		instanceId = job.InstanceId,
		state = job.State,
		cancelRequested = job.CancelRequested,
		failedFile = job.FailedFile,
		failedReason = job.FailedReason,
		items = ItemSnapshots(job)
	};

	private static object[] ItemSnapshots(BatchUpdateJob job)
	{
		return job.FileNames.Zip(job.Statuses, (name, status) => new { fileName = name, status = status.ToString().ToLowerInvariant() }).ToArray();
	}

	private static ModVersion? ParseVersion(JsonElement item)
	{
		if (!item.TryGetProperty("version", out JsonElement versionEl) || versionEl.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		ModVersion version = new ModVersion
		{
			Source = (JsonUtil.GetString(versionEl, "source") ?? string.Empty).Equals("curseforge", StringComparison.OrdinalIgnoreCase)
				? ModSource.CurseForge
				: ModSource.Modrinth,
			ProjectId = JsonUtil.GetString(versionEl, "projectId")?.Trim() ?? string.Empty,
			VersionId = JsonUtil.GetString(versionEl, "versionId")?.Trim() ?? string.Empty,
			VersionNumber = JsonUtil.GetString(versionEl, "versionNumber")?.Trim() ?? string.Empty,
			FileName = JsonUtil.GetString(versionEl, "fileName")?.Trim() ?? string.Empty,
			DisplayName = JsonUtil.GetString(versionEl, "displayName")?.Trim() ?? string.Empty
		};

		if (string.IsNullOrWhiteSpace(version.VersionNumber) && string.IsNullOrWhiteSpace(version.VersionId))
		{
			return null;
		}
		return version;
	}
}
