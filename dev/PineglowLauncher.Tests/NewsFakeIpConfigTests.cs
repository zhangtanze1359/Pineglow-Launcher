using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// R2-02：NewsFakeIpCompat 高级设置端到端契约测试（先红后绿，review-fix-1 返工版）。
///
/// 隔离要求（review-fix-1 返工）：
///   - 不读取、不写入、不删除用户真实 config.json/.bak/.prev
///   - 不触碰 SecretStore
///   - 通过 LocalApiService.IConfigStore 注入内存 store；每个测试实例独立
///   - 测试启动/停止失败必须让测试失败（不吞异常）
///   - 测试结束校验真实 config 三文件 SHA-256/存在状态前后一致
/// </summary>
[Collection("LocalApiService")]
public class NewsFakeIpConfigTests
{
	// 与 ConfigStore.Options 完全一致（WriteIndented=true + CamelCase + 大小写不敏感）
	private static readonly JsonSerializerOptions StoreOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	/* ===========================================================
	 * 1. AppConfig JSON 序列化
	 * =========================================================== */

	[Fact]
	public void Deserialize_MissingField_DefaultsToFalse()
	{
		string json = "{\"gameDirectory\":\"D:/games/.minecraft\"}";
		AppConfig config = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions)!;
		Assert.NotNull(config);

		string roundTrip = JsonSerializer.Serialize(config, StoreOptions);
		using JsonDocument doc = JsonDocument.Parse(roundTrip);

