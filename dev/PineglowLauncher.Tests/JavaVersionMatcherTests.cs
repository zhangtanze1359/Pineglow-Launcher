using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

public class JavaVersionMatcherTests
{
	[Theory]
	[InlineData("1.8.9", 8)]
	[InlineData("1.12.2", 8)]
	[InlineData("1.16.5", 8)]
	[InlineData("1.17.1", 16)]
	[InlineData("1.18.2", 17)]
	[InlineData("1.20.1", 17)]
	[InlineData("1.20.5", 21)]
	[InlineData("1.21.1", 21)]
	[InlineData("24w14a", 21)]      // 快照
	[InlineData("", 17)]            // 空默认 17
	[InlineData("bad-version", 17)] // 无法解析默认 17
	public void GetRequiredJavaMajor_MapsKnownVersions(string version, int expected)
	{
		Assert.Equal(expected, JavaVersionMatcher.GetRequiredJavaMajor(version));
	}

	[Fact]
	public void GetRequiredJavaMajor_NewerMajor_GreaterThanTwentyFive()
	{
		// 26+ 需要 Java 25
		Assert.Equal(25, JavaVersionMatcher.GetRequiredJavaMajor("26.1"));
	}

	[Theory]
	[InlineData(21, 21, true)]
	[InlineData(17, 21, false)]
	[InlineData(8, 8, true)]
	[InlineData(17, 16, false)]
	public void IsCompatible_ExactMatchOnly(int actual, int required, bool expected)
	{
		Assert.Equal(expected, JavaVersionMatcher.IsCompatible(actual, required));
	}

	[Theory]
	[InlineData(null, 17)]
	[InlineData("", 17)]
	[InlineData(" ", 17)]
	public void GetRequiredJavaMajor_NullOrEmpty_DefaultsTo17(string? version, int expected)
	{
		Assert.Equal(expected, JavaVersionMatcher.GetRequiredJavaMajor(version));
	}
}
public class JavaVersionMetadataTests
{
	private static string NewRoot()
	{
		// 每次用例独立临时根，完整模拟 .minecraft/versions/ 目录结构
		return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-java-meta-" + System.Guid.NewGuid().ToString("N"));
	}

	private static void WriteVersionJson(string root, string id, string json)
	{
		string dir = System.IO.Path.Combine(root, "versions", id);
		System.IO.Directory.CreateDirectory(dir);
		System.IO.File.WriteAllText(System.IO.Path.Combine(dir, id + ".json"), json);
	}

	[Fact]
	public void GetRequiredJavaMajor_ReadsMajorFromVersionMetadata()
	{
		string root = NewRoot();
		try
		{
			// 元数据声明 Java 8；若忽略元数据，启发式对任意名称默认只到 17 → 二者不同，可拦截“跳过读取”变异
			WriteVersionJson(root, "weird-pack", """{"id":"weird-pack","javaVersion":{"majorVersion":8}}""");

			Assert.Equal(8, JavaVersionMatcher.GetRequiredJavaMajor("weird-pack", root));
		}
		finally
		{
			if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void GetRequiredJavaMajor_FollowsInheritsFromChain()
	{
		string root = NewRoot();
		try
		{
			WriteVersionJson(root, "child", """{"id":"child","inheritsFrom":"parent"}""");
			WriteVersionJson(root, "parent", """{"id":"parent","javaVersion":{"majorVersion":8}}""");

			// 子版本自身 JSON 无 javaVersion，须沿链读到 8（启发式对 "child" 默认 17）
			Assert.Equal(8, JavaVersionMatcher.GetRequiredJavaMajor("child", root));
		}
		finally
		{
			if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void GetRequiredJavaMajor_FallsBackToHeuristic_WhenMetadataMissing()
	{
		string root = NewRoot();
		try
		{
			// versions 目录存在但 JSON 不存在
			Assert.Equal(17, JavaVersionMatcher.GetRequiredJavaMajor("1.20.1", root));
			Assert.Equal(21, JavaVersionMatcher.GetRequiredJavaMajor("1.21.1", root));

			// JSON 存在但没有 javaVersion 字段 → 回退启发式
			WriteVersionJson(root, "no-java", """{"id":"no-java"}""");
			Assert.Equal(17, JavaVersionMatcher.GetRequiredJavaMajor("no-java", root));
		}
		finally
		{
			if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void GetRequiredJavaMajor_FallsBackToHeuristic_WhenMetadataInvalid()
	{
		string root = NewRoot();
		try
		{
			// javaVersion 是字符串而非对象 → 忽略并回退
			WriteVersionJson(root, "bad-java", """{"id":"bad-java","javaVersion":"8"}""");
			Assert.Equal(17, JavaVersionMatcher.GetRequiredJavaMajor("bad-java", root));

			// 损坏 JSON → catch → 回退
			WriteVersionJson(root, "corrupt", """{"id":"corrupt","java""");
			Assert.Equal(17, JavaVersionMatcher.GetRequiredJavaMajor("corrupt", root));
		}
		finally
		{
			if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
		}
	}
}