using System;
using System.Collections.Generic;
using System.Linq;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

public static class ModHealthService
{
	public static ModHealthReport Analyze(GameInstance instance, IReadOnlyCollection<ModFile> mods)
	{
		List<ModFile> list = mods.Where((ModFile mod) => mod.IsEnabled).ToList();
		List<ModHealthIssue> list2 = new List<ModHealthIssue>();
		string loader = ModLoaderInfo.Normalize(instance.LoaderType);
		if (list.Count > 0 && !ModLoaderInfo.IsModLoader(loader))
		{
			list2.Add(new ModHealthIssue(ModHealthSeverity.Error, "当前游戏没有 Mod 支持", $"已启用 {list.Count} 个 Mod，但当前游戏是原版。请在下载页安装 Fabric、Forge 或 NeoForge 后再启动。"));
		}
		foreach (IGrouping<string, ModFile> item in from @group in list.Where((ModFile mod) => mod.HasOnlineMetadata).GroupBy<ModFile, string>((ModFile mod) => mod.OnlineSource + ":" + mod.ProjectId, StringComparer.OrdinalIgnoreCase)
			where @group.Count() > 1
			select @group)
		{
			list2.Add(new ModHealthIssue(ModHealthSeverity.Warning, "发现重复安装的在线 Mod", $"{item.First().DisplayName} 有 {item.Count()} 个启用文件。通常只保留一个版本，避免重复加载。"));
		}
		foreach (IGrouping<string, ModFile> item2 in from @group in list.Where((ModFile mod) => !string.IsNullOrWhiteSpace(mod.ModId)).GroupBy<ModFile, string>((ModFile mod) => mod.ModId, StringComparer.OrdinalIgnoreCase)
			where @group.Count() > 1
			select @group)
		{
			list2.Add(new ModHealthIssue(ModHealthSeverity.Warning, "发现相同 Mod 标识", $"{item2.Key} 有 {item2.Count()} 个启用文件，游戏可能只会加载其中一个或直接报错。"));
		}
		foreach (ModFile item3 in list.Where((ModFile mod) => !IsCompatibleWithLoader(mod.LoaderHint, loader)))
		{
			list2.Add(new ModHealthIssue(ModHealthSeverity.Warning, "Mod 可能不适配当前模组支持", $"{ModName(item3)} 标注为 {item3.LoaderHint}，当前游戏使用 {FriendlyLoader(loader)}。请确认它们可以一起使用。"));
		}
		foreach (ModFile item4 in list)
		{
			foreach (ModManifestDependency dependency in item4.Dependencies.Where((ModManifestDependency item) => item.IsRequired))
			{
				List<ModFile> list3 = list.Where((ModFile item) => item.ModId.Equals(dependency.ModId, StringComparison.OrdinalIgnoreCase)).ToList();
				if (list3.Count == 0)
				{
					list2.Add(new ModHealthIssue(ModHealthSeverity.Error, "缺少 Mod 前置依赖", $"{ModName(item4)} 需要 {dependency.ModId}{FormatVersionRange(dependency.VersionRange)}，但当前没有启用它。"));
				}
				else if (!list3.Any((ModFile candidate) => VersionMatches(candidate.Version, dependency.VersionRange)))
				{
					list2.Add(new ModHealthIssue(ModHealthSeverity.Error, "Mod 前置依赖版本不匹配", $"{ModName(item4)} 需要 {dependency.ModId}{FormatVersionRange(dependency.VersionRange)}，当前版本为 {string.Join(" / ", list3.Select((ModFile item) => (!string.IsNullOrWhiteSpace(item.Version)) ? item.Version : "未知"))}。"));
				}
			}
		}
		int num = list.Count((ModFile mod) => !mod.HasOnlineMetadata);
		if (num > 0)
		{
			list2.Add(new ModHealthIssue(ModHealthSeverity.Info, "部分 Mod 无法自动检查更新", $"有 {num} 个手动导入的 Mod 没有在线来源记录，启动器不会自动替换这些文件。"));
		}
		return new ModHealthReport
		{
			EnabledCount = list.Count,
			DisabledCount = mods.Count - list.Count,
			OnlineManagedCount = mods.Count((ModFile mod) => mod.HasOnlineMetadata),
			Issues = list2
		};
	}

