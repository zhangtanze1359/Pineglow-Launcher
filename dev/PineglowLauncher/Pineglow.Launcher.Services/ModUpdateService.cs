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
///   * 结果缓存 <see cref="CacheTtl"/>：失败也缓存，避免连点把网络请求翻倍。
///
/// 缓存按 `实例 + 文件名 + 已装 VersionId` 建键：**已装版本变了（刚更新完）自然失效**，
/// 不需要额外的失效逻辑——这也是为什么不按"项目 ID"建键。
///
/// 缓存里存的是 <see cref="ModQueryOutcome"/> 而不是裸版本号：**"查失败"与"没元数据"
/// 必须能穿过缓存**，否则用户在缓存窗口内连点两次，第二次看到的仍是坍塌后的"无元数据"。
/// </summary>
public sealed class ModUpdateService
{
	public const int MaxConcurrency = 6;

	public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

	private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);

	private sealed record CacheEntry(ModQueryOutcome Outcome, DateTimeOffset At);

	public static string CacheKey(string instanceId, ModFile mod) =>
		instanceId + "|" + mod.FileName + "|" + mod.VersionId;

	/// <summary>纯查询：缓存里有没有仍然新鲜的结果（不做 IO，便于直测）。</summary>
	public static bool TryGetFresh(string key, DateTimeOffset now, out ModQueryOutcome? outcome)
	{
		outcome = null;
		if (!Cache.TryGetValue(key, out CacheEntry? entry))
		{
			return false;
		}
		if (now - entry.At > CacheTtl)
		{
			return false;
		}
		outcome = entry.Outcome;
		return true;
	}

	public static void Put(string key, ModQueryOutcome outcome, DateTimeOffset now) =>
		Cache[key] = new CacheEntry(outcome, now);

	/// <summary>仅测试用：避免用例之间互相看到对方的缓存。</summary>
	public static void ClearCache() => Cache.Clear();

	/// <summary>
	/// 按 `mods` 的顺序返回查询结果。只对有在线元数据的条目发请求。
	/// 有界并发：<see cref="SemaphoreSlim"/> 限制在途请求数，结果按下标回填，顺序与 <paramref name="mods"/> 对齐。
	/// </summary>
	public Task<List<ModQueryOutcome>> QueryOutcomesAsync(
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
		return QueryOutcomesAsync(
			mods,
			instanceId,
			now,
			(mod, token) => content.GetLatestCompatibleVersionAsync(mod, minecraftVersion, loader, curseForgeApiKey, token),
			log,
			ct);
	}

	/// <summary>
	/// 真正干活的版本：查询动作由调用方注入（<paramref name="query"/>）。
	/// 这样"有界并发 + 缓存 + 失败与无元数据之分"这几条规则可以在**完全离线**的测试里验证。
	/// </summary>
	public async Task<List<ModQueryOutcome>> QueryOutcomesAsync(
		IReadOnlyList<ModFile> mods,
		string instanceId,
		DateTimeOffset now,
		Func<ModFile, CancellationToken, Task<ModVersion?>> query,
		Action<string>? log = null,
		CancellationToken ct = default(CancellationToken))
	{
		ModQueryOutcome?[] result = new ModQueryOutcome?[mods.Count];
		using SemaphoreSlim gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

		List<Task> tasks = new List<Task>(mods.Count);
		for (int i = 0; i < mods.Count; i++)
		{
			int index = i;
			ModFile mod = mods[i];
			string key = CacheKey(instanceId, mod);

			if (TryGetFresh(key, now, out ModQueryOutcome? cached) && cached != null)
			{
				result[index] = cached;
				continue;
			}

			if (!mod.HasOnlineMetadata)
			{
				// 没有在线元数据就查不到，也不必打网络；**这一种才是真正的"无元数据"**
				ModQueryOutcome outcome = ModQueryOutcome.NoMetadata();
				Put(key, outcome, now);
				result[index] = outcome;
				continue;
			}

			tasks.Add(Task.Run(async () =>
			{
				await gate.WaitAsync(ct).ConfigureAwait(false);
				try
				{
					ModVersion? latest = await query(mod, ct).ConfigureAwait(false);
					ModQueryOutcome outcome = latest == null
						? ModQueryOutcome.NoCompatible("上游没有与当前 MC 版本/加载器兼容的版本")
						: ModQueryOutcome.Found(latest);
					result[index] = outcome;
					Put(key, outcome, DateTimeOffset.Now);
				}
				catch (OperationCanceledException)
				{
					result[index] = ModQueryOutcome.Failed("已取消");
				}
				catch (Exception ex)
				{
					// 单个项目查询失败不拖垮整批；失败也缓存，避免用户连点导致重试风暴
					log?.Invoke("[mod-updates] " + mod.FileName + " 查询失败：" + ex.Message);
					ModQueryOutcome outcome = ModQueryOutcome.Failed(ex.Message);
					result[index] = outcome;
					Put(key, outcome, DateTimeOffset.Now);
				}
				finally
				{
					gate.Release();
				}
			}, ct));
		}

		await Task.WhenAll(tasks).ConfigureAwait(false);
		return result.Select(x => x ?? ModQueryOutcome.NoMetadata()).ToList();
	}

	/// <summary>
	/// 兼容入口：只关心"查到的版本"的调用方仍可用它；查不到（任何一种原因）都给 null。
	/// 需要区分原因的新调用方用 <see cref="QueryOutcomesAsync"/>。
	/// </summary>
	public async Task<List<ModVersion?>> QueryLatestAsync(
		IReadOnlyList<ModFile> mods,
		string instanceId,
		DateTimeOffset now,
		Func<ModFile, CancellationToken, Task<ModVersion?>> query,
		Action<string>? log = null,
		CancellationToken ct = default(CancellationToken))
	{
		List<ModQueryOutcome> outcomes = await QueryOutcomesAsync(mods, instanceId, now, query, log, ct).ConfigureAwait(false);
		return outcomes.Select(o => o.Status == ModQueryStatus.Found ? o.Version : null).ToList();
	}

	/// <summary>兼容入口（生产形状）：直接对接 ModContentService 的真实上游查询。</summary>
	public async Task<List<ModVersion?>> QueryLatestAsync(
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
		List<ModQueryOutcome> outcomes = await QueryOutcomesAsync(
			mods, minecraftVersion, loader, curseForgeApiKey, instanceId, content, now, log, ct).ConfigureAwait(false);
		return outcomes.Select(o => o.Status == ModQueryStatus.Found ? o.Version : null).ToList();
	}
}
