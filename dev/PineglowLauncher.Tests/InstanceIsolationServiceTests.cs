using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

public class InstanceIsolationServiceTests
{
	[Fact]
	public void GetMinecraftRoot_FindsInstancesParent()
	{
		// Windows 路径：.../.minecraft/instances/my-pack → 根为 .../.minecraft
		string root = InstanceIsolationService.GetMinecraftRoot(@"C:\games\.minecraft\instances\my-pack");

		Assert.Equal(@"C:\games\.minecraft", root);
	}

	[Fact]
	public void GetMinecraftRoot_NoInstances_ReturnsInput()
	{
		string input = @"C:\games\.minecraft";
		Assert.Equal(input, InstanceIsolationService.GetMinecraftRoot(input));
	}

	[Fact]
	public void GetMinecraftRoot_NestedInstances_FindsFirstParent()
	{
		// 从更深层实例目录也能找到根
		string root = InstanceIsolationService.GetMinecraftRoot(@"C:\games\.minecraft\instances\pack\mods");

		Assert.Equal(@"C:\games\.minecraft", root);
	}

	[Theory]
	[InlineData("My Pack", "My Pack")]
	[InlineData("Bad:Name/Path", "Bad_Name_Path")] // 非法字符替换为 _
	[InlineData("", "fallback-id")]                // 空名回退
	[InlineData("   ", "fallback-id")]             // 全空白回退
	public void GetAvailableInstanceDirectory_SanitizesName(string name, string expectedDir)
	{
		// 用临时目录做根，避免污染真实 .minecraft
		string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-test-" + Guid.NewGuid().ToString("N"));
		try
		{
			string dir = InstanceIsolationService.GetAvailableInstanceDirectory(tempRoot, name, "fallback-id");
			Assert.True(System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(dir)));
			Assert.Equal(expectedDir, System.IO.Path.GetFileName(dir));
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
