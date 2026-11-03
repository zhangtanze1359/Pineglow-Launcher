using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 路径真实性与防替换守卫（本轮 S1 / RA-01 的最小新文件）。
///
/// 为什么必须新增这个文件：上一轮的修复只在**记录里的根**（`mod-replace` 时是 `game\mods`）以内做
/// 词法 + 重解析点检查，于是把**上层** `game` 换成 junction 后，"撤销替换"仍会把文件写到实例目录之外，
/// 而接口返回 200/ok=true/restored。仅靠字符串前缀或"写完之后再复核"不足以证明不越界。
///
/// 本文件提供的可证明限制（Windows）：
///   1. 用 `CreateFileW(FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS)` 打开链上的每个目录
///      —— **不跟随最后一级**，但**中间级仍由内核解析**；
///   2. 用 `GetFinalPathNameByHandleW` 取该句柄的**真实路径**，与请求的词法路径逐字比较：
///      只要链上任何一级（含记录根**之上**的层级，例如 `game`）被替换成 junction/符号链接，
///      真实路径就会不同，于是在**写入之前**拒绝；
///   3. 句柄共享模式**不含 FILE_SHARE_DELETE**，因此操作期间这些目录**不能被改名或删除**，
///      "校验→写入之间把目录换成联接"的竞态被真正封住，而不是"检查过所以应该没问题"。
///
/// 非 Windows：没有等价的内核句柄语义，退回托管属性检查，并在交付文档中如实标注**竞态未关闭**。
/// </summary>
internal static class PathSafetyGuard
{
	private const uint GenericRead = 0x80000000u;

	private const uint FileShareRead = 0x00000001u;

	private const uint FileShareWrite = 0x00000002u;

	private const uint OpenExisting = 3u;

	private const uint FileFlagBackupSemantics = 0x02000000u;

	private const uint FileFlagOpenReparsePoint = 0x00200000u;

	private const int ErrorFileNotFound = 2;

