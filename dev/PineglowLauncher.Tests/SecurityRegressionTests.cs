using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;

public sealed class SecurityRegressionTests
{
	[Fact]
	public void IsValidSessionToken_RejectsMissingWrongAndLengthMismatchedTokens()
	{
		const string expected = "0123456789abcdef";
		Assert.False(LocalApiService.IsValidSessionToken(null, expected));
		Assert.False(LocalApiService.IsValidSessionToken("short", expected));
		Assert.False(LocalApiService.IsValidSessionToken("0123456789abcdee", expected));
		Assert.True(LocalApiService.IsValidSessionToken(expected, expected));
	}

	[Fact]
	public void ExceedsJsonBodyLimit_RejectsBodiesOver32MiB()
	{
		Assert.False(LocalApiService.ExceedsJsonBodyLimit(32L * 1024 * 1024));
		Assert.True(LocalApiService.ExceedsJsonBodyLimit(32L * 1024 * 1024 + 1));
	}

	[Fact]
	public void CreateSafeTemporaryUploadPath_IgnoresClientTraversalAndAbsolutePaths()
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-upload-test");
		foreach (string name in new[] { "..\\..\\evil.zip", "C:\\evil.zip", "/tmp/evil.mrpack" })
		{
			string result = LocalApiService.CreateSafeTemporaryUploadPath(root, name);
			Assert.True(LocalApiService.IsPathWithinRoot(root, result));
			Assert.DoesNotContain("evil", Path.GetFileName(result), StringComparison.OrdinalIgnoreCase);
		}
	}

	[Theory]
	[InlineData(null, true)]
	[InlineData("http://127.0.0.1:48123", true)]
	[InlineData("http://localhost:48123", false)]
	[InlineData("https://127.0.0.1:48123", false)]
	[InlineData("http://127.0.0.1:9999", false)]
	[InlineData("https://evil.example", false)]
	public void IsAllowedOrigin_OnlyAcceptsExactLoopbackOrigin(string? origin, bool expected)
	{
		Assert.Equal(expected, LocalApiService.IsAllowedOrigin(origin, 48123));
	}

	[Fact]
	public void IsPathWithinRoot_RejectsSiblingPrefixAndTraversal()
	{
		string parent = Path.Combine(Path.GetTempPath(), "pineglow-security-tests");
		string root = Path.Combine(parent, "UI");
		Assert.True(LocalApiService.IsPathWithinRoot(root, Path.Combine(root, "js", "api.js")));
		Assert.False(LocalApiService.IsPathWithinRoot(root, Path.Combine(parent, "UI-other", "secret.txt")));
		Assert.False(LocalApiService.IsPathWithinRoot(root, Path.Combine(root, "..", "secret.txt")));
	}

	[Theory]
	[InlineData(null, false)]
	[InlineData("", false)]
	[InlineData("abc", false)]
	[InlineData("zz00000000000000000000000000000000000000000000000000000000000000", false)]
	[InlineData("0000000000000000000000000000000000000000000000000000000000000000", true)]
	public void IsValidSha256_RequiresExactly64HexCharacters(string? hash, bool expected)
	{
		Assert.Equal(expected, UpdateService.IsValidSha256(hash));
	}

	[Fact]
	public void Sha256Matches_FailsClosedAndAcceptsMatchingDigest()
	{
		string file = Path.GetTempFileName();
		try
		{
			File.WriteAllText(file, "pineglow", Encoding.UTF8);
			string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
			Assert.False(UpdateService.Sha256Matches(file, ""));
			Assert.False(UpdateService.Sha256Matches(file, new string('0', 64)));
			Assert.True(UpdateService.Sha256Matches(file, hash));
		}
		finally { File.Delete(file); }
	}

	[Fact]
	public void AppConfigSerialization_DoesNotPersistProxyPassword()
	{
		string json = JsonSerializer.Serialize(new AppConfig { ProxyPassword = "super-secret" });
		Assert.DoesNotContain("super-secret", json);
		Assert.DoesNotContain("ProxyPassword", json, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void AiProviderProfile_ApiKeyNotSerialized_RegressionLock()
	{
		// Phase 3B A-14：当前 GREEN（[JsonIgnore] 已存在）；保留为回归锁，防止后续 PR 删注解导致密钥泄露。
		string json = JsonSerializer.Serialize(new AiProviderProfile { ApiKey = "SUPER_SECRET_AI_KEY" });
		Assert.DoesNotContain("SUPER_SECRET_AI_KEY", json);
		Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
	}
}
