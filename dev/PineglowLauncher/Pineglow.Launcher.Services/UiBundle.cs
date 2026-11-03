using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 发行资源包（发版阻断项 R01 的修复件）。
///
/// 背景（docs/release-readiness-review-2026-10-05.md R01）：
///   Release 工作流以 PublishSingleFile 发布会话，但当时只上传单个 EXE，
///   而 UI 静态资源是「EXE 旁的外置 UI/ 目录」（主 csproj 的 Content + Link="UI\..."），
///   原生库也没有随单文件打包。结果：把发布出来的 EXE 单独放进新目录启动，
///   直接因缺 Skia 原生库而退出；即使补上原生库，UI/ 仍然缺失。
///
/// 本类的职责：把 UI 资源嵌进程序集，并在**单文件发行**场景下解包到可服务目录。
///   - 解包目录以「版本 + 资源内容指纹」命名，因此升级换 EXE 后不会复用旧界面
///     （避免「新服务配旧 UI」）；同一版本重复启动直接复用已解包目录。
///   - 解包先写临时目录再整体改名，避免半包目录被当成可用资源。
///   - 非单文件部署（开发树、目录包、测试宿主）里 EXE 旁本来就有 UI/，
///     本类不做任何解包，行为与修复前一致。
/// </summary>
public static class UiBundle
{
	/// <summary>嵌入资源名前缀，与 Pineglow.Launcher.csproj 的 LogicalName 保持一致。</summary>
	public const string ResourcePrefix = "Pineglow.Launcher.Ui.";

	/// <summary>服务入口文件；解包目录必须含它才算可用。</summary>
	public const string EntryFileName = "index.html";

	/// <summary>测试缝：强制指定解包根目录，并允许在非单文件宿主里触发解包。</summary>
	public const string BundleRootEnvVar = "PINEGLOW_UI_BUNDLE_ROOT";

	private static readonly string[] StableResourceNames = Discover();

	private static readonly Lazy<string> LazyBundleKey = new Lazy<string>(ComputeBundleKey);

	/// <summary>嵌入资源名（已排序，顺序稳定，便于指纹与断言）。</summary>
	public static IReadOnlyList<string> ResourceNames => StableResourceNames;

	/// <summary>是否真的嵌入了 UI 资源（目录包/旧构件可能没有）。</summary>
	public static bool HasEmbeddedUi => StableResourceNames.Length > 0;

	/// <summary>版本 + 内容指纹；同一份资源在任何机器上都得到同一个键。</summary>
	public static string BundleKey => LazyBundleKey.Value;

	/// <summary>资源名 → 相对 UI 根的路径（正斜杠）。</summary>
	public static string RelativePathOf(string resourceName)
	{
		string rel = resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
			? resourceName.Substring(ResourcePrefix.Length)
			: resourceName;
		return rel.Replace('\\', '/');
	}

	/// <summary>
	/// 生产路径：需要时解包并返回可直接服务的 UI 目录；不需要或失败返回 null
	/// （调用方按既有顺序回退，启动永不因为解包失败而中断）。
	/// </summary>
	public static string? EnsureExtracted()
	{
		if (!HasEmbeddedUi)
		{
			return null;
		}

		string? forcedRoot = Environment.GetEnvironmentVariable(BundleRootEnvVar);
		bool singleFileDeployment = IsSingleFileDeployment();
		if (string.IsNullOrWhiteSpace(forcedRoot) && !singleFileDeployment)
		{
			// 开发树 / 目录包 / 测试宿主：EXE 旁本来就有 UI/，不做重复解包。
			return null;
		}

		List<string> roots = new List<string>();
		if (!string.IsNullOrWhiteSpace(forcedRoot))
		{
			roots.Add(forcedRoot!);
		}
		else
		{
			roots.Add(Path.Combine(PlatformPaths.GetConfigDirectory(), "UI"));
			roots.Add(Path.Combine(Path.GetTempPath(), "pineglow-ui"));
		}

		foreach (string root in roots)
		{
			try
			{
				return ExtractTo(root);
			}
			catch (Exception ex)
			{
				LauncherLog.Info("[UiBundle] 解包失败，尝试下一位置 " + root + ": " + ex.Message);
			}
		}

		return null;
	}

