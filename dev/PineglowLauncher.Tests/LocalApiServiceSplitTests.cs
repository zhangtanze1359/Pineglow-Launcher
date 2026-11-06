using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// M1-2（2026-11-06）：<c>LocalApiService</c> 按域拆分后的**结构守卫**。
///
/// **为什么需要这个测试（而不是"拆完就算完"）**：
/// M1-1 的教训是"没有机器门槛的东西会退回去"——路由表之所以能保持可枚举，靠的是
/// <c>ApiRouteInventoryTests</c> 每次都在断言它。同理，拆分如果只做一次、没有守卫，
/// 下一个功能很自然就会往"顺手能找到的那个文件"里加代码，几个月后体积又回到 5000 行，
/// 而那时候没人还记得为什么当初要拆。
///
/// 三条门槛：
///   1. **单文件体积上限**：任何域文件不得超过 <see cref="MaxLinesPerFile"/> 行；
///      超了就必须继续分域，而不是先塞进去再说。
///   2. **域清单与磁盘一致**：源码目录里不许出现未登记的
///      <c>LocalApiService*.cs</c>（否则 <see cref="LocalApiServiceSource.All"/> 会漏读它，
///      靠读源码做的断言会变成"漏读即通过"）。
///   3. **拆分不得引入第二份安全逻辑**：会话令牌校验与 Origin 白名单只允许存在于宿主文件，
///      不得被复制到某个域文件里——M1-2 的约束原文是"不改变令牌校验或 Origin 白名单逻辑"，
///      复制一份就等于有两套口径。
///
/// 这些断言都是**结构**断言，不替代行为断言：真正的行为由 978 项既有测试与真实 HTTP 路由可达性测试负责。
/// </summary>
public class LocalApiServiceSplitTests
{
	/// <summary>
	/// 单个域文件的行数上限。
	///
	/// 取 600 的理由：拆分后最大的域文件是 <c>LocalApiService.Diagnostics.cs</c>（约 554 行），
	/// 上限留出 ~10% 余量，既能容纳正常的增量修改，又能在"又长成一个新上帝类"之前强制再分域。
	/// 挑一个略大于现状的数字而不是理想值（比如 300），是为了让门槛**当下就通过**——
	/// 一条一直红的门槛等于没有门槛，团队会习惯性忽略它。
	/// </summary>
	private const int MaxLinesPerFile = 600;

	[Fact]
	public void EveryDomainFile_StaysUnderTheLineCap()
	{
		var over = new List<string>();
		foreach (string name in LocalApiServiceSource.ExpectedFiles)
		{
			int lines = File.ReadAllLines(LocalApiServiceSource.PathOf(name)).Length;
			Assert.True(lines > 0, $"{name} 是空文件，疑似拆分时漏了内容");
			if (lines > MaxLinesPerFile)
			{
				over.Add($"{name} = {lines} 行");
			}
		}

		Assert.True(over.Count == 0,
			$"以下域文件超过 {MaxLinesPerFile} 行上限，请继续按域拆分而不是继续往里加代码：{string.Join("；", over)}");
	}

	[Fact]
	public void SourceDirectory_HasNoUnregisteredPartialFile()
	{
		string[] onDisk = Directory
			.GetFiles(LocalApiServiceSource.Directory, "LocalApiService*.cs")
			.Select(p => Path.GetFileName(p))
			.OrderBy(p => p, StringComparer.Ordinal)
			.ToArray();

		string[] registered = LocalApiServiceSource.ExpectedFiles
			.OrderBy(p => p, StringComparer.Ordinal)
			.ToArray();

		string[] unregistered = onDisk.Except(registered, StringComparer.Ordinal).ToArray();
		Assert.True(unregistered.Length == 0,
			"源码目录里有未登记的 LocalApiService 部分类文件：" + string.Join("、", unregistered)
			+ "。必须在 LocalApiServiceSource.ExpectedFiles 与域清单测试里登记，否则拼读源码的断言会漏掉它。");

		string[] missing = registered.Except(onDisk, StringComparer.Ordinal).ToArray();
		Assert.True(missing.Length == 0,
			"清单登记的域文件在磁盘上不存在：" + string.Join("、", missing));
	}

	/// <summary>
	/// 会话令牌与 Origin 白名单只允许有一套实现。
	///
	/// 允许出现的唯一位置：宿主文件（<c>LocalApiService.cs</c>）。
	/// 域文件里出现同名字段/方法意味着有人把安全逻辑复制了一份，两份迟早会分叉。
	/// </summary>
	[Fact]
	public void SessionTokenAndOriginPolicy_LiveOnlyInHostFile()
	{
		string[] securityMarkers =
		{
			"SessionToken",
			"IsAllowedOrigin",
			"HasValidSessionToken",
			"IsValidSessionToken",
			"ValidateOrigin"
		};

		var violations = new List<string>();
		foreach (string name in LocalApiServiceSource.ExpectedFiles)
		{
			if (string.Equals(name, "LocalApiService.cs", StringComparison.Ordinal))
			{
				continue;
			}

			string src = LocalApiServiceSource.Single(name);
			foreach (string marker in securityMarkers)
			{
				// 只算"定义"，不算"调用"：域文件里出现一次调用（SetCors 之类）是正常的，
				// 出现字段/方法声明才是复制了实现。
				if (src.Contains("static readonly string SessionToken", StringComparison.Ordinal)
					|| src.Contains("bool IsAllowedOrigin(string? origin", StringComparison.Ordinal)
					|| src.Contains("bool HasValidSessionToken(HttpListenerRequest", StringComparison.Ordinal)
					|| src.Contains("bool IsValidSessionToken(string? supplied", StringComparison.Ordinal)
					|| src.Contains("bool ValidateOrigin(HttpListenerRequest request", StringComparison.Ordinal))
				{
					violations.Add($"{name} 里重复声明了 {marker} 的实现");
				}
			}
		}

		Assert.True(violations.Count == 0,
			"安全边界逻辑被复制到了域文件：" + string.Join("；", violations)
			+ "。令牌校验与 Origin 白名单必须只有一份，且位于宿主文件。");
	}

	/// <summary>
	/// 反向保护：守卫本身必须能咬人。
	///
	/// 若哪天有人把 <see cref="MaxLinesPerFile"/> 调到 100000，或把域清单清空，
	/// 上面三条断言都会变成恒真。这里断言门槛参数仍在"有意义"的范围内。
	/// </summary>
	[Fact]
	public void TheGuardsThemselves_AreNotHollow()
	{
		Assert.True(MaxLinesPerFile <= 800,
			$"行数上限被放宽到 {MaxLinesPerFile}，已超过 LocalApiService 拆分前体积的 1/6，门槛失去意义");
		Assert.True(LocalApiServiceSource.ExpectedFiles.Count >= 10,
			$"域清单只剩 {LocalApiServiceSource.ExpectedFiles.Count} 项，疑似拆分被回退");
		Assert.True(LocalApiServiceSource.ExpectedFiles.Contains("LocalApiService.Routing.cs", StringComparer.Ordinal),
			"路由表必须留在独立域文件中，否则 M1-1 的端点清单测试会失去稳定的读取目标");
	}
}
