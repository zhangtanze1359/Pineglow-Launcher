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
/// 内容搜索、图标与安装。
/// <para>
/// Mod/整合包/资源包的远程搜索、图标代理与安装任务机，以及世界列表。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static async Task HandleListWorldsAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		List<object> worlds = new List<object>();
		foreach (GameInstance instance in config.Instances)
		{
			string gameDir = ResolveGameDirectory(config, instance);
			List<SaveInfo> saves;
			try
			{
				saves = new SaveService(gameDir).GetSaves(instance);
			}
			catch
			{
				continue;
			}

			worlds.AddRange(saves.Select(s => new
			{
				s.FolderName,
				s.DisplayName,
				s.LastPlayed,
				s.SizeText,
				s.HasIcon,
				instanceId = instance.Id,
				instanceName = instance.Name
			}));
		}

		await WriteJsonAsync(response, 200, new { worlds });
	}

	private static async Task HandleSearchContentAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		string query = request.QueryString["q"] ?? string.Empty;
		string category = request.QueryString["category"] ?? "mod";
		string gameVersion = request.QueryString["gameVersion"] ?? string.Empty;
		string loader = request.QueryString["loader"] ?? string.Empty;
		string source = request.QueryString["source"] ?? "modrinth";
		if (string.IsNullOrWhiteSpace(query))
		{
			await WriteJsonAsync(response, 200, new { results = new object[0] });
			return;
		}

		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		AppConfig config = LoadConfig();
		try
		{
			switch (category.ToLowerInvariant())
			{
				case "modpack":
				{
					List<ModpackProject> packs = await new ModrinthModpackService(ApiHttp).SearchAsync(query, cts.Token);
					// modpack 当前只走 Modrinth；保留 source 字段以便前端统一处理
					string packSource = "modrinth";
					foreach (ModpackProject p in packs)
					{
						// Phase 2：把上游 iconUrl 注册到 ContentIconService 可信映射。
						// 前端只看到 source/category/id 三元组，绝不直接拿到上游 URL。
						ContentIconService.Default.RegisterTrustedUrl(packSource, "modpack", p.ProjectId, p.IconUrl);
					}
					await WriteJsonAsync(response, 200, new
					{
						results = packs.Select(p => new
						{
							id = p.ProjectId,
							title = p.Title,
							desc = p.Description,
							author = p.Author,
							downloads = p.Downloads,
							category,
							source = packSource,
							versions = p.Categories,
							loaders = new string[0],
							websiteUrl = p.WebsiteUrl,
							// Phase 2：仅向前端暴露可信标识；上游 iconUrl 不再透传给 DOM。
							// 客户端只能从 id/source/category 构造 /api/content/icon 本地端点。
							iconUrl = (string.IsNullOrEmpty(p.IconUrl)) ? string.Empty : ("/api/content/icon?source=" + packSource + "&category=modpack&projectId=" + Uri.EscapeDataString(p.ProjectId)),
						})
					});
					return;
				}
				case "shader":
				case "resourcepack":
				{
					ResourceContentKind kind = category.Equals("shader", StringComparison.OrdinalIgnoreCase)
						? ResourceContentKind.Shader
						: ResourceContentKind.ResourcePack;
					List<ResourceContentProject> projects = await new ResourceContentService(ApiHttp).SearchAsync(query, gameVersion, kind, cts.Token);
					// 光影/资源包目前由 Modrinth 提供；保留 source 字段以便前端统一处理
					string resSource = "modrinth";
					foreach (ResourceContentProject p in projects)
					{
						ContentIconService.Default.RegisterTrustedUrl(resSource, category, p.ProjectId, p.IconUrl);
					}
					await WriteJsonAsync(response, 200, new
					{
						results = projects.Select(p => new
						{
							id = p.ProjectId,
							title = p.Title,
							desc = p.Description,
							author = p.Author,
							downloads = p.Downloads,
							category,
							source = resSource,
							versions = new string[0],
							loaders = new string[0],
							websiteUrl = p.WebsiteUrl,
							iconUrl = (string.IsNullOrEmpty(p.IconUrl)) ? string.Empty : ("/api/content/icon?source=" + resSource + "&category=" + category + "&projectId=" + Uri.EscapeDataString(p.ProjectId)),
						})
					});
					return;
				}
				default:
				{
					string modSource = source.Equals("curseforge", StringComparison.OrdinalIgnoreCase) ? "curseforge" : "modrinth";
					ModSearchRequest req = new ModSearchRequest
					{
						Source = modSource == "curseforge" ? ModSource.CurseForge : ModSource.Modrinth,
						Query = query,
						MinecraftVersion = gameVersion,
						Loader = loader,
						Limit = 24,
						CurseForgeApiKey = config?.CurseForgeApiKey ?? string.Empty
					};
					List<ModSearchResult> results = await new ModContentService(ApiHttp).SearchAsync(req, cts.Token);
					foreach (ModSearchResult m in results)
					{
						// Phase 2：注册上游 iconUrl 到可信映射（仅 modrinth 当前可达，
						// curseforge 主机未实测故即使注册了也只会在 IsAllowedHost 阶段被拒）。
						ContentIconService.Default.RegisterTrustedUrl(modSource, "mod", m.ProjectId, m.IconUrl);
					}
					await WriteJsonAsync(response, 200, new
					{
						results = results.Select(m => new
						{
							id = m.ProjectId,
							title = m.Title,
							desc = m.Description,
							author = m.Author,
							downloads = m.Downloads,
							category,
							source = modSource,
							versions = m.GameVersions,
							loaders = m.Categories.Where(c => ModLoaderInfo.SupportedKinds.Any(k => k.ToString().Equals(c, StringComparison.OrdinalIgnoreCase))).ToArray(),
							websiteUrl = m.WebsiteUrl,
							iconUrl = (string.IsNullOrEmpty(m.IconUrl)) ? string.Empty : ("/api/content/icon?source=" + modSource + "&category=mod&projectId=" + Uri.EscapeDataString(m.ProjectId)),
						})
					});
					return;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[LocalApi] content search error: {ex.Message}");
			// 区分"无结果"与"上游故障"：错误时返回 error 字段与空 results，
			// 前端不再据此显示"假装成功"的 mock 数据。
			await WriteJsonAsync(response, 502, new { results = new object[0], error = ex.Message });
		}
	}

	private static async Task HandleContentIconAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		// Phase 2：受保护的本地资源图标端点。
		// 1) 不接受 url / host / redirect 等客户端可控上游地址参数；
		// 2) 只允许 source / category / projectId 三个身份字段；
		// 3) 验证 session token（由前置 HasValidSessionToken 守门）；
		// 4) Origin 验证已在外层 HandleAsync 完成；
		// 5) 身份三元组命中 ContentIconService 的可信映射（搜索阶段注册）才发起下载；
		// 6) 错误响应不泄漏上游 URL、token、内部错误信息。
		string source = (request.QueryString["source"] ?? string.Empty).Trim().ToLowerInvariant();
		string category = (request.QueryString["category"] ?? string.Empty).Trim().ToLowerInvariant();
		string projectId = (request.QueryString["projectId"] ?? string.Empty).Trim();

		// 拒绝任何额外未知查询参数（防止 url= 之类被偷偷加进来作为上游 URL 探针）
		foreach (string key in request.QueryString.AllKeys)
		{
			if (key is null) continue;
			if (key.Equals("source", StringComparison.OrdinalIgnoreCase)) continue;
			if (key.Equals("category", StringComparison.OrdinalIgnoreCase)) continue;
			if (key.Equals("projectId", StringComparison.OrdinalIgnoreCase)) continue;
			if (key.Equals("token", StringComparison.OrdinalIgnoreCase)) continue;
			await WriteJsonErrorAsync(response, 400, "图标身份参数非法");
			return;
		}

		if (!ContentIconService.IsValidSource(source)
			|| !ContentIconService.IsValidCategory(category)
			|| !ContentIconService.IsValidProjectId(projectId))
		{
			await WriteJsonErrorAsync(response, 400, "图标身份参数非法");
			return;
		}

		try
		{
			IconResult result = await ContentIconService.Default.GetAsync(source, category, projectId, CancellationToken.None).ConfigureAwait(false);
			switch (result.Status)
			{
				case IconStatus.NotFound:
					await WriteJsonErrorAsync(response, 404, "图标不存在");
					return;
				case IconStatus.TooLarge:
					await WriteJsonErrorAsync(response, 413, "图标超过大小上限");
					return;
				case IconStatus.BadContentType:
					await WriteJsonErrorAsync(response, 415, "图标 MIME 不允许");
					return;
				case IconStatus.BadImage:
					await WriteJsonErrorAsync(response, 415, "图标文件签名无效");
					return;
				case IconStatus.UpstreamError:
					await WriteJsonErrorAsync(response, 502, "上游不可用");
					return;
				case IconStatus.NetworkError:
					await WriteJsonErrorAsync(response, 504, "上游超时或不可达");
					return;
				case IconStatus.Ok:
				case IconStatus.FromCache:
				case IconStatus.Stale:
					// 成功路径：写图片字节 + 安全头
					response.StatusCode = 200;
					response.ContentType = result.Mime ?? "image/png";
					if (result.Bytes != null) response.ContentLength64 = result.Bytes.LongLength;
					response.Headers["X-Content-Type-Options"] = "nosniff";
					response.Headers["Referrer-Policy"] = "no-referrer";
					response.Headers["Cache-Control"] = "private, max-age=3600";
					if (result.Status == IconStatus.Stale) response.Headers["X-Pineglow-Cache"] = "stale";
					if (result.Bytes != null)
					{
						try
						{
							await response.OutputStream.WriteAsync(result.Bytes, 0, result.Bytes.Length).ConfigureAwait(false);
						}
						catch
						{
							// 客户端中途断开
						}
					}
					try { response.Close(); } catch { }
					return;
				default:
					await WriteJsonErrorAsync(response, 502, "图标不可用");
					return;
			}
		}
		catch (Exception ex)
		{
			// 任何意外都不向客户端泄漏内部信息
			LauncherLog.Error("[LocalApi] content icon error: " + ex.Message);
			await WriteJsonErrorAsync(response, 502, "图标不可用");
		}
	}

	private static async Task HandleContentInstallAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string category = (JsonUtil.GetString(body.RootElement, "category") ?? "mod").Trim().ToLowerInvariant();
		string projectId = JsonUtil.GetString(body.RootElement, "projectId")?.Trim() ?? string.Empty;
		string instanceId = JsonUtil.GetString(body.RootElement, "instanceId")?.Trim() ?? string.Empty;
		string source = (JsonUtil.GetString(body.RootElement, "source") ?? "modrinth").Trim().ToLowerInvariant();
		if (string.IsNullOrWhiteSpace(projectId))
		{
			throw new InvalidOperationException("projectId 不能为空");
		}

		AppConfig config = LoadConfig();
		if (category != "modpack")
		{
			if (string.IsNullOrWhiteSpace(instanceId))
			{
				instanceId = config.SelectedInstanceId;
			}

			if (string.IsNullOrWhiteSpace(instanceId))
			{
				throw new InvalidOperationException("请先选择一个实例再安装内容");
			}

			RequireInstance(config, instanceId);
		}

		ContentTaskEntry entry = new ContentTaskEntry
		{
			Id = "ct" + DateTimeOffset.Now.ToUnixTimeMilliseconds(),
			Category = category,
			ProjectId = projectId,
			InstanceId = instanceId,
			Source = source,
			Status = "queued",
			Message = "排队中",
			StartedAt = DateTimeOffset.Now
		};
		lock (ContentGate)
		{
			ContentTasks.Add(entry);
		}

		_ = Task.Run(() => RunContentInstallTaskAsync(entry));
		await WriteJsonAsync(response, 200, new { ok = true, id = entry.Id, category, status = "queued" });
	}

	private static async Task HandleListContentTasksAsync(HttpListenerResponse response)
	{
		object[] snapshot;
		lock (ContentGate)
		{
			snapshot = ContentTasks.Select(t => new
			{
				t.Id,
				t.Category,
				t.ProjectId,
				t.InstanceId,
				t.Title,
				t.Status,
				t.Progress,
				t.Message,
				t.ResultInstanceId,
				t.StartedAt
			}).ToArray();
		}

		await WriteJsonAsync(response, 200, new { tasks = snapshot });
	}

	private static async Task RunContentInstallTaskAsync(ContentTaskEntry entry)
	{
		try
		{
			lock (ContentGate)
			{
				entry.Status = "running";
				entry.Message = "正在准备安装";
			}

			AppConfig config = LoadConfig();
			switch (entry.Category)
			{
				case "modpack":
					entry.Title = "整合包";
					await InstallModpackContentAsync(entry, config);
					break;
				case "shader":
				case "resourcepack":
					entry.Title = entry.Category == "shader" ? "光影" : "资源包";
					await InstallResourceContentAsync(entry, config);
					break;
				default:
					entry.Title = "Mod";
					await InstallModContentAsync(entry, config);
					break;
			}

			lock (ContentGate)
			{
				entry.Status = "completed";
				entry.Progress = 1.0;
				entry.Message = "安装完成";
			}
		}
		catch (Exception ex)
		{
			lock (ContentGate)
			{
				entry.Status = "failed";
				entry.Message = ex.Message;
			}

			LauncherLog.Error($"[ContentInstall] {entry.Category} {entry.ProjectId} 失败: {ex.Message}");
		}
	}

	private static void UpdateContentTask(ContentTaskEntry entry, string message, double progress = -1)
	{
		lock (ContentGate)
		{
			if (!string.IsNullOrWhiteSpace(message))
			{
				entry.Message = message;
			}

			if (progress >= 0)
			{
				entry.Progress = progress;
			}
		}
	}

	private static async Task InstallModContentAsync(ContentTaskEntry entry, AppConfig config)
	{
		GameInstance instance = RequireInstance(config, entry.InstanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		// MR05：带上恢复服务，已安装同一在线项目时走事务化替换而不是并列导入。
		ModContentService content = new ModContentService(ApiHttp, CreateRecoveryService());
		ModSource sourceKind = entry.Source.Equals("curseforge", StringComparison.OrdinalIgnoreCase) ? ModSource.CurseForge : ModSource.Modrinth;
		ModSearchResult project = new ModSearchResult
		{
			Source = sourceKind,
			ProjectId = entry.ProjectId
		};
		UpdateContentTask(entry, "正在获取版本信息", 0.1);
		List<ModVersion> versions = await content.GetVersionsAsync(project, instance.MinecraftVersion, instance.LoaderType, config.CurseForgeApiKey ?? string.Empty);
		ModVersion version = versions.FirstOrDefault(v => v.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase)) ?? versions.FirstOrDefault();
		if (version == null)
		{
			throw new InvalidOperationException("该 Mod 没有找到兼容当前实例的版本");
		}

		UpdateContentTask(entry, $"正在安装 {version.DisplayName} {version.VersionNumber}", 0.2);
		ModService modService = new ModService(gameDir);
		ModInstallResult result = await content.InstallWithDependenciesAsync(instance, version, modService, instance.MinecraftVersion, instance.LoaderType,
			curseForgeApiKey: config.CurseForgeApiKey ?? string.Empty,
			progress: new Progress<double>(p => UpdateContentTask(entry, string.Empty, 0.2 + p * 0.7)),
			status: msg => UpdateContentTask(entry, msg),
			ct: CancellationToken.None);
		entry.Title = version.DisplayName;
	}

	private static async Task InstallResourceContentAsync(ContentTaskEntry entry, AppConfig config)
	{
		GameInstance instance = RequireInstance(config, entry.InstanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		ResourceContentKind kind = entry.Category.Equals("shader", StringComparison.OrdinalIgnoreCase)
			? ResourceContentKind.Shader
			: ResourceContentKind.ResourcePack;
		ResourceContentService content = new ResourceContentService(ApiHttp);
		ResourceContentProject project = new ResourceContentProject
		{
			ProjectId = entry.ProjectId,
			Kind = kind
		};
		UpdateContentTask(entry, "正在获取版本信息", 0.1);
		List<ResourceContentVersion> versions = await content.GetVersionsAsync(project, instance.MinecraftVersion);
		ResourceContentVersion version = versions.FirstOrDefault(v => v.VersionNumber != string.Empty) ?? versions.FirstOrDefault();
		if (version == null)
		{
			throw new InvalidOperationException("该内容没有找到可用版本");
		}

		UpdateContentTask(entry, $"正在安装 {version.FileName}", 0.2);
		await content.DownloadAsync(instance, project, version, gameDir,
			progress: new Progress<double>(p => UpdateContentTask(entry, string.Empty, 0.2 + p * 0.7)),
			ct: CancellationToken.None);
		entry.Title = version.FileName;
	}

	private static async Task InstallModpackContentAsync(ContentTaskEntry entry, AppConfig config)
	{
		ModrinthModpackService modrinth = new ModrinthModpackService(ApiHttp);
		ModpackProject project = new ModpackProject
		{
			ProjectId = entry.ProjectId
		};
		UpdateContentTask(entry, "正在获取整合包版本", 0.05);
		List<ModpackVersion> versions = await modrinth.GetVersionsAsync(project);
		ModpackVersion version = versions.FirstOrDefault(v => v.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase)) ?? versions.FirstOrDefault();
		if (version == null)
		{
			throw new InvalidOperationException("该整合包没有可用版本");
		}

		UpdateContentTask(entry, $"正在下载 {project.Title} {version.VersionNumber}", 0.1);
		string zipPath = await modrinth.DownloadAsync(project, version);
		try
		{
			string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : config.GameDirectory;
			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, msg => UpdateContentTask(entry, msg));
			UpdateContentTask(entry, "正在创建实例并安装整合包", 0.15);
			GameInstance newInstance = await packService.ImportModrinthPackAsync(zipPath,
				msg => UpdateContentTask(entry, msg),
				CancellationToken.None);
			AppConfig latest = LoadConfig();
			latest.Instances.Add(newInstance);
			latest.SelectedInstanceId = newInstance.Id;
			SaveConfig(latest);
			entry.Title = newInstance.Name;
			entry.ResultInstanceId = newInstance.Id;
			UpdateContentTask(entry, "整合包实例创建完成", 0.95);
		}
		finally
		{
			ModrinthModpackService.DeleteDownload(zipPath);
		}
	}
}
