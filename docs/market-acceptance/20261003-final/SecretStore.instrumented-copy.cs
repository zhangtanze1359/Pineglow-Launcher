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

public static class SecretStore
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

	public const string MicrosoftRefreshToken = "MicrosoftRefreshToken";

	public const string CurseForgeApiKey = "CurseForgeApiKey";

	public const string ProxyPassword = "ProxyPassword";

	private const string TargetPrefix = "MCMiniLauncher/";

	private const uint CredentialTypeGeneric = 1u;

	private const uint CredentialPersistLocalMachine = 2u;

	private const int ErrorNotFound = 1168;

	public static string MicrosoftAccountToken(string accountId)
	{
		return "MicrosoftRefreshToken_" + accountId;
	}

	public static string AccountToken(string accountId)
	{
		return "AccountToken_" + accountId;
	}

	public static string Load(string name)
	{
		ValidateName(name);
		File.AppendAllText(Environment.GetEnvironmentVariable("PINEGLOW_ACCEPTANCE_SECRET_EVENTS")!, JsonSerializer.Serialize(new { name, operation = "read-stub" }) + Environment.NewLine);
		return string.Empty;
		if (!OperatingSystem.IsWindows())
		{
			return LoadRestrictedFile(name);
		}
		return LoadWindows(name);
	}

	public static void Save(string name, string? value)
	{
		ValidateName(name);
		File.AppendAllText(Environment.GetEnvironmentVariable("PINEGLOW_ACCEPTANCE_SECRET_EVENTS")!, JsonSerializer.Serialize(new { name, operation = string.IsNullOrEmpty(value) ? "delete-intent" : "write-intent" }) + Environment.NewLine);
		return;
		if (OperatingSystem.IsWindows())
		{
			SaveWindows(name, value ?? string.Empty);
		}
		else
		{
			SaveRestrictedFile(name, value ?? string.Empty);
		}
	}

	private static string LoadWindows(string name)
	{
		if (!CredReadW("MCMiniLauncher/" + name, 1u, 0u, out var credentialPointer))
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			if (lastWin32Error == 1168)
			{
				return string.Empty;
			}
			throw new Win32Exception(lastWin32Error, "无法从 Windows 凭据管理器读取启动器凭据。");
		}
		try
		{
			NativeCredential nativeCredential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
			if (nativeCredential.CredentialBlob == IntPtr.Zero || nativeCredential.CredentialBlobSize == 0)
			{
				return string.Empty;
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

	private static void SaveWindows(string name, string value)
	{
		string text = "MCMiniLauncher/" + name;
		if (string.IsNullOrEmpty(value))
		{
			if (!CredDeleteW(text, 1u, 0u))
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				if (lastWin32Error != 1168)
				{
					throw new Win32Exception(lastWin32Error, "无法删除 Windows 凭据管理器中的启动器凭据。");
				}
			}
			return;
		}
		byte[] bytes = Encoding.Unicode.GetBytes(value);
		nint num = Marshal.AllocCoTaskMem(bytes.Length);
		try
		{
			Marshal.Copy(bytes, 0, num, bytes.Length);
			NativeCredential credential = new NativeCredential
			{
				Type = 1u,
				TargetName = text,
				CredentialBlobSize = (uint)bytes.Length,
				CredentialBlob = num,
				Persist = 2u,
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

	private static string LoadRestrictedFile(string name)
	{
		string restrictedFilePath = GetRestrictedFilePath();
		if (!File.Exists(restrictedFilePath))
		{
			return string.Empty;
		}
		try
		{
			Dictionary<string, string> dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(restrictedFilePath));
			string value;
			return (dictionary != null && dictionary.TryGetValue(name, out value)) ? value : string.Empty;
		}
		catch (JsonException)
		{
			return string.Empty;
		}
	}

	private static void SaveRestrictedFile(string name, string value)
	{
		string restrictedFilePath = GetRestrictedFilePath();
		Directory.CreateDirectory(Path.GetDirectoryName(restrictedFilePath));
		Dictionary<string, string> dictionary;
		try
		{
			dictionary = (File.Exists(restrictedFilePath) ? (JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(restrictedFilePath)) ?? new Dictionary<string, string>()) : new Dictionary<string, string>());
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

	private static string GetRestrictedFilePath()
	{
		return Path.Combine(PlatformPaths.GetConfigDirectory(), "secrets.json");
	}

	private static void ValidateName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Any(delegate(char ch)
		{
			bool flag = char.IsLetterOrDigit(ch);
			if (!flag)
			{
				bool flag2 = ((ch == '-' || ch == '_') ? true : false);
				flag = flag2;
			}
			return !flag;
		}))
		{
			throw new ArgumentException("凭据名称无效。", "name");
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
