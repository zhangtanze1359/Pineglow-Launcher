using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 读取 <c>LocalApiService</c> 的源码文本，供「静态结构断言」类测试使用。
///
/// **为什么需要这层间接（M1-2，2026-11-06）**：
/// 有若干测试靠读源码做断言（例如「服务端不得生成 operationId」「HandleApiAsync 里不得再有
/// seg.Length 分支」）。M1-2 把这个类按域拆成了 18 个 partial 文件，原先硬编码
/// <c>LocalApiService.cs</c> 单个路径的读法会直接 FileNotFound 或读到不完整的文本——
/// 断言于是变成"读不到就失败"或更糟的"读到一半却意外通过"。
///
/// 这里统一成两种口径：
///   * <see cref="All"/> —— 全部 partial 文件按**固定域顺序**拼接，用于"某段文本是否存在于这个类"；
///   * <see cref="Single"/> —— 指定某一个域文件，用于"某段代码必须在这个文件里"。
///
/// 顺序必须显式写死，不能靠 <c>Directory.GetFiles</c> 的返回顺序（文件系统不保证），
/// 否则拼接结果不确定，跨文件的位置比较会变成偶发通过。
/// </summary>
internal static class LocalApiServiceSource
{
	/// <summary>域文件的固定顺序。新增 partial 文件必须在这里登记，否则 <see cref="All"/> 读不到它。</summary>
	private static readonly string[] OrderedFiles =
	{
		"LocalApiService.cs",
		"LocalApiService.Routing.cs",
		"LocalApiService.Configuration.cs",
		"LocalApiService.Instances.cs",
		"LocalApiService.Instances.Import.cs",
		"LocalApiService.Instances.Launch.cs",
		"LocalApiService.Saves.cs",
		"LocalApiService.Mods.cs",
		"LocalApiService.Bisect.cs",
		"LocalApiService.Bisect.Persistence.cs",
		"LocalApiService.Diagnostics.cs",
		"LocalApiService.Accounts.cs",
		"LocalApiService.Content.cs",
		"LocalApiService.Downloads.cs",
		"LocalApiService.News.cs",
		"LocalApiService.Update.cs",
		"LocalApiService.Servers.cs",
		"LocalApiService.StaticFiles.cs"
	};

	/// <summary>域文件所在的源码目录。</summary>
	internal static string Directory => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory, "..", "..", "..", "..",
		"PineglowLauncher", "Pineglow.Launcher.Services"));

	/// <summary>域文件的固定顺序（只读视图，供结构守卫测试核对目录里没有未登记的文件）。</summary>
	internal static IReadOnlyList<string> ExpectedFiles => OrderedFiles;

	/// <summary>取某个域文件的绝对路径。<paramref name="fileName"/> 必须已登记。</summary>
	internal static string PathOf(string fileName)
	{
		if (!OrderedFiles.Contains(fileName, StringComparer.Ordinal))
		{
			throw new ArgumentException(
				$"{fileName} 未登记在 {nameof(OrderedFiles)} 中；新增 partial 文件必须同时登记，否则拼接文本会漏掉它。",
				nameof(fileName));
		}

		return Path.Combine(Directory, fileName);
	}

	/// <summary>读某个域文件的文本。</summary>
	internal static string Single(string fileName) => File.ReadAllText(PathOf(fileName));

	/// <summary>按固定顺序拼接全部域文件的文本。文件缺失直接抛出，不允许静默跳过。</summary>
	internal static string All()
	{
		var parts = new List<string>(OrderedFiles.Length);
		foreach (string name in OrderedFiles)
		{
			string path = Path.Combine(Directory, name);
			if (!File.Exists(path))
			{
				throw new FileNotFoundException(
					$"LocalApiService 的域文件缺失：{path}。若确实删除了某个域，必须同步更新 {nameof(OrderedFiles)}。",
					path);
			}

			parts.Add(File.ReadAllText(path));
		}

		return string.Join("\n", parts);
	}

	/// <summary>
	/// 从源码文本里截出某个方法的方法体（含首尾花括号），按花括号配对求范围。
	/// 找不到签名或方法体未闭合时抛出，而不是返回空串——空串会让后续索引断言静默通过。
	/// </summary>
	internal static string MethodBody(string source, string signature)
	{
		int sig = source.IndexOf(signature, StringComparison.Ordinal);
		if (sig < 0)
		{
			throw new InvalidOperationException($"未找到方法签名：{signature}");
		}

		int open = source.IndexOf('{', sig);
		if (open < 0)
		{
			throw new InvalidOperationException($"方法 {signature} 后没有找到方法体起始花括号");
		}

		int depth = 0;
		for (int i = open; i < source.Length; i++)
		{
			if (source[i] == '{') depth++;
			else if (source[i] == '}')
			{
				depth--;
				if (depth == 0)
				{
					return source.Substring(open, i - open + 1);
				}
			}
		}

		throw new InvalidOperationException($"方法 {signature} 的方法体未闭合，提取失败");
	}
}
