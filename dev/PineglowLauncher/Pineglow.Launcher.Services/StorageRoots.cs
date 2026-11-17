using System;
using System.IO;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 三个**本地存储根**的一次性快照：恢复区、二分定位落盘、诊断报告历史
/// （架构评审候选 ③ 第 2 步把 <c>IRoots</c> 落成具体类型）。
///
/// <para>
/// 为什么是具体类型而不是接口：这三个值只被 <c>new XxxService(root)</c> 消费，
/// 没有任何行为可替换。给它一个接口只会让测试多写一个空实现。
/// </para>
/// <para>
/// **为什么这三个根必须一起走**：它们原来各有一个静态 override，测试要分别重定向，
/// 漏一个就往真实 <c>%APPDATA%</c> 里写（第 47 轮实测：诊断报告一直在写）。
/// 做成一个不可分割的值，就再没有"只隔离了两个"的中间状态。
/// </para>
/// </summary>
internal sealed class StorageRoots : IEquatable<StorageRoots>
{
	public StorageRoots(string recovery, string bisect, string diagnostics)
	{
		Recovery = Require(recovery, nameof(recovery));
		Bisect = Require(bisect, nameof(bisect));
		Diagnostics = Require(diagnostics, nameof(diagnostics));
	}

	/// <summary>恢复区（文件恢复与存档恢复共用，存档记录在它的 <c>saves/</c> 子目录）。</summary>
	public string Recovery { get; }

	/// <summary>二分定位会话的落盘根。</summary>
	public string Bisect { get; }

	/// <summary>诊断报告历史根。</summary>
	public string Diagnostics { get; }

	/// <summary>生产默认：配置目录下三个并列子目录（都在游戏目录之外）。</summary>
	public static StorageRoots Production()
	{
		string config = PlatformPaths.GetConfigDirectory();
		return new StorageRoots(
			Path.Combine(config, "recovery"),
			Path.Combine(config, "bisect"),
			Path.Combine(config, "diagnostics"));
	}

	private static string Require(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException("存储根不能为空", name);
		}

		return value;
	}

	public bool Equals(StorageRoots? other) => other is not null
		&& string.Equals(Recovery, other.Recovery, StringComparison.OrdinalIgnoreCase)
		&& string.Equals(Bisect, other.Bisect, StringComparison.OrdinalIgnoreCase)
		&& string.Equals(Diagnostics, other.Diagnostics, StringComparison.OrdinalIgnoreCase);

	public override bool Equals(object? obj) => Equals(obj as StorageRoots);

	public override int GetHashCode() => HashCode.Combine(
		Recovery.ToLowerInvariant(), Bisect.ToLowerInvariant(), Diagnostics.ToLowerInvariant());

	public override string ToString() => $"recovery={Recovery}; bisect={Bisect}; diagnostics={Diagnostics}";
}
