using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR09 补测（缺口登记表 A9）：把**冻结案例集**的声明变成机器校验的不变量。
///
/// MR09 报告 §一 的声明："冻结案例集 | ✅ **30 例**，SHA256 `D9F55189FA05845B…`，冻结于 2026-10-03"、
/// "dev 20 / holdout 10，holdout 各占独立成因族，与 dev **零家族重叠**（防近重复泄漏）"；
/// §六 的代价说明："保留集 10 例**全部是规则未知**样本（这是为了与 dev 零家族重叠而付出的代价）"。
///
/// 本类把上面这些**散文声明**变成 5 条不变量：
///   ① 案例集必须是**冻结的那一份**（哈希一致）+ 30 例 + 20/10 拆分；
///   ② holdout 家族与 dev **零交集**，且 holdout 内部**各占独立族**；
///   ③ holdout **全部是规则未知**（`generic`）；
///   ④ 每条案例字段齐全、枚举取值都在声明集合内；
///   ⑤ **dev 只覆盖 5 个家族、holdout 覆盖 10 个** —— 这是"dev 能测什么"的硬边界。
///
/// **口径**：本类校验的是**案例集自身的自洽性与冻结性**，不是评测质量（真实模型评测仍未做，见 MR09 §六）。
/// </summary>
public class DiagnosticCaseSetInvariantTests
{
	private readonly ITestOutputHelper _out;

	public DiagnosticCaseSetInvariantTests(ITestOutputHelper output) => _out = output;

	/// <summary>MR09 证据里记录的冻结哈希（完整值）。</summary>
	private const string FrozenSha256 = "D9F55189FA05845BAC0403A058AA63912A725AFF61555BDF25A6865DD5AB45AF";

	private static string CaseSetPath
	{
		get
		{
			string dir = AppContext.BaseDirectory;
			for (int i = 0; i < 8 && dir != null; i++)
			{
				string candidate = Path.Combine(dir, "dev", "PineglowLauncher.Tests", "TestAssets", "Diagnostics", "diagnostic-cases.json");
				if (File.Exists(candidate))
				{
					return candidate;
				}

				dir = Path.GetDirectoryName(dir);
			}

			throw new FileNotFoundException("找不到 diagnostic-cases.json");
		}
	}

	private sealed record CaseRow(string Id, string Category, string Family, bool Holdout, string Bucket, string Verdict, bool Answerable);

	private static List<CaseRow> Load()
	{
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(CaseSetPath));
		JsonElement root = doc.RootElement;

		var holdout = new HashSet<string>(StringComparer.Ordinal);
		if (root.TryGetProperty("holdoutIds", out JsonElement h) && h.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement e in h.EnumerateArray())
			{
				holdout.Add(e.GetString() ?? string.Empty);
			}
		}

		var correctFamily = new Dictionary<string, string>(StringComparer.Ordinal);
		if (root.TryGetProperty("correctFamily", out JsonElement cf) && cf.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty p in cf.EnumerateObject())
			{
				correctFamily[p.Name] = p.Value.GetString() ?? string.Empty;
			}
		}

		var rows = new List<CaseRow>();
		foreach (JsonElement c in root.GetProperty("cases").EnumerateArray())
		{
			string id = c.GetProperty("id").GetString() ?? string.Empty;
			string category = c.GetProperty("category").GetString() ?? string.Empty;
			// 有效家族：correctFamily 覆盖少数"真实成因与 category 名称不同"的误导样本
			string family = correctFamily.TryGetValue(id, out string? f) && !string.IsNullOrWhiteSpace(f) ? f : category;
			rows.Add(new CaseRow(
				id,
				category,
				family,
				holdout.Contains(id),
				c.GetProperty("expectRuleBucket").GetString() ?? string.Empty,
				c.GetProperty("expectScorer").GetString() ?? string.Empty,
				c.GetProperty("answerable").GetBoolean()));
		}

		return rows;
	}

	/* ===================== ① 冻结性 + 拆分 ===================== */

	[Fact]
	public void CaseSet_IsTheFrozenOne_WithTheRecordedSplit()
	{
		string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(CaseSetPath)));
		Assert.True(actual == FrozenSha256,
			"冻结案例集**已被改动**。\n" +
			$"  记录值：{FrozenSha256}\n  当前值：{actual}\n" +
			"若这是有意变更（例如修正某条案例），请同步更新 MR09 证据里的哈希与本用例的记录值；\n" +
			"若并非有意，请还原该文件 —— 冻结集被悄悄改动会让此前所有评测数字失去可比性。");

		List<CaseRow> rows = Load();
		Assert.True(rows.Count == 30, $"案例数应为 30，实际 {rows.Count}");
		int holdout = rows.Count(r => r.Holdout);
		Assert.True(holdout == 10, $"保留集应为 10 例，实际 {holdout}");
		Assert.True(rows.Count - holdout == 20, $"开发集应为 20 例，实际 {rows.Count - holdout}");
		Assert.True(rows.Select(r => r.Id).Distinct().Count() == 30, "案例 id 必须唯一");
		_out.WriteLine($"[A9] 冻结集一致（{FrozenSha256[..12]}…）：30 例 = dev 20 + holdout 10");
	}

	/* ===================== ② 零家族重叠 + holdout 各占独立族 ===================== */

	[Fact]
	public void HoldoutFamilies_AreDisjointFromDev_AndMutuallyDistinct()
	{
		List<CaseRow> rows = Load();
		var devFamilies = rows.Where(r => !r.Holdout).Select(r => r.Family).ToHashSet(StringComparer.Ordinal);
		var holdFamilies = rows.Where(r => r.Holdout).Select(r => r.Family).ToList();

		var overlap = devFamilies.Intersect(holdFamilies, StringComparer.Ordinal).ToList();
		Assert.True(overlap.Count == 0,
			"MR09 声明 holdout 与 dev **零家族重叠**（防近重复泄漏），实际存在交集：" + string.Join(", ", overlap));

		Assert.True(holdFamilies.Distinct(StringComparer.Ordinal).Count() == holdFamilies.Count,
			"MR09 声明 holdout **各占独立成因族**，实际有重复：" +
			string.Join(", ", holdFamilies.GroupBy(f => f).Where(g => g.Count() > 1).Select(g => g.Key + "×" + g.Count())));

		_out.WriteLine($"[A9] dev 家族 {devFamilies.Count} 个 / holdout 家族 {holdFamilies.Count} 个，交集为空，holdout 内部无重复");
	}

	/* ===================== ③ holdout 全部规则未知 ===================== */

	[Fact]
	public void Holdout_IsEntirelyRuleUnknown()
	{
		List<CaseRow> rows = Load();
		var holdout = rows.Where(r => r.Holdout).ToList();
		var notGeneric = holdout.Where(r => r.Bucket != "generic").ToList();
		Assert.True(notGeneric.Count == 0,
			"MR09 声明保留集 10 例**全部是规则未知**（generic）；实际这些不是：" +
			string.Join(", ", notGeneric.Select(r => r.Id + "=" + r.Bucket)));

		// dev 里也有规则未知样本，但只是少数 —— 这正是"代价"的具体形状
		int devGeneric = rows.Count(r => !r.Holdout && r.Bucket == "generic");
		_out.WriteLine($"[A9] holdout 全部 generic（10/10）；dev 里 generic {devGeneric}/20 —— 保留集的偏向是**集中**而非「绝对新」");
		Assert.True(devGeneric < 10, "dev 的 generic 例数应远少于 holdout（否则'保留集全是未知样本'这一偏向描述不成立）");
	}

	/* ===================== ④ 字段齐全 + 枚举取值合法 ===================== */

	[Fact]
	public void EveryCase_HasRequiredFieldsAndDeclaredEnumValues()
	{
		using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(CaseSetPath));
		JsonElement root = doc.RootElement;

		var verdicts = root.GetProperty("scorerVerdicts").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);
		var buckets = root.GetProperty("ruleBuckets").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);

		var problems = new List<string>();
		foreach (JsonElement c in root.GetProperty("cases").EnumerateArray())
		{
			string id = c.GetProperty("id").GetString() ?? "(无 id)";
			foreach (string field in new[] { "log", "expectedCause", "safeFix" })
			{
				if (!c.TryGetProperty(field, out JsonElement v) || v.ValueKind != JsonValueKind.String
					|| string.IsNullOrWhiteSpace(v.GetString()))
				{
					problems.Add($"{id}.{field} 缺失或为空");
				}
			}

			string verdict = c.GetProperty("expectScorer").GetString() ?? string.Empty;
			if (!verdicts.Contains(verdict))
			{
				problems.Add($"{id}.expectScorer={verdict} 不在 scorerVerdicts 内");
			}

			string bucket = c.GetProperty("expectRuleBucket").GetString() ?? string.Empty;
			if (!buckets.Contains(bucket))
			{
				problems.Add($"{id}.expectRuleBucket={bucket} 不在 ruleBuckets 内");
			}
		}

		Assert.True(problems.Count == 0, "案例集存在字段/枚举问题：\n" + string.Join("\n", problems));
		_out.WriteLine($"[A9] 30 例字段齐全；scorerVerdicts={string.Join("/", verdicts)}；ruleBuckets={string.Join("/", buckets)}");
	}

	/* ===================== ⑤ dev 覆盖边界（"能测什么"的硬约束） ===================== */

	[Fact]
	public void DevSetFamilyCoverage_BoundsWhatTheDevSplitCanMeasure()
	{
		List<CaseRow> rows = Load();
		var devFamilies = rows.Where(r => !r.Holdout).Select(r => r.Family).Distinct(StringComparer.Ordinal).OrderBy(f => f).ToList();
		var holdFamilies = rows.Where(r => r.Holdout).Select(r => r.Family).Distinct(StringComparer.Ordinal).OrderBy(f => f).ToList();

		// MR09 说"规则已知正确率不下降必须在 dev 集上判定" —— 但 dev 只有 5 个家族，
		// 因此该判定**只对这 5 个家族成立**，对 holdout 的 10 个家族无从谈起。
		Assert.True(devFamilies.Count == 5,
			"dev 家族数应为 5（这是「dev 能测什么」的边界），实际 " + devFamilies.Count + "：" + string.Join(", ", devFamilies));
		Assert.True(holdFamilies.Count == 10,
			"holdout 家族数应为 10，实际 " + holdFamilies.Count + "：" + string.Join(", ", holdFamilies));

		_out.WriteLine("[A9] dev 家族：" + string.Join(", ", devFamilies));
		_out.WriteLine("[A9] holdout 家族：" + string.Join(", ", holdFamilies));
		_out.WriteLine("[A9] 结论：dev 上的「规则已知正确率不下降」只能覆盖 5 个家族；holdout 的 10 个家族在 dev 里**没有任何样本**");
	}
}
