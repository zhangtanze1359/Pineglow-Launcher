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
/// 整合包导入与启动前检查。
/// <para>
/// 整合包导入（含取消）与启动前环境自检。导入是可取消的长任务，取消注册表复用
/// <c>AiOperationRegistry</c>，与 AI 诊断共用同一份 TTL/容量/tombstone 语义。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	private static async Task HandleImportInstanceAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string fileName = Path.GetFileName(JsonUtil.GetString(body.RootElement, "fileName")?.Trim() ?? "pack.zip");
		string fileBase64 = JsonUtil.GetString(body.RootElement, "fileBase64")?.Trim() ?? string.Empty;
		// MR11：可选的 operationId —— 传了就能被 POST /api/instances/import/cancel 取消。
		string operationId = JsonUtil.GetString(body.RootElement, "operationId")?.Trim() ?? string.Empty;
		if (operationId.Length > 0 && (operationId.Length < 8 || operationId.Length > 128))
		{
			throw new InvalidOperationException("operationId 长度必须在 8~128 之间");
		}
		if (string.IsNullOrWhiteSpace(fileBase64))
		{
			throw new InvalidOperationException("fileBase64 不能为空");
		}

		byte[] bytes;
		try
		{
			bytes = Convert.FromBase64String(fileBase64);
		}
		catch (FormatException)
		{
			throw new InvalidOperationException("上传内容不是有效的 base64 数据");
		}
		if (bytes.Length > MaxImportBytes) throw new InvalidOperationException("实例包超过 24 MiB 的 JSON 上传上限，请使用流式导入。");

		string tempDir = Path.Combine(Path.GetTempPath(), "pineglow-import-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDir);
		string zipPath = CreateSafeTemporaryUploadPath(tempDir, fileName);
		CancellationTokenSource? importCts = null;
		try
		{
			await File.WriteAllBytesAsync(zipPath, bytes);
			AppConfig config = LoadConfig();
			string defaultGameDir = string.IsNullOrWhiteSpace(config.GameDirectory) ? PlatformPaths.GetDefaultMinecraftDirectory() : config.GameDirectory;
			InstancePackService packService = new InstancePackService(ApiHttp, defaultGameDir, _ => { });

			CancellationToken ct = CancellationToken.None;
			if (operationId.Length > 0)
			{
				importCts = new CancellationTokenSource();
				// 先取 token 再注册：注册表在 cancel-before-register 分支里会 cancel 并 dispose 这个 CTS
				// （token 回调 Remove 会 Dispose），之后再读 importCts.Token 会抛 ObjectDisposedException。
				// CancellationToken 是结构体，事先取到的副本在源被 dispose 后仍可安全查询取消状态。
				ct = importCts.Token;
				if (!PackOps.Register(ImportOperationScope, operationId, importCts))
				{
					// MR11：注册失败有两种原因，必须区分开。
					// Get 返回 null 说明表里只有墓碑 —— 即"取消先于服务端受理"（用户在上传阶段就点了取消）。
					bool cancelledBeforeReceipt = PackOps.Get(ImportOperationScope, operationId) == null;
					await WriteJsonAsync(response, cancelledBeforeReceipt ? 409 : 400, new
					{
						ok = false,
						cancelled = cancelledBeforeReceipt,
						operationId,
						error = cancelledBeforeReceipt
							? "该导入已被取消（取消发生在服务端受理之前）。"
							: "operationId 已被占用/registry 满"
					});
					return;
				}
			}

			GameInstance newInstance;
			try
			{
				switch (InstancePackService.DetectModpackFormat(zipPath))
				{
					case ModpackArchiveFormat.Modrinth:
						newInstance = await packService.ImportModrinthPackAsync(zipPath, _ => { }, ct);
						break;
					case ModpackArchiveFormat.CurseForge:
						newInstance = await packService.ImportCurseForgePackAsync(zipPath, config.CurseForgeApiKey ?? string.Empty, _ => { }, ct);
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
				// MR11：取消必须给出确定性结果，且**绝不能**留下半成品实例（游戏目录由导入器自行清理）。
				await WriteJsonAsync(response, 409, new
				{
					ok = false,
					cancelled = true,
					error = "导入已取消，未生成实例。"
				});
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
			if (operationId.Length > 0)
			{
				// 结束即退场：之后的迟到取消返回"没有可取消的操作"，不会误导用户。
				PackOps.Cancel(ImportOperationScope, operationId);
			}

			importCts?.Dispose();
			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir, recursive: true);
			}
		}
	}

	/// <summary>
	/// MR11：导入取消端点。语义与 AI 诊断的 cancel 一致：
	/// 重复取消幂等；不存在的 operationId 返回确定性结果，不泄露其他操作。
	/// </summary>
	private static async Task HandleCancelImportAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		string operationId = string.Empty;
		try
		{
			JsonDocument body = await ReadBodyAsync(request);
			operationId = JsonUtil.GetString(body.RootElement, "operationId")?.Trim() ?? string.Empty;
		}
		catch
		{
			// 无 body 时按空 operationId 处理，返回确定性结果
		}

		bool wasActive = operationId.Length > 0 && PackOps.Get(ImportOperationScope, operationId) != null;
		// 注意：Cancel 必须**无条件调用**。它除了取消在途操作，还负责登记 cancel-before-register 墓碑；
		// 写成 "cancelled = wasActive && Cancel(...)" 会因短路求值跳过登记，"取消先到" 的能力就没了（实测踩过）。
		bool cancelResult = operationId.Length > 0 && PackOps.Cancel(ImportOperationScope, operationId);
		bool cancelled = wasActive && cancelResult;
		// 没有在途操作时，取消以墓碑形式记录：该 operationId 之后才被受理会被直接取消。
		// 如实报成 pendingCancel，避免界面误报"已取消进行中的导入"。
		bool pendingCancel = operationId.Length > 0 && !wasActive;
		await WriteJsonAsync(response, 200, new
		{
			ok = true,
			operationId,
			cancelled,
			pendingCancel,
			message = cancelled ? "cancelled" : (pendingCancel ? "recorded_for_later" : "no_active_operation"),
		});
	}

	/// <summary>
	/// M2-1④ 启动前自检。
	///
	/// 本方法只做两件事：**采集事实**（IO）与**序列化**。判定全部交给
	/// <see cref="PrelaunchCheckEvaluator.Evaluate"/>，因为判定逻辑是纯函数，
	/// 可以脱离文件系统直测——这正是原来做不到的（原先判定和 IO 混在一起，
	/// 想测「内存接近上限时要给警告」就得先造一台内存刚好不够的机器）。
	///
	/// 返回体的兼容性口径：
	///   * <c>errors</c> / <c>warnings</c> 两个字符串数组**保留**，旧前端与外部调用方不受影响；
	///   * 新增 <c>issues</c> 结构化数组（code / severity / message / action / actionLabel），
	///     新前端只按 code 与 action 分支，**不解析任何中文文案**。
	/// </summary>
	private static async Task HandlePrelaunchCheckAsync(HttpListenerResponse response, string instanceId)
	{
		AppConfig config = LoadConfig();
		GameInstance instance = RequireInstance(config, instanceId);

		// ---- 采集：Java ----
		string javaPath = string.Empty;
		bool javaExists = false;
		string javaError = string.Empty;
		try
		{
			javaPath = ResolveJavaPath(config, instance, instance.MinecraftVersion ?? string.Empty);
			javaExists = File.Exists(javaPath);
		}
		catch (Exception ex)
		{
			// 解析抛异常是"该去重新选 Java"，与"路径存在但文件没了"不是一回事，
			// 所以单独记在 JavaResolveError 上，由判定层给出不同的问题码。
			javaError = ex.Message;
		}

		// ---- 采集：游戏目录 ----
		string gameDir = ResolveGameDirectory(config, instance);
		bool gameDirExists = Directory.Exists(gameDir);

		// ---- 采集：磁盘剩余 ----
		long driveFree = 0;
		try
		{
			string root = Path.GetPathRoot(Path.GetFullPath(gameDir));
			if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
			{
				DriveInfo drive = new DriveInfo(root);
				if (drive.IsReady)
				{
					driveFree = drive.AvailableFreeSpace;
				}
			}
		}
		catch
		{
			// 拿不到就按 0 处理，判定层把 0 视为"未知，不提示"——不把未知当成通过。
		}

		// ---- 采集：内存 ----
		// TotalAvailableMemoryBytes 是当前进程的可用内存预算，跨平台无依赖。
		int maxMemoryMb = instance.MaxMemoryMb > 0 ? instance.MaxMemoryMb : config.MaxMemoryMb;
		long availableBytes = 0;
		try
		{
			availableBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
		}
		catch
		{
		}

		PrelaunchFacts facts = new PrelaunchFacts(
			instance.MinecraftVersion ?? string.Empty,
			javaPath,
			javaExists,
			javaError,
			gameDir,
			gameDirExists,
			driveFree,
			availableBytes,
			maxMemoryMb);

		List<PrelaunchIssue> issues = PrelaunchCheckEvaluator.Evaluate(facts);

		// 由 issues 派生旧字段，保证两套口径**不可能**互相矛盾。
		List<string> errors = issues.Where(x => x.IsError).Select(x => x.Message).ToList();
		List<string> warnings = issues.Where(x => !x.IsError).Select(x => x.Message).ToList();

		await WriteJsonAsync(response, 200, new
		{
			ok = errors.Count == 0,
			errors,
			warnings,
			issues,
			maxMemoryMb,
			gameDir
		});
	}
}
