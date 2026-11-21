using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 生产服务层源码的**按文件**读取入口（架构第 3 步的替身）。
///
/// **它取代了什么（2026-11-21）**：原先这里是 <c>LocalApiServiceSource</c>——把 19 个
/// <c>LocalApiService*.cs</c> 按一份硬编码顺序表拼成一个字符串（<c>All()</c>），
/// 再对这段文本做 <c>Contains</c>/<c>IndexOf</c> 断言。那套用法的两个结构性问题：
///
///   1. **维护成本落在人造契约上**：新增一个 partial 文件必须先"登记"，否则拼接器悄悄漏读，
///      而漏读的后果正是"断言变成恒真"。这条规则还得写进 <c>AGENTS.md</c> 才有人记得。
///   2. **断言的粒度错了**：在 19 个文件拼出来的 5000 行文本里找子串，改注释会误红，
///      真正的行为回归（handler 返回错对象、路由顺序颠倒、客户端接错）却完全看不见。
///
/// 第 3 步把这些断言换成了三类可以说清楚的东西：**可枚举的数据**（<see cref="Services.ApiRoute"/>
/// 表、反射枚举公开成员、磁盘枚举 partial 文件）、**真实请求断言的响应形状**、
/// 以及**对象图上的接线断言**（新闻提供器的客户端工厂返回哪一个具名客户端）。
///
/// **为什么还留这个类**：有少数守卫守的是"**某个文件里有没有某种代码形态**"
/// （启动链是否用宿主的 Download 客户端、<c>HandleApiAsync</c> 里是否又长出手写长度分支）。
/// <c>LocalApiService</c> 是 <c>static partial class</c>，**编译产物里没有"某个成员属于哪个文件"
/// 的信息**——反射只能看到整个类型、看不到文件归属，所以这类"形态/接线在哪"的判定只能读单文件文本。
/// 这种用例逐个在方法上写明理由，并且**只剩这几条**（本文件没有任何 <c>All()</c> 拼接、
/// 没有域清单、没有 <c>MethodBody</c> 之类的文本解析工具）。
/// </summary>
internal static class ProductionServicesSource
{
	/// <summary>生产服务层源码目录（服务层 = <c>Pineglow.Launcher.Services</c>）。</summary>
	internal static string Root => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory, "..", "..", "..", "..",
		"PineglowLauncher", "Pineglow.Launcher.Services"));

	/// <summary>某个源码文件的绝对路径。文件不存在直接抛出——静默返回空串会让断言恒真。</summary>
	internal static string PathOf(string fileName)
	{
		string path = Path.Combine(Root, fileName);
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"生产源码文件不存在：{path}", path);
		}

		return path;
	}

	/// <summary>读某个源码文件的文本。</summary>
	internal static string Read(string fileName) => File.ReadAllText(PathOf(fileName));

	/// <summary>
	/// 磁盘上实际存在的 <c>LocalApiService*.cs</c> 文件名（按序排列）。
	///
	/// **以磁盘为准，不维护登记表**：第 3 步之后没有任何断言依赖"固定顺序拼接"，
	/// 所以"清单"这件事只剩一个正当用途——守住"按域拆分的文件数不要被合回去"。
	/// 目录枚举天然不会漏掉新文件，正好把旧清单承担的那条"漏读即通过"风险消掉。
	/// </summary>
	internal static IReadOnlyList<string> LocalApiServicePartialFileNames()
		=> System.IO.Directory
			.GetFiles(Root, "LocalApiService*.cs")
			.Select(Path.GetFileName)
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray()!;
}
