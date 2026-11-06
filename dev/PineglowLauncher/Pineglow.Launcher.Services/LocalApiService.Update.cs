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
/// 自更新。
/// <para>
/// 自更新的检查、下载、状态查询与应用。更新清单地址链可被测试缝替换，
/// 生产路径不打真实网络替代品。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/* ================= 自更新 ================= */

	private static async Task HandleCheckUpdateAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		IReadOnlyList<string> chain = ResolveUpdateUrlChain(config.UpdateUrl);
		if (chain.Count == 0)
		{
			await WriteJsonAsync(response, 200, new { enabled = false, currentVersion = LauncherService.LauncherVersion, message = "未配置更新源（config.json 的 updateUrl）" });
			return;
		}

		try
		{
			// R03：主源失败（404 / 超时 / 坏 JSON / 字段非法）时按链降级到备用源，
			// 并把每个源的真实结果回给界面，避免只给一句"检查失败"。
			UpdateService.ManifestFetchResult fetch = await UpdateService.FetchManifestChainAsync(ApiHttp, chain, CancellationToken.None);
			if (!fetch.Ok || fetch.Manifest == null || string.IsNullOrWhiteSpace(fetch.Manifest.Version))
			{
				LauncherLog.Error("[update] 检查更新失败: " + fetch.FailureSummary);
				await WriteJsonAsync(response, 200, new
				{
					enabled = true,
					currentVersion = LauncherService.LauncherVersion,
					error = fetch.FailureSummary,
					manifestSources = fetch.Attempts.Select(a => new { url = UpdateService.Redact(a.Url), ok = a.Ok, reason = a.Reason }).ToArray()
				});
				return;
			}
			UpdateManifest manifest = fetch.Manifest;
			bool available = UpdateService.IsNewerVersion(manifest.Version, LauncherService.LauncherVersion);
			LauncherLog.Info("[update] 检查更新：current=" + LauncherService.LauncherVersion + " latest=" + manifest.Version
				+ " available=" + available + " source=" + UpdateService.Redact(fetch.SourceUrl) + " fallback=" + fetch.UsedFallback);
			await WriteJsonAsync(response, 200, new
			{
				enabled = true,
				currentVersion = LauncherService.LauncherVersion,
				updateAvailable = available,
				latestVersion = manifest.Version,
				notes = manifest.Notes,
				mandatory = manifest.Mandatory,
				downloadUrl = manifest.Url,
				manifestSource = UpdateService.Redact(fetch.SourceUrl),
				usedFallback = fetch.UsedFallback
			});
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 检查更新失败: " + ex.Message, ex);
			await WriteJsonAsync(response, 200, new { enabled = true, currentVersion = LauncherService.LauncherVersion, error = ex.Message });
		}
	}

	private static async Task HandleStartUpdateDownloadAsync(HttpListenerResponse response)
	{
		bool alreadyDownloading;
		lock (UpdateGate)
		{
			alreadyDownloading = UpdateState == "downloading";
		}
		if (alreadyDownloading)
		{
			await WriteJsonAsync(response, 200, new { ok = true, state = "downloading" });
			return;
		}

		AppConfig config = LoadConfig();
		IReadOnlyList<string> chain = ResolveUpdateUrlChain(config.UpdateUrl);
		if (chain.Count == 0)
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "未配置更新源（config.json 的 updateUrl）" });
			return;
		}

		try
		{
			// R03：与检查更新走同一条降级链，避免"检查能看到新版本、下载却取不到清单"。
			UpdateService.ManifestFetchResult fetch = await UpdateService.FetchManifestChainAsync(ApiHttp, chain, CancellationToken.None);
			UpdateManifest? manifest = fetch.Manifest;
			if (!fetch.Ok || manifest == null || string.IsNullOrWhiteSpace(manifest.Url))
			{
				LauncherLog.Error("[update] 启动下载失败: " + fetch.FailureSummary);
				await WriteJsonAsync(response, 400, new
				{
					ok = false,
					message = fetch.FailureSummary,
					manifestSources = fetch.Attempts.Select(a => new { url = UpdateService.Redact(a.Url), ok = a.Ok, reason = a.Reason }).ToArray()
				});
				return;
			}
			lock (UpdateGate)
			{
				UpdateState = "downloading";
				UpdateProgress = 0.0;
				UpdateVersion = manifest.Version;
				UpdatePath = string.Empty;
				UpdateError = string.Empty;
				UpdateMandatory = manifest.Mandatory;
			}
			LauncherLog.Info("[update] 开始下载更新 v" + manifest.Version + " ← " + manifest.Url);
			_ = Task.Run(() => DownloadUpdateWorkerAsync(manifest));
			await WriteJsonAsync(response, 200, new { ok = true, state = "downloading", version = manifest.Version });
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 启动下载失败: " + ex.Message, ex);
			await WriteJsonAsync(response, 500, new { ok = false, message = ex.Message });
		}
	}

	private static async Task DownloadUpdateWorkerAsync(UpdateManifest manifest)
	{
		try
		{
			Progress<double> progress = new Progress<double>(value =>
			{
				lock (UpdateGate)
				{
					UpdateProgress = value;
				}
			});
			string path = await UpdateService.DownloadUpdateAsync(ApiHttp, manifest, progress, CancellationToken.None);
			lock (UpdateGate)
			{
				UpdateState = "completed";
				UpdatePath = path;
				UpdateProgress = 1.0;
			}
			LauncherLog.Info("[update] 下载完成: " + path);
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 下载失败: " + ex.Message, ex);
			lock (UpdateGate)
			{
				UpdateState = "failed";
				UpdateError = ex.Message;
			}
		}
	}

	private static async Task HandleUpdateStatusAsync(HttpListenerResponse response)
	{
		string state, version, path, error;
		double progress;
		bool mandatory;
		lock (UpdateGate)
		{
			state = UpdateState;
			version = UpdateVersion;
			path = UpdatePath;
			error = UpdateError;
			progress = UpdateProgress;
			mandatory = UpdateMandatory;
		}
		// MR20：把"上次替换脚本的结果"与信任链声明一并回给界面。
		// 界面**不能**因为"脚本已启动"就显示更新完成 —— 只有脚本写出 applied 才算替换成功。
		string updatesDir = Path.Combine(PlatformPaths.GetConfigDirectory(), "updates");
		UpdateService.ApplyResult? applyResult = UpdateService.ReadApplyResult(UpdateService.GetApplyStatePath(updatesDir));
		await WriteJsonAsync(response, 200, new
		{
			state,
			progress,
			version,
			path,
			error,
			mandatory,
			currentVersion = LauncherService.LauncherVersion,
			trustNote = UpdateService.TrustModelNote,
			lastApplyResult = applyResult == null ? null : new
			{
				result = applyResult.Result,
				applied = applyResult.Applied,
				version = applyResult.Version,
				writtenAtUtc = applyResult.WrittenAtUtc
			}
		});
	}

	private static async Task HandleApplyUpdateAsync(HttpListenerResponse response)
	{
		string path, version;
		lock (UpdateGate)
		{
			path = UpdatePath;
			version = UpdateVersion;
		}
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			await WriteJsonAsync(response, 400, new { ok = false, message = "更新包未就绪，请先下载。" });
			return;
		}

		try
		{
			UpdateService.ApplyUpdate(path, PlatformPaths.GetScriptExtension(), version);
			lock (UpdateGate)
			{
				UpdateState = "applying";
			}
			LauncherLog.Info("[update] 正在应用更新 v" + version + " → " + path);
			await WriteJsonAsync(response, 200, new { ok = true, message = "更新已启动，重启后生效。" });
			_ = Task.Run(async () =>
			{
				await Task.Delay(1200);
				Environment.Exit(0);
			});
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 应用更新失败: " + ex.Message, ex);
			await WriteJsonAsync(response, 500, new { ok = false, message = ex.Message });
		}
	}
}
