using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// <c>LocalApiService</c> 按域拆分后的**结构守卫**（M1-2，2026-11-06；架构第 3 步重写，2026-11-21）。
///
/// **为什么需要这个测试（而不是"拆完就算完"）**：
/// M1-1 的教训是"没有机器门槛的东西会退回去"——路由表之所以能保持可枚举，靠的是
/// <c>ApiRouteInventoryTests</c> 每次都在断言它。同理，拆分如果只做一次、没有守卫，
/// 下一个功能很自然就会往"顺手能找到的那个文件"里加代码，几个月后体积又回到 5000 行，
/// 而那时候没人还记得为什么当初要拆。
///
/// **第 3 步改掉了什么（2026-11-21）**：原先三条门槛里有两条是"把 19 个 partial 拼成一段文本再 grep"——
///   1. 单文件体积上限：**保留**，但改成磁盘枚举（不再依赖一份硬编码的域清单）；
///   2. 域清单与磁盘一致：**换掉**。它守的其实是"拼接器别漏读文件"，拼接器退役后这条没有消费者了；
///      现在改成"磁盘上的 partial 文件数仍然 ≥ 10，且整个程序集里只有一个以 <c>LocalApiService</c>
///      开头的类型"——把"拆分被合回去"和"有人另起一个同名家族的类"两件事都变成可计数的断言。
///   3. 安全逻辑只允许一份：**换掉**。按文件 grep 的形态看不到"另一份实现被搬进新的帮助类"，
///      而且同类型内重复声明同名成员本就是编译错误（旧断言那部分是空洞的）。
///      现在枚举**整个程序集**里所有声明了令牌/Origin 成员的**类型**并计数，再用两个真实函数调用
///      确认那份唯一实现的行为是对的。
///
/// 这些都是**结构**断言，不替代行为断言：真正的行为由既有测试与真实 HTTP 路由可达性测试负责。
/// </summary>
public class LocalApiServiceSplitTests
{
	/// <summary>
	/// 单个域文件的行数上限。取 600 的理由：拆分后最大的域文件约 554 行，上限留出 ~10% 余量，
	/// 既能容纳正常增量修改，又能在"又长成一个新上帝类"之前强制再分域。
	/// 挑一个略大于现状的数字而不是理想值（比如 300），是为了让门槛**当下就通过**——
	/// 一条一直红的门槛等于没有门槛，团队会习惯性忽略它。
	/// </summary>
	private const int MaxLinesPerFile = 600;

	/// <summary>
	/// 安全边界成员的标记名。**只能**由宿主类型声明。
	///
	/// 分两类：
	///   * 精确名（字段/方法名与标记完全相同）；
	///   * 包含名（名字里带这些片段的成员同样算"又写了一份"，例如 <c>SessionTokenValidator</c>）。
	/// 反例已核对：<c>FileRecoveryRecord.OriginalPath</c> 这类"含 origin 字样但与鉴权无关"的成员
	/// 不会被包含名误伤（"OriginalPath" 不含 "AllowedOrigin"/"ValidSessionToken"/"SessionToken"）。
	/// </summary>
	private static readonly string[] ExactSecurityMemberNames =
	{
		"SessionToken",
		"IsAllowedOrigin",
		"HasValidSessionToken",
		"IsValidSessionToken",
		"ValidateOrigin",
	};

	private static readonly string[] ContainedSecurityFragments =
	{
		"SessionToken",
		"AllowedOrigin",
		"ValidSessionToken",
	};

	[Fact]
	public void EveryDomainFile_StaysUnderTheLineCap()
	{
		IReadOnlyList<string> files = ProductionServicesSource.LocalApiServicePartialFileNames();
		Assert.True(files.Count >= 10,
			$"源码目录里只剩 {files.Count} 个 LocalApiService 域文件，疑似拆分被合回单文件");

		var over = new List<string>();
		foreach (string name in files)
		{
			int lines = File.ReadAllLines(ProductionServicesSource.PathOf(name)).Length;
			Assert.True(lines > 0, $"{name} 是空文件，疑似拆分时漏了内容");
			if (lines > MaxLinesPerFile)
			{
				over.Add($"{name} = {lines} 行");
			}
		}

		Assert.True(over.Count == 0,
			$"以下域文件超过 {MaxLinesPerFile} 行上限，请继续按域拆分而不是继续往里加代码：{string.Join("；", over)}");
	}

