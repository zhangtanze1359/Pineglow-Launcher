using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR20 补测（缺口登记表 A19 的**可验证部分**）：**凭据持久化契约**。
///
/// 背景：`ConfigStore.SaveCore` 会写 `SecretStore`（真实凭据存储）。
/// 直接调用 `SaveAsync` 会写用户真实凭据 ✗，而 `SecretStore` **没有测试缝** ——
/// 给凭据存储加缝属于"需要确认的改动"，因此本轮**不碰凭据代码**，
/// 改用**源码级契约**把"该写哪些凭据"钉住（与 A6/A13 的静态守卫同一思路）。
///
/// **本类能证明的**：保存路径**仍然**按设计写入这 7 项、且旧键仍被清空、
/// 字面量键名与常量一致、常量值未变（防改名导致用户已存凭据成为孤儿）。
///
/// **本类不能证明的**（如实记录）：真实的**写入 → 读回**往返（那需要凭据存储的测试缝）。
/// </summary>
public class SecretPersistenceContractTests
{
	private readonly ITestOutputHelper _out;

	public SecretPersistenceContractTests(ITestOutputHelper output) => _out = output;

	private static string ConfigStoreSource
	{
		get
		{
			string dir = AppContext.BaseDirectory;
			for (int i = 0; i < 8 && dir != null; i++)
			{
				string candidate = Path.Combine(dir, "dev", "PineglowLauncher", "Pineglow.Launcher.Services", "ConfigStore.cs");
				if (File.Exists(candidate))
				{
					return File.ReadAllText(candidate);
				}

				dir = Path.GetDirectoryName(dir);
			}

			throw new FileNotFoundException("找不到 ConfigStore.cs");
		}
	}

	/// <summary>只取 <c>SaveCore</c> 的方法体，避免把别处（如 LoadAsync）的写入算进来。</summary>
	private static string SaveCoreBody
	{
		get
		{
			string src = ConfigStoreSource;
			int start = src.IndexOf("private static void SaveCore", StringComparison.Ordinal);
			Assert.True(start > 0, "应能定位 SaveCore");
			int end = src.IndexOf("private static void RotateBackupBeforeWrite", start, StringComparison.Ordinal);
			Assert.True(end > start, "应能定位 SaveCore 的结束");
			return src.Substring(start, end - start);
		}
	}

	/* ===================== ① 7 项写入一个都不能少 ===================== */

	[Fact]
	public void SaveCore_WritesEveryExpectedSecret()
	{
		string body = SaveCoreBody;
		var writes = Regex.Matches(body, @"SecretStore\.Save\(([^;]*?)\);")
			.Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())
			.ToList();

		_out.WriteLine("[A19] SaveCore 里的 SecretStore.Save 调用：");
		foreach (string w in writes)
		{
			_out.WriteLine("    " + w);
		}

		Assert.Equal(7, writes.Count);

		// 逐项：键表达式 → 值表达式（顺序即源码顺序）
		Assert.Contains("SecretStore.AccountToken(item.Id), value", writes);
		Assert.Contains("SecretStore.MicrosoftAccountToken(item.Id), string.Empty", writes);
		Assert.Contains("\"MicrosoftRefreshToken\", string.Empty", writes);
		Assert.Contains("\"CurseForgeApiKey\", config.CurseForgeApiKey", writes);
		Assert.Contains("SecretStore.ProxyPassword, config.ProxyPassword", writes);
		Assert.Contains("\"AiApiKey\", string.Empty", writes);
		Assert.Contains("AiKey(aiProvider.Id), aiProvider.ApiKey", writes);
	}

	/* ===================== ② 三个"旧键清空"必须保留（迁移语义） ===================== */

	[Fact]
	public void SaveCore_KeepsClearingTheThreeLegacyGlobalKeys()
	{
		string body = SaveCoreBody;
		string[] cleared = Regex.Matches(body, @"SecretStore\.Save\(([^;]*?), string\.Empty\);")
			.Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())
			.ToArray();

		_out.WriteLine("[A19] 被清空的旧键：" + string.Join(" | ", cleared));

		// 这三个是"凭据从全局键迁移到 per-account / per-provider 键"的**收尾动作**：
		// 少清一个，用户机器上就会**同时**留着旧键与新键（旧键可能被别处读走 → 行为不一致）。
		Assert.Contains("SecretStore.MicrosoftAccountToken(item.Id)", cleared);
		Assert.Contains("\"MicrosoftRefreshToken\"", cleared);
		Assert.Contains("\"AiApiKey\"", cleared);
	}

	/* ===================== ③ 字面量键名必须与常量一致（防改名漏改） ===================== */

	[Fact]
	public void LiteralKeysInConfigStore_MatchSecretStoreConstants()
	{
		string body = SaveCoreBody;

		// ConfigStore 用的是**字面量**；SecretStore 用的是**常量**。
		// 两者一旦不一致（例如常量改名而字面量没改），凭据就会写到一个没人读的键上 → 静默失效。
		var pairs = new (string Literal, string ConstantName, string Expected)[]
		{
			("\"MicrosoftRefreshToken\"", nameof(SecretStore.MicrosoftRefreshToken), SecretStore.MicrosoftRefreshToken),
			("\"CurseForgeApiKey\"", nameof(SecretStore.CurseForgeApiKey), SecretStore.CurseForgeApiKey),
			("\"AiApiKey\"", "AiApiKey", "AiApiKey"),
		};

		foreach (var (literal, constantName, expected) in pairs)
		{
			Assert.True(body.Contains(literal), $"SaveCore 应包含字面量 {literal}");
			_out.WriteLine($"[A19] {literal} == {expected}");

			if (constantName != "AiApiKey")
			{
				FieldInfo? field = typeof(SecretStore).GetField(constantName, BindingFlags.Public | BindingFlags.Static);
				Assert.NotNull(field);
				Assert.Equal(expected, (string?)field!.GetRawConstantValue());
			}
		}
	}

	/* ===================== ④ 键名常量本身不得随意改名（否则用户已存凭据成孤儿） ===================== */

	[Fact]
	public void SecretStoreKeyConstants_KeepTheirOnDiskNames()
	{
		// 这些字符串是**落盘格式**的一部分：改名会让用户机器上已保存的凭据变成孤儿
		// （新键为空、旧键还在但没人读）→ 表现为"登录态/Key 莫名丢失"。
		Assert.Equal("MicrosoftRefreshToken", SecretStore.MicrosoftRefreshToken);
		Assert.Equal("CurseForgeApiKey", SecretStore.CurseForgeApiKey);
		Assert.Equal("ProxyPassword", SecretStore.ProxyPassword);
		_out.WriteLine("[A19] 三个键名常量与落盘格式一致");
	}
}