	private static bool IsCompatibleWithLoader(string loaderHint, string loader)
	{
		if (string.IsNullOrWhiteSpace(loaderHint) || !ModLoaderInfo.IsModLoader(loader))
		{
			return true;
		}
		return loaderHint switch
		{
			"Fabric" => (loader == "Fabric" || loader == "Quilt") ? true : false, 
			"Quilt" => loader == "Quilt", 
			"Forge / NeoForge" => (loader == "Forge" || loader == "NeoForge") ? true : false, 
			"Forge" => loader == "Forge", 
			_ => true, 
		};
	}

	private static string FriendlyLoader(string loader)
	{
		if (!(loader == "Vanilla"))
		{
			return loader;
		}
		return "原版";
	}

	private static string ModName(ModFile mod)
	{
		if (!string.IsNullOrWhiteSpace(mod.DisplayName))
		{
			return mod.DisplayName;
		}
		return mod.FileName;
	}

	private static string FormatVersionRange(string range)
	{
		if (!string.IsNullOrWhiteSpace(range) && !(range == "*"))
		{
			return " " + range;
		}
		return string.Empty;
	}

	private static bool VersionMatches(string actual, string range)
	{
		if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(range) || range == "*" || range == "(,)")
		{
			return true;
		}
		return range.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any((string option) => VersionMatchesSingle(actual, option));
	}

	private static bool VersionMatchesSingle(string actual, string range)
	{
		range = range.Trim();
		string text3;
		if (range.Length >= 3 && (range[0] == '[' || range[0] == '('))
		{
			string text = range;
			if (text[text.Length - 1] != ']')
			{
				string text2 = range;
				if (text2[text2.Length - 1] != ')')
				{
					goto IL_010e;
				}
			}
			text3 = range;
			string[] array = text3.Substring(1, text3.Length - 1 - 1).Split(',', 2, StringSplitOptions.TrimEntries);
			if (array.Length == 2)
			{
				bool num = string.IsNullOrWhiteSpace(array[0]) || CompareVersions(actual, array[0]) > 0 || (range[0] == '[' && CompareVersions(actual, array[0]) == 0);
				int num2;
				if (!string.IsNullOrWhiteSpace(array[1]) && CompareVersions(actual, array[1]) >= 0)
				{
					string text4 = range;
					num2 = ((text4[text4.Length - 1] == ']' && CompareVersions(actual, array[1]) == 0) ? 1 : 0);
				}
				else
				{
					num2 = 1;
				}
				bool flag = (byte)num2 != 0;
				return num && flag;
			}
		}
		goto IL_010e;
		IL_010e:
		string[] array2 = range.Split(new char[2] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array2.Length > 1)
		{
			return array2.All((string constraint) => VersionMatchesSingle(actual, constraint));
		}
		string[] array3 = new string[5] { ">=", "<=", ">", "<", "=" };
		foreach (string text5 in array3)
		{
			if (range.StartsWith(text5, StringComparison.Ordinal))
			{
				int num4 = CompareVersions(actual, range.Substring(text5.Length));
				return text5 switch
				{
					">=" => num4 >= 0, 
					"<=" => num4 <= 0, 
					">" => num4 > 0, 
					"<" => num4 < 0, 
					_ => num4 == 0, 
				};
			}
		}
		if (!range.EndsWith(".*", StringComparison.Ordinal))
		{
			return CompareVersions(actual, range) == 0;
		}
		string text6 = actual;
		text3 = range;
		return text6.StartsWith(text3.Substring(0, text3.Length - 1), StringComparison.OrdinalIgnoreCase);
	}

	private static int CompareVersions(string left, string right)
	{
		string[] array = left.Trim().TrimStart(new char[2] { 'v', 'V' }).Split(new char[4] { '.', '-', '+', '_' }, StringSplitOptions.RemoveEmptyEntries);
		string[] array2 = right.Trim().TrimStart(new char[2] { 'v', 'V' }).Split(new char[4] { '.', '-', '+', '_' }, StringSplitOptions.RemoveEmptyEntries);
		int num = Math.Max(array.Length, array2.Length);
		for (int i = 0; i < num; i++)
		{
			string text = ((i < array.Length) ? array[i] : "0");
			string text2 = ((i < array2.Length) ? array2[i] : "0");
			if (int.TryParse(text, out var result) && int.TryParse(text2, out var result2))
			{
				int num2 = result.CompareTo(result2);
				if (num2 != 0)
				{
					return num2;
				}
			}
			else
			{
				int num3 = string.Compare(text, text2, StringComparison.OrdinalIgnoreCase);
				if (num3 != 0)
				{
					return num3;
				}
			}
		}
		return 0;
	}
}