	/// <summary>
	/// 域文件清单**以磁盘为准**，并且整个程序集里只能有一个 <c>LocalApiService</c> 家族的类型。
	///
	/// 旧版本这条守的是"目录里不许出现未登记的文件"（否则拼接器漏读它）。拼接器退役后，
	/// 剩下的真问题是两件可以用反射数出来的事：
	///   * 拆分有没有被合回单个文件（文件数）；
	///   * 有没有人新增一个 <c>LocalApiService.Xxx.cs</c> 却声明了**另一个类型**
	///     （那个文件里的代码就不再是这个类的一部分，域清单的"一家人"幻觉会骗过所有人）。
	/// </summary>
	[Fact]
	public void PartialFileInventory_IsStillSplit_AndTheFamilyHasExactlyOneType()
	{
		IReadOnlyList<string> files = ProductionServicesSource.LocalApiServicePartialFileNames();

		Assert.True(files.Count >= 10,
			$"源码目录里只有 {files.Count} 个 LocalApiService 域文件（{string.Join("、", files)}），疑似拆分被回退");
		Assert.Contains("LocalApiService.Routing.cs", files, StringComparer.Ordinal);

		Type[] family = AllAssemblyTypes()
			.Where(t => t.Name.StartsWith("LocalApiService", StringComparison.Ordinal))
			.ToArray();

		Assert.True(family.Length == 1,
			"程序集里出现了 " + family.Length + " 个 LocalApiService 家族类型："
			+ string.Join("、", family.Select(t => t.FullName))
			+ "。域文件必须都是同一个 partial 类型；另起一个同名家族的类等于把 API 面分成两半。");
		Assert.Equal(typeof(LocalApiService), family[0]);
		Assert.True(typeof(LocalApiService).IsAbstract && typeof(LocalApiService).IsSealed,
			"LocalApiService 必须是 static class（abstract + sealed）；否则它就不是那个「唯一实例」的宿主类型");
	}

