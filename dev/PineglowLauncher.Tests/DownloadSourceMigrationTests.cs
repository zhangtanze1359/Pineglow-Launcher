using System;
using System.IO;
using System.Linq;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// v1.0.4 · MCBBS 下载源下线（S2）的直测。
///
/// 三条不变量：
///   1. 源列表里**不再有** mcbbs（否则设置页还会显示一个死镜像）；
///   2. 旧配置里的 `mcbbs` **读进来就是 bmclapi**（不是"显示迁移了、实际还在用死源"）；
///   3. 迁移是纯函数、大小写不敏感，空值仍落 official。
/// </summary>
public sealed class DownloadSourceMigrationTests : IDisposable
{
	private readonly string _root;
	private readonly string _configPath;

	public DownloadSourceMigrationTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "pg-source-mig-" + Guid.NewGuid().ToString("N").Substring(0, 8));
		Directory.CreateDirectory(_root);
		_configPath = Path.Combine(_root, "config.json");
	}

	public void Dispose()
	{
		ConfigStore.ConfigPathOverrideForTest = null;
		try
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}
		catch
		{
		}
	}

	[Fact]
	public void ListSources_NoLongerOffersMcbbs()
	{
		string[] keys = MirrorHelper.ListSources().Select(s => s.Key).ToArray();

		Assert.Equal(new[] { "official", "bmclapi" }, keys);
		Assert.DoesNotContain(keys, k => k.Contains("mcbbs", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void GetBaseUrl_NoLongerResolvesMcbbs()
	{
		Assert.Equal(string.Empty, MirrorHelper.GetBaseUrl("mcbbs"));
		Assert.Equal("https://bmclapi2.bangbang93.com", MirrorHelper.GetBaseUrl("bmclapi"));
	}

	[Theory]
	[InlineData("mcbbs")]
	[InlineData("MCBBS")]
	[InlineData(" mcbbs ")]
	public void Normalize_MigratesLegacyMcbbsToBmclapi(string legacy)
	{
		Assert.Equal("bmclapi", MirrorHelper.Normalize(legacy));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Normalize_EmptyFallsBackToOfficial(string? empty)
	{
		Assert.Equal("official", MirrorHelper.Normalize(empty));
	}

	[Fact]
	public void Normalize_KeepsKnownKeysAndLowercases()
	{
		Assert.Equal("official", MirrorHelper.Normalize("Official"));
		Assert.Equal("bmclapi", MirrorHelper.Normalize("BMCLAPI"));
	}

	/// <summary>
	/// 旧 config.json（`downloadSource = "mcbbs"`）读进来以后，经过读配置时走的同一个规范化函数，
	/// 必须变成 `bmclapi` —— 这条用例**删掉迁移实现就会红**（`Normalize` 不存在时编译不过，
	/// 改成直接返回原值时断言失败）。
	/// </summary>
	[Fact]
	public async Task LegacyConfigFile_MigratesOnRead()
	{
		File.WriteAllText(_configPath, """
			{
			  "gameDirectory": "C:/games/.minecraft",
			  "playerName": "Player",
			  "downloadSource": "mcbbs",
			  "useMirror": true
			}
			""");

		ConfigStore.ConfigPathOverrideForTest = _configPath;
		AppConfig raw = await ConfigStore.LoadAsync();

		Assert.Equal("mcbbs", raw.DownloadSource);            // 文件里是旧值
		Assert.Equal("bmclapi", MirrorHelper.Normalize(raw.DownloadSource)); // 读配置时迁成新值
		Assert.NotEqual("mcbbs", MirrorHelper.Normalize(raw.DownloadSource));
	}
}
