using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-11-06 未覆盖路径补测（批次 6）：<c>SecretStore</c> 的名字校验、命名约定、空值语义、
/// 以及后端覆盖的**异步流隔离**。
///
/// 为什么值得补：<c>SecretStore</c> 总覆盖率 88.9%，缺的正是这几条——而它们每一条都对应
/// 一次"用户凭据不见了"或"令牌写到了不该写的地方"。
///
/// <b>本文件同时记录一个无法验证的边界</b>（见 <see cref="FileBackend_HasNoInjectablePath_SoItsOwnBranchesRemainUnverified"/>）：
/// <c>RestrictedFileSecretBackend</c> 的路径来自 <c>PlatformPaths.GetConfigDirectory()</c>，
/// 没有构造参数也没有内部 setter，因此它自身的分支（权限设置、JSON 损坏恢复、原子写）
/// **在测试里无法定向验证**，只能靠"生产上真的读写过一次"来间接确认。
/// 这是设计遗留的测试缝缺口，不是本次改动引入。
/// </summary>
public class SecretStoreContractTests
{
	/* ===================== 名字校验 ===================== */

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("has space")]
	[InlineData("slash/name")]
	[InlineData("back\\slash")]
	[InlineData("dot.name")]
	[InlineData("colon:name")]
	[InlineData("tab\there")]
	public void SecretStore_RejectsNamesWithSeparatorsOrWhitespace(string bad)
	{
		// 名字会拼进凭据目标名（Windows 凭据管理器）或 JSON 键。允许 . / : \ 等字符，
		// 轻则被凭据管理器拒绝写入，重则把凭据写到预期之外的位置。
		Assert.Throws<ArgumentException>(() => SecretStore.Load(bad));
		Assert.Throws<ArgumentException>(() => SecretStore.Save(bad, "v"));
	}

	[Fact]
	public void SecretStore_AcceptsUnicodeLetters_BecauseValidationIsCharIsLetterOrDigit()
	{
		// **实测发现的真实口径**：ValidateName 用的是 char.IsLetterOrDigit，
		// 它按 Unicode 判定，因此中文/日文/西里尔字母都算合法名称。
		//
		// 产品当前不生成这类名字（全部是 ASCII 常量 + 账号 id），所以不构成缺陷；
		// 但这是一个**未被文档记录的行为**：如果有人用中文名写凭据，
		// 会走 Windows 凭据管理器（目标名支持 Unicode）或 secrets.json（JSON 键支持 Unicode），
		// 两条路都不会报错，也不会写错位置。
		//
		// 这里把它锁成"已知行为"，避免后人误以为中文名会被拒。
		SecretStore.Save("中文名", "值");
		Assert.Equal("值", SecretStore.Load("中文名"));
		SecretStore.Save("中文名", string.Empty);
	}

	[Theory]
	[InlineData("MicrosoftRefreshToken")]
	[InlineData("CurseForgeApiKey")]
	[InlineData("ProxyPassword")]
	[InlineData("MicrosoftRefreshToken_acc-1")]
	[InlineData("AccountToken_abc_123")]
	[InlineData("UPPER_and-lower-123")]
	public void SecretStore_AcceptsDocumentedNameShapes(string good)
	{
		SecretStore.Save(good, "value");
		Assert.Equal("value", SecretStore.Load(good));
		SecretStore.Save(good, string.Empty); // 清理，避免污染进程默认内存后端
	}

	[Fact]
	public void SecretStore_AccountTokenNaming_IsStable()
	{
		// 命名约定一旦改动，升级用户的所有凭据都会"读不到"，表现为"莫名其妙要重新登录"。
		Assert.Equal("MicrosoftRefreshToken_acc-1", SecretStore.MicrosoftAccountToken("acc-1"));
		Assert.Equal("AccountToken_acc-1", SecretStore.AccountToken("acc-1"));
		Assert.Equal("MicrosoftRefreshToken", SecretStore.MicrosoftRefreshToken);
		Assert.Equal("CurseForgeApiKey", SecretStore.CurseForgeApiKey);
		Assert.Equal("ProxyPassword", SecretStore.ProxyPassword);
	}

	/* ===================== 空值语义 ===================== */

	[Fact]
	public void SecretStore_SaveEmptyOrNull_DeletesInsteadOfWritingEmpty()
	{
		SecretStore.Save("ContractToken1", "secret");
		Assert.Equal("secret", SecretStore.Load("ContractToken1"));

		SecretStore.Save("ContractToken1", string.Empty);
		Assert.True(string.IsNullOrEmpty(SecretStore.Load("ContractToken1")), "空串应删除凭据");

		SecretStore.Save("ContractToken2", "secret");
		SecretStore.Save("ContractToken2", null);
		Assert.True(string.IsNullOrEmpty(SecretStore.Load("ContractToken2")), "null 应删除凭据");
	}

	[Fact]
	public void SecretStore_LoadMissing_ReturnsEmptyString_NotNull()
	{
		Assert.True(string.IsNullOrEmpty(SecretStore.Load("ContractNeverWritten")), "不存在应返回空串而不是 null");
	}

	/* ===================== 后端覆盖的隔离语义 ===================== */

	[Fact]
	public async Task SecretStore_OverrideIsAsyncLocal_SoParallelFlowsDoNotShareBackend()
	{
		// R0 修复的核心机制：覆盖必须按异步流隔离。若用普通静态字段，
		// xUnit 并行调度下两个用例会互相串后端，甚至串到真实凭据管理器。
		var a = new InMemorySecretStoreBackend("a");
		var b = new InMemorySecretStoreBackend("b");

		async Task<string> RunWith(ISecretStoreBackend backend, string value)
		{
			using var scope = SecretStore.OverrideBackendForTest(backend);
			SecretStore.Save("SharedKey", value);
			await Task.Yield();
			return SecretStore.Load("SharedKey");
		}

		string[] results = await Task.WhenAll(
			RunWith(a, "from-A"),
			RunWith(b, "from-B"));

		Assert.Equal(new[] { "from-A", "from-B" }, results);
		Assert.Equal("from-A", a.Peek("SharedKey"));
		Assert.Equal("from-B", b.Peek("SharedKey"));
	}

	[Fact]
	public void SecretStore_ScopeDispose_RestoresPreviousBackend()
	{
		var outer = new InMemorySecretStoreBackend("outer");
		var inner = new InMemorySecretStoreBackend("inner");

		using (SecretStore.OverrideBackendForTest(outer))
		{
			SecretStore.Save("K1", "outer-value");

			using (SecretStore.OverrideBackendForTest(inner))
			{
				SecretStore.Save("K1", "inner-value");
				Assert.Equal("inner-value", SecretStore.Load("K1"));
			}

			// 内层释放后必须回到外层，而不是掉回进程默认后端
			Assert.Equal("outer-value", SecretStore.Load("K1"));
			// 内层后端应保留它自己那份值（互不覆盖）
			Assert.Equal("inner-value", inner.Peek("K1"));
		}
	}

	[Fact]
	public void SecretStore_OverriddenScope_DoesNotTouchProductionBackend()
	{
		// "生产后端调用次数 = 0"是 R0 的验收口径。作用域内做完整读/写/删，计数必须不动。
		SecretStore.ResetProductionBackendCallCountForTest();
		var memory = new InMemorySecretStoreBackend("counter-check");
		using (SecretStore.OverrideBackendForTest(memory))
		{
			SecretStore.Save("K", "v");
			SecretStore.Load("K");
			SecretStore.Save("K", string.Empty);
		}

		Assert.Equal(0, SecretStore.ProductionBackendCallCount);
		// 正向证据：这些操作确实被内存后端接住了（不是"没执行所以没计数"）
		Assert.Equal(1, memory.CountOf("write"));
		Assert.Equal(1, memory.CountOf("read"));
		Assert.Equal(1, memory.CountOf("delete"));
		Assert.Contains("K", memory.DeletedNames);
	}

	[Fact]
	public void SecretStore_BackendThrows_PropagatesInsteadOfPretendingSuccess()
	{
		// 凭据写入失败必须让上层知道（提示用户"保存失败"），
		// 不能吞掉异常假装成功——那会让用户以为令牌已保存，下次启动又要重新登录。
		using var scope = SecretStore.OverrideBackendForTest(new ThrowingSecretStoreBackend());
		Assert.Throws<InvalidOperationException>(() => SecretStore.Save("K", "v"));
		Assert.Throws<InvalidOperationException>(() => SecretStore.Load("K"));
	}

	/* ===================== 无法验证的边界（如实记录） ===================== */

	[Fact]
	public void FileBackend_HasNoInjectablePath_SoItsOwnBranchesRemainUnverified()
	{
		// 本条不是"验证功能"，而是把**验证能力的缺口**变成可执行的记录。
		//
		// 事实：RestrictedFileSecretBackend 只有一个无参构造函数，
		// 路径写死为 Path.Combine(PlatformPaths.GetConfigDirectory(), "secrets.json")。
		// 因此下面这些分支**没有任何办法在测试里定向触发**：
		//   * File.SetUnixFileMode(0600) 的权限收紧（仅非 Windows）
		//   * JSON 损坏时 catch(JsonException) 退回空字典
		//   * File.WriteAllText 覆盖写（非原子，断电可能写坏）
		//   * PlatformNotSupportedException 的兜底
		//
		// 唯一间接证据是生产/便携模式真的跑过一次读写。这条测试锁住"缺口存在"这件事：
		// 一旦有人给它加上可注入路径（正确的修法），这里会因为构造函数重载出现而需要更新。
		var ctors = typeof(RestrictedFileSecretBackend).GetConstructors(
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

		Assert.True(ctors.Length >= 1, "至少应有一个构造函数");
		Assert.All(ctors, c => Assert.Empty(c.GetParameters()));
	}

	private sealed class ThrowingSecretStoreBackend : ISecretStoreBackend
	{
		public string? Read(string name) => throw new InvalidOperationException("合成后端故障（read）");
		public void Write(string name, string value) => throw new InvalidOperationException("合成后端故障（write）");
		public void Delete(string name) => throw new InvalidOperationException("合成后端故障（delete）");
	}
}
