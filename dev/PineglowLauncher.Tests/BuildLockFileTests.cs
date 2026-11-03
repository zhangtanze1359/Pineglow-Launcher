using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR19：RID 边界与发布锁定的**不变量守卫**。
///
/// 背景（本包实测复现，不是推测）：
///   - 非 RID 路径：`dotnet restore --locked-mode` 通过（lock 文件只覆盖 net10.0）；
///   - RID 路径：`dotnet publish -r win-x64 -p:RestoreLockedMode=true` 失败于 **NU1004**
///     （lock 文件没有 RID 段，且 RID 发布会额外引入 `Microsoft.NET.ILLink.Tasks`）；
///   - 普通 `dotnet publish -r win-x64` **会就地改写** `packages.lock.json`：
///     8663 → 10868 字节，新增 `net10.0/win-x64` 段（8 个包，含 ILLink.Tasks）。
///
/// **关键结论（本包新增的实验）**：改写后的 lock 文件能让 RID 锁定还原通过（exit 0），
/// 但会**同时让非 RID 锁定还原失败**（NU1004：net10.0 段多出 ILLink.Tasks）。
/// 两者**互斥** —— 因此"提交 RID 形态的 lock 文件"不是修复，而是会静默破坏 CI 里
/// 那两个 `--locked-mode` 步骤。这正是当前 CI 取舍（非 RID 锁定 + RID 不锁定）正确的原因。
///
/// 本文件把该不变量钉死：提交的 lock 文件必须是**非 RID 形态**。
/// </summary>
public class BuildLockFileTests
{
	private readonly ITestOutputHelper _out;

	public BuildLockFileTests(ITestOutputHelper output) => _out = output;

	private static string RepoRoot()
	{
		// 测试程序集位于 dev/PineglowLauncher.Tests/bin/<cfg>/net10.0/ → 上溯 4 层到 dev/
		return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
	}

	private static string LockFile(string project) =>
		Path.Combine(RepoRoot(), project, "packages.lock.json");

	private static readonly string[] Projects = { "PineglowLauncher", "PineglowLauncher.Tests" };

	/// <summary>目录名 → 项目文件名（两者不同名，别按目录名拼）。</summary>
	private static readonly Dictionary<string, string> ProjectFiles = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["PineglowLauncher"] = "Pineglow.Launcher.csproj",
		["PineglowLauncher.Tests"] = "Pineglow.Launcher.Tests.csproj",
	};

	/* ===================== 1. 非 RID 形态 ===================== */

	[Fact]
	public void CommittedLockFiles_AreInNonRidForm()
	{
		foreach (string project in Projects)
		{
			string path = LockFile(project);
			Assert.True(File.Exists(path), $"缺少 lock 文件：{path}");

			string text = File.ReadAllText(path);

			// RID 段形如 "net10.0/win-x64"。出现它就说明 lock 文件被 RID 发布改写过，
			// 而那种形态会让非 RID 的 --locked-mode 还原失败（NU1004）。
			Assert.False(
				text.Contains("\"net10.0/", StringComparison.Ordinal),
				$"{project}/packages.lock.json 含 RID 段：这会让非 RID 的 --locked-mode 还原失败（NU1004）。" +
				"请不要提交被 `dotnet publish -r <rid>` 就地改写后的 lock 文件。");

			// ILLink.Tasks 只在 RID 发布时进入依赖图；出现在 net10.0 段同样会破坏非 RID 锁定还原。
			Assert.False(
				text.Contains("Microsoft.NET.ILLink.Tasks", StringComparison.Ordinal),
				$"{project}/packages.lock.json 引用了 Microsoft.NET.ILLink.Tasks：" +
				"该包只由 RID 发布注入，会让非 RID 的 --locked-mode 还原失败（NU1004）。");

			_out.WriteLine($"[lock] {project} 非 RID 形态 OK");
		}
	}

	[Fact]
	public void CommittedLockFiles_AreValidJson_AndStillDescribeTheirProjects()
	{
		foreach (string project in Projects)
		{
			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LockFile(project)));
			JsonElement root = doc.RootElement;

			Assert.Equal(1, root.GetProperty("version").GetInt32());
			Assert.True(root.TryGetProperty("dependencies", out JsonElement dependencies), "lock 文件缺少 dependencies");

			List<string> sections = dependencies.EnumerateObject().Select(p => p.Name).ToList();
			_out.WriteLine($"[lock] {project} 段 = {string.Join(", ", sections)}");

			// 只应有 net10.0 一个段（非 RID 形态）
			Assert.Single(sections);
			Assert.Equal("net10.0", sections[0]);

			// 仍然描述了本项目：抽两个直接依赖做存在性检查（防止 lock 文件被换成别的项目的）
			JsonElement net = dependencies.GetProperty("net10.0");
			Assert.True(net.TryGetProperty("Avalonia", out _), $"{project} 的 lock 文件里没有 Avalonia，可能被换成了别的项目");
			Assert.True(net.TryGetProperty("CommunityToolkit.Mvvm", out _), $"{project} 的 lock 文件里没有 CommunityToolkit.Mvvm");
		}
	}

	/* ===================== 2. 锁定不能被静默关掉 ===================== */

	[Fact]
	public void RestorePackagesWithLockFile_IsEnabledInBothProjects()
	{
		foreach (string project in Projects)
		{
			string csproj = Path.Combine(RepoRoot(), project, ProjectFiles[project]);
			Assert.True(File.Exists(csproj), $"缺少项目文件：{csproj}");

			string text = File.ReadAllText(csproj);
			Assert.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", text);
			_out.WriteLine($"[lock] {project} 已启用 RestorePackagesWithLockFile");
		}
	}
}
