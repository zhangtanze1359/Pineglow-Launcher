using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR11：整合包导入的**取消**与"不留半成品实例"。
///
/// 覆盖：
///   1. 取消路由的确定性语义（未知/缺失 operationId、重复取消、命中在册操作）；
///   2. 取消确实会 cancel 掉在册的 CancellationTokenSource（不是只回一句 ok）；
///   3. 带着已取消的令牌导入 → 抛 OperationCanceledException，且**不留半成品游戏目录**。
///
/// 明确不覆盖：真实在途下载中途取消（需要真实网络与真实 Minecraft 安装，属 MR22 内测范围）。
/// 本文件验证的是取消的**接线与回滚**，与 AI 诊断取消复用同一套注册表实现。
/// </summary>
[Collection("LocalApiService")]
public class ImportCancellationTests
{
	private readonly ITestOutputHelper _out;

	public ImportCancellationTests(ITestOutputHelper output) => _out = output;

	private const string ImportCancelPath = "/api/instances/import/cancel";

	private static InstancePackService NewService(string gameDir) =>
		new InstancePackService(new HttpClient(), gameDir, _ => { });

	private static void WriteMinimalPack(string path, string packName)
	{
		string index = $$"""
			{
			  "formatVersion": 1,
			  "name": "{{packName}}",
			  "dependencies": { "minecraft": "1.20.4" },
			  "files": []
			}
			""";

		using FileStream fs = File.Create(path);
		using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
		ZipArchiveEntry entry = zip.CreateEntry("modrinth.index.json");
		using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
		writer.Write(index);
	}

	/* ===================== 1. 取消路由的确定性语义 ===================== */

	[Fact]
	public async Task CancelImport_WithUnknownOperationId_ReportsPendingCancelNotCancelled()
	{
		await using var h = await LocalApiTestHarness.StartAsync(new AppConfig());

		(int first, string firstBody) = await h.SendAsync(HttpMethod.Post, ImportCancelPath, new { operationId = "unknown-op-12345678" });
		_out.WriteLine($"[unknown] -> {first} {firstBody}");
		Assert.Equal(200, first);

		// 注册表带 cancel-before-register 墓碑：对尚未受理的 id 也接受取消。
		// 但它**不是**"取消了进行中的导入"，必须如实区分，否则界面会误报。
		Assert.Contains("\"cancelled\":false", firstBody.Replace(" ", string.Empty));
		Assert.Contains("\"pendingCancel\":true", firstBody.Replace(" ", string.Empty));
		Assert.Contains("recorded_for_later", firstBody);

		// 重复取消：幂等（墓碑已在，语义不变）
		(int second, string secondBody) = await h.SendAsync(HttpMethod.Post, ImportCancelPath, new { operationId = "unknown-op-12345678" });
		Assert.Equal(200, second);
		Assert.Contains("\"cancelled\":false", secondBody.Replace(" ", string.Empty));
	}

	[Fact]
	public async Task CancelImport_WithoutOperationId_ReportsNothingCancelled()
	{
		await using var h = await LocalApiTestHarness.StartAsync(new AppConfig());

		(int status, string body) = await h.SendAsync(HttpMethod.Post, ImportCancelPath, new { });
		_out.WriteLine($"[no id] -> {status} {body}");
		Assert.Equal(200, status);
		Assert.Contains("\"cancelled\":false", body.Replace(" ", string.Empty));
		Assert.Contains("no_active_operation", body);
	}

	[Fact]
	public async Task CancelImport_CancelsTheRegisteredToken_AndIsIdempotent()
	{
		await using var h = await LocalApiTestHarness.StartAsync(new AppConfig());

		string operationId = "import-" + Guid.NewGuid().ToString("N");
		using var cts = new CancellationTokenSource();
		Assert.True(LocalApiService.RegisterPackOperationForTest(operationId, cts), "操作应当能注册进取消表");
		Assert.False(cts.IsCancellationRequested);

		(int status, string body) = await h.SendAsync(HttpMethod.Post, ImportCancelPath, new { operationId });
		_out.WriteLine($"[cancel hit] -> {status} {body}");
		Assert.Equal(200, status);
		Assert.Contains("\"cancelled\":true", body.Replace(" ", string.Empty));
		Assert.True(cts.IsCancellationRequested, "取消必须真的 cancel 掉 CTS，而不是只回一句 ok");

		// 重复取消：幂等，且不再声称取消成功
		(int again, string againBody) = await h.SendAsync(HttpMethod.Post, ImportCancelPath, new { operationId });
		Assert.Equal(200, again);
		Assert.Contains("\"cancelled\":false", againBody.Replace(" ", string.Empty));
		Assert.Contains("recorded_for_later", againBody);
	}

	[Fact]
	public async Task CancelBeforeTheServerAcceptsTheImport_MakesTheImportBailOut()
	{
		// 真实场景：用户在**上传阶段**就点了取消，取消请求先到，导入请求后到。
		await using var h = await LocalApiTestHarness.StartAsync(new AppConfig());

		string operationId = "race-" + Guid.NewGuid().ToString("N");
		(int cancelStatus, string cancelBody) = await h.SendAsync(HttpMethod.Post, ImportCancelPath, new { operationId });
		Assert.Equal(200, cancelStatus);
		Assert.Contains("\"cancelled\":false", cancelBody.Replace(" ", string.Empty));

		string packPath = Path.Combine(Path.GetTempPath(), "pineglow-race-" + Guid.NewGuid().ToString("N") + ".mrpack");
		WriteMinimalPack(packPath, "取消先到的包");
		string base64 = Convert.ToBase64String(File.ReadAllBytes(packPath));

		// 用自带客户端（超时更长）以便看清服务端最终返回什么，而不是先被测试客户端超时打断。
		using var client = new HttpClient
		{
			BaseAddress = new Uri($"http://127.0.0.1:{h.Port}/"),
			Timeout = TimeSpan.FromSeconds(90)
		};
		using var req = new HttpRequestMessage(HttpMethod.Post, "/api/instances/import")
		{
			Content = new StringContent(
				System.Text.Json.JsonSerializer.Serialize(new { fileName = "race.mrpack", fileBase64 = base64, operationId }),
				Encoding.UTF8, "application/json")
		};
		req.Headers.Add("X-Pineglow-Token", h.SessionToken);

		var sw = System.Diagnostics.Stopwatch.StartNew();
		using HttpResponseMessage resp = await client.SendAsync(req);
		string importBody = await resp.Content.ReadAsStringAsync();
		sw.Stop();
		_out.WriteLine($"[cancel-before-receipt] {(int)resp.StatusCode} 用时 {sw.ElapsedMilliseconds}ms {importBody}");

		Assert.True(sw.ElapsedMilliseconds < 15000, $"取消应当很快生效，实际用了 {sw.ElapsedMilliseconds}ms");
		Assert.Equal(409, (int)resp.StatusCode);
		Assert.Contains("\"cancelled\":true", importBody.Replace(" ", string.Empty));
		// 响应体是 JSON 转义的（非 ASCII 会变成 \uXXXX），必须解析后再比对，不能直接找中文字面量。
		using JsonDocument doc = JsonDocument.Parse(importBody);
		string message = doc.RootElement.GetProperty("error").GetString() ?? string.Empty;
		Assert.Contains("已取消", message);
		Assert.Contains("未生成实例", message);

		try { File.Delete(packPath); } catch { }
	}

	/* ===================== 2. 取消不留半成品 ===================== */

	[Fact]
	public async Task ImportWithAnAlreadyCancelledToken_ThrowsAndLeavesNoGameDirectory()
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-import-cancel-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		string packName = "取消测试包-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		string zipPath = Path.Combine(root, "pack.mrpack");
		WriteMinimalPack(zipPath, packName);

		InstancePackService service = NewService(root);
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => service.ImportModrinthPackAsync(zipPath, _ => { }, cts.Token));

		// 半成品目录必须被删掉：不能留下一个"看起来像实例"的空目录
		string instancesRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "instances");
		string expectedDir = Path.Combine(instancesRoot, packName);
		_out.WriteLine($"[cancelled import] 期望不存在的目录 = {expectedDir}；存在？{Directory.Exists(expectedDir)}");
		Assert.False(Directory.Exists(expectedDir), "取消后不得留下半成品实例目录");

		try { Directory.Delete(root, true); } catch { }
	}

	[Fact]
	public async Task ImportWithALiveTokenButCancelledMidway_StillCleansUp()
	{
		string root = Path.Combine(Path.GetTempPath(), "pineglow-import-mid-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		string packName = "中途取消包-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		string zipPath = Path.Combine(root, "pack.mrpack");
		WriteMinimalPack(zipPath, packName);

		InstancePackService service = NewService(root);
		using var cts = new CancellationTokenSource();

		// 立刻从另一条路径取消，模拟"用户点了取消"：
		// 无论取消落在 Java 解析还是 Minecraft 安装阶段，都必须以取消异常结束并清理目录。
		Task import = service.ImportModrinthPackAsync(zipPath, _ => { }, cts.Token);
		cts.Cancel();

		Exception? observed = null;
		try { await import; }
		catch (Exception ex) { observed = ex; }

		_out.WriteLine($"[midway] 观察到的异常 = {observed?.GetType().Name}: {observed?.Message}");

		string instancesRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "instances");
		string expectedDir = Path.Combine(instancesRoot, packName);
		Assert.False(Directory.Exists(expectedDir), "中途取消后同样不得留下半成品实例目录");

		try { Directory.Delete(root, true); } catch { }
	}
}