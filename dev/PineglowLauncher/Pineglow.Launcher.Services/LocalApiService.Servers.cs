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
/// 服务器、统计与来源。
/// <para>
/// 收藏服务器的增删与延迟探测、玩法统计、Java 列表、下载来源切换、打开日志目录。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static void HandleOpenLogDirectory(HttpListenerResponse response)
	{
		string dir = LauncherLog.GetLogDirectory();
		if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
		{
			WriteJsonAsync(response, 400, new { ok = false, message = "日志目录不存在" }).GetAwaiter().GetResult();
			return;
		}

		try
		{
			if (OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + dir + "\"", UseShellExecute = true });
			}
			else
			{
				Process.Start(new ProcessStartInfo { FileName = "xdg-open", Arguments = dir, UseShellExecute = true });
			}
			LauncherLog.Info("[logs] 已打开日志目录: " + dir);
			WriteJsonAsync(response, 200, new { ok = true, path = dir }).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			WriteJsonAsync(response, 500, new { ok = false, message = ex.Message }).GetAwaiter().GetResult();
		}
	}

	private static async Task HandleGetStatsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		long totalSeconds = config.Instances.Sum(x => x.TotalPlaySeconds);
		int totalCount = config.Instances.Sum(x => x.PlayCount);
		var perInstance = config.Instances.Select(x => new
		{
			x.Id,
			x.Name,
			x.PlayCount,
			x.TotalPlaySeconds,
			lastLaunch = x.LastLaunchTime
		}).ToList();
		await WriteJsonAsync(response, 200, new
		{
			totalPlaySeconds = totalSeconds,
			totalPlayCount = totalCount,
			totalInstances = config.Instances.Count,
			perInstance,
			sessions = config.Instances.SelectMany(instance => (instance.PlaySessions ?? new List<GamePlaySession>()).Select(session => new
			{
				instanceId = instance.Id,
				instanceName = instance.Name,
				session.StartedAt,
				session.EndedAt
			})).OrderByDescending(session => session.StartedAt).ToList()
		});
	}

	private static async Task HandleListJavaAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		List<JavaInstallation> installations = await JavaLocator.ScanAsync(null);
		await WriteJsonAsync(response, 200, new
		{
			config.JavaPath,
			installations = installations.Select(j => new { j.JavaPath, j.VersionMajor, j.VersionText, j.Vendor, j.IsValid })
		});
	}

	private static async Task HandleListSourcesAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		IReadOnlyList<DownloadSourceInfo> sources = MirrorHelper.ListSources();
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		// 并行测速（官方源也测，便于对比）
		foreach (DownloadSourceInfo s in sources)
		{
			s.LatencyMs = await MirrorHelper.MeasureLatencyAsync(ApiHttp, s.BaseUrl, cts.Token);
		}

		await WriteJsonAsync(response, 200, new
		{
			selected = MirrorHelper.SelectedKey,
			sources = sources.Select(s => new
			{
				key = s.Key,
				name = s.Name,
				baseUrl = s.BaseUrl,
				latencyMs = s.LatencyMs,
				available = s.LatencyMs >= 0,
			})
		});
	}

	private static async Task HandleSelectSourceAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string key = JsonUtil.GetString(body.RootElement, "key")?.Trim() ?? string.Empty;
		DownloadSourceInfo source = MirrorHelper.ListSources().FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidOperationException("未知下载源：" + key);

		AppConfig config = LoadConfig();
		config.DownloadSource = source.Key;
		config.UseMirror = !source.Key.Equals("official", StringComparison.OrdinalIgnoreCase);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, selected = source.Key, useMirror = config.UseMirror });
	}

	private static async Task HandleListServersAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		await WriteJsonAsync(response, 200, new { servers = config.FavoriteServers.Select(BuildServerSummary) });
	}

	private static object BuildServerSummary(FavoriteServer s)
	{
		return new
		{
			s.Id,
			s.Name,
			s.Host,
			s.Port,
			s.StatusText,
			s.Motd,
			s.VersionName,
			s.OnlinePlayers,
			s.MaxPlayers,
			s.LatencyMs,
			s.LastCheckedAt,
			favicon = s.FaviconBytes != null ? "data:image/png;base64," + Convert.ToBase64String(s.FaviconBytes) : null
		};
	}

	private static async Task HandleAddServerAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string name = JsonUtil.GetString(body.RootElement, "name")?.Trim();
		string host = JsonUtil.GetString(body.RootElement, "host")?.Trim();
		int port = 25565;
		string portText = JsonUtil.GetString(body.RootElement, "port");
		if (!int.TryParse(portText, out port) || port <= 0 || port > 65535)
		{
			port = 25565;
		}

		if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host))
		{
			throw new InvalidOperationException("服务器名称与地址不能为空");
		}

		AppConfig config = LoadConfig();
		FavoriteServer server = new FavoriteServer { Name = name, Host = host, Port = port };
		config.FavoriteServers.Add(server);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, server = BuildServerSummary(server) });
	}

	private static async Task HandlePingServerAsync(HttpListenerResponse response, string serverId)
	{
		AppConfig config = LoadConfig();
		FavoriteServer server = config.FavoriteServers.FirstOrDefault(x => x.Id == serverId)
			?? throw new InvalidOperationException("服务器不存在：" + serverId);
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
		try
		{
			MinecraftServerStatus status = await ServerPingService.QueryAsync(server.Host, server.Port, cts.Token);
			server.Motd = status.Motd;
			server.VersionName = status.VersionName;
			server.OnlinePlayers = status.OnlinePlayers;
			server.MaxPlayers = status.MaxPlayers;
			server.LatencyMs = status.LatencyMs;
			server.FaviconBytes = status.FaviconBytes;
			server.StatusText = status.OnlinePlayers >= 0 ? "可连接 · " + status.LatencyMs + " ms" : "无法连接";
			server.LastCheckedAt = DateTimeOffset.Now;
		}
		catch
		{
			server.StatusText = "无法连接";
			server.LastCheckedAt = DateTimeOffset.Now;
		}

		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true, server = BuildServerSummary(server) });
	}

	private static async Task HandleDeleteServerAsync(HttpListenerResponse response, string serverId)
	{
		AppConfig config = LoadConfig();
		config.FavoriteServers.RemoveAll(x => x.Id == serverId);
		SaveConfig(config);
		await WriteJsonAsync(response, 200, new { ok = true });
	}
}
