using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// MR20：自更新安全、失效与恢复。
///
/// 本包补上的**最大缺口**：原 `ApplyUpdate` 直接 `copy /Y` 覆盖旧 exe，**没有备份、没有校验、没有回滚** ——
/// 一旦新包有问题，用户就再也回不到能跑的版本（MG3 风险清单里的"更新不能回退"）。
///
/// 本文件的关键点是：**替换脚本被真实执行**（不是只断言字符串里出现了关键字）：
///   1. 正常路径：备份 → 替换 → 逐字节校验 → applied，备份里留着旧版本；
///   2. 备份失败：什么都不做，记 backup_failed，旧版本原样可跑；
///   3. 替换失败：用备份放回，记 replace_failed，旧版本内容不变；
///   4. 任何失败路径都会启动旧版本。
///
/// 明确不覆盖：真实发布端点、真实安装目录替换（需另行授权）、真实重启（测试用空操作代替启动命令）。
/// </summary>
public class UpdateRecoveryTests
{
	private readonly ITestOutputHelper _out;

	public UpdateRecoveryTests(ITestOutputHelper output) => _out = output;

	private sealed class TempDir : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pineglow-update-" + Guid.NewGuid().ToString("N"));

		public TempDir() => Directory.CreateDirectory(Path);

		public string File(string name) => System.IO.Path.Combine(Path, name);

