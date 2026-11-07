using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

public class InstanceIsolationServiceTests
{
	private static string BuildPath(params string[] parts)
	{
		// 用临时根目录构造绝对路径，确保各平台 DirectoryInfo.Parent 行为一致
		return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-mcroot-test", System.IO.Path.Combine(parts));
	}

	[Fact]
	public void GetMinecraftRoot_FindsInstancesParent()
	{
		// <temp>/.../.minecraft/instances/my-pack → 根为 <temp>/.../.minecraft
		string input = BuildPath(".minecraft", "instances", "my-pack");
		string root = InstanceIsolationService.GetMinecraftRoot(input);

		Assert.Equal(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-mcroot-test", ".minecraft"), root);
	}

	[Fact]
	public void GetMinecraftRoot_NoInstances_ReturnsInput()
	{
		string input = BuildPath(".minecraft");
		Assert.Equal(input, InstanceIsolationService.GetMinecraftRoot(input));
	}

	[Fact]
	public void GetMinecraftRoot_NestedInstances_FindsFirstParent()
	{
		// 从更深层实例目录也能找到根
		string input = BuildPath(".minecraft", "instances", "pack", "mods");
		string root = InstanceIsolationService.GetMinecraftRoot(input);

		Assert.Equal(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-mcroot-test", ".minecraft"), root);
	}

	[Theory]
	[InlineData("My Pack", "My Pack")]
	[InlineData("", "fallback-id")]                // 空名回退
	[InlineData("   ", "fallback-id")]             // 全空白回退
	public void GetAvailableInstanceDirectory_SanitizesName(string name, string? expectedDir)
	{
		// 用临时目录做根，避免污染真实 .minecraft
		string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-test-" + Guid.NewGuid().ToString("N"));
		try
		{
			string dir = InstanceIsolationService.GetAvailableInstanceDirectory(tempRoot, name, "fallback-id");
			// 目录名必须可用（无路径分隔符、无平台非法字符）
			Assert.DoesNotContain(System.IO.Path.DirectorySeparatorChar, System.IO.Path.GetFileName(dir));
			foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
			{
				Assert.DoesNotContain(invalid, System.IO.Path.GetFileName(dir));
			}
			if (expectedDir != null)
			{
				Assert.Equal(expectedDir, System.IO.Path.GetFileName(dir));
			}
		}
		finally
		{
			if (System.IO.Directory.Exists(tempRoot))
				System.IO.Directory.Delete(tempRoot, recursive: true);
		}
	}

	[Fact]
	public void GetAvailableInstanceDirectory_SanitizesIllegalChars()
	{
		// 跨平台确定性：非法字符随平台不同，只验证不抛异常且目录可创建
		string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-test-" + Guid.NewGuid().ToString("N"));
		try
		{
			string dir = InstanceIsolationService.GetAvailableInstanceDirectory(tempRoot, "Bad*Name?", "fallback-id");
			string name = System.IO.Path.GetFileName(dir);

			// 通用契约：平台非法字符必须被替换成下划线，而不是抛异常或创建失败
			foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
			{
				Assert.DoesNotContain(invalid, name);
			}

			if (OperatingSystem.IsWindows())
			{
				// Windows 的非法集含 '*' 与 '?'，两者都变成下划线
				Assert.DoesNotContain("*", name);
				Assert.DoesNotContain("?", name);
				Assert.Equal("Bad_Name_", name);
			}
			else
			{
				// POSIX 的 Path.GetInvalidFileNameChars() 只有 '/' 与 '\0'：
				// '*' 与 '?' 在 Linux/macOS 上是**合法文件名字符**，产品（InstanceIsolationService
				// 直接按 GetInvalidFileNameChars 过滤）因此原样保留。若这里也被替换成下划线，
				// 说明产品的过滤表被硬编码成了 Windows 规则，反而会误改合法名字。
				Assert.Equal("Bad*Name?", name);
				Assert.Equal(System.IO.Path.Combine(tempRoot, "instances", "Bad*Name?"), dir);
			}
		}
		finally
		{
			if (System.IO.Directory.Exists(tempRoot))
				System.IO.Directory.Delete(tempRoot, recursive: true);
		}
	}

	[Fact]
	public void GetAvailableInstanceDirectory_AppendsNumber_WhenExists()
	{
		string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-test-" + Guid.NewGuid().ToString("N"));
		try
		{
			string first = InstanceIsolationService.GetAvailableInstanceDirectory(tempRoot, "Pack", "f");
			System.IO.Directory.CreateDirectory(first);
			string second = InstanceIsolationService.GetAvailableInstanceDirectory(tempRoot, "Pack", "f");

			Assert.Equal("Pack (1)", System.IO.Path.GetFileName(second));
		}
		finally
		{
			if (System.IO.Directory.Exists(tempRoot))
				System.IO.Directory.Delete(tempRoot, recursive: true);
		}
	}
}

public class JsonUtilTests
{
	[Fact]
	public void GetString_MissingProperty_ReturnsNull()
	{
		using var doc = System.Text.Json.JsonDocument.Parse("{\"name\":\"x\"}");
		Assert.Null(JsonUtil.GetString(doc.RootElement, "missing"));
		Assert.Equal("x", JsonUtil.GetString(doc.RootElement, "name"));
	}

	[Fact]
	public void GetString_NullAndEmpty_ReturnEmptyString()
	{
		using var doc = System.Text.Json.JsonDocument.Parse("{\"a\":null,\"b\":\"\"}");
		// null 值（ValueKind=Null）与空字符串均返回空串
		Assert.Equal(string.Empty, JsonUtil.GetString(doc.RootElement, "a"));
		Assert.Equal(string.Empty, JsonUtil.GetString(doc.RootElement, "b"));
	}

	[Fact]
	public void GetString_NumberValue_ToString()
	{
		using var doc = System.Text.Json.JsonDocument.Parse("{\"majorVersion\":21}");
		Assert.Equal("21", JsonUtil.GetString(doc.RootElement, "majorVersion"));
	}

	[Fact]
	public void BuildReplacements_PrefixesDollarBraces()
	{
		var result = JsonUtil.BuildReplacements(new System.Collections.Generic.Dictionary<string, string> { ["auth_player_name"] = "Steve" });
		Assert.Equal("Steve", result["${auth_player_name}"]);
	}

	[Fact]
	public void ReplacePlaceholders_ReplacesAll()
	{
		var rep = new System.Collections.Generic.Dictionary<string, string> { ["${name}"] = "Steve", ["${ver}"] = "1.21" };
		Assert.Equal("Steve / 1.21", JsonUtil.ReplacePlaceholders("${name} / ${ver}", rep));
	}
}
