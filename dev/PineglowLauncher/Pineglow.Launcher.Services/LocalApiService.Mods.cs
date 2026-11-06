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
/// Mod、恢复区与资源包。
/// <para>
/// Mod 列举/启停/删除/替换、恢复区（隔离副本）的列举与还原、资源包与光影包列举。
/// 这些操作都会写游戏目录，因此与二分定位的写保护守卫联动。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static async Task HandleListModsAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// 验收阻断项 F-04（本轮 R3 修复）：这里原本也被"并发启动守卫"拦住 ——
		// 但本路由是**只读列表**，不可能重复启动游戏。实测：把合成实例标记为运行中后，
		// 真实 HTTP `GET /api/instances/{id}/mods` 返回 400「请勿重复启动」，前端在游戏运行时
		// 连 Mod 列表都读不出来。
		//
		// 因此把守卫**逐路由**收窄到"变更/启动/危险文件操作"（同文件的 toggle/delete/replace、
		// 启动、诊断、存档恢复、实例移除、Bisect 写操作都保留守卫）；
		// 这里不再做运行态检查，只读地列出 Mod。
		string gameDir = ResolveGameDirectory(config, instance);
		List<ModFile> mods = await new ModService(gameDir).GetModsAsync(instance);
		await WriteJsonAsync(response, 200, new { instanceId, mods = mods.Select(BuildModSummary) });
	}

	private static object BuildModSummary(ModFile m)
	{
		return new
		{
			m.FileName,
			m.FullPath,
			m.SizeBytes,
			m.SizeText,
			m.IsEnabled,
			m.LastModifiedTime,
			m.DisplayName,
			m.ModId,
			m.Version,
			m.LoaderHint,
			m.Summary,
			m.OnlineSource,
			m.ProjectId,
			m.VersionId,
			m.OnlineVersionNumber,
			m.HasOnlineMetadata,
			dependencies = m.Dependencies?.Select(d => new { d.ModId, d.VersionRange, isRequired = d.IsRequired })
		};
	}

	private static async Task HandleToggleModAsync(HttpListenerResponse response, string instanceId, string fileName)
	{
		RejectManualModEditDuringBisect(instanceId);
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// MR03 收尾之二（缺口登记表 A7）：同一实例的**并发启动守卫**。
		// 修复前这里从不检查运行态，`MarkRunning` 会"以最后一次为准"覆盖注册表 ——
		// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），
		// 而删除守卫此后只跟踪最新的那个 PID。
		// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。
		if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))
		{
			throw new InvalidOperationException($"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。");
		}

		string gameDir = ResolveGameDirectory(config, instance);
		ModService service = new ModService(gameDir);
		ModFile mod = (await service.GetModsAsync(instance)).FirstOrDefault(x => x.FileName == fileName)
			?? throw new InvalidOperationException("Mod 不存在：" + fileName);
		if (mod.IsEnabled)
		{
			service.DisableMod(mod);
		}
		else
		{
			service.EnableMod(mod);
		}

		await WriteJsonAsync(response, 200, new { ok = true, fileName, isEnabled = !mod.IsEnabled });
	}

	/// <summary>
	/// MR04：删除 Mod 改为「移入恢复区」。jar 与 <c>.mcmini.json</c> 侧车作为同一次操作隔离，
	/// 返回 operationId 供用户在恢复区还原。此实现路径**不存在任何 File.Delete**。
	/// 逐项失败会被如实抛出，不会把"没删掉"报成成功。
	/// </summary>
	private static async Task HandleDeleteModAsync(HttpListenerResponse response, string instanceId, string fileName)
	{
		RejectManualModEditDuringBisect(instanceId);
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// MR03 收尾之二（缺口登记表 A7）：同一实例的**并发启动守卫**。
		// 修复前这里从不检查运行态，`MarkRunning` 会"以最后一次为准"覆盖注册表 ——
		// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），
		// 而删除守卫此后只跟踪最新的那个 PID。
		// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。
		if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))
		{
			throw new InvalidOperationException($"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。");
		}

		string gameDir = ResolveGameDirectory(config, instance);
		ModService service = new ModService(gameDir);
		ModFile mod = (await service.GetModsAsync(instance)).FirstOrDefault(x => x.FileName == fileName)
			?? throw new InvalidOperationException("Mod 不存在：" + fileName);

		var targets = new List<string> { mod.FullPath };
		string sidecar = mod.FullPath + ".mcmini.json";
		if (File.Exists(sidecar))
		{
			targets.Add(sidecar);
		}

		FileRecoveryRecord record = await CreateRecoveryService()
			.QuarantineAsync(instanceId, gameDir, "mod-delete", targets);

		FileRecoveryEntry[] failed = record.Files.Where(x => x.State == "failed").ToArray();
		if (failed.Length > 0 || !record.Files.Any(x => x.State == "quarantined"))
		{
			throw new InvalidOperationException(
				"删除未能完全隔离，文件仍保留在原处：" +
				string.Join("；", record.Files.Select(x => x.RelativePath + " → " + (x.Error ?? x.State))));
		}

		await WriteJsonAsync(response, 200, new
		{
			ok = true,
			fileName,
			operationId = record.OperationId,
			recoverable = true,
			status = record.Status,
			files = record.Files.Select(x => new { x.RelativePath, x.State, x.Sha256, x.SizeBytes })
		});
	}

	/// <summary>MR04：列出该实例恢复区的记录（只读，不触碰游戏文件）。</summary>
	private static async Task HandleListRecoveryAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		FileRecoveryService recoveryService = CreateRecoveryService();
		List<FileRecoveryRecord> records = await recoveryService.ListAsync(instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		await WriteJsonAsync(response, 200, new
		{
			instanceId,
			retentionDays = FileRecoveryService.DefaultRetentionDays,
			recoveries = records.Select(r => new
			{
				r.OperationId,
				r.Reason,
				r.Status,
				r.CreatedAt,
				expiresAt = FileRecoveryService.GetExpiresAt(r),
				totalBytes = r.Files.Sum(f => f.SizeBytes),
				// A14：替换类操作展示「旧版本 → 新版本」。
				// 旧版本读**被隔离的侧车**（恢复区里那份），新版本读**现网侧车**（若 Mod 还在）。
				oldVersion = ReadSidecarVersion(r, recoveryService, gameDir, instanceId, quarantined: true),
				newVersion = ReadSidecarVersion(r, recoveryService, gameDir, instanceId, quarantined: false),
				files = r.Files.Select(f => new { f.RelativePath, f.State, f.Sha256, f.SizeBytes, f.Error })
			})
		});
	}

	/// <summary>
	/// A14：从侧车（<c>.mcmini.json</c>）读出 <c>VersionNumber</c>。
	///
	/// 两处来源：
	///   * <paramref name="quarantined"/> = true → 恢复区里那份（**旧版本**）；
	///   * false → 现网那份（**新版本**，只有替换成功后 Mod 还在时才有）。
	///
	/// 路径基准要按 <c>Reason</c> 区分：`mod-replace` 的隔离基准是 **mods 目录**，
	/// 其它（如 `mod-delete`）是**游戏目录** —— 记录里的 <c>RelativePath</c> 是相对那个基准的。
	/// 读不到/解析失败一律返回 null：**展示信息绝不能把列表接口弄挂**。
	/// </summary>
	private static string? ReadSidecarVersion(
		FileRecoveryRecord record, FileRecoveryService recoveryService,
		string gameDir, string instanceId, bool quarantined)
	{
		try
		{
			FileRecoveryEntry? sidecar = record.Files.FirstOrDefault(
				f => f.RelativePath.EndsWith(".mcmini.json", StringComparison.OrdinalIgnoreCase));
			if (sidecar == null || string.IsNullOrWhiteSpace(sidecar.RelativePath))
			{
				return null;
			}

			string path;
			if (quarantined)
			{
				path = Path.Combine(
					recoveryService.GetOperationDirectory(instanceId, record.OperationId),
					"files", sidecar.RelativePath);
			}
			else
			{
				string baseDir = string.Equals(record.Reason, "mod-replace", StringComparison.OrdinalIgnoreCase)
					? Path.Combine(gameDir, "mods")
					: gameDir;
				path = Path.Combine(baseDir, sidecar.RelativePath);
			}

			if (!File.Exists(path))
			{
				return null;
			}

			ModInstallMetadata? meta = JsonSerializer.Deserialize<ModInstallMetadata>(File.ReadAllText(path), JsonUtil.Options);
			return string.IsNullOrWhiteSpace(meta?.VersionNumber) ? null : meta.VersionNumber;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// MR04 / MR05：把一次隔离操作的文件恢复到原位置。
	/// 默认**不覆盖**同名现有文件；撤销一次替换（MR05）时客户端显式传 <c>{"overwrite":true}</c>。
	/// </summary>
	private static async Task HandleRestoreRecoveryAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId, string operationId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		FileRecoveryService service = CreateRecoveryService();
		FileRecoveryRecord? record = await service.LoadAsync(instanceId, operationId);
		if (record == null)
		{
			throw new InvalidOperationException("恢复记录不存在或不可读：" + operationId);
		}

		// R1（验收阻断项 F-01）：记录是**磁盘上的输入**，不能直接相信它的游戏目录。
		// 记录只允许绑定当前实例的游戏目录（`mod-delete` 等）或它下面的子目录（`mod-replace` 用 mods 目录）。
		// 越界记录会让一次"恢复"把文件写到实例配置之外的路径。
		string instanceGameDir = ResolveGameDirectory(config, instance);
		string recordGameDir = string.IsNullOrWhiteSpace(record.GameDirectory) ? string.Empty : Path.GetFullPath(record.GameDirectory);
		bool boundToInstance = recordGameDir.Length > 0
			&& (string.Equals(recordGameDir, Path.GetFullPath(instanceGameDir), StringComparison.OrdinalIgnoreCase)
				|| FileRecoveryService.IsPathInside(instanceGameDir, recordGameDir));
		if (!boundToInstance)
		{
			throw new InvalidOperationException(
				$"恢复记录与当前实例的游戏目录不匹配，拒绝恢复：{operationId}（记录 {record.GameDirectory}，实例 {instanceGameDir}）");
		}

		bool overwrite = false;
		if (request.HasEntityBody)
		{
			try
			{
				JsonDocument body = await ReadBodyAsync(request);
				overwrite = body.RootElement.TryGetProperty("overwrite", out JsonElement el)
					&& el.ValueKind == JsonValueKind.True;
			}
			catch
			{
				// 空 body 或非 JSON 时按默认（不覆盖）处理，不因为解析失败就放行覆盖。
			}
		}

		// S1（RA-01）：把**可信实例根**交给服务层作为唯一信任锚。
		// 记录里的 GameDirectory 只当候选：mod-replace 记录是 `game\mods`，
		// 只校验它自己会让"上层 game 被换成 junction"的越界写入通过。
		FileRecoveryRecord restored = await service.RestoreAsync(record, overwrite, instanceGameDir);
		bool allOk = restored.Files.All(f => f.State == "restored" || f.State == "already_restored");

		await WriteJsonAsync(response, 200, new
		{
			ok = allOk,
			operationId = restored.OperationId,
			status = restored.Status,
			overwrite,
			conflicts = restored.Files.Count(f => f.State == "conflict"),
			files = restored.Files.Select(f => new { f.RelativePath, f.State, f.Sha256, f.Error })
		});
	}

	/// <summary>
	/// MR05：替换/更新一个 Mod。新包以 base64 传入，先落到 staging 并核对 SHA256，
	/// 校验通过后才进入事务（隔离旧包 → 写新包 → 写元数据），任何一步失败都回滚到旧版本。
	/// 旧文件若处于禁用态（<c>.jar.disabled</c>），新版本同样保持禁用。
	/// </summary>
	private static async Task HandleReplaceModAsync(HttpListenerRequest request, HttpListenerResponse response, string instanceId, string fileName)
	{
		RejectManualModEditDuringBisect(instanceId);
		JsonDocument body = await ReadBodyAsync(request);
		string newFileBase64 = JsonUtil.GetString(body.RootElement, "newFileBase64")?.Trim() ?? string.Empty;
		string expectedSha256 = JsonUtil.GetString(body.RootElement, "expectedSha256")?.Trim() ?? string.Empty;

		// A22：接受**可选**的版本元数据。
		// 修复前本端点只解析 newFileBase64/expectedSha256，于是 ReplaceAsync 收到 version=null
		// → `if (version != null)` 不成立 → **不写新侧车** → 恢复区永远只能显示"旧版本 →"一半。
		// 这里按安装流程（WriteOnlineMetadata）的同一形状接收并透传。
		ModVersion? version = null;
		if (body.RootElement.TryGetProperty("version", out JsonElement versionEl) && versionEl.ValueKind == JsonValueKind.Object)
		{
			string versionSource = JsonUtil.GetString(versionEl, "source")?.Trim() ?? string.Empty;
			version = new ModVersion
			{
				Source = versionSource.Equals("curseforge", StringComparison.OrdinalIgnoreCase) ? ModSource.CurseForge : ModSource.Modrinth,
				ProjectId = JsonUtil.GetString(versionEl, "projectId")?.Trim() ?? string.Empty,
				VersionId = JsonUtil.GetString(versionEl, "versionId")?.Trim() ?? string.Empty,
				VersionNumber = JsonUtil.GetString(versionEl, "versionNumber")?.Trim() ?? string.Empty,
				FileName = JsonUtil.GetString(versionEl, "fileName")?.Trim() ?? string.Empty,
				DisplayName = JsonUtil.GetString(versionEl, "displayName")?.Trim() ?? string.Empty
			};

			// 连版本号与版本 ID 都没有 → 没有可用信息，宁可不写侧车
			if (string.IsNullOrWhiteSpace(version.VersionNumber) && string.IsNullOrWhiteSpace(version.VersionId))
			{
				version = null;
			}
		}

		if (string.IsNullOrWhiteSpace(newFileBase64))
		{
			throw new InvalidOperationException("newFileBase64 不能为空");
		}

		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// MR03 收尾之二（缺口登记表 A7）：同一实例的**并发启动守卫**。
		// 修复前这里从不检查运行态，`MarkRunning` 会"以最后一次为准"覆盖注册表 ——
		// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），
		// 而删除守卫此后只跟踪最新的那个 PID。
		// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。
		if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))
		{
			throw new InvalidOperationException($"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。");
		}

		string gameDir = ResolveGameDirectory(config, instance);

		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(newFileBase64);
		}
		catch (FormatException)
		{
			throw new InvalidOperationException("newFileBase64 不是合法的 base64");
		}

		string stagingDir = Path.Combine(Path.GetTempPath(), "pineglow-replace-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stagingDir);
		string stagedPath = Path.Combine(stagingDir, "staged.jar");
		try
		{
			await File.WriteAllBytesAsync(stagedPath, bytes);

			ModService modService = new ModService(gameDir);
			var replacement = new ModReplacementService(CreateRecoveryService());
			ModReplacementResult result = await replacement.ReplaceAsync(
				instance, modService, fileName, stagedPath,
				string.IsNullOrWhiteSpace(expectedSha256) ? null : expectedSha256,
				version); // A22：把版本元数据透传下去（写新侧车）

			if (!result.Ok)
			{
				// S3（RA-03）：失败必须给出**结构化字段**，而不是把 needsRecovery 等信息塞进中文散文，
				// 让客户端只能靠解析文案。既有 `error` 与失败状态码 400 保持不变（向后兼容），
				// 同时补上 status / needsRecovery / operationId / recoveryStatus / failedFiles。
				await WriteJsonAsync(response, 400, new
				{
					ok = false,
					status = result.Status,
					error = $"[{result.Status}] {result.Error}",
					needsRecovery = result.NeedsRecovery,
					operationId = result.OperationId,
					recoveryStatus = result.RecoveryStatus,
					failedFiles = result.FailedFiles
				});
				return;
			}

			await WriteJsonAsync(response, 200, new
			{
				ok = true,
				fileName = result.FileName,
				operationId = result.OperationId,
				status = result.Status,
				wasEnabled = result.WasEnabled,
				oldSha256 = result.OldSha256,
				newSha256 = result.NewSha256,
				undoHint = "如需撤销本次更新，对 operationId 调用 restore 并传 overwrite=true"
			});
		}
		finally
		{
			try
			{
				if (Directory.Exists(stagingDir))
				{
					Directory.Delete(stagingDir, recursive: true);
				}
			}
			catch
			{
			}
		}
	}

	private static async Task HandleListResourcePacksAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// MR03 收尾之二（缺口登记表 A7）：同一实例的**并发启动守卫**。
		// 修复前这里从不检查运行态，`MarkRunning` 会"以最后一次为准"覆盖注册表 ——
		// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），
		// 而删除守卫此后只跟踪最新的那个 PID。
		// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。
		if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))
		{
			throw new InvalidOperationException($"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。");
		}

		string gameDir = ResolveGameDirectory(config, instance);
		List<ResourcePackInfo> packs = new ResourcePackService(gameDir).GetResourcePacks(instance);
		await WriteJsonAsync(response, 200, new
		{
			instanceId,
			packs = packs.Select(p => new
			{
				p.FileName,
				p.FullPath,
				p.DisplayName,
				p.Description,
				p.PackFormat,
				p.SizeText,
				p.HasCover,
				cover = p.HasCover && p.CoverBytes != null ? "data:image/png;base64," + Convert.ToBase64String(p.CoverBytes) : null
			})
		});
	}

	private static async Task HandleListShaderPacksAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// MR03 收尾之二（缺口登记表 A7）：同一实例的**并发启动守卫**。
		// 修复前这里从不检查运行态，`MarkRunning` 会"以最后一次为准"覆盖注册表 ——
		// 于是同一实例能起两个游戏进程同时写同一份存档（真实的损坏风险），
		// 而删除守卫此后只跟踪最新的那个 PID。
		// 位置刻意放在最前：在任何网络调用/进程派发**之前**就拒绝。
		if (RunningInstanceRegistry.IsRunning(instanceId, out int alreadyRunningPid))
		{
			throw new InvalidOperationException($"该实例已在运行（进程 {alreadyRunningPid}），请勿重复启动。");
		}

		string gameDir = ResolveGameDirectory(config, instance);
		List<ShaderPackInfo> packs = new ShaderPackService(gameDir).GetPacks(instance);
		await WriteJsonAsync(response, 200, new { instanceId, packs = packs.Select(p => new { p.FileName, p.FullPath, p.SizeText }) });
	}
}
