using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 版本与下载。
/// <para>
/// 可安装版本的本地/远程列举、下载任务与任务列表。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static async Task HandleListVersionsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		string gameDir = ResolveGameDirectory(config, null);
		List<string> local = VersionResolver.ListLocalVersions(gameDir);
		await WriteJsonAsync(response, 200, new { gameDir, local });
	}

	private static async Task HandleListRemoteVersionsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		string gameDir = ResolveGameDirectory(config, null);
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		List<VersionEntry> online = await new MinecraftDownloader(ApiHttp, _ => { }).FetchOnlineVersionsAsync(cts.Token);
		List<string> local = VersionResolver.ListLocalVersions(gameDir);
		await WriteJsonAsync(response, 200, new
		{
			gameDir,
			versions = online.Select(v => new { v.Id, v.Type, v.ReleaseTime, installed = local.Contains(v.Id) })
		});
	}

	private static async Task HandleDownloadVersionAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string versionId = JsonUtil.GetString(body.RootElement, "versionId")?.Trim();
		if (string.IsNullOrWhiteSpace(versionId))
		{
			throw new InvalidOperationException("versionId 不能为空");
		}

		AppConfig config = LoadConfig();
		string gameDir = ResolveGameDirectory(config, null);

		DownloadTaskEntry entry;
		lock (DownloadGate)
		{
			entry = new DownloadTaskEntry
			{
				Id = "dlv" + DateTimeOffset.Now.ToUnixTimeMilliseconds(),
				VersionId = versionId,
				Status = "queued",
				Message = "排队中",
				StartedAt = DateTimeOffset.Now
			};
			DownloadTasks.Add(entry);
		}

		_ = Task.Run(() => RunDownloadTaskAsync(entry, gameDir));
		await WriteJsonAsync(response, 200, new { ok = true, id = entry.Id, versionId, status = "queued" });
	}

	private static async Task RunDownloadTaskAsync(DownloadTaskEntry entry, string gameDir)
	{
		try
		{
			lock (DownloadGate)
			{
				entry.Status = "downloading";
				entry.Message = "正在获取版本元数据";
			}

			var downloader = new MinecraftDownloader(ApiHttp, msg =>
			{
				lock (DownloadGate)
				{
					entry.Message = msg;
				}
			}, progress =>
			{
				lock (DownloadGate)
				{
					entry.Progress = progress.Percentage;
					if (!string.IsNullOrWhiteSpace(progress.Message))
					{
						entry.Message = progress.Message;
					}
				}
			});

			await downloader.InstallVersionAsync(gameDir, entry.VersionId, CancellationToken.None);
			lock (DownloadGate)
			{
				entry.Status = "completed";
				entry.Progress = 1.0;
				entry.Message = "安装完成";
			}
		}
		catch (Exception ex)
		{
			lock (DownloadGate)
			{
				entry.Status = "failed";
				entry.Message = ex.Message;
			}
		}
	}

	private static async Task HandleListDownloadsAsync(HttpListenerResponse response)
	{
		object[] snapshot;
		lock (DownloadGate)
		{
			snapshot = DownloadTasks.Select(t => new
			{
				t.Id,
				t.VersionId,
				name = "Minecraft " + t.VersionId + " 客户端",
				t.Status,
				t.Progress,
				t.Message,
				t.StartedAt
			}).ToArray();
		}

		await WriteJsonAsync(response, 200, new { tasks = snapshot });
	}
}