		public void Dispose()
		{
			try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { }
		}
	}

	private static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

	/// <summary>按 URL 返回指定字节的假 handler（用于下载/截断用例）。</summary>
	private sealed class BytesHandler : HttpMessageHandler
	{
		private readonly Dictionary<string, byte[]> _payloads;

		public BytesHandler(Dictionary<string, byte[]> payloads) => _payloads = payloads;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string url = request.RequestUri!.ToString();
			if (!_payloads.TryGetValue(url, out byte[]? body))
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
		}
	}

	/* ===================== 1. 版本比较：绝不回退 ===================== */

	[Theory]
	[InlineData("1.0.1", "1.0.0", true)]
	[InlineData("1.0.0", "1.0.1", false)]
	[InlineData("1.0.0", "1.0.0", false)]
	[InlineData("1.0.10", "1.0.9", true)]
	[InlineData("1.0", "1.0.0", false)]
	[InlineData("2.0.0", "1.9.9", true)]
	[InlineData("1.0.1-beta", "1.0.0", false)] // 非数字段按 0 处理：不把预发布当升级
	public void IsNewerVersion_NeverDowngrades(string latest, string current, bool expected)
	{
		Assert.Equal(expected, UpdateService.IsNewerVersion(latest, current));
	}

	/* ===================== 2. 平台资产匹配 ===================== */

	[Fact]
	public void AssetCompatibility_RejectsForeignPlatform()
	{
		bool ok = UpdateService.IsAssetCompatible(
			"https://example.com/Pineglow-Launcher-1.0.1-linux-x64-single-file", "win-x64", out string reason);

		_out.WriteLine("[foreign] " + reason);
		Assert.False(ok);
		Assert.Contains("不匹配", reason);
		Assert.Contains("linux-x64", reason);
	}

	[Fact]
	public void AssetCompatibility_AcceptsMatchingPlatform()
	{
		bool ok = UpdateService.IsAssetCompatible(
			"https://example.com/Pineglow-Launcher-1.0.1-win-x64-single-file.exe", "win-x64", out string reason);

		Assert.True(ok);
		Assert.Empty(reason);
	}

	[Fact]
	public void AssetCompatibility_AcceptsButFlagsWhenNoPlatformTokenIsPresent()
	{
		bool ok = UpdateService.IsAssetCompatible("https://example.com/latest.exe", "win-x64", out string reason);

		_out.WriteLine("[no token] " + reason);
		Assert.True(ok);
		Assert.Contains("无法断言平台匹配", reason);
	}

	/* ===================== 3. 下载截断 ===================== */

	[Fact]
	public async Task TruncatedDownload_IsRejectedWithAPreciseReason()
	{
		byte[] full = Encoding.UTF8.GetBytes(new string('A', 4096));
		byte[] truncated = full.Take(1000).ToArray();
		const string url = "https://cdn.example.com/Pineglow-Launcher-9.9.9-win-x64-single-file.exe";

		using HttpClient http = new HttpClient(new BytesHandler(new Dictionary<string, byte[]> { [url] = truncated }));
		var manifest = new UpdateManifest
		{
			Version = "9.9.9",
			Url = url,
			Sha256 = Sha256Hex(full),
			Size = full.Length
		};

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => UpdateService.DownloadUpdateAsync(http, manifest, null, CancellationToken.None));

		_out.WriteLine("[truncated] " + ex.Message);
		Assert.Contains("下载不完整", ex.Message);
		Assert.Contains("4096", ex.Message);
		Assert.Contains("1000", ex.Message);
	}

	[Fact]
	public async Task ForeignPlatformAsset_IsRejectedBeforeDownloading()
	{
		const string url = "https://cdn.example.com/Pineglow-Launcher-9.9.9-linux-x64-single-file";
		using HttpClient http = new HttpClient(new BytesHandler(new Dictionary<string, byte[]> { [url] = new byte[] { 1, 2, 3 } }));
		var manifest = new UpdateManifest { Version = "9.9.9", Url = url, Sha256 = new string('a', 64), Size = 3 };

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => UpdateService.DownloadUpdateAsync(http, manifest, null, CancellationToken.None));

		_out.WriteLine("[foreign asset] " + ex.Message);
		Assert.Contains("不匹配", ex.Message);
	}

	/* ===================== 4. 替换脚本被真实执行 ===================== */

	private sealed record ApplyOutcome(string ScriptOutput, string TargetContent, string? BackupContent, UpdateService.ApplyResult? State);

	/// <summary>
	/// 真实跑一遍生成的替换脚本。启动命令替换为空操作（脚本其余部分与产品生成的完全一致）。
	/// </summary>
	private ApplyOutcome RunApplyScript(string scriptExt, string targetPath, string downloadedPath, string backupPath, string statePath)
	{
		return RunApplyScriptCore(scriptExt, targetPath, downloadedPath, backupPath, statePath,
			scriptExt == ".bat" ? "rem noop-restart" : "true");
	}

	/// <summary>
	/// 同上，但**由调用方给出真实会执行的重启命令**。
	///
	/// 2026-10-10 补测：既有用例把重启命令固定成空操作，于是
	/// 「任何失败路径都会启动旧版本」只在字符串层面被断言过（脚本里出现两次 <c>start</c>），
	/// 没有执行证据。更新链的最后一个真实缺口正是「真实重启（测试用空操作代替启动命令）」，
	/// 这里用一条真会落地副作用的命令把两个重启点都跑出来。
	/// </summary>
	private ApplyOutcome RunApplyScriptCore(string scriptExt, string targetPath, string downloadedPath, string backupPath, string statePath, string restartCommand)
	{
		string scriptPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(statePath)!, "apply-update" + scriptExt);
		string script = UpdateService.BuildApplyScript(scriptExt, targetPath, downloadedPath, backupPath, statePath, "9.9.9", restartCommand);
		File.WriteAllText(scriptPath, script);

		var psi = scriptExt == ".bat"
			? new ProcessStartInfo("cmd.exe", "/c \"" + scriptPath + "\"")
			: new ProcessStartInfo("/bin/sh", scriptPath);
		psi.UseShellExecute = false;
		psi.CreateNoWindow = true;
		psi.RedirectStandardOutput = true;
		psi.RedirectStandardError = true;

		using Process proc = Process.Start(psi)!;
		string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
		proc.WaitForExit(30000);

		return new ApplyOutcome(
			output,
			File.Exists(targetPath) ? File.ReadAllText(targetPath) : string.Empty,
			File.Exists(backupPath) ? File.ReadAllText(backupPath) : null,
			UpdateService.ReadApplyResult(statePath));
	}

	[Fact]
	public void ApplyScript_HappyPath_BacksUpReplacesVerifiesAndRecordsApplied()
	{
		if (!OperatingSystem.IsWindows())
		{
			return; // 本用例针对 .bat 路径；Unix 分支结构相同但本机不可验证
		}

		using var dir = new TempDir();
		string target = dir.File("pineglow-not-running.exe");
		string downloaded = dir.File("new.exe");
		string backup = dir.File("pineglow-not-running.exe.bak");
		string state = dir.File("apply-state.json");
		File.WriteAllText(target, "OLD-CONTENT");
		File.WriteAllText(downloaded, "NEW-CONTENT");

		ApplyOutcome outcome = RunApplyScript(".bat", target, downloaded, backup, state);
		_out.WriteLine($"[happy] state={outcome.State?.Result} target={outcome.TargetContent} backup={outcome.BackupContent}");

		Assert.Equal("applied", outcome.State?.Result);
		Assert.True(outcome.State!.Applied);
		Assert.Equal("NEW-CONTENT", outcome.TargetContent);
		Assert.Equal("OLD-CONTENT", outcome.BackupContent); // 旧版本被保留，可回退
	}

	[Fact]
	public void ApplyScript_WhenBackupFails_DoesNothingAndRecordsBackupFailed()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		using var dir = new TempDir();
		string target = dir.File("pineglow-not-running.exe");
		string downloaded = dir.File("new.exe");
		// 备份路径落在一个不存在的目录里 → copy 必然失败
		string backup = System.IO.Path.Combine(dir.Path, "no-such-dir", "backup.exe");
		string state = dir.File("apply-state.json");
		File.WriteAllText(target, "OLD-CONTENT");
		File.WriteAllText(downloaded, "NEW-CONTENT");

		ApplyOutcome outcome = RunApplyScript(".bat", target, downloaded, backup, state);
		_out.WriteLine($"[backup_failed] state={outcome.State?.Result} target={outcome.TargetContent}");

		Assert.Equal("backup_failed", outcome.State?.Result);
		Assert.False(outcome.State!.Applied);
		Assert.Equal("OLD-CONTENT", outcome.TargetContent); // 没动过旧版本
	}

	[Fact]
	public void ApplyScript_WhenReplaceFails_RollsBackToTheOldContent()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		using var dir = new TempDir();
		string target = dir.File("pineglow-not-running.exe");
		// 把"下载产物"做成一个目录 → copy /Y 目录到文件必然失败
		string downloaded = dir.File("new-as-directory");
		Directory.CreateDirectory(downloaded);
		string backup = dir.File("pineglow-not-running.exe.bak");
		string state = dir.File("apply-state.json");
		File.WriteAllText(target, "OLD-CONTENT");

		ApplyOutcome outcome = RunApplyScript(".bat", target, downloaded, backup, state);
		_out.WriteLine($"[replace_failed] state={outcome.State?.Result} target={outcome.TargetContent} backup={outcome.BackupContent}");

		Assert.Equal("replace_failed", outcome.State?.Result);
		Assert.False(outcome.State!.Applied);
		Assert.Equal("OLD-CONTENT", outcome.TargetContent); // 回滚：旧版本内容原样
		Assert.Equal("OLD-CONTENT", outcome.BackupContent);
	}

	[Fact]
	public void ApplyScript_ContainsBackupVerifyAndRollbackSteps()
	{
		string script = UpdateService.BuildApplyScript(
			".bat", @"C:\app\launcher.exe", @"C:\updates\new.exe", @"C:\app\launcher.exe.bak",
			@"C:\updates\apply-state.json", "1.2.3", "start \"\" \"%TARGET%\"");

		// 结构断言（补充上面三个真实执行用例）
		Assert.Contains("copy /Y \"%TARGET%\" \"%BACKUP%\"", script);   // 先备份
		Assert.Contains("fc /b \"%NEW%\" \"%TARGET%\"", script);       // 逐字节校验
		Assert.Contains("backup_failed", script);
		Assert.Contains("replace_failed", script);
		Assert.Contains("verify_failed", script);
		Assert.Contains("applied", script);
		// 失败路径也要启动旧版本
		Assert.Equal(2, script.Split(new[] { "start \"\" \"%TARGET%\"" }, StringSplitOptions.None).Length - 1);
		// 生成的脚本里不应出现非 ASCII（cmd 的编码风险）
		Assert.True(script.All(c => c < 128), "替换脚本必须是纯 ASCII");
	}

	/* ========== 4b. 重启命令被真实执行（2026-10-10 补测：更新链最后一处证据缺口） ========== */

	[Fact]
	public void ApplyScript_OnSuccess_ActuallyRunsTheRestartCommand()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		using var dir = new TempDir();
		string target = dir.File("pineglow-not-running.exe");
		string downloaded = dir.File("new.exe");
		string backup = dir.File("pineglow-not-running.exe.bak");
		string state = dir.File("apply-state.json");
		string marker = dir.File("restart-marker-on-success.txt");
		File.WriteAllText(target, "OLD-CONTENT");
		File.WriteAllText(downloaded, "NEW-CONTENT");

		// 真实会执行的重启命令（替换掉默认的空操作）：真的落下副作用才说明它被执行了
		ApplyOutcome outcome = RunApplyScriptCore(".bat", target, downloaded, backup, state, "echo restarted>\"" + marker + "\"");

		_out.WriteLine($"[restart-success] state={outcome.State?.Result} markerExists={File.Exists(marker)}");
		Assert.Equal("applied", outcome.State?.Result);
		Assert.True(File.Exists(marker), "成功路径必须真的执行重启命令，否则用户更新后不会被拉起来");
		Assert.Equal("NEW-CONTENT", outcome.TargetContent);
		Assert.Equal("OLD-CONTENT", outcome.BackupContent);
	}

	[Fact]
	public void ApplyScript_OnRollback_StillRunsTheRestartCommand()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		using var dir = new TempDir();
		string target = dir.File("pineglow-not-running.exe");
		string downloaded = dir.File("new-as-directory"); // copy 目录到文件必然失败 → 走回滚
		Directory.CreateDirectory(downloaded);
		string backup = dir.File("pineglow-not-running.exe.bak");
		string state = dir.File("apply-state.json");
		string marker = dir.File("restart-marker-on-rollback.txt");
		File.WriteAllText(target, "OLD-CONTENT");

		ApplyOutcome outcome = RunApplyScriptCore(".bat", target, downloaded, backup, state, "echo restarted>\"" + marker + "\"");

		_out.WriteLine($"[restart-rollback] state={outcome.State?.Result} markerExists={File.Exists(marker)} target={outcome.TargetContent}");
		Assert.Equal("replace_failed", outcome.State?.Result);
		Assert.False(outcome.State!.Applied);
		Assert.Equal("OLD-CONTENT", outcome.TargetContent);
		Assert.True(File.Exists(marker),
			"回滚路径同样必须真的执行重启命令：更新失败不能让用户既没更新成功、又没有任何可运行界面");
	}

	/* ===================== 5. apply 结果记录 ===================== */

	[Fact]
	public void ReadApplyResult_IsNullForMissingOrCorruptFiles()
	{
		using var dir = new TempDir();
		string missing = dir.File("nope.json");
		Assert.Null(UpdateService.ReadApplyResult(missing));

		string corrupt = dir.File("corrupt.json");
		File.WriteAllText(corrupt, "{ not json");
		Assert.Null(UpdateService.ReadApplyResult(corrupt));
	}

	[Fact]
	public void ReadApplyResult_ParsesAppliedAndFailedRecords()
	{
		using var dir = new TempDir();
		string state = dir.File("apply-state.json");

		File.WriteAllText(state, "{\"result\":\"applied\",\"version\":\"1.2.3\"}");
		UpdateService.ApplyResult? applied = UpdateService.ReadApplyResult(state);
		Assert.NotNull(applied);
		Assert.True(applied!.Applied);
		Assert.Equal("1.2.3", applied.Version);

		File.WriteAllText(state, "{\"result\":\"verify_failed\",\"version\":\"1.2.3\"}");
		UpdateService.ApplyResult? failed = UpdateService.ReadApplyResult(state);
		Assert.NotNull(failed);
		Assert.False(failed!.Applied);
	}

	/* ===================== 6. 信任链声明必须准确 ===================== */

	[Fact]
	public void TrustModelNote_StatesThatSha256IsNotIdentityAuthentication()
	{
		string note = UpdateService.TrustModelNote;

		_out.WriteLine("[trust] " + note);
		Assert.Contains("不提供发布者身份认证", note);
		Assert.Contains("篡改", note);
		Assert.Contains("签名清单", note);
	}
}
