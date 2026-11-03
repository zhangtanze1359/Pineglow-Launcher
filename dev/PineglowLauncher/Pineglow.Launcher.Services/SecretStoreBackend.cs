using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 凭据后端的**唯一**抽象（本轮 R0 / 验收阻断项 F-02 的最小修复）。
///
/// 背景：修复前 <see cref="SecretStore"/> 的读/写/删直接内联手写原生调用
/// （Windows 的 <c>CredReadW</c>/<c>CredWriteW</c>/<c>CredDeleteW</c>），**没有测试缝**。
/// 实测（独立验收 <c>credential-intents.jsonl</c>）：仅执行 5 个配置用例就会产生
/// **20 次 delete-intent**，即原版测试会在真实用户会话里对真实凭据发起删除/写入。
///
/// 因此把"落到哪里"抽成后端：
///   * 生产默认：Windows → <see cref="WindowsCredentialSecretBackend"/>；
///     非 Windows → <see cref="RestrictedFileSecretBackend"/>（行为与修复前完全一致）；
///   * 测试：<see cref="InMemorySecretStoreBackend"/> 或任何注入实现；
///   * **命名、目标前缀（<c>MCMiniLauncher/</c>）、迁移策略一律不变** ——
///     用户机器上已保存的凭据不会变成孤儿。
///
/// 所有后端都是 <c>internal</c>：<see cref="SecretStore"/> 的公开 API 不变。
/// </summary>
internal interface ISecretStoreBackend
{
	/// <summary>读取；不存在时返回 <c>null</c>（<see cref="SecretStore.Load"/> 会归一化为空串）。</summary>
	string? Read(string name);

	/// <summary>写入一个非空值。</summary>
	void Write(string name, string value);

	/// <summary>删除（<see cref="SecretStore.Save"/> 收到空值时走这里）。</summary>
	void Delete(string name);
}

/// <summary>
/// Windows 原生凭据管理器后端 —— 生产默认实现，行为与修复前 <see cref="SecretStore"/> 内联代码逐行等价：
///   目标名 <c>MCMiniLauncher/{name}</c>、类型 Generic（1）、持久化 LocalMachine（2）、
///   不存在（1168）不抛异常、其他 Win32 错误抛 <see cref="Win32Exception"/>。
/// </summary>
internal sealed class WindowsCredentialSecretBackend : ISecretStoreBackend
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct NativeCredential
	{
		public uint Flags;

		public uint Type;

		public string TargetName;

		public string? Comment;

		public FILETIME LastWritten;

		public uint CredentialBlobSize;

		public nint CredentialBlob;

		public uint Persist;

		public uint AttributeCount;

		public nint Attributes;

		public string? TargetAlias;

		public string UserName;
	}

	private const string TargetPrefix = "MCMiniLauncher/";

	private const uint CredentialTypeGeneric = 1u;

	private const uint CredentialPersistLocalMachine = 2u;

	private const int ErrorNotFound = 1168;

	public string? Read(string name)
	{
		SecretStore.CountProductionBackendCall();
		string target = TargetPrefix + name;
		if (!CredReadW(target, CredentialTypeGeneric, 0u, out var credentialPointer))
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			if (lastWin32Error == ErrorNotFound)
			{
				return null;
			}

			throw new Win32Exception(lastWin32Error, "无法从 Windows 凭据管理器读取启动器凭据。");
		}

		try
		{
			NativeCredential nativeCredential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
			if (nativeCredential.CredentialBlob == IntPtr.Zero || nativeCredential.CredentialBlobSize == 0)
			{
				return null;
			}

			byte[] array = new byte[nativeCredential.CredentialBlobSize];
			Marshal.Copy(nativeCredential.CredentialBlob, array, 0, array.Length);
			return Encoding.Unicode.GetString(array).TrimEnd('\0');
		}
		finally
		{
			CredFree(credentialPointer);
		}
	}

	public void Write(string name, string value)
	{
		SecretStore.CountProductionBackendCall();
		string text = TargetPrefix + name;
		byte[] bytes = Encoding.Unicode.GetBytes(value);
		nint num = Marshal.AllocCoTaskMem(bytes.Length);
		try
		{
			Marshal.Copy(bytes, 0, num, bytes.Length);
			NativeCredential credential = new NativeCredential
			{
				Type = CredentialTypeGeneric,
				TargetName = text,
				CredentialBlobSize = (uint)bytes.Length,
				CredentialBlob = num,
				Persist = CredentialPersistLocalMachine,
				UserName = Environment.UserName
			};
			if (!CredWriteW(ref credential, 0u))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将启动器凭据写入 Windows 凭据管理器。");
			}
		}
		finally
		{
			for (int i = 0; i < bytes.Length; i++)
			{
				Marshal.WriteByte(num, i, 0);
			}

			Marshal.FreeCoTaskMem(num);
		}
	}

	public void Delete(string name)
	{
		SecretStore.CountProductionBackendCall();
		if (!CredDeleteW(TargetPrefix + name, CredentialTypeGeneric, 0u))
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			if (lastWin32Error != ErrorNotFound)
			{
				throw new Win32Exception(lastWin32Error, "无法删除 Windows 凭据管理器中的启动器凭据。");
			}
		}
	}

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CredReadW(string target, uint type, uint flags, out nint credentialPointer);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CredDeleteW(string target, uint type, uint flags);

	[DllImport("advapi32.dll")]
	private static extern void CredFree(nint buffer);
}

