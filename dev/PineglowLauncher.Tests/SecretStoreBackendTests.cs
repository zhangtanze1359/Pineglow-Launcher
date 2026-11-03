using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R0（最终验收阻断项 F-02）：**测试进程默认凭据后端 = 内存后端**。
///
/// 为什么要在 <c>[ModuleInitializer]</c> 里做，而不是每个用例自己加作用域：
///   * 独立验收已证明"忘记隔离"的代价是**真实凭据被删**（20 次 delete-intent）；
///   * 单靠"记得写 using"是**约定**，不是**保证**。把进程默认后端换掉之后，
///     任何现有或将来的用例（含只写了一半的）都不可能碰到真实凭据 —— fail-safe。
///
/// 生产不受影响：<see cref="SecretStore.InstallProductionBackendForTest"/> 是 <c>internal</c>，
/// 只有测试程序集（<c>InternalsVisibleTo</c>）能调用；GUI / VerifyHost 走的是原生后端。
/// </summary>
internal static class TestSecretStoreIsolation
{
	/// <summary>测试进程默认后端（只保存合成值）。</summary>
	internal static readonly InMemorySecretStoreBackend ProcessDefault = new InMemorySecretStoreBackend("test-process-default");

	[ModuleInitializer]
	internal static void Install()
	{
		SecretStore.InstallProductionBackendForTest(ProcessDefault);
	}
}

/// <summary>用于验证"后端异常不被吞掉"的测试替身。</summary>
internal sealed class ThrowingSecretStoreBackend : ISecretStoreBackend
{
	public string Message { get; set; } = "合成后端故障";

	public string? Read(string name) => throw new InvalidOperationException(Message + "（read）");

	public void Write(string name, string value) => throw new InvalidOperationException(Message + "（write）");

	public void Delete(string name) => throw new InvalidOperationException(Message + "（delete）");
}

/// <summary>
/// R0：凭据后端的注入、作用域与"原生不可达"证明。
///
/// 覆盖交付要求的四件事：合成**读取/写入/删除**、**异常**路径、**scope 恢复**、
/// 以及 <b>native API 调用次数 0</b>（不是"secrets.json 没变"）。
/// </summary>
public class SecretStoreBackendTests
{
	private readonly ITestOutputHelper _out;

	public SecretStoreBackendTests(ITestOutputHelper output) => _out = output;

	/* ===================== ① 读：不存在 → 空串（与修复前语义一致） ===================== */

	[Fact]
	public void Load_MissingName_ReturnsEmptyString_AndGoesThroughTheInjectedBackend()
	{
		var backend = new InMemorySecretStoreBackend("r0-read-missing");
		using IDisposable scope = SecretStore.OverrideBackendForTest(backend);

		string value = SecretStore.Load(SecretStore.ProxyPassword);

		Assert.Equal(string.Empty, value);
		Assert.Contains("read:" + SecretStore.ProxyPassword, backend.Operations);
	}

	/* ===================== ② 写 → 读 往返；键名必须与生产完全一致 ===================== */

	[Fact]
	public void WriteThenRead_RoundTrips_WithTheSameKeyNamesAsProduction()
	{
		var backend = new InMemorySecretStoreBackend("r0-roundtrip");
		using IDisposable scope = SecretStore.OverrideBackendForTest(backend);

		SecretStore.Save("CurseForgeApiKey", "synthetic-cf-key");
		SecretStore.Save(SecretStore.ProxyPassword, "synthetic-proxy");
		SecretStore.Save(SecretStore.AccountToken("acct-1"), "synthetic-account-token");

		Assert.Equal("synthetic-cf-key", SecretStore.Load("CurseForgeApiKey"));
		Assert.Equal("synthetic-proxy", SecretStore.Load(SecretStore.ProxyPassword));
		Assert.Equal("synthetic-account-token", SecretStore.Load(SecretStore.AccountToken("acct-1")));
		Assert.Equal("synthetic-account-token", backend.Peek(SecretStore.AccountToken("acct-1")));

		// 常量值本身是**落盘/落地格式**的一部分：改名会让用户已存凭据变孤儿。
		Assert.Equal("MicrosoftRefreshToken", SecretStore.MicrosoftRefreshToken);
		Assert.Equal("CurseForgeApiKey", SecretStore.CurseForgeApiKey);
		Assert.Equal("ProxyPassword", SecretStore.ProxyPassword);

		_out.WriteLine("[R0] 后端收到的键名：" + string.Join(" | ", backend.Operations));
	}

	/* ===================== ③ 删：空值/null → Delete（与修复前语义一致） ===================== */

	[Fact]
	public void Save_EmptyOrNull_DeletesThroughTheInjectedBackend()
	{
		var backend = new InMemorySecretStoreBackend("r0-delete");
		using IDisposable scope = SecretStore.OverrideBackendForTest(backend);

		SecretStore.Save("MicrosoftRefreshToken", "synthetic-token");
		Assert.True(backend.Contains("MicrosoftRefreshToken"));

		SecretStore.Save("MicrosoftRefreshToken", string.Empty);
		Assert.False(backend.Contains("MicrosoftRefreshToken"));
		Assert.Equal(string.Empty, SecretStore.Load("MicrosoftRefreshToken"));

		SecretStore.Save("AiApiKey", "synthetic-ai");
		SecretStore.Save("AiApiKey", null);
		Assert.False(backend.Contains("AiApiKey"));

		Assert.Equal(2, backend.CountOf("delete"));
		Assert.Contains("delete:MicrosoftRefreshToken", backend.DeletedNames.Select(x => "delete:" + x));
	}

	/* ===================== ④ 异常：不得被后端层吞掉 ===================== */

	[Fact]
	public void BackendFailures_Propagate_InsteadOfBeingSwallowed()
	{
		var throwing = new ThrowingSecretStoreBackend();
		using IDisposable scope = SecretStore.OverrideBackendForTest(throwing);

		InvalidOperationException readEx = Assert.Throws<InvalidOperationException>(() => SecretStore.Load("CurseForgeApiKey"));
		InvalidOperationException writeEx = Assert.Throws<InvalidOperationException>(() => SecretStore.Save("CurseForgeApiKey", "x"));
		InvalidOperationException deleteEx = Assert.Throws<InvalidOperationException>(() => SecretStore.Save("CurseForgeApiKey", string.Empty));

		Assert.Contains("read", readEx.Message);
		Assert.Contains("write", writeEx.Message);
		Assert.Contains("delete", deleteEx.Message);
		_out.WriteLine("[R0] 三类后端异常都如实向上抛出：" + readEx.Message + " / " + writeEx.Message + " / " + deleteEx.Message);
	}

	/* ===================== ⑤ 作用域：嵌套与还原 ===================== */

	[Fact]
	public void OverrideScope_NestsAndRestores_ThePreviousBackend()
	{
		var outer = new InMemorySecretStoreBackend("r0-scope-outer");
		var inner = new InMemorySecretStoreBackend("r0-scope-inner");

		using (SecretStore.OverrideBackendForTest(outer))
		{
			Assert.Same(outer, SecretStore.CurrentBackend);
			Assert.True(SecretStore.IsBackendOverriddenForTest);
			SecretStore.Save(SecretStore.ProxyPassword, "outer-value");

			using (SecretStore.OverrideBackendForTest(inner))
			{
				Assert.Same(inner, SecretStore.CurrentBackend);
				// 内层看不到外层的值（后端是分开的内存实例，不是共享静态字典）
				Assert.Equal(string.Empty, SecretStore.Load(SecretStore.ProxyPassword));
				SecretStore.Save(SecretStore.ProxyPassword, "inner-value");
			}

			// 释放内层 → 还原到外层，且外层的值仍在
			Assert.Same(outer, SecretStore.CurrentBackend);
			Assert.Equal("outer-value", SecretStore.Load(SecretStore.ProxyPassword));
		}

		// 释放外层 → 回到测试进程默认（内存）后端，而不是原生后端
		Assert.False(SecretStore.IsBackendOverriddenForTest);
		Assert.Same(TestSecretStoreIsolation.ProcessDefault, SecretStore.CurrentBackend);

		// 作用域结束后不得把合成值漏进进程默认后端
		Assert.NotEqual("outer-value", SecretStore.Load(SecretStore.ProxyPassword));
		Assert.NotEqual("inner-value", SecretStore.Load(SecretStore.ProxyPassword));
	}

	/* ===================== ⑥ native 调用次数必须为 0 ===================== */

	[Fact]
	public void ProductionBackend_IsNeverEntered_WhenTheIsolatedBackendIsArmed()
	{
		var backend = new InMemorySecretStoreBackend("r0-native-zero");
		using IDisposable scope = SecretStore.OverrideBackendForTest(backend);

		long before = SecretStore.ProductionBackendCallCount;
		SecretStore.Save("CurseForgeApiKey", "synthetic");
		SecretStore.Load("CurseForgeApiKey");
		SecretStore.Save("CurseForgeApiKey", string.Empty);
		SecretStore.Save(SecretStore.AccountToken("acct-1"), "synthetic");
		long after = SecretStore.ProductionBackendCallCount;

		Assert.Equal(0, after - before);
		// 反向证明：这些调用确实被内存后端受理了（否则"0 次"可能只是"什么都没做"）
		Assert.Equal(4, backend.Operations.Count);
		_out.WriteLine($"[R0] 生产后端 delta={after - before}；内存后端受理 {backend.Operations.Count} 次操作");
	}

	/* ===================== ⑦ 进程默认后端必须是测试用内存实现 ===================== */

	[Fact]
	public void ProcessDefaultBackend_IsTheInMemoryTestBackend_NotTheNativeOne()
	{
		Assert.False(SecretStore.IsBackendOverriddenForTest);
		Assert.IsType<InMemorySecretStoreBackend>(SecretStore.CurrentBackend);
		Assert.False(SecretStore.CurrentBackend is WindowsCredentialSecretBackend);
	}

	/* ===================== ⑧ 名称校验在进入后端之前完成 ===================== */

	[Fact]
	public void InvalidNames_AreRejectedBeforeReachingAnyBackend()
	{
		var backend = new InMemorySecretStoreBackend("r0-name-validation");
		using IDisposable scope = SecretStore.OverrideBackendForTest(backend);

		Assert.Throws<ArgumentException>(() => SecretStore.Load("bad name"));
		Assert.Throws<ArgumentException>(() => SecretStore.Save("bad/name", "x"));
		Assert.Empty(backend.Operations);
	}
}