	/// <summary>
	/// 会话令牌与 Origin 白名单只允许有**一份实现**，且由宿主持有。
	///
	/// 旧版本的做法是把 19 个域文件逐个读出来，找 <c>static readonly string SessionToken</c> 这类声明文本。
	/// 那个做法有两个毛病：同一个 partial 类型里重复声明同名成员本身就是编译错误（那部分断言是空洞的），
	/// 而真正会发生的场景——**复制到另一个帮助类/服务类**——又被"只扫 LocalApiService 的域文件"这一范围漏掉。
	///
	/// 现在的口径：枚举整个程序集所有类型（含嵌套）声明的成员名，对每个标记断言
	/// "声明它的类型恰好一个，且就是 <c>LocalApiService</c>"；再用两个真实调用确认这份唯一实现的行为
	/// （白名单只接受回环 Origin、令牌必须逐字节相同），免得守卫退化成"只有一个名字存在"。
	/// </summary>
	[Fact]
	public void SessionTokenAndOriginPolicy_AreDeclaredByExactlyOneType()
	{
		Type host = typeof(LocalApiService);
		var violations = new List<string>();

		foreach (Type type in AllAssemblyTypes())
		{
			if (type == host)
			{
				continue;
			}

			foreach (string memberName in DeclaredMemberNames(type))
			{
				if (ExactSecurityMemberNames.Contains(memberName, StringComparer.Ordinal))
				{
					violations.Add($"{type.FullName} 声明了安全边界成员 {memberName}");
					continue;
				}

				if (ContainedSecurityFragments.Any(f => memberName.Contains(f, StringComparison.Ordinal)))
				{
					violations.Add($"{type.FullName} 的成员 {memberName} 看起来是第二份令牌/Origin 逻辑");
				}
			}
		}

		Assert.True(violations.Count == 0,
			"安全边界逻辑被复制到了其它类型：" + string.Join("；", violations)
			+ "。令牌校验与 Origin 白名单必须只有一份，且由 LocalApiService 宿主持有。");

		// 宿主自己必须真的声明了它们（否则上面的"只在宿主里"变成恒真：一个都没有也算通过）。
		var declaredOnHost = DeclaredMemberNames(host).ToArray();
		foreach (string marker in ExactSecurityMemberNames)
		{
			Assert.True(declaredOnHost.Contains(marker, StringComparer.Ordinal),
				$"LocalApiService 上找不到 {marker}：安全边界被搬走或改名了，本守卫的「唯一实现」前提不成立");
		}

		// 行为牙齿：那份唯一实现确实按白名单工作。
		Assert.True(LocalApiService.IsAllowedOrigin("http://127.0.0.1:48123", 48123),
			"回环 Origin 必须被接受；若这里为 false，说明白名单实现已被改坏");
		Assert.False(LocalApiService.IsAllowedOrigin("https://evil.example", 48123),
			"外部 Origin 必须被拒绝");
		Assert.False(LocalApiService.IsAllowedOrigin("http://localhost:48123", 48123),
			"localhost 与 127.0.0.1 不是同一个 Origin，必须拒绝（历史上正是靠精确匹配挡住 DNS 重绑定）");

		string token = LocalApiService.SessionTokenForTests;
		Assert.False(LocalApiService.IsValidSessionToken(token + "x", token), "长度不符的令牌必须被拒绝");
		Assert.False(LocalApiService.IsValidSessionToken(null, token), "缺失令牌必须被拒绝");
		Assert.True(LocalApiService.IsValidSessionToken(token, token), "同一令牌必须通过");
	}

	/// <summary>
	/// 反向保护：守卫本身必须能咬人。
	///
	/// 若哪天有人把 <see cref="MaxLinesPerFile"/> 调到 100000，或把安全标记清单删空，
	/// 上面几条断言都会变成恒真。这里断言门槛参数仍在"有意义"的范围内。
	/// </summary>
	[Fact]
	public void TheGuardsThemselves_AreNotHollow()
	{
		Assert.True(MaxLinesPerFile <= 800,
			$"行数上限被放宽到 {MaxLinesPerFile}，已超过 LocalApiService 拆分前体积的 1/6，门槛失去意义");

		IReadOnlyList<string> files = ProductionServicesSource.LocalApiServicePartialFileNames();
		Assert.True(files.Count >= 10,
			$"域文件只剩 {files.Count} 个，疑似拆分被回退");
		Assert.Contains("LocalApiService.Routing.cs", files, StringComparer.Ordinal);

		Assert.True(ExactSecurityMemberNames.Length >= 5,
			$"安全标记清单只剩 {ExactSecurityMemberNames.Length} 项，守卫已被削弱成恒真");
		Assert.True(ContainedSecurityFragments.Length >= 3,
			$"包含式安全片段只剩 {ContainedSecurityFragments.Length} 项，第二份实现的漏网点又回来了");
	}

	private static IEnumerable<Type> AllAssemblyTypes()
	{
		try
		{
			return typeof(LocalApiService).Assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			// 加载不全时只保留能加载的类型：宁可少扫，也不静默返回空序列（空序列会让"零违规"恒真）。
			Type?[] loaded = ex.Types;
			return loaded.Where(t => t != null).Select(t => t!).ToArray();
		}
	}

	/// <summary>某个类型**自己声明**的成员名（不含继承来的、不含类型自身的名字）。</summary>
	private static IEnumerable<string> DeclaredMemberNames(Type type)
	{
		const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
			| BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

		foreach (MethodInfo method in type.GetMethods(Declared))
		{
			yield return method.Name;
		}

		foreach (FieldInfo field in type.GetFields(Declared))
		{
			yield return field.Name;
		}

		foreach (PropertyInfo property in type.GetProperties(Declared))
		{
			yield return property.Name;
		}
	}
}
