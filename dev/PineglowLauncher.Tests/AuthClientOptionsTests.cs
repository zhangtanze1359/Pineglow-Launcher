using System;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// <see cref="AuthClientOptions"/> 的直测（此前 0/24 行，从未被任何测试提到）。
///
/// 这个类只有 24 行，但它决定"用哪个 Microsoft OAuth Client ID 去登录"。
/// 判错的两个方向都是真实故障：
///   * 用了别人的/错误的 Client ID → 微软直接拒绝授权（invalid_client），用户完全登不进去；
///   * 把非法值当成合法 GUID 返回 → 请求发出去了才失败，报错信息与真正原因无关，极难排查。
///
/// 它的优先级是"账号里已有的 → 环境变量 → 内置常量"，这三层顺序是产品决策，
/// 之前没有任何地方把它写下来，改顺序不会被任何测试挡住。
/// </summary>
public class AuthClientOptionsTests
{
	private const string BuiltIn = "60934999-9c8b-4731-8b78-a98d010edcde";

	/// <summary>
	/// 环境变量是**进程级状态**，测试之间会互相污染。每个用例前后都恢复原值。
	/// </summary>
	private sealed class EnvScope : IDisposable
	{
		private readonly string? _original;

		public EnvScope(string? value)
		{
			_original = Environment.GetEnvironmentVariable(AuthClientOptions.ClientIdEnvironmentVariable);
			Environment.SetEnvironmentVariable(AuthClientOptions.ClientIdEnvironmentVariable, value);
		}

		public void Dispose()
			=> Environment.SetEnvironmentVariable(AuthClientOptions.ClientIdEnvironmentVariable, _original);
	}

	[Fact]
	public void Resolve_BuiltInIsAWellFormedGuid()
	{
		// 内置常量是发版前必须存在的东西：它是空白账号能登录的唯一来源。
		Assert.True(Guid.TryParse(AuthClientOptions.BuiltInMicrosoftClientId, out _),
			"内置 Client ID 必须是合法 GUID，否则所有新用户都无法登录微软账号");
		Assert.Equal(BuiltIn, AuthClientOptions.BuiltInMicrosoftClientId);
	}

	[Fact]
	public void Resolve_PrefersAccountClientIdOverEverything()
	{
		const string fromAccount = "11111111-2222-3333-4444-555555555555";
		using EnvScope env = new EnvScope("99999999-8888-7777-6666-555555555555");

		// 账号里记着 Client ID = 该账号是用哪个应用授权的，刷新令牌只能配同一个应用。
		// 这里若优先环境变量，会导致"用 A 应用换 B 应用的 refresh token"，必然失败。
		Assert.Equal(fromAccount, AuthClientOptions.ResolveMicrosoftClientId(fromAccount));
	}

	[Fact]
	public void Resolve_UsesEnvironmentVariableWhenAccountHasNone()
	{
		const string fromEnv = "99999999-8888-7777-6666-555555555555";
		using EnvScope env = new EnvScope(fromEnv);

		Assert.Equal(fromEnv, AuthClientOptions.ResolveMicrosoftClientId(null));
		Assert.Equal(fromEnv, AuthClientOptions.ResolveMicrosoftClientId("   "));
	}

	[Fact]
	public void Resolve_FallsBackToBuiltInWhenNeitherAccountNorEnvIsUsable()
	{
		using EnvScope env = new EnvScope(null);

		Assert.Equal(BuiltIn, AuthClientOptions.ResolveMicrosoftClientId(null));
		Assert.Equal(BuiltIn, AuthClientOptions.ResolveMicrosoftClientId("not-a-guid"));
	}

	[Fact]
	public void Resolve_IgnoresMalformedEnvValue()
	{
		// 用户手输环境变量时常见的错误：带空格、带引号、粘贴成了 URL。
		// 这些值必须被忽略并退回内置常量，而不是原样拿去请求（那样只会得到 invalid_client）。
		using EnvScope env = new EnvScope("  \"60934999-9c8b-4731-8b78-a98d010edcde\"  ");

		Assert.Equal(BuiltIn, AuthClientOptions.ResolveMicrosoftClientId(null));
	}

	[Fact]
	public void Resolve_IgnoresMalformedAccountClientIdAndKeepsFallingBack()
	{
		using EnvScope env = new EnvScope(null);

		// 配置文件被手改成非 GUID（例如清空成 "null" 字符串）时不能直接返回它。
		Assert.Equal(BuiltIn, AuthClientOptions.ResolveMicrosoftClientId("null"));
		Assert.Equal(BuiltIn, AuthClientOptions.ResolveMicrosoftClientId("{not-a-guid}"));
	}

	[Fact]
	public void Resolve_TrimsSurroundingWhitespaceFromValidValues()
	{
		const string fromAccount = "11111111-2222-3333-4444-555555555555";
		using EnvScope env = new EnvScope(null);

		// Guid.TryParse 容忍两侧空白，但拼进 URL 后 " xxx " 会变成 %20xxx%20。
		Assert.Equal(fromAccount, AuthClientOptions.ResolveMicrosoftClientId("  " + fromAccount + "  "));
	}

	[Fact]
	public void HasUsable_MatchesWhetherResolveReturnsSomething()
	{
		using EnvScope env = new EnvScope(null);

		// HasUsable 是 UI 判断"能不能点微软登录"的开关。它与 Resolve 必须永远一致：
		// 若 HasUsable 说 true 而 Resolve 给空串，用户点了登录就发出一个 client_id= 的空请求。
		Assert.True(AuthClientOptions.HasUsableMicrosoftClientId(null));
		Assert.True(AuthClientOptions.HasUsableMicrosoftClientId("11111111-2222-3333-4444-555555555555"));
		Assert.False(string.IsNullOrWhiteSpace(AuthClientOptions.ResolveMicrosoftClientId(null)));
	}

	[Fact]
	public void EnvironmentVariableName_IsStable()
	{
		// 环境变量名是**对外契约**：文档/CI/用户脚本都按这个名字设置。
		// 改名等于让所有已配置的用户静默退回内置 Client ID。
		Assert.Equal("MCMINI_MS_CLIENT_ID", AuthClientOptions.ClientIdEnvironmentVariable);
	}
}
