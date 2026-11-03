using System;
using System.Linq;
using System.Threading;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 启动器凭据存储（公开 API 不变）。
///
/// 本轮 R0（验收阻断项 F-02）修复：读/写/删**全部**经由可注入的
/// <see cref="ISecretStoreBackend"/>，不再内联原生调用。
///
/// 为什么必须改：修复前 5 个配置用例会真实进入 Windows 凭据管理器并产生
/// **20 次 delete-intent**（独立验收 <c>credential-intents.jsonl</c> 实测），
/// 也就是说"跑一遍测试"可能删掉用户真实凭据。
///
/// 三条硬约束（本轮保持不变）：
///   1. **生产默认行为不变**：Windows 走原生凭据管理器，非 Windows 走 <c>secrets.json</c>；
///   2. **凭据命名/前缀/迁移策略不变**：目标名仍是 <c>MCMiniLauncher/{name}</c>，
///      常量值仍是 <c>MicrosoftRefreshToken</c> / <c>CurseForgeApiKey</c> / <c>ProxyPassword</c>；
///   3. **测试不再可达原生 API**：测试程序集可安装内存后端（见
///      <see cref="InstallProductionBackendForTest"/>），并且可证明原生调用次数为 0。
/// </summary>
public static class SecretStore
{
	public const string MicrosoftRefreshToken = "MicrosoftRefreshToken";

	public const string CurseForgeApiKey = "CurseForgeApiKey";

	public const string ProxyPassword = "ProxyPassword";

	private static ISecretStoreBackend _productionBackend = CreateProductionBackend();

	/// <summary>
	/// 作用域内（当前异步流）生效的后端覆盖。用 <see cref="AsyncLocal{T}"/> 而不是普通静态字段：
	/// xUnit 按测试类并行调度，普通静态字段会让并行用例互相串后端。
	/// </summary>
	private static readonly AsyncLocal<ISecretStoreBackend?> BackendOverrideForTest = new AsyncLocal<ISecretStoreBackend?>();

	private static long _productionBackendCallCount;

	/// <summary>生产后端（Windows 原生 / 受限文件）被真正进入的次数。测试用它证明"原生不可达"。</summary>
	internal static long ProductionBackendCallCount => Interlocked.Read(ref _productionBackendCallCount);

	/// <summary>由后端实现调用：每进入一次生产后端计一次数（内存后端不计）。</summary>
	internal static void CountProductionBackendCall() => Interlocked.Increment(ref _productionBackendCallCount);

	/// <summary>当前实际生效的后端。</summary>
	internal static ISecretStoreBackend CurrentBackend => BackendOverrideForTest.Value ?? _productionBackend;

	/// <summary>当前是否处于测试注入的作用域内。</summary>
	internal static bool IsBackendOverriddenForTest => BackendOverrideForTest.Value != null;

	/// <summary>
	/// 测试程序集专用：替换**进程默认后端**。
	///
	/// 测试程序集在 <c>[ModuleInitializer]</c> 里调用它，把整个测试进程的默认后端换成内存后端 ——
	/// 这样"忘记加作用域"的未来用例也碰不到真实凭据（fail-safe），
	/// 而生产代码路径（GUI / VerifyHost）永远不会执行到这里（<c>internal</c> + InternalsVisibleTo）。
	/// </summary>
	internal static void InstallProductionBackendForTest(ISecretStoreBackend backend)
	{
		_productionBackend = backend ?? throw new ArgumentNullException(nameof(backend));
	}

	/// <summary>测试程序集专用：在当前异步流内临时覆盖后端；<see cref="IDisposable.Dispose"/> 时还原。</summary>
	internal static IDisposable OverrideBackendForTest(ISecretStoreBackend backend)
	{
		if (backend == null)
		{
			throw new ArgumentNullException(nameof(backend));
		}

		ISecretStoreBackend? previous = BackendOverrideForTest.Value;
		BackendOverrideForTest.Value = backend;
		return new BackendOverrideScope(previous);
	}

	/// <summary>测试程序集专用：把计数清零，便于"某段操作期间原生调用为 0"的差分断言。</summary>
	internal static void ResetProductionBackendCallCountForTest() => Interlocked.Exchange(ref _productionBackendCallCount, 0L);

	private sealed class BackendOverrideScope : IDisposable
	{
		private readonly ISecretStoreBackend? _previous;
		private bool _disposed;

		public BackendOverrideScope(ISecretStoreBackend? previous) => _previous = previous;

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			BackendOverrideForTest.Value = _previous;
		}
	}

	private static ISecretStoreBackend CreateProductionBackend() =>
		OperatingSystem.IsWindows()
			? (ISecretStoreBackend)new WindowsCredentialSecretBackend()
			: new RestrictedFileSecretBackend();

	public static string MicrosoftAccountToken(string accountId)
	{
		return "MicrosoftRefreshToken_" + accountId;
	}

	public static string AccountToken(string accountId)
	{
		return "AccountToken_" + accountId;
	}

	/// <summary>读取凭据；不存在返回空串（与修复前一致）。</summary>
	public static string Load(string name)
	{
		ValidateName(name);
		return CurrentBackend.Read(name) ?? string.Empty;
	}

	/// <summary>写入凭据；<paramref name="value"/> 为空/null 表示删除（与修复前一致）。</summary>
	public static void Save(string name, string? value)
	{
		ValidateName(name);
		ISecretStoreBackend backend = CurrentBackend;
		if (string.IsNullOrEmpty(value))
		{
			backend.Delete(name);
		}
		else
		{
			backend.Write(name, value);
		}
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
}