/// <summary>
/// 非 Windows 的受限文件后端（<c>%APPDATA%/MCMiniLauncher/secrets.json</c>）——
/// 行为与修复前 <c>LoadRestrictedFile</c>/<c>SaveRestrictedFile</c> 逐行等价，
/// 键名就是 <paramref name="name"/>（**不加** <c>MCMiniLauncher/</c> 前缀，保持落盘兼容）。
/// </summary>
internal sealed class RestrictedFileSecretBackend : ISecretStoreBackend
{
	public string? Read(string name)
	{
		SecretStore.CountProductionBackendCall();
		string restrictedFilePath = GetRestrictedFilePath();
		if (!File.Exists(restrictedFilePath))
		{
			return null;
		}

		try
		{
			Dictionary<string, string>? dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(restrictedFilePath));
			if (dictionary != null && dictionary.TryGetValue(name, out string? value))
			{
				return value;
			}
		}
		catch (JsonException)
		{
		}

		return null;
	}

	public void Write(string name, string value)
	{
		SecretStore.CountProductionBackendCall();
		Mutate(name, value);
	}

	public void Delete(string name)
	{
		SecretStore.CountProductionBackendCall();
		Mutate(name, null);
	}

	private static void Mutate(string name, string? value)
	{
		string restrictedFilePath = GetRestrictedFilePath();
		Directory.CreateDirectory(Path.GetDirectoryName(restrictedFilePath)!);
		Dictionary<string, string> dictionary;
		try
		{
			dictionary = (File.Exists(restrictedFilePath)
				? (JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(restrictedFilePath)) ?? new Dictionary<string, string>())
				: new Dictionary<string, string>());
		}
		catch (JsonException)
		{
			dictionary = new Dictionary<string, string>();
		}

		if (string.IsNullOrEmpty(value))
		{
			dictionary.Remove(name);
		}
		else
		{
			dictionary[name] = value;
		}

		File.WriteAllText(restrictedFilePath, JsonSerializer.Serialize(dictionary));
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				File.SetUnixFileMode(restrictedFilePath, UnixFileMode.UserWrite | UnixFileMode.UserRead);
			}
		}
		catch (PlatformNotSupportedException)
		{
		}
	}

	private static string GetRestrictedFilePath() =>
		Path.Combine(PlatformPaths.GetConfigDirectory(), "secrets.json");
}

/// <summary>
/// 纯内存后端（测试与取证专用）。生产**永不**选用它。
///
/// 它同时是"本次调用到底走了哪个后端"的**正向证据**：只有它记录了逐次操作，
/// 因此"生产后端调用次数 = 0"不是空口断言 —— 断言旁边还能证明这些调用确实被它接住了。
/// 只保存合成值；不接触磁盘、注册表、凭据管理器。
/// </summary>
internal sealed class InMemorySecretStoreBackend : ISecretStoreBackend
{
	private readonly object _gate = new object();
	private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
	private readonly List<string> _operations = new List<string>();
	private readonly List<string> _deletedNames = new List<string>();

	public InMemorySecretStoreBackend(string label = "in-memory")
	{
		Label = label;
	}

	public string Label { get; }

	/// <summary>按发生顺序记录的操作（<c>read:name</c> / <c>write:name</c> / <c>delete:name</c>）。</summary>
	public IReadOnlyList<string> Operations
	{
		get { lock (_gate) { return _operations.ToArray(); } }
	}

	/// <summary>记录到的删除目标名（用于"配置测试会删除哪些凭据"的取证）。</summary>
	public IReadOnlyList<string> DeletedNames
	{
		get { lock (_gate) { return _deletedNames.ToArray(); } }
	}

	public int CountOf(string operation)
	{
		string prefix = operation + ":";
		lock (_gate) { return _operations.Count(x => x.StartsWith(prefix, StringComparison.Ordinal)); }
	}

	public bool Contains(string name)
	{
		lock (_gate) { return _values.ContainsKey(name); }
	}

	public string? Peek(string name)
	{
		lock (_gate) { return _values.TryGetValue(name, out string? value) ? value : null; }
	}

	public string? Read(string name)
	{
		lock (_gate)
		{
			_operations.Add("read:" + name);
			return _values.TryGetValue(name, out string? value) ? value : null;
		}
	}

	public void Write(string name, string value)
	{
		lock (_gate)
		{
			_operations.Add("write:" + name);
			_values[name] = value;
		}
	}

	public void Delete(string name)
	{
		lock (_gate)
		{
			_operations.Add("delete:" + name);
			_deletedNames.Add(name);
			_values.Remove(name);
		}
	}
}
