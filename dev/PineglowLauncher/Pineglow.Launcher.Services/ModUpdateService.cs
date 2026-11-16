using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// D3③「检查更新」的查询层：**有界并发 + 短期缓存**。
///
/// 为什么需要（上一版实测的缺口）：每个 Mod 一次 API 调用，200 Mod 的整合包会串行卡很久；
/// 而且用户连点两次"检查更新"会把同一批请求再打一遍。这里补两件事：
///   * 并发上限 <see cref="MaxConcurrency"/>：不把上游 API 打成筛子，也不让 UI 等到天荒地老；
///   * 结果缓存 <see cref="CacheTtl"/>：失败（null）也缓存，避免连点把网络请求翻倍。
///
/// 缓存按 `实例 + 文件名 + 已装 VersionId` 建键：**已装版本变了（刚更新完）自然失效**，
/// 不需要额外的失效逻辑——这也是为什么不按"项目 ID"建键。
/// </summary>
public sealed class ModUpdateService
{
	public const int MaxConcurrency = 6;

	public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

	private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);

	private sealed record CacheEntry(ModVersion? Version, DateTimeOffset At);

	public static string CacheKey(string instanceId, ModFile mod) =>
		instanceId + "|" + mod.FileName + "|" + mod.VersionId;

	/// <summary>纯查询：缓存里有没有仍然新鲜的结果（不做 IO，便于直测）。</summary>
	public static bool TryGetFresh(string key, DateTimeOffset now, out ModVersion? version)
	{
		version = null;
		if (!Cache.TryGetValue(key, out CacheEntry? entry))
		{
			return false;
		}
		if (now - entry.At > CacheTtl)
		{
			return false;
		}
		version = entry.Version;
		return true;
	}

	public static void Put(string key, ModVersion? version, DateTimeOffset now) =>
		Cache[key] = new CacheEntry(version, now);

	/// <summary>仅测试用：避免用例之间互相看到对方的缓存。</summary>
	public static void ClearCache() => Cache.Clear();

	/// <summary>
	/// 按 `mods` 的顺序返回"最新兼容版本"（查不到为 null）。只对有在线元数据的条目发请求。
	/// 有界并发：<see cref="SemaphoreSlim"/> 限制在途请求数，结果按下标回填，顺序与 <paramref name="mods"/> 对齐。
	/// </summary>
	public Task<List<ModVersion?>> QueryLatestAsync(
		IReadOnlyList<ModFile> mods,
		string minecraftVersion,
		string loader,
		string curseForgeApiKey,
		string instanceId,
		ModContentService content,
		DateTimeOffset now,
		Action<string>? log = null,
		CancellationToken ct = default(CancellationToken))
	{
		return QueryLatestAsync(
			mods,
			instanceId,
			now,
			(mod, token) => content.GetLatestCompatibleVersionAsync(mod, minecraftVersion, loader, curseForgeApiKey, token),
			log,
			ct);
	}

	/// <summary>
	/// 真正干活的版本：查询动作由调用方注入（<paramref name="query"/>）。
	/// 这样"有界并发 + 缓存"这两条规则可以在**完全离线**的测试里验证，
	/// 不必伪造 Modrinth/CurseForge 的 JSON。
	/// </summary>
	public async Task<List<ModVersion?>> QueryLatestAsync(
		IReadOnlyList<ModFile> mods,
		string instanceId,
		DateTimeOffset now,
		Func<ModFile, CancellationToken, Task<ModVersion?>> query,
		Action<string>? log = null,
		CancellationToken ct = default(CancellationToken))
	{
		ModVersion?[] result = new ModVersion?[mods.Count];
		using SemaphoreSlim gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

		List<Task> tasks = new List<Task>(mods.Count);
		for (int i = 0; i < mods.Count; i++)
		{
			int index = i;
			ModFile mod = mods[i];
			string key = CacheKey(instanceId, mod);

			if (TryGetFresh(key, now, out ModVersion? cached))
			{
				result[index] = cached;
				continue;
			}

			if (!mod.HasOnlineMetadata)
			{
				// 没有在线元数据就查不到，也不必打网络；交给 ModUpdatePlanner 记为 skipped
				Put(key, null, now);
				result[index] = null;
				continue;
			}

			tasks.Add(Task.Run(async () =>
			{
				await gate.WaitAsync(ct).ConfigureAwait(false);
				try
				{
					ModVersion? latest = await query(mod, ct).ConfigureAwait(false);
					result[index] = latest;
					Put(key, latest, DateTimeOffset.Now);
				}
				catch (OperationCanceledException)
				{
					result[index] = null;
				}
				catch (Exception ex)
				{
					// 单个项目查询失败不拖垮整批；失败也缓存，避免用户连点导致重试风暴
					log?.Invoke("[mod-updates] " + mod.FileName + " 查询失败：" + ex.Message);
					result[index] = null;
					Put(key, null, DateTimeOffset.Now);
				}
				finally
				{
					gate.Release();
				}
			}, ct));
		}

		await Task.WhenAll(tasks).ConfigureAwait(false);
		return result.ToList();
	}
}
