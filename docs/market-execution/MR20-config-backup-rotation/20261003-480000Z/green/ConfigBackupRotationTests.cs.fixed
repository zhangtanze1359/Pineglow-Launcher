using System;
using System.IO;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR20 补测（缺口登记表 A15 的**剩余项**）：配置备份的**主备轮转留证**（<c>.bak</c> / <c>.prev</c>）。
///
/// 为什么此前没测：轮转在 <c>SaveCore</c> 里，而 <c>SaveCore</c> 还会写 <c>SecretStore</c>
/// （**真实凭据存储**，没有测试缝）—— 直接调 <c>SaveAsync</c> 会碰用户凭据 ✗。
///
/// 本轮给 <c>ConfigStore</c> 加了一个**窄测试缝** <c>RotateBackupBeforeWriteForTest</c>
/// （只暴露那个**纯文件操作**的轮转，不碰任何凭据代码），于是这一半可以安全验证 ✓。
///
/// 轮转语义（读实现得出，本类逐条钉住）：
///   1. 若 <c>.bak</c> 已存在 → 先把 <c>.bak</c> 复制到 <c>.prev</c>（更旧的一代）；
///   2. 若 <c>config.json</c> 已存在 → 把它复制到 <c>.bak</c>（次新的一代）；
///   3. **任何失败都不影响主写入**（实现里 catch 吞掉）。
/// </summary>
public class ConfigBackupRotationTests : IDisposable
{
	private readonly ITestOutputHelper _out;
	private readonly string _root;

	public ConfigBackupRotationTests(ITestOutputHelper output)
	{
		_out = output;
		_root = Path.Combine(Path.GetTempPath(), "pineglow-a15b-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
	}

	private string ConfigPath => Path.Combine(_root, "config.json");
	private string BakPath => ConfigPath + ".bak";
	private string PrevPath => ConfigPath + ".prev";

	private void WriteConfig(string content) => File.WriteAllText(ConfigPath, content);
	private string? ReadOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

	/* ===================== ① 第一次轮转：只有 config → 生成 .bak，不生成 .prev ===================== */

	[Fact]
	public void FirstRotation_CreatesBakOnly_WithTheCurrentContent()
	{
		WriteConfig("gen1");

		ConfigStore.RotateBackupBeforeWriteForTest(ConfigPath);

		Assert.Equal("gen1", ReadOrNull(BakPath));
		Assert.False(File.Exists(PrevPath), "第一次轮转时还没有更旧的一代，不应生成 .prev");
		_out.WriteLine("[A15b] 第一次轮转：.bak=gen1，无 .prev");
	}

	/* ===================== ② 第二次轮转：两代都要留证 ===================== */

	[Fact]
	public void SecondRotation_KeepsBothGenerations()
	{
		WriteConfig("gen1");
		ConfigStore.RotateBackupBeforeWriteForTest(ConfigPath);   // .bak = gen1
		WriteConfig("gen2");                                       // 模拟新配置落盘
		ConfigStore.RotateBackupBeforeWriteForTest(ConfigPath);   // .prev = gen1, .bak = gen2

		Assert.Equal("gen2", ReadOrNull(BakPath));
		Assert.Equal("gen1", ReadOrNull(PrevPath));
		_out.WriteLine("[A15b] 第二次轮转：.bak=gen2，.prev=gen1");
	}

	/* ===================== ③ 连续三次：世代顺序正确（最旧的一代被覆盖） ===================== */

	[Fact]
	public void ThirdRotation_KeepsOnlyTheTwoMostRecentGenerations()
	{
		WriteConfig("gen1");
		ConfigStore.RotateBackupBeforeWriteForTest(ConfigPath);
		WriteConfig("gen2");
		ConfigStore.RotateBackupBeforeWriteForTest(ConfigPath);
		WriteConfig("gen3");
		ConfigStore.RotateBackupBeforeWriteForTest(ConfigPath);

		Assert.Equal("gen3", ReadOrNull(BakPath));
		Assert.Equal("gen2", ReadOrNull(PrevPath));
		Assert.Equal("gen3", File.ReadAllText(ConfigPath));   // 轮转**不改动**现配置本身
		_out.WriteLine("[A15b] 第三次轮转：.bak=gen3，.prev=gen2（gen1 被淘汰，符合两代设计）");
	}

	/* ===================== ④ 没有 config.json 时：不生成任何备份 ===================== */

	[Fact]
	public void Rotation_WithoutConfigFile_CreatesNothing()
	{
		ConfigStore.RotateBackupBeforeWriteForTest(ConfigPath);

		Assert.False(File.Exists(BakPath));
		Assert.False(File.Exists(PrevPath));
		_out.WriteLine("[A15b] 无 config.json 时不生成备份");
	}

	/* ===================== ⑤ 备份失败不得抛出（"备份失败不影响主写入"） ===================== */

	[Fact]
	public void Rotation_WhenBackupTargetIsUnusable_DoesNotThrow()
	{
		WriteConfig("gen1");
		// 把 .bak 的位置占成**目录** → File.Copy 必然失败
		Directory.CreateDirectory(BakPath);

		Exception? thrown = null;
		try
		{
			ConfigStore.RotateBackupBeforeWriteForTest(ConfigPath);
		}
		catch (Exception ex)
		{
			thrown = ex;
		}

		_out.WriteLine("[A15b] .bak 被占成目录时：" + (thrown == null ? "未抛出 ✓" : thrown.GetType().Name + " ✗"));
		Assert.Null(thrown);
		Assert.True(Directory.Exists(BakPath), "那个目录应原样保留（轮转不该删别人的东西）");
		Assert.Equal("gen1", File.ReadAllText(ConfigPath));
	}
}
