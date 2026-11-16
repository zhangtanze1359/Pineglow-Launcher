using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
	private static async Task HandleStartBatchUpdateAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId)
	{
		RejectManualModEditDuringBisect(instanceId);
		using JsonDocument body = await ReadBodyAsync(request);
		JsonElement root = body.RootElement;

		if (!root.TryGetProperty("items", out JsonElement itemsEl) || itemsEl.ValueKind != JsonValueKind.Array
			|| itemsEl.GetArrayLength() == 0)
		{
			throw new InvalidOperationException("items 不能为空");
		}

		List<BatchUpdateInput> inputs = new List<BatchUpdateInput>();
		foreach (JsonElement item in itemsEl.EnumerateArray())
		{
			string fileName = JsonUtil.GetString(item, "fileName")?.Trim() ?? string.Empty;
			string base64 = JsonUtil.GetString(item, "newFileBase64")?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(base64))
			{
				throw new InvalidOperationException("items 里每一项都需要 fileName 与 newFileBase64");
			}

			byte[] bytes;
			try
			{
				bytes = Convert.FromBase64String(base64);
			}
			catch (FormatException)
			{
				throw new InvalidOperationException("newFileBase64 不是合法 base64：" + fileName);
			}

			inputs.Add(new BatchUpdateInput(
				fileName,
				bytes,
				JsonUtil.GetString(item, "expectedSha256")?.Trim(),
				ParseVersion(item)));
		}

		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
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

		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
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
