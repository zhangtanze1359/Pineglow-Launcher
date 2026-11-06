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
/// 存档与皮肤上传。
/// <para>
/// 存档的列举、备份、恢复、删除、打开目录，以及皮肤文件上传。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static async Task HandleListSavesAsync(HttpListenerResponse response, string instanceId)
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
		List<SaveInfo> saves = new SaveService(gameDir).GetSaves(instance);
		await WriteJsonAsync(response, 200, new
		{
			instanceId,
			gameDir,
			saves = saves.Select(s => new
			{
				s.FolderName,
				s.FullPath,
				s.DisplayName,
				s.LastPlayed,
				s.SizeText,
				s.HasIcon,
				icon = s.HasIcon && s.IconBytes != null ? "data:image/png;base64," + Convert.ToBase64String(s.IconBytes) : null
			})
		});
	}

	private static SaveInfo RequireSave(AppConfig config, string instanceId, string folderName)
	{
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		return new SaveService(gameDir).GetSaves(instance).FirstOrDefault(x => x.FolderName == folderName)
			?? throw new InvalidOperationException("存档不存在：" + folderName);
	}

	private static async Task HandleCreateSaveBackupAsync(HttpListenerResponse response, string instanceId, string folderName)
	{
		AppConfig config = LoadConfig();
		SaveInfo save = RequireSave(config, instanceId, folderName);
		GameInstance instance = RequireInstance(config, instanceId);
		string gameDir = ResolveGameDirectory(config, instance);
		string backupPath = new SaveBackupService(gameDir).CreateBackup(instance, save);
		await WriteJsonAsync(response, 200, new { ok = true, path = backupPath, folderName });
	}

	private static async Task HandleListSaveBackupsAsync(HttpListenerResponse response, string instanceId, string folderName)
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
		// Codex 独立探针发现的缺陷 1（第 63 轮修复）：列表**必须按请求的世界过滤**。
		// 修复前这里返回该实例的**全部**备份 —— 请求 worldB 会拿到 worldA 的备份（探针实测）。
		//
		// GroupName 的真实来源（最终验收 F-05 的文案更正）：`SaveBackupService.ToBackupInfo`
		// 取的是**备份文件相对备份根的父目录名**（`<备份根>/<世界名>/<时间戳>.zip` → `<世界名>`），
		// **不是**从备份文件名里解析出来的。为空的回退分支才用「存档」。
		List<BackupFileInfo> backups = new SaveBackupService(gameDir).GetBackups(instance)
			.Where(x => string.Equals(x.GroupName, folderName, StringComparison.OrdinalIgnoreCase))
			.ToList();
		await WriteJsonAsync(response, 200, new { instanceId, folderName, backups });
	}

	private static async Task HandleRestoreSaveBackupAsync(HttpListenerResponse response, string instanceId, string folderName, string backupName)
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
		SaveBackupService backupService = new SaveBackupService(gameDir);
		BackupFileInfo backup = backupService.GetBackups(instance).FirstOrDefault(x => x.FileName == backupName)
			?? throw new InvalidOperationException("备份不存在：" + backupName);

		// Codex 独立探针发现的缺陷 2（第 63 轮修复，**数据安全**）：
		// 修复前跨世界恢复会被接受 —— 把 worldA 的备份恢复到 worldB 会返回 200 并写出 `saves\worldA (1)`。
		// 备份归属于哪个世界由 GroupName 决定；而 GroupName 是**备份相对备份根的父目录名**
		// （见 SaveBackupService.ToBackupInfo，F-05 文案更正），不匹配就拒绝。
		if (!string.Equals(backup.GroupName, folderName, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"该备份属于「{backup.GroupName}」，不能恢复到「{folderName}」。");
		}

		string restoredPath = backupService.RestoreBackup(instance, backup.FullPath);
		await WriteJsonAsync(response, 200, new { ok = true, path = restoredPath, backupName });
	}

	private static async Task HandleDeleteSaveAsync(HttpListenerResponse response, string instanceId, string folderName)
	{
		AppConfig config = LoadConfig();
		SaveInfo save = RequireSave(config, instanceId, folderName);
		string gameDir = ResolveGameDirectory(config, RequireInstance(config, instanceId));
		new SaveService(gameDir).DeleteSave(save);
		await WriteJsonAsync(response, 200, new { ok = true, folderName });
	}

	private static async Task HandleOpenSaveDirectoryAsync(HttpListenerResponse response, string instanceId, string folderName)
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
		SaveService service = new SaveService(gameDir);
		SaveInfo save = RequireSave(config, instanceId, folderName);
		service.OpenSavesDirectory(instance);
		await WriteJsonAsync(response, 200, new { ok = true, path = save.FullPath });
	}

	private static async Task HandleUploadSkinAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string pngBase64 = JsonUtil.GetString(body.RootElement, "pngBase64")?.Trim() ?? string.Empty;
		bool slim = body.RootElement.TryGetProperty("slim", out JsonElement slimEl) && slimEl.ValueKind == JsonValueKind.True;
		string accountId = JsonUtil.GetString(body.RootElement, "accountId")?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(pngBase64))
		{
			throw new InvalidOperationException("pngBase64 不能为空");
		}

		AppConfig config = LoadConfig();
		LauncherAccount account = string.IsNullOrWhiteSpace(accountId)
			? AccountService.GetSelected(config)
			: config.Accounts.FirstOrDefault(x => x.Id == accountId);
		if (account == null)
		{
			throw new InvalidOperationException("请先登录一个账号");
		}

		if (!account.IsMicrosoft)
		{
			throw new InvalidOperationException("皮肤上传仅支持 Microsoft 正版账号");
		}

		string clientId = !string.IsNullOrWhiteSpace(account.ClientId) ? account.ClientId : AuthClientOptions.ResolveMicrosoftClientId();
		string refreshToken = SecretStore.Load(SecretStore.AccountToken(account.Id));
		if (string.IsNullOrWhiteSpace(refreshToken))
		{
			refreshToken = SecretStore.Load(SecretStore.MicrosoftAccountToken(account.Id));
		}

		if (string.IsNullOrWhiteSpace(refreshToken))
		{
			throw new InvalidOperationException("账号刷新凭据缺失，请重新登录 Microsoft 账号");
		}

		MicrosoftAuthService authService = new MicrosoftAuthService(ApiHttp);
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
		MinecraftAccount mc = await authService.LoginWithRefreshTokenAsync(clientId, refreshToken, cts.Token);

		string tempDir = Path.Combine(Path.GetTempPath(), "pineglow-skin-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDir);
		string pngPath = Path.Combine(tempDir, "skin.png");
		try
		{
			byte[] bytes;
			try
			{
				bytes = Convert.FromBase64String(pngBase64);
			}
			catch (FormatException)
			{
				throw new InvalidOperationException("皮肤文件不是有效的 base64 数据");
			}
			if (bytes.Length > SkinImageProxyService.MaxBytes) throw new InvalidOperationException("皮肤图片超过 1 MiB 上限");
			if (!SkinImageProxyService.TryReadPngDimensions(bytes, out _, out _)) throw new InvalidOperationException("皮肤图片必须是 64×64 或 64×32 PNG");

			await File.WriteAllBytesAsync(pngPath, bytes);
			MinecraftAppearance appearance = await authService.UploadSkinAsync(mc.AccessToken, pngPath, slim, cts.Token);
			account.SkinUrl = appearance.SkinUrl;
			account.SkinModel = appearance.IsSlim || slim ? "slim" : "classic";
			account.SkinCount = appearance.SkinCount;
			account.CapeCount = appearance.CapeCount;
			account.Capes = appearance.Capes;
			SaveConfig(config);
			await WriteJsonAsync(response, 200, new { ok = true, account = BuildAccountSummary(account) });
		}
		finally
		{
			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir, recursive: true);
			}
		}
	}
}