	/// <summary>
	/// 把嵌入资源解包到 <paramref name="uiRoot"/>/&lt;BundleKey&gt; 并返回该目录。
	/// 已存在且含入口文件时直接复用；否则先写临时目录再整体改名（原子切换）。
	/// </summary>
	internal static string ExtractTo(string uiRoot)
	{
		if (string.IsNullOrWhiteSpace(uiRoot))
		{
			throw new ArgumentException("解包根目录不能为空。", nameof(uiRoot));
		}
		if (!HasEmbeddedUi)
		{
			throw new InvalidOperationException("程序集内没有嵌入 UI 资源。");
		}

		string key = BundleKey;
		string target = Path.Combine(uiRoot, key);
		if (IsExtractionUsable(target))
		{
			return target;
		}

		Directory.CreateDirectory(uiRoot);
		string staging = Path.Combine(uiRoot, ".tmp-" + key + "-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(staging);
			Assembly assembly = typeof(UiBundle).Assembly;
			foreach (string name in StableResourceNames)
			{
				string relative = RelativePathOf(name);
				string destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
				string? parent = Path.GetDirectoryName(destination);
				if (!string.IsNullOrEmpty(parent))
				{
					Directory.CreateDirectory(parent);
				}

				using Stream source = assembly.GetManifestResourceStream(name)
					?? throw new InvalidOperationException("缺少嵌入资源: " + name);
				using FileStream file = File.Create(destination);
				source.CopyTo(file);
			}

			// 目标已存在但不是可用解包（缺文件/文件数不符）：先清干净再整体改名，
			// 让「坏解包」不会永久留在磁盘上。
			if (Directory.Exists(target))
			{
				Directory.Delete(target, true);
			}

			Directory.Move(staging, target);
			return target;
		}
		catch
		{
			TryDeleteDirectory(staging);
			throw;
		}
	}

	/// <summary>
	/// 是否运行在单文件发行形态。
	///
	/// 判据是「被捆绑进程集内的程序集 Location 为空」——这正是 .NET 单文件捆绑的官方语义，
	/// 也正是本类需要的信号（IL3000 就是这个语义的告警，这里显式承担该行为，不是误用）。
	/// </summary>
	internal static bool IsSingleFileDeployment()
	{
#pragma warning disable IL3000 // Assembly.Location 在单文件捆绑里恒为空；本方法正是以此为判据。
		string location = typeof(UiBundle).Assembly.Location;
#pragma warning restore IL3000
		return string.IsNullOrEmpty(location);
	}

	/// <summary>解包目录可用判定：入口存在且文件数与嵌入清单一致。</summary>
	internal static bool IsExtractionUsable(string target)	{
		if (!File.Exists(Path.Combine(target, EntryFileName)))
		{
			return false;
		}

		try
		{
			return Directory.GetFiles(target, "*", SearchOption.AllDirectories).Length == StableResourceNames.Length;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>测试缝：读取单个嵌入资源的内容。</summary>
	internal static byte[] ReadResource(string resourceName)
	{
		using Stream? stream = typeof(UiBundle).Assembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			throw new FileNotFoundException("找不到嵌入资源: " + resourceName);
		}

		using MemoryStream buffer = new MemoryStream();
		stream.CopyTo(buffer);
		return buffer.ToArray();
	}

	private static string[] Discover()
	{
		return typeof(UiBundle).Assembly
			.GetManifestResourceNames()
			.Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();
	}

	private static string ComputeBundleKey()
	{
		Assembly assembly = typeof(UiBundle).Assembly;
		using SHA256 sha = SHA256.Create();
		using MemoryStream payload = new MemoryStream();
		foreach (string name in StableResourceNames)
		{
			byte[] nameBytes = Encoding.UTF8.GetBytes(RelativePathOf(name));
			payload.Write(nameBytes, 0, nameBytes.Length);
			payload.WriteByte(0);
			using Stream? stream = assembly.GetManifestResourceStream(name);
			if (stream != null)
			{
				stream.CopyTo(payload);
			}

			payload.WriteByte(0);
		}

		payload.Position = 0;
		byte[] hash = sha.ComputeHash(payload);
		StringBuilder sb = new StringBuilder(12);
		for (int i = 0; i < 6; i++)
		{
			sb.Append(hash[i].ToString("x2"));
		}

		string version = LauncherService.LauncherVersion;
		return version + "-" + sb.ToString();
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, true);
			}
		}
		catch
		{
			// 清理失败不影响主流程：残留的 .tmp-* 目录不会被当成可用资源。
		}
	}
}
