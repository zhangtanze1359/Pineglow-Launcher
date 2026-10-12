using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

public class MemoryAllocationServiceTests
{
	[Theory]
	[InlineData("1.12.2", 8192, 2048, 6144)]  // 旧版本：base 2048，max = 8192-2048
	[InlineData("1.16.5", 8192, 3072, 6144)]  // 13~17：base 3072
	[InlineData("1.18.2", 8192, 4096, 6144)]  // 18+：base 4096
	[InlineData("1.20.1", 16384, 4096, 12288)] // 大内存机器：max = 16384-4096
	[InlineData("", 8192, 4096, 6144)]        // 空版本走默认 minor20 → base 4096
	public void Recommend_ProducesValidRange(string version, int totalMb, int expectedRecommended, int expectedMax)
	{
		MemoryRecommendation rec = MemoryAllocationService.Recommend(version, null, 0, totalMb);

		Assert.True(rec.RecommendedMb >= MemoryAllocationService.MinimumMemoryMb);
		Assert.True(rec.RecommendedMb <= rec.MaximumMb);
		Assert.True(rec.MaximumMb <= totalMb);
		Assert.Equal(expectedMax, rec.MaximumMb);
		Assert.Equal(expectedRecommended, rec.RecommendedMb);
	}

	[Theory]
	[InlineData("1.20.1", "forge", 0, 8192, 5120)]   // 4096 + 1024
	[InlineData("1.20.1", "fabric", 0, 8192, 4608)]  // 4096 + 512
	[InlineData("1.20.1", null, 0, 8192, 4096)]      // 无加载器
	[InlineData("1.20.1", "forge", 25, 8192, 5632)]  // +512 mods
	public void Recommend_LoaderAndMods_IncreaseMemory(string version, string? loader, int modCount, int totalMb, int expectedRecommended)
	{
		MemoryRecommendation rec = MemoryAllocationService.Recommend(version, loader, modCount, totalMb);

		Assert.Equal(expectedRecommended, rec.RecommendedMb);
	}

	[Theory]
	[InlineData(1024, 8192, 1024)]
	[InlineData(1500, 8192, 1536)]   // 就近对齐 256
	[InlineData(0, 8192, 1024)]      // 低于最小值被钳制
	[InlineData(999999, 4096, 4096)] // 高于最大值被钳制
	public void NormalizeManualMemory_ClampsAndAligns(int input, int max, int expected)
	{
		Assert.Equal(expected, MemoryAllocationService.NormalizeManualMemory(input, max));
	}

	[Theory]
	[InlineData(1024, "1 GB")]
	[InlineData(2048, "2 GB")]
	[InlineData(1536, "1.5 GB")]
	[InlineData(4096, "4 GB")]
	public void FormatMemory_HumanReadable(int mb, string expected)
	{
		Assert.Equal(expected, MemoryAllocationService.FormatMemory(mb));
	}
}
public class MemoryAllocationCountTests
{
	private static string NewRoot()
	{
		return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-modcount-" + System.Guid.NewGuid().ToString("N"));
	}

	[Fact]
	public void CountEnabledMods_ReturnsJarCount()
	{
		string root = NewRoot();
		try
		{
			string mods = System.IO.Path.Combine(root, "mods");
			System.IO.Directory.CreateDirectory(mods);
			System.IO.File.WriteAllText(System.IO.Path.Combine(mods, "a.jar"), "x");
			System.IO.File.WriteAllText(System.IO.Path.Combine(mods, "b.jar"), "x");
			System.IO.File.WriteAllText(System.IO.Path.Combine(mods, "readme.txt"), "x");
			System.IO.Directory.CreateDirectory(System.IO.Path.Combine(mods, "sub"));
			System.IO.File.WriteAllText(System.IO.Path.Combine(mods, "sub", "c.jar"), "x");

			// 只统计 mods 顶层 .jar，忽略其他扩展名与子目录
			Assert.Equal(2, MemoryAllocationService.CountEnabledMods(root));
		}
		finally
		{
			if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void CountEnabledMods_MissingModsDirectory_ReturnsZero()
	{
		string root = NewRoot();
		try
		{
			System.IO.Directory.CreateDirectory(root);
			Assert.Equal(0, MemoryAllocationService.CountEnabledMods(root));
		}
		finally
		{
			if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void CountEnabledMods_NullOrEmpty_ReturnsZero()
	{
		Assert.Equal(0, MemoryAllocationService.CountEnabledMods(null));
		Assert.Equal(0, MemoryAllocationService.CountEnabledMods(string.Empty));
	}
}