using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pineglow.Launcher.Services;

public static class ModSearchQueryResolver
{
	private static readonly IReadOnlyList<KeyValuePair<string, string>> ChineseAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["铁氧体磁芯"] = "ferritecore",
		["应用能源"] = "applied energistics",
		["旅行地图"] = "journeymap",
		["暮色森林"] = "twilight forest",
		["农夫乐事"] = "farmer's delight",
		["帕秋莉手册"] = "patchouli",
		["通用机械"] = "mekanism",
		["植物魔法"] = "botania",
		["工业时代"] = "industrial craft",
		["拔刀剑"] = "slashblade",
		["匠魂"] = "tinkers construct",
		["等价交换"] = "projecte",
		["无尽贪婪"] = "avaritia",
		["潘马斯农场"] = "pam harvestcraft",
		["更多箱子"] = "iron chests",
		["连锁挖矿"] = "vein mining",
		["一键整理"] = "inventory profiles",
		["高清修复"] = "optifine",
		["物品管理器"] = "jei",
		["合成表"] = "jei",
		["苹果皮"] = "appleskin",
		["小地图"] = "minimap",
		["世界地图"] = "world map",
		["动态光源"] = "dynamic lights",
		["鼠标手势"] = "mouse tweaks",
		["耐久显示"] = "durability viewer",
		["钠"] = "sodium",
		["锂"] = "lithium",
		["磷"] = "phosphor",
		["玉"] = "jade",
		["工业"] = "industrial",
		["科技"] = "technology",
		["魔法"] = "magic",
		["冒险"] = "adventure",
		["生存"] = "survival",
		["建筑"] = "building",
		["装饰"] = "decoration",
		["背包"] = "backpack",
		["地图"] = "map",
		["优化"] = "optimization",
		["性能"] = "performance",
		["模组"] = "mod"
	}.OrderByDescending((KeyValuePair<string, string> pair) => pair.Key.Length).ToList();

	public static string Resolve(string? query)
	{
		string text = NormalizeSpaces(query ?? string.Empty);
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		string text2 = text;
		bool flag = false;
		foreach (KeyValuePair<string, string> chineseAlias in ChineseAliases)
		{
			if (text2.Contains(chineseAlias.Key, StringComparison.OrdinalIgnoreCase))
			{
				text2 = text2.Replace(chineseAlias.Key, " " + chineseAlias.Value + " ", StringComparison.OrdinalIgnoreCase);
				flag = true;
			}
		}
		if (!flag)
		{
			return text;
		}
		text2 = RemainingChineseRegex().Replace(text2, " ");
		return NormalizeSpaces(text2);
	}

	public static bool WasTranslated(string? query, out string resolved)
	{
		string text = NormalizeSpaces(query ?? string.Empty);
		resolved = Resolve(text);
		if (!string.IsNullOrWhiteSpace(resolved))
		{
			return !resolved.Equals(text, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static string NormalizeSpaces(string value)
	{
		return WhitespaceRegex().Replace(value.Trim(), " ");
	}

	private static readonly Regex RemainingChinese = new Regex("[\u3400-\u9fff]+");

	private static readonly Regex Whitespace = new Regex("\\s+");

	private static Regex RemainingChineseRegex()
	{
		return RemainingChinese;
	}

	private static Regex WhitespaceRegex()
	{
		return Whitespace;
	}
}