	private const int ErrorPathNotFound = 3;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFileW(
		string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
		uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetFinalPathNameByHandleW(
		SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

	/// <summary>持有已打开目录句柄的集合；释放前这些目录不能被改名/删除。</summary>
	internal sealed class PinnedPath : IDisposable
	{
		private readonly List<SafeFileHandle> _handles = new List<SafeFileHandle>();

		public int Count => _handles.Count;

		/// <summary>本次真正生效的目录锁数量（0 表示当前平台没有句柄语义，只有托管检查）。</summary>
		public bool HasHandleLocks => _handles.Count > 0;

		internal void Add(SafeFileHandle handle) => _handles.Add(handle);

		public void Dispose()
		{
			for (int i = _handles.Count - 1; i >= 0; i--)
			{
				try { _handles[i].Dispose(); } catch { }
			}

			_handles.Clear();
		}
	}

	public static bool PathsEqual(string a, string b) =>
		string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

	private static string Normalize(string path)
	{
		string value = path ?? string.Empty;
		if (value.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
		{
			value = @"\\" + value.Substring(8);
		}
		else if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
		{
			value = value.Substring(4);
		}

		return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	/// <summary>
	/// 校验并**钉住** <paramref name="targetFullPath"/> 之上、<paramref name="anchorFullPath"/> 之内的目录链。
	///
	/// 语义：
	///   * 词法上 target 必须严格位于 anchor 之内；
	///   * 从 target 的父目录向上直到 anchor（含）的每一级**已存在**目录都要：不是重解析点、且
	///     句柄真实路径与请求路径逐字一致；缺失层级在最近存在的祖先被钉住之后才创建，创建后再钉一次；
	///   * 返回的 <see cref="PinnedPath"/> 在 Dispose 之前阻止这些目录被改名/删除。
	/// </summary>
	public static bool TryPinDirectoryChain(
		string anchorFullPath, string targetFullPath, bool createMissingTargetDirectory,
		out PinnedPath? pinned, out string error)
	{
		pinned = null;
		error = string.Empty;

		string anchor;
		string target;
		try
		{
			anchor = Normalize(Path.GetFullPath(anchorFullPath));
			target = Normalize(Path.GetFullPath(targetFullPath));
		}
		catch (Exception ex)
		{
			error = "路径无法规范化：" + ex.Message;
			return false;
		}

		if (string.Equals(anchor, target, StringComparison.OrdinalIgnoreCase))
		{
			error = "目标就是锚点目录自身";
			return false;
		}

		if (!FileRecoveryService.IsPathInside(anchor, target))
		{
			error = "目标不在锚点目录之内：" + target + " 不属于 " + anchor;
			return false;
		}

		if (!Directory.Exists(anchor))
		{
			error = "锚点目录不存在：" + anchor;
			return false;
		}

		string? targetDir = Path.GetDirectoryName(target);
		if (string.IsNullOrEmpty(targetDir))
		{
			error = "无法取得目标父目录";
			return false;
		}

		var pin = new PinnedPath();
		bool needCreate = false;
		bool reachedAnchor = false;
		string? current = targetDir;

		while (!string.IsNullOrEmpty(current))
		{
			if (PathsEqual(current, anchor))
			{
				reachedAnchor = true;
			}

			if (Directory.Exists(current))
			{
				if (!TryPinDirectory(current, pin, out error))
				{
					pin.Dispose();
					return false;
				}
			}
			else if (PathsEqual(current, targetDir))
			{
				needCreate = true;
			}

			if (reachedAnchor)
			{
				break;
			}

			string? parent = Path.GetDirectoryName(current);
			if (string.IsNullOrEmpty(parent) || PathsEqual(parent, current))
			{
				break;
			}

			current = parent;
		}

		if (!reachedAnchor)
		{
			pin.Dispose();
			error = "目标父目录链没有落到锚点之内：" + targetDir;
			return false;
		}

		if (!Directory.Exists(anchor) || !TryPinDirectory(anchor, pin, out error))
		{
			pin.Dispose();
			return false;
		}

		if (needCreate && !Directory.Exists(targetDir))
		{
			if (!createMissingTargetDirectory)
			{
				pin.Dispose();
				error = "目标父目录不存在：" + targetDir;
				return false;
			}

			try
			{
				Directory.CreateDirectory(targetDir);
			}
			catch (Exception ex)
			{
				pin.Dispose();
				error = "创建目标父目录失败：" + ex.Message;
				return false;
			}

			if (!TryPinDirectory(targetDir, pin, out error))
			{
				pin.Dispose();
				return false;
			}
		}

		pinned = pin;
		return true;
	}

	private static bool TryPinDirectory(string dir, PinnedPath pin, out string error)
	{
		error = string.Empty;

		if (!OperatingSystem.IsWindows())
		{
			// 非 Windows 没有等价的内核句柄语义：只做托管属性检查，并在交付文档中标注竞态未关闭。
			try
			{
				if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
				{
					error = "目录是符号链接/重解析点：" + dir;
					return false;
				}
			}
			catch (Exception ex)
			{
				error = "无法读取目录属性：" + ex.Message;
				return false;
			}

			return true;
		}

		SafeFileHandle handle = CreateFileW(
			dir, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero,
			OpenExisting, FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);

		if (handle.IsInvalid)
		{
			int code = Marshal.GetLastWin32Error();
			handle.Dispose();
			if (code == ErrorFileNotFound || code == ErrorPathNotFound)
			{
				error = "目录已不存在：" + dir;
				return false;
			}

			error = "无法打开目录句柄（可能被其他进程以独占方式占用）：" + dir
				+ "（Win32 " + code.ToString() + "：" + new Win32Exception(code).Message + "）";
			return false;
		}

		try
		{
			FileAttributes attributes = File.GetAttributes(handle);
			if ((attributes & FileAttributes.ReparsePoint) != 0)
			{
				error = "目录是符号链接/重解析点（junction）：" + dir;
				return false;
			}

			if ((attributes & FileAttributes.Directory) == 0)
			{
				error = "路径不是目录：" + dir;
				return false;
			}

			string? real = GetRealPath(handle);
			if (real == null)
			{
				error = "无法解析目录的真实路径：" + dir;
				return false;
			}

			if (!PathsEqual(real, dir))
			{
				// RA-01 的核心：链上更高层被换成联接时，句柄真实路径与请求路径不同。
				error = "路径被重定向：请求「" + dir + "」，实际「" + real + "」（链上有符号链接/重解析点）";
				return false;
			}
		}
		catch (Exception ex)
		{
			error = "校验目录真实路径失败：" + ex.Message;
			return false;
		}

		pin.Add(handle);
		return true;
	}

	private static string? GetRealPath(SafeFileHandle handle)
	{
		var buffer = new StringBuilder(512);
		uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0u);
		if (length == 0)
		{
			return null;
		}

		if (length >= buffer.Capacity)
		{
			buffer = new StringBuilder((int)length + 1);
			length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0u);
			if (length == 0)
			{
				return null;
			}
		}

		return Normalize(buffer.ToString());
	}

	/// <summary>叶子文件本身不得是重解析点（不跟随最后一级）。</summary>
	public static bool TryRejectReparseLeaf(string fileFullPath, out string error)
	{
		error = string.Empty;
		try
		{
			if (File.Exists(fileFullPath) && (File.GetAttributes(fileFullPath) & FileAttributes.ReparsePoint) != 0)
			{
				error = "目标文件本身是符号链接/重解析点：" + fileFullPath;
				return false;
			}
		}
		catch (Exception ex)
		{
			error = "无法读取文件属性：" + ex.Message;
			return false;
		}

		return true;
	}
}