		Assert.True(
			doc.RootElement.TryGetProperty("newsFakeIpCompat", out JsonElement field),
			$"AppConfig 序列化输出缺少 newsFakeIpCompat 字段。当前 JSON：{roundTrip}");
		Assert.Equal(JsonValueKind.False, field.ValueKind);
	}

	[Fact]
	public void Roundtrip_True_PreservesThroughJson()
	{
		string json = "{\"newsFakeIpCompat\":true,\"gameDirectory\":\"D:/games\"}";
		AppConfig config = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions)!;
		Assert.NotNull(config);

		string roundTrip = JsonSerializer.Serialize(config, StoreOptions);
		using JsonDocument doc = JsonDocument.Parse(roundTrip);
		Assert.True(
			doc.RootElement.TryGetProperty("newsFakeIpCompat", out JsonElement field),
			$"true 重载后字段丢失。roundTrip={roundTrip}");
		Assert.Equal(JsonValueKind.True, field.ValueKind);
	}

	[Fact]
	public void Roundtrip_False_PreservesThroughJson()
	{
		string json = "{\"newsFakeIpCompat\":false,\"gameDirectory\":\"D:/games\"}";
		AppConfig config = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions)!;
		Assert.NotNull(config);

		string roundTrip = JsonSerializer.Serialize(config, StoreOptions);
		using JsonDocument doc = JsonDocument.Parse(roundTrip);
		Assert.True(
			doc.RootElement.TryGetProperty("newsFakeIpCompat", out JsonElement field),
			$"false 重载后字段丢失。roundTrip={roundTrip}");
		Assert.Equal(JsonValueKind.False, field.ValueKind);
	}

	[Fact]
	public void NewInstance_DefaultsToFalse()
	{
		AppConfig config = new AppConfig();
		string json = JsonSerializer.Serialize(config, StoreOptions);
		using JsonDocument doc = JsonDocument.Parse(json);
		Assert.True(
			doc.RootElement.TryGetProperty("newsFakeIpCompat", out JsonElement field),
			$"新 AppConfig 序列化缺少 newsFakeIpCompat。json={json}");
		Assert.Equal(JsonValueKind.False, field.ValueKind);
	}

	/* ===========================================================
	 * 2. PatchConfig（私有方法）通过反射调用
	 * =========================================================== */

	[Theory]
	[InlineData("{\"newsFakeIpCompat\":true}", true)]
	[InlineData("{\"newsFakeIpCompat\":false}", false)]
	[InlineData("{}", null)]
	public void PatchConfig_AppliesOrPreserves(string patchJson, bool? expected)
	{
		AppConfig config = MakeConfigWithCompat(true);

		using JsonDocument doc = JsonDocument.Parse(patchJson);
		InvokePatchConfig(config, doc.RootElement);

		bool actual = ReadCompatViaJson(config);
		if (expected.HasValue)
		{
			Assert.Equal(expected.Value, actual);
		}
		else
		{
			Assert.True(actual, "PATCH 省略 newsFakeIpCompat 时必须保留旧值 true");
		}
	}

	[Fact]
	public void PatchConfig_NonBooleanValue_DoesNotChangeExisting()
	{
		AppConfig config = MakeConfigWithCompat(true);

		using JsonDocument doc = JsonDocument.Parse("{\"newsFakeIpCompat\":\"yes\"}");
		InvokePatchConfig(config, doc.RootElement);

		bool actual = ReadCompatViaJson(config);
		Assert.True(actual, "非布尔字符串值绝不能隐式把 true 改成 false 或反向隐式开启");
	}

	[Fact]
	public void PatchConfig_NumberValue_DoesNotChangeExisting()
	{
		AppConfig config = MakeConfigWithCompat(false);

		using JsonDocument doc = JsonDocument.Parse("{\"newsFakeIpCompat\":1}");
		InvokePatchConfig(config, doc.RootElement);

		bool actual = ReadCompatViaJson(config);
		Assert.False(actual, "数字 1 不得被隐式当作 true（必须严格 bool 类型）");
	}

	[Fact]
	public void PatchConfig_OnlyOtherFields_DoesNotReset()
	{
		AppConfig config = MakeConfigWithCompat(true);

		using JsonDocument doc = JsonDocument.Parse("{\"playerName\":\"NewName\"}");
		InvokePatchConfig(config, doc.RootElement);

		bool actual = ReadCompatViaJson(config);
		Assert.True(actual, "PATCH 仅改其他字段时必须保留 newsFakeIpCompat=true");
	}

	/* ===========================================================
	 * 3. 真实 HTTP API：GET /api/state + POST /api/config
	 *    通过 IConfigStore 内存 store 隔离；不接触真实 config
	 * =========================================================== */

	[Fact]
	public async Task State_ReturnsNewsFakeIpCompat_BooleanField()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using HttpResponseMessage resp = await harness.GetAsync("/api/state");
			string body = await resp.Content.ReadAsStringAsync();
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

			using JsonDocument doc = JsonDocument.Parse(body);
			Assert.True(
				doc.RootElement.TryGetProperty("config", out JsonElement configSection),
				$"state 响应缺少 config 段：{body}");
			Assert.True(
				configSection.TryGetProperty("newsFakeIpCompat", out JsonElement field),
				$"config 段缺少 newsFakeIpCompat 字段。state={body}");
			Assert.True(
				field.ValueKind == JsonValueKind.True || field.ValueKind == JsonValueKind.False,
				$"newsFakeIpCompat 必须是布尔；当前 ValueKind={field.ValueKind}，state={body}");
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task UpdateConfig_MissingToken_Returns401()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/config")
			{
				Content = new StringContent("{\"newsFakeIpCompat\":true}", Encoding.UTF8, "application/json")
			};
			using HttpResponseMessage resp = await harness.SendWithoutTokenAsync(req);
			Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task UpdateConfig_WithToken_PersistsTrue()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using HttpResponseMessage resp = await harness.PostConfigAsync(new { newsFakeIpCompat = true });
			Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

			// 通过内存 store 验证持久化（不接触真实磁盘，不引用未实现属性）
			bool actual = ReadCompatFromStore(harness.MemoryStore);
			Assert.True(actual, "POST /api/config 后内存 store 必须更新 NewsFakeIpCompat=true");

			using HttpResponseMessage stateResp = await harness.GetAsync("/api/state");
			string stateBody = await stateResp.Content.ReadAsStringAsync();
			using JsonDocument stateDoc = JsonDocument.Parse(stateBody);
			Assert.Equal(
				JsonValueKind.True,
				stateDoc.RootElement.GetProperty("config").GetProperty("newsFakeIpCompat").ValueKind);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task UpdateConfig_OmittedField_PreservesExistingValue()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using (HttpResponseMessage r1 = await harness.PostConfigAsync(new { newsFakeIpCompat = true }))
			{
				Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
			}

			using (HttpResponseMessage r2 = await harness.PostConfigAsync(new { playerName = "X" }))
			{
				Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
			}

			// 内存 store 仍必须是 true
			bool actual = ReadCompatFromStore(harness.MemoryStore);
			Assert.True(actual, "PATCH 仅改其他字段时必须保留 newsFakeIpCompat=true");

			using HttpResponseMessage stateResp = await harness.GetAsync("/api/state");
			string stateBody = await stateResp.Content.ReadAsStringAsync();
			using JsonDocument stateDoc = JsonDocument.Parse(stateBody);
			Assert.Equal(
				JsonValueKind.True,
				stateDoc.RootElement.GetProperty("config").GetProperty("newsFakeIpCompat").ValueKind);
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	[Fact]
	public async Task State_Runtime_ReflectsUpdatedConfig_WithoutRestart()
	{
		await using TestHarness harness = await TestHarness.StartAsync();
		try
		{
			using (HttpResponseMessage r1 = await harness.GetAsync("/api/state"))
			{
				string body = await r1.Content.ReadAsStringAsync();
				using JsonDocument doc = JsonDocument.Parse(body);
				JsonElement cfg = doc.RootElement.GetProperty("config");
				if (cfg.TryGetProperty("newsFakeIpCompat", out JsonElement first))
				{
					Assert.Equal(JsonValueKind.False, first.ValueKind);
				}
			}

			using (HttpResponseMessage r2 = await harness.PostConfigAsync(new { newsFakeIpCompat = true }))
			{
				Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
			}

			using (HttpResponseMessage r3 = await harness.GetAsync("/api/state"))
			{
				string body = await r3.Content.ReadAsStringAsync();
				using JsonDocument doc = JsonDocument.Parse(body);
				JsonElement cfg = doc.RootElement.GetProperty("config");
				Assert.True(
					cfg.TryGetProperty("newsFakeIpCompat", out JsonElement second),
					$"PATCH true 之后再次 GET /api/state 仍未返回 newsFakeIpCompat 字段。state={body}");
				Assert.Equal(JsonValueKind.True, second.ValueKind);
			}
		}
		finally
		{
			await harness.DisposeAsync();
		}
	}

	/* ===========================================================
	 * 4. 反射 + 路径辅助
	 * =========================================================== */

	private static void InvokePatchConfig(AppConfig config, JsonElement root)
	{
		MethodInfo? method = typeof(LocalApiService).GetMethod(
			"PatchConfig",
			BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(method);
		method!.Invoke(null, new object[] { config, root });
	}

	private static AppConfig MakeConfigWithCompat(bool value)
	{
		string json = $"{{\"newsFakeIpCompat\":{(value ? "true" : "false")}}}";
		return JsonSerializer.Deserialize<AppConfig>(json, ReadOptions)!;
	}

	private static bool ReadCompatViaJson(AppConfig config)
	{
		string json = JsonSerializer.Serialize(config, StoreOptions);
		using JsonDocument doc = JsonDocument.Parse(json);
		return doc.RootElement.GetProperty("newsFakeIpCompat").GetBoolean();
	}

	// 从内存 store 读取（不引用不存在的属性，不接触磁盘）
	private static bool ReadCompatFromStore(MemoryConfigStore store)
	{
		string json = JsonSerializer.Serialize(store.Load(), StoreOptions);
		using JsonDocument doc = JsonDocument.Parse(json);
		return doc.RootElement.GetProperty("newsFakeIpCompat").GetBoolean();
	}

	/* ===========================================================
	 * 5. 隔离测试环境：内存 store + LocalApiService + 真实配置守护
	 *
	 * review-fix-1 关键设计：
	 *   - 完全不写入/读取/删除 %APPDATA%\MCMiniLauncher\config.{json,bak,prev}
	 *   - 通过 IConfigStore.OverrideConfigStoreForTest 注入 MemoryConfigStore
	 *   - 每个测试独立 store（不是 shared static）
	 *   - 测试结束后校验真实 config 三文件状态/哈希与测试前一致
	 *   - 启动/停止失败、override 失败都让测试失败（不吞异常）
	 * =========================================================== */

	private sealed class TestHarness : IAsyncDisposable
	{
		public MemoryConfigStore MemoryStore { get; }
		public HttpClient Client { get; }
		public string SessionToken => LocalApiService.SessionTokenForTests;
		private readonly IDisposable _configScope;
		private readonly bool _serviceStartedHere;
		private readonly int _port;
		private readonly RealConfigSnapshot _preSnapshot;
		private bool _disposed;

		private TestHarness(MemoryConfigStore store, IDisposable configScope, int port, bool serviceStartedHere, RealConfigSnapshot pre)
		{
			MemoryStore = store;
			_configScope = configScope;
			_port = port;
			_serviceStartedHere = serviceStartedHere;
			_preSnapshot = pre;
			Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(8) };
		}

		public static async Task<TestHarness> StartAsync()
		{
			// 1) 测试前置：守护真实 config 状态
			RealConfigSnapshot pre = RealConfigSnapshot.Capture();

			// 2) 严格隔离断言：本测试 store 绝不能位于生产配置路径
			// MemoryConfigStore 没有磁盘路径，所以天然满足；这里显式记录
			string productionPath = PlatformPaths.GetConfigPath();
			Assert.True(
				!string.Equals(productionPath, "<memory>", StringComparison.OrdinalIgnoreCase),
				"PlatformPaths.GetConfigPath() 解析异常");

			// 3) 准备内存 store
			var store = new MemoryConfigStore();

			// 4) 注入 override；失败必须让测试失败
			IDisposable scope;
			try
			{
				scope = LocalApiService.OverrideConfigStoreForTest(store);
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException("OverrideConfigStoreForTest 失败：" + ex.Message, ex);
			}
			Assert.NotNull(scope);

			// 5) 启动 LocalApiService
			// 严格：若已运行则不接管（不在未启动端口欺骗客户端）；直接 fail
			if (LocalApiService.IsRunning)
			{
				scope.Dispose();
				throw new InvalidOperationException("LocalApiService 已在运行；测试不能启动新端口。");
			}

			// A21（第 49 轮）：本夹具原先只重定向了配置存储，恢复区/二分落盘/诊断报告会落到真实 %APPDATA%。
			TestIsolationRoots.InstallCurrent();
			int port = await AcquirePortAsync();
			LocalApiService.Start(port);
			if (!LocalApiService.IsRunning)
			{
				scope.Dispose();
				throw new InvalidOperationException("LocalApiService.Start 后未运行。");
			}
			if (LocalApiService.Port != port)
			{
				LocalApiService.Stop();
				scope.Dispose();
				throw new InvalidOperationException($"端口冲突：期望 {port}，实际 {LocalApiService.Port}");
			}

			return new TestHarness(store, scope, port, serviceStartedHere: true, pre);
		}

		public Task<HttpResponseMessage> GetAsync(string path)
		{
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, path);
			req.Headers.Add("X-Pineglow-Token", SessionToken);
			return Client.SendAsync(req);
		}

		public Task<HttpResponseMessage> PostConfigAsync(object body)
		{
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "/api/config")
			{
				Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
			};
			req.Headers.Add("X-Pineglow-Token", SessionToken);
			return Client.SendAsync(req);
		}

		public Task<HttpResponseMessage> SendWithoutTokenAsync(HttpRequestMessage req)
		{
			// 不附加 X-Pineglow-Token
			return Client.SendAsync(req);
		}

		public async ValueTask DisposeAsync()
		{
			if (_disposed) return;
			_disposed = true;

			Exception? firstError = null;

			// 1) 停止 LocalApiService
			try
			{
				if (_serviceStartedHere && LocalApiService.IsRunning)
				{
					LocalApiService.Stop();
				}
			}
			catch (Exception ex)
			{
				firstError ??= new InvalidOperationException("LocalApiService.Stop 失败：" + ex.Message, ex);
			}
			// A21：释放三个存储根的重定向（恢复区 / 二分落盘 / 诊断报告）
			try { TestIsolationRoots.ReleaseCurrent(); } catch { }

			// 2) 释放 HttpClient
			try
			{
				Client.Dispose();
			}
			catch (Exception ex)
			{
				firstError ??= new InvalidOperationException("HttpClient.Dispose 失败：" + ex.Message, ex);
			}

			// 3) 释放 override scope（恢复生产 ConfigStore）
			try
			{
				_configScope.Dispose();
			}
			catch (Exception ex)
			{
				firstError ??= new InvalidOperationException("ConfigStoreOverrideScope.Dispose 失败：" + ex.Message, ex);
			}

			// 4) 守护真实 config 状态：前后必须一致
			RealConfigSnapshot post = RealConfigSnapshot.Capture();
			if (!_preSnapshot.Equals(post))
			{
				firstError = new InvalidOperationException(
					$"真实 config 状态被改动！\npre: {_preSnapshot}\npost: {post}");
			}

			if (firstError != null)
			{
				throw firstError;
			}
		}

		/// <summary>找一个可绑定的端口并保存给 LocalApiService.Start。</summary>
		private static async Task<int> AcquirePortAsync()
		{
			int seed = Interlocked.Increment(ref _nextPortSeed) | 1;
			for (int i = 0; i < 200; i++)
			{
				int p = 49000 + ((seed + i * 37) % 16000);
				try
				{
					var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, p);
					listener.Start();
					listener.Stop();
					return p;
				}
				catch
				{
					// port taken; try next
				}
			}
			throw new InvalidOperationException("找不到可用端口（review-fix-1 端口搜索耗尽）");
		}

		private static int _nextPortSeed = 0;
	}

	/// <summary>纯内存 store；不接触磁盘、SecretStore、.bak/.prev。</summary>
	private sealed class MemoryConfigStore : LocalApiService.IConfigStore
	{
		private AppConfig _current = new AppConfig();
		public AppConfig Load() => _current;
		public void Save(AppConfig config) => _current = config;
	}

	/// <summary>真实 config 三文件存在性 + SHA-256 快照。</summary>
	private sealed class RealConfigSnapshot : IEquatable<RealConfigSnapshot>
	{
		public string ConfigPath { get; }
		public string? ConfigJsonSha { get; }
		public long? ConfigJsonSize { get; }
		public string? ConfigBakSha { get; }
		public long? ConfigBakSize { get; }
		public string? ConfigPrevSha { get; }
		public long? ConfigPrevSize { get; }

		private RealConfigSnapshot(string configPath)
		{
			ConfigPath = configPath;
			(ConfigJsonSha, ConfigJsonSize) = ReadOrNull(Path.Combine(configPath, "config.json"));
			(ConfigBakSha, ConfigBakSize) = ReadOrNull(Path.Combine(configPath, "config.json.bak"));
			(ConfigPrevSha, ConfigPrevSize) = ReadOrNull(Path.Combine(configPath, "config.json.prev"));
		}

		public static RealConfigSnapshot Capture() => new RealConfigSnapshot(PlatformPaths.GetConfigDirectory());

		public bool Equals(RealConfigSnapshot? other)
		{
			if (other == null) return false;
			return ConfigPath == other.ConfigPath
				&& ConfigJsonSha == other.ConfigJsonSha && ConfigJsonSize == other.ConfigJsonSize
				&& ConfigBakSha == other.ConfigBakSha && ConfigBakSize == other.ConfigBakSize
				&& ConfigPrevSha == other.ConfigPrevSha && ConfigPrevSize == other.ConfigPrevSize;
		}

		public override bool Equals(object? obj) => Equals(obj as RealConfigSnapshot);
		public override int GetHashCode() => ConfigPath.GetHashCode();
		public override string ToString()
		{
			return $"json={ConfigJsonSha ?? "MISSING"}/{ConfigJsonSize}, " +
				$"bak={ConfigBakSha ?? "MISSING"}/{ConfigBakSize}, " +
				$"prev={ConfigPrevSha ?? "MISSING"}/{ConfigPrevSize}";
		}

		private static (string? sha, long? size) ReadOrNull(string path)
		{
			if (!File.Exists(path)) return (null, null);
			using FileStream fs = File.OpenRead(path);
			using var sha = System.Security.Cryptography.SHA256.Create();
			byte[] hash = sha.ComputeHash(fs);
			return (Convert.ToHexString(hash), fs.Length);
		}
	}
}
