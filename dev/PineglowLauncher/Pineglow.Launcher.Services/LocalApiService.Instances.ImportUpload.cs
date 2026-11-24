using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 整合包导入的**分块上传**（2026-11-24 用户反馈）。
///
/// <para>
/// 问题：前端把整个 zip 转成 base64 塞进一个 JSON 请求。base64 膨胀 33%，
/// 超过 ~24 MiB 的整合包会被 <see cref="LocalApiService.MaxJsonBodyBytes"/>（32 MiB）拒绝，
/// 用户看到的是莫名其妙的「fileBase64 不能为空」。
/// </para>
/// <para>
/// 方案：begin → chunk ×N → finish 三段式。
/// 服务端边收边把 base64 解码追加到临时文件，最终 zip 不再受整包膨胀影响。
/// 每块仍走 JSON+base64（复用现有管道与鉴权），单块默认 2 MiB（base64 后 ~2.7 MiB，远小于 32 MiB）。
/// </para>
/// <para>
/// 会话状态在**进程内字典**里（<see cref="UploadSessions"/>），带 TTL 与容量上限，
/// 语义与 <c>AiOperationRegistry</c> 一致：容量满/过期会话被拒或清理，不无界增长。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/// <summary>单块 base64 解码后的最大字节数。2 MiB —— base64 后 ~2.7 MiB，远小于 32 MiB 请求体上限。</summary>
	internal const int ImportChunkBytes = 2 * 1024 * 1024;

	/// <summary>分块上传的整包上限。与旧单请求路径的 MaxImportBytes 对齐（24 MiB）。</summary>
	internal const long ImportUploadTotalBytes = 24L * 1024 * 1024;

	/// <summary>同时活跃的分块上传会话上限。</summary>
	internal const int MaxImportUploadSessions = 8;

	/// <summary>会话空闲过期时间。超过后可被新会话挤掉。</summary>
	internal static readonly TimeSpan ImportUploadSessionTtl = TimeSpan.FromMinutes(30);

	/// <summary>
	/// 一个进行中的分块上传会话。**只在上传阶段使用**；
	/// finish 之后文件交给导入流程，会话即被移除。
	/// </summary>
	private sealed record ImportUploadSession(
		string UploadId,
		string TempDir,
		string ZipPath,
		string FileName,
		long ReceivedBytes,
		DateTimeOffset LastActivityUtc);

	private static readonly object UploadSessionGate = new object();
	private static readonly Dictionary<string, ImportUploadSession> UploadSessions = new();

	/// <summary>把会话登记为已接收字节数（供测试注入）。</summary>
	internal static long UploadSessionReceivedForTest(string uploadId)
	{
		lock (UploadSessionGate)
		{
			return UploadSessions.TryGetValue(uploadId, out ImportUploadSession? s) ? s.ReceivedBytes : -1;
		}
	}

	/// <summary>
	/// POST /api/instances/import/upload-begin
	/// body: { fileName, sizeBytes? } → { ok, uploadId, chunkBytes }
	/// </summary>
	private static async Task HandleImportUploadBeginAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string fileName = Path.GetFileName(JsonUtil.GetString(body.RootElement, "fileName")?.Trim() ?? "pack.zip");
		long declaredSize = JsonUtil.GetInt64(body.RootElement, "sizeBytes") ?? -1;

		if (declaredSize > ImportUploadTotalBytes)
		{
			throw new InvalidOperationException(
				"整合包超过 " + (ImportUploadTotalBytes / 1024 / 1024) + " MiB 上限（实际 " +
				(declaredSize / 1024 / 1024) + " MiB）");
		}

		// 容量控制：满员时先清过期会话；仍满则拒绝。
		lock (UploadSessionGate)
		{
			DateTimeOffset now = DateTimeOffset.UtcNow;
			List<string> expired = UploadSessions
				.Where(kv => now - kv.Value.LastActivityUtc > ImportUploadSessionTtl)
				.Select(kv => kv.Key)
				.ToList();
			foreach (string id in expired)
			{
				ImportUploadSession dead = UploadSessions[id];
				UploadSessions.Remove(id);
				TryDeleteDirQuiet(dead.TempDir);
			}

			if (UploadSessions.Count >= MaxImportUploadSessions)
			{
				throw new InvalidOperationException("已有太多导入在上传中，请稍后再试");
			}

			string uploadId = Guid.NewGuid().ToString("N");
			string tempDir = Path.Combine(Path.GetTempPath(), "pineglow-import-" + uploadId);
			Directory.CreateDirectory(tempDir);
			string zipPath = CreateSafeTemporaryUploadPath(tempDir, fileName);

			UploadSessions[uploadId] = new ImportUploadSession(
				uploadId, tempDir, zipPath, fileName, 0, now);

			// 释放锁后再写响应。
			Task unused = WriteJsonAsync(response, 200, new
			{
				ok = true,
				uploadId,
				chunkBytes = ImportChunkBytes
			});
		}
	}

	/// <summary>
	/// POST /api/instances/import/upload-chunk
	/// body: { uploadId, offset, dataBase64 } → { ok, received }
	/// </summary>
	private static async Task HandleImportUploadChunkAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string uploadId = JsonUtil.GetString(body.RootElement, "uploadId")?.Trim() ?? string.Empty;
		long offset = JsonUtil.GetInt64(body.RootElement, "offset") ?? -1;
		string dataB64 = JsonUtil.GetString(body.RootElement, "dataBase64")?.Trim() ?? string.Empty;

		if (uploadId.Length == 0) throw new InvalidOperationException("uploadId 不能为空");
		if (offset < 0) throw new InvalidOperationException("offset 不能为负");
		if (dataB64.Length == 0) throw new InvalidOperationException("dataBase64 不能为空");

		byte[] chunk;
		try
		{
			chunk = Convert.FromBase64String(dataB64);
		}
		catch (FormatException)
		{
			throw new InvalidOperationException("上传内容不是有效的 base64 数据");
		}

		if (chunk.Length == 0 || chunk.Length > ImportChunkBytes)
		{
			throw new InvalidOperationException("分块大小必须在 1~" + ImportChunkBytes + " 字节之间");
		}

		ImportUploadSession session;
		lock (UploadSessionGate)
		{
			if (!UploadSessions.TryGetValue(uploadId, out ImportUploadSession? s))
			{
				throw new InvalidOperationException("上传会话不存在或已过期，请重新开始导入");
			}
			if (s.ReceivedBytes != offset)
			{
				throw new InvalidOperationException(
					"分块顺序错误：期望 offset=" + s.ReceivedBytes + "，实际 " + offset);
			}
			if (s.ReceivedBytes + chunk.Length > ImportUploadTotalBytes)
			{
				throw new InvalidOperationException("整合包超过 " + (ImportUploadTotalBytes / 1024 / 1024) + " MiB 上限");
			}
			session = s with { ReceivedBytes = s.ReceivedBytes + chunk.Length, LastActivityUtc = DateTimeOffset.UtcNow };
			UploadSessions[uploadId] = session;
		}

		// 落盘在锁外：IO 不该占着会话锁。
		// .NET 10 没有 File.AppendAllBytesAsync，用 FileStream 追加写。
		await using (FileStream fs = new FileStream(session.ZipPath, FileMode.Append, FileAccess.Write, FileShare.None))
		{
			await fs.WriteAsync(chunk, 0, chunk.Length);
		}

		await WriteJsonAsync(response, 200, new { ok = true, received = session.ReceivedBytes });
	}

	/// <summary>
	/// POST /api/instances/import/upload-finish
	/// body: { uploadId } → 走原有的导入流程（格式识别 → 建实例）
	/// </summary>
	private static async Task HandleImportUploadFinishAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string uploadId = JsonUtil.GetString(body.RootElement, "uploadId")?.Trim() ?? string.Empty;
		if (uploadId.Length == 0) throw new InvalidOperationException("uploadId 不能为空");

		ImportUploadSession? session = null;
		lock (UploadSessionGate)
		{
			if (!UploadSessions.TryGetValue(uploadId, out ImportUploadSession? s))
			{
				throw new InvalidOperationException("上传会话不存在或已过期，请重新开始导入");
			}
			// finish 后立刻摘除会话：同一 uploadId 不能 finish 两次。
			UploadSessions.Remove(uploadId);
			session = s;
		}

		string tempDir = session.TempDir;
		string zipPath = session.ZipPath;
		CancellationTokenSource? importCts = null;
		try
		{
			if (new FileInfo(zipPath).Length == 0)
			{
				throw new InvalidOperationException("上传内容为空");
			}

			AppConfig config = LoadConfig();
			string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory)
				? PlatformPaths.GetDefaultMinecraftDirectory()
				: config.GameDirectory;
			InstancePackService packService = new InstancePackService(Outbound.Api, defaultGameDir, _ => { });

			GameInstance newInstance;
			try
			{
				switch (InstancePackService.DetectModpackFormat(zipPath))
				{
					case ModpackArchiveFormat.Modrinth:
						newInstance = await packService.ImportModrinthPackAsync(zipPath, _ => { }, CancellationToken.None);
						break;
					case ModpackArchiveFormat.CurseForge:
						newInstance = await packService.ImportCurseForgePackAsync(zipPath, config.CurseForgeApiKey ?? string.Empty, _ => { }, CancellationToken.None);
						break;
					case ModpackArchiveFormat.Mcm:
						newInstance = packService.ImportMcm(zipPath);
						break;
					default:
						throw new InvalidOperationException("无法识别整合包格式：请上传 Modrinth (.mrpack)、CurseForge 或 MCM 格式的压缩包。");
				}
			}
			catch (OperationCanceledException)
			{
				await WriteJsonAsync(response, 409, new { ok = false, cancelled = true, error = "导入已取消，未生成实例。" });
				return;
			}

			AppConfig latest = LoadConfig();
			latest.Instances.Add(newInstance);
			latest.SelectedInstanceId = newInstance.Id;
			SaveConfig(latest);
			await WriteJsonAsync(response, 200, new { ok = true, instanceId = newInstance.Id, name = newInstance.Name });
		}
		finally
		{
			importCts?.Dispose();
			TryDeleteDirQuiet(tempDir);
		}
	}

	private static void TryDeleteDirQuiet(string dir)
	{
		try
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
		catch
		{
			// 清理失败不打断主流程；会话 TTL 会再尝试。
		}
	}
}
