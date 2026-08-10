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
