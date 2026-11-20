using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Models;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 自更新。
/// <para>
/// 自更新的检查、下载、状态查询与应用。更新清单地址链可被测试缝替换，
/// 生产路径不打真实网络替代品。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/* ================= 自更新 ================= */

	/// <summary>
	/// 下载完成时从清单里记下的 SHA256。apply 前用它**再对磁盘复核一次**。
	///
	/// 为什么需要（v1.0.5，RED 4）：下载阶段确实校验了 size + SHA256，但**从下载完成到
	/// 用户点“立即重启更新”之间**，磁盘上的包可能被截断/改写/替换；而替换脚本只做
	/// `fc /b NEW TARGET`（拷贝完整性），不是对清单声明的 SHA256 再校验一次。
	/// 实测：把包换成 100KB 截断文件后，脚本仍然写出 `applied`，把可用旧 EXE 换成了无法启动的坏文件。
	/// 本字段只用于 apply 前复核，**不改变**下载阶段的任何校验与脚本结构。
	/// </summary>
	private static string UpdateExpectedSha256 = string.Empty;

	/// <summary>
	/// 下载代次（epoch）。每次「开始一次新下载」+1；下载 worker 写回结果前必须比对代次，
	/// 代次不一致就丢弃。
	///
	/// 为什么需要：`HandleStartUpdateDownloadAsync` 原本在 **`await FetchManifestChainAsync`
	/// 之前**只检查 `state == "downloading"`；两个请求可以同时通过那个检查、各自跨 await 取清单，
	/// 然后先后把状态写成 downloading 并各起一个 worker —— 后完成的那次会把先完成的结果覆盖掉
	/// （旧结果覆盖新状态）。有了代次，「落伍的启动请求」与「落伍的 worker」都写不进去。
	/// </summary>
	private static int UpdateEpoch;

	/// <summary>
	/// apply 的认领令牌：不为 0 表示**已经有一次合法的替换被认领并正在推进**。
	/// 认领在 `UpdateGate` 下原子完成，且发生在**文件校验与启动脚本之前**。
	/// </summary>
	private static int UpdateApplyClaim;

	/// <summary>认领次数（只用于日志与测试断言「只认领了一次」）。</summary>
	private static int UpdateApplyClaimCount;

	/// <summary>
	/// 测试用：把自更新状态机复位到初始态（idle，无认领、无包、无错误），并推进代次作废在飞的 worker。
	/// 自更新状态是**进程级静态字段**，同一 collection 里的用例会互相污染；
	/// 每个用例开始时调用一次，保证起点确定（而不是靠上一条用例“碰巧”留在 idle）。
	/// </summary>
	internal static void ResetUpdateStateForTest()
	{
		lock (UpdateGate)
		{
			UpdateEpoch++;
			UpdateState = "idle";
			UpdateProgress = 0.0;
			UpdateVersion = string.Empty;
			UpdatePath = string.Empty;
			UpdateError = string.Empty;
			UpdateMandatory = false;
			UpdateExpectedSha256 = string.Empty;
			UpdateApplyClaim = 0;
		}
	}

	/// <summary>
	/// 测试用：把下载代次推进到下一轮，模拟「另一个更晚的下载认领已经发生」。
	///
	/// 存在的理由：落伍 worker / 落伍启动请求的守卫只有在「代次已经被别人推进」时才生效，
	/// 而正常路径下（状态仍是 downloading）第二个下载会被当成合法重复、不会领新代次。
	/// 没有这个缝，就只能靠随机时序去撞，那不是可复现的回归。
	/// 它**只动代次计数**，不动状态、不动包，生产路径从不调用。
	/// </summary>
	internal static int BumpUpdateEpochForTest()
	{
		lock (UpdateGate)
		{
			UpdateEpoch++;
			return UpdateEpoch;
		}
	}

	/// <summary>测试只读：当前认领令牌与累计认领次数（用于断言“只认领了一次”）。</summary>
	internal static (int Claim, int Count) ApplyClaimSnapshotForTest
	{
		get { lock (UpdateGate) { return (UpdateApplyClaim, UpdateApplyClaimCount); } }
	}

	/// <summary>测试只读：下载代次与当前状态（用于断言“落伍的 worker 没写回”）。</summary>
	internal static (int Epoch, string State, string Path, string Sha) UpdateStateSnapshotForTest
	{
		get
		{
			lock (UpdateGate)
			{
				return (UpdateEpoch, UpdateState, UpdatePath, UpdateExpectedSha256);
			}
		}
	}

	/// <summary>
	/// 下载更新包的委托。生产路径恒为 <see cref="UpdateService.DownloadUpdateAsync"/>；
	/// 测试可换成“可控延迟/受控结果”的实现，从而在**不访问网络**的前提下做下载与应用的时序交错。
	/// </summary>
	internal static Func<UpdateManifest, IProgress<double>?, CancellationToken, Task<string>> DownloadUpdateInvokerForTest { get; set; } =
		(manifest, progress, ct) => UpdateService.DownloadUpdateAsync(Outbound.Api, manifest, progress, ct);

	/// <summary>
	/// 取更新清单的委托。生产路径恒为 `UpdateService.FetchManifestChainAsync(Outbound.Api, chain, …)`；
	/// 测试可换成受控结果（含“故意延迟到另一个请求先认领”），用来复现跨 await 的时序问题。
	/// </summary>
	internal static Func<IReadOnlyList<string>, CancellationToken, Task<UpdateService.ManifestFetchResult>> FetchManifestInvokerForTest { get; set; } =
		(chain, ct) => UpdateService.FetchManifestChainAsync(Outbound.Api, chain, ct);

	/// <summary>
	/// 启动替换脚本的委托。生产路径恒为 <see cref="UpdateService.ApplyUpdate"/>；
	/// 测试可换成「只记录被调用了几次」的受控实现 —— 这样并发/交错用例**不需要**真的写 .bat
	/// 或起 cmd，也不会碰到任何用户 EXE。
	/// </summary>
	internal static Action<string, string, string> ApplyUpdateInvokerForTest { get; set; } =
		(path, scriptExt, version) => UpdateService.ApplyUpdate(path, scriptExt, version);

	/// <summary>
	/// 替换脚本启动后「延迟退出当前进程」的调度器。生产路径恒为真实 Delay + Environment.Exit(0)；
	/// 测试换成记录调用的空操作 —— 否则 testhost 会在 1.2 秒后被真实杀掉，并发用例根本跑不完。
	/// 只影响「何时退出」，不影响替换脚本的生成与启动。
	/// </summary>
	internal static Action<int, TimeSpan> ExitSchedulerForTest { get; set; } =
		(exitCode, delay) => { _ = Task.Run(async () => { await Task.Delay(delay); Environment.Exit(exitCode); }); };

	/// <summary>
	/// 测试缝：把自更新状态机设到指定状态。生产路径从不调用。
	///
	/// 存在的理由：`/api/update/apply` 的并发守卫依赖“已在 applying”这个内部状态，
	/// 而 real apply 会启动 cmd 并 1.2s 后 Environment.Exit —— 测试不可能真走一遍再断言第二次被拒。
	/// 有了这个缝，就可以把状态设成 applying，发一次 POST，断言它**不会**再起第二个替换。
	/// </summary>
	internal static IDisposable OverrideUpdateStateForTest(string state, string? path = null, string? version = null, string? expectedSha256 = null)
	{
		lock (UpdateGate)
		{
			string prevState = UpdateState, prevPath = UpdatePath, prevVersion = UpdateVersion, prevSha = UpdateExpectedSha256;
			int prevClaim = UpdateApplyClaim;
			UpdateState = state;
			// 测试缝把状态设成非 applying 时，同时清掉认领令牌；
			// 否则 TryClaimApplyForUpdate 会因为“已认领”一直拒绝，用例就测不到 apply 本身。
			UpdateApplyClaim = 0;
			if (path != null) UpdatePath = path;
			if (version != null) UpdateVersion = version;
			if (expectedSha256 != null) UpdateExpectedSha256 = expectedSha256;
			return new UpdateStateScope(prevState, prevPath, prevVersion, prevSha, prevClaim);
		}
	}

	/// <summary>
	/// 测试缝：把自更新的**下载路径**与状态设好，但不启动真下载。
	///
	/// 存在的理由：`/api/update/apply` 只有在 UpdatePath 指向的文件存在时才会进入替换流程。
	/// 要把"替换前链路走通"测出来（包括写入 apply-update 脚本这一步），
	/// 就得能伪造"已下载完成"而**不真跑一次网络下载**（真下载会把测试变成对外网的依赖）。
	/// </summary>
	internal static IDisposable OverrideDownloadedUpdateForTest(string path, string version)
	{
		return OverrideUpdateStateForTest("completed", path, version);
	}

	private sealed class UpdateStateScope : IDisposable
	{
		private readonly string _state, _path, _version, _sha;
		private readonly int _claim;
		private bool _disposed;

		public UpdateStateScope(string state, string path, string version, string sha, int claim)
		{
			_state = state;
			_path = path;
			_version = version;
			_sha = sha;
			_claim = claim;
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			lock (UpdateGate)
			{
				UpdateState = _state;
				UpdatePath = _path;
				UpdateVersion = _version;
				UpdateExpectedSha256 = _sha;
				UpdateApplyClaim = _claim;
			}
		}
	}

	private static async Task HandleCheckUpdateAsync(HttpListenerResponse response)
	{
		AppConfig config = LoadConfig();
		IReadOnlyList<string> chain = ResolveUpdateUrlChain(config.UpdateUrl);
		if (chain.Count == 0)
		{
			await WriteJsonAsync(response, 200, new { enabled = false, currentVersion = LauncherService.LauncherVersion, message = "未配置更新源（config.json 的 updateUrl）" });
			return;
		}

		try
		{
			// R03：主源失败（404 / 超时 / 坏 JSON / 字段非法）时按链降级到备用源，
			// 并把每个源的真实结果回给界面，避免只给一句"检查失败"。
			UpdateService.ManifestFetchResult fetch = await UpdateService.FetchManifestChainAsync(Outbound.Api, chain, CancellationToken.None);
			if (!fetch.Ok || fetch.Manifest == null || string.IsNullOrWhiteSpace(fetch.Manifest.Version))
			{
				LauncherLog.Error("[update] 检查更新失败: " + fetch.FailureSummary);
				await WriteJsonAsync(response, 200, new
				{
					enabled = true,
					currentVersion = LauncherService.LauncherVersion,
					error = fetch.FailureSummary,
					manifestSources = fetch.Attempts.Select(a => new { url = UpdateService.Redact(a.Url), ok = a.Ok, reason = a.Reason }).ToArray()
				});
				return;
			}
			UpdateManifest manifest = fetch.Manifest;
			bool available = UpdateService.IsNewerVersion(manifest.Version, LauncherService.LauncherVersion);
			LauncherLog.Info("[update] 检查更新：current=" + LauncherService.LauncherVersion + " latest=" + manifest.Version
				+ " available=" + available + " source=" + UpdateService.Redact(fetch.SourceUrl) + " fallback=" + fetch.UsedFallback);
			await WriteJsonAsync(response, 200, new
			{
				enabled = true,
				currentVersion = LauncherService.LauncherVersion,
				updateAvailable = available,
				latestVersion = manifest.Version,
				notes = manifest.Notes,
				mandatory = manifest.Mandatory,
				downloadUrl = manifest.Url,
				manifestSource = UpdateService.Redact(fetch.SourceUrl),
				usedFallback = fetch.UsedFallback
			});
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 检查更新失败: " + ex.Message, ex);
			await WriteJsonAsync(response, 200, new { enabled = true, currentVersion = LauncherService.LauncherVersion, error = ex.Message });
		}
	}

	private static async Task HandleStartUpdateDownloadAsync(HttpListenerResponse response)
	{
		// v1.0.5 r2：**占位必须在第一个 await 之前完成**，而且要原子。
		// 原实现只读一次 `state == "downloading"` 就去 await 取清单：
		// 两个请求能同时通过检查、各自跨 await，随后先后把状态写成 downloading 并各起一个 worker，
		// 后完成的那次会覆盖先完成的结果（旧结果覆盖新状态）。
		// 现在的语义：
		//   · downloading        → 合法重复点击，回同一状态，不启第二个 worker；
		//   · completed          → 已有可安装包，不重复下载；
		//   · applying           → 已在替换，下载与应用互斥，拒绝（否则会把 UpdatePath/Sha 改掉）；
		//   · idle / failed      → 原子认领：状态立即置 downloading 并领一个 epoch。
		int claimEpoch;
		string claimState;
		lock (UpdateGate)
		{
			if (UpdateState == "applying")
			{
				claimEpoch = -1;
				claimState = UpdateState;
			}
			else if (UpdateState == "downloading" || UpdateState == "completed")
			{
				claimEpoch = 0;
				claimState = UpdateState;
			}
			else
			{
				UpdateEpoch++;
				claimEpoch = UpdateEpoch;
				UpdateState = "downloading";
				UpdateProgress = 0.0;
				UpdateVersion = string.Empty;
				UpdatePath = string.Empty;
				UpdateError = string.Empty;
				UpdateMandatory = false;
				UpdateExpectedSha256 = string.Empty;
				claimState = UpdateState;
			}
		}

		if (claimEpoch == -1)
		{
			LauncherLog.Info("[update] 已在替换中，忽略下载请求");
			await WriteJsonAsync(response, 409, new { ok = false, state = "applying", message = "更新正在应用，已忽略重复的下载请求。" });
			return;
		}
		if (claimEpoch == 0)
		{
			await WriteJsonAsync(response, 200, new { ok = true, state = claimState });
			return;
		}

		// 从这一刻起“这次下载”已经占位：后面任何失败都必须把状态落到 failed（不能留在 downloading），
		// 否则界面会永远转圈。
		AppConfig config = LoadConfig();
		IReadOnlyList<string> chain = ResolveUpdateUrlChain(config.UpdateUrl);
		if (chain.Count == 0)
		{
			FailUpdateClaim(claimEpoch, "未配置更新源（config.json 的 updateUrl）");
			await WriteJsonAsync(response, 400, new { ok = false, state = "failed", message = "未配置更新源（config.json 的 updateUrl）" });
			return;
		}

		try
		{
			// R03：与检查更新走同一条降级链，避免"检查能看到新版本、下载却取不到清单"。
			UpdateService.ManifestFetchResult fetch = await FetchManifestInvokerForTest(chain, CancellationToken.None);
			UpdateManifest? manifest = fetch.Manifest;
			if (!fetch.Ok || manifest == null || string.IsNullOrWhiteSpace(manifest.Url))
			{
				LauncherLog.Error("[update] 启动下载失败: " + fetch.FailureSummary);
				FailUpdateClaim(claimEpoch, fetch.FailureSummary);
				await WriteJsonAsync(response, 400, new
				{
					ok = false,
					state = "failed",
					message = fetch.FailureSummary,
					manifestSources = fetch.Attempts.Select(a => new { url = UpdateService.Redact(a.Url), ok = a.Ok, reason = a.Reason }).ToArray()
				});
				return;
			}

			bool stillMine;
			lock (UpdateGate)
			{
				// 代次校验：如果期间有更新的一次下载认领了（或状态已被别的路径改掉），
				// 这次就是“落伍的启动请求”，不许覆盖新状态、不许起 worker。
				stillMine = (claimEpoch == UpdateEpoch) && UpdateState == "downloading";
				if (stillMine)
				{
					UpdateProgress = 0.0;
					UpdateVersion = manifest.Version;
					UpdatePath = string.Empty;
					UpdateError = string.Empty;
					UpdateMandatory = manifest.Mandatory;
					UpdateExpectedSha256 = manifest.Sha256 ?? string.Empty;
				}
			}
			if (!stillMine)
			{
				LauncherLog.Info("[update] 取清单期间已有更新的下载认领，丢弃本次结果");
				await WriteJsonAsync(response, 409, new { ok = false, state = "superseded", message = "已有更新的一次下载，已忽略本次结果。" });
				return;
			}
			LauncherLog.Info("[update] 开始下载更新 v" + manifest.Version + " ← " + manifest.Url);
			_ = Task.Run(() => DownloadUpdateWorkerAsync(manifest, claimEpoch));
			await WriteJsonAsync(response, 200, new { ok = true, state = "downloading", version = manifest.Version });
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 启动下载失败: " + ex.Message, ex);
			FailUpdateClaim(claimEpoch, ex.Message);
			await WriteJsonAsync(response, 500, new { ok = false, state = "failed", message = ex.Message });
		}
	}

	/// <summary>
	/// 把“这一次下载认领”落到 failed —— 但只在代次仍然匹配时写。
	/// 不匹配说明已经有更新的一次下载接手了，不能把它的状态改成失败。
	/// </summary>
	private static void FailUpdateClaim(int epoch, string error)
	{
		lock (UpdateGate)
		{
			if (epoch != UpdateEpoch)
			{
				return;
			}
			UpdateState = "failed";
			UpdateError = error;
		}
	}

	private static async Task DownloadUpdateWorkerAsync(UpdateManifest manifest, int epoch)
	{
		try
		{
			// 进度写回同样受代次约束：落伍的 worker 不得改动当前进度。
			Progress<double> progress = new Progress<double>(value =>
			{
				lock (UpdateGate)
				{
					if (epoch == UpdateEpoch)
					{
						UpdateProgress = value;
					}
				}
			});
			string path = await DownloadUpdateInvokerForTest(manifest, progress, CancellationToken.None);
			bool stillMine;
			lock (UpdateGate)
			{
				stillMine = epoch == UpdateEpoch;
				if (stillMine)
				{
					UpdateState = "completed";
					UpdatePath = path;
					UpdateProgress = 1.0;
				}
			}
			if (!stillMine)
			{
				// 落伍的 worker：包已经落在磁盘上，但**不许**把状态标成 completed，
				// 否则界面会把旧结果当成本次结果。文件会被下次下载的“已有且哈希一致”分支复用。
				LauncherLog.Info("[update] 落伍的下载完成，已丢弃: " + path);
				return;
			}
			LauncherLog.Info("[update] 下载完成: " + path);
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 下载失败: " + ex.Message, ex);
			FailUpdateClaim(epoch, ex.Message);
		}
	}

	private static async Task HandleUpdateStatusAsync(HttpListenerResponse response)
	{
		string state, version, path, error;
		double progress;
		bool mandatory;
		lock (UpdateGate)
		{
			state = UpdateState;
			version = UpdateVersion;
			path = UpdatePath;
			error = UpdateError;
			progress = UpdateProgress;
			mandatory = UpdateMandatory;
		}
		// MR20：把"上次替换脚本的结果"与信任链声明一并回给界面。
		// 界面**不能**因为"脚本已启动"就显示更新完成 —— 只有脚本写出 applied 才算替换成功。
		string updatesDir = Path.Combine(PlatformPaths.GetConfigDirectory(), "updates");
		UpdateService.ApplyResult? applyResult = UpdateService.ReadApplyResult(UpdateService.GetApplyStatePath(updatesDir));
		await WriteJsonAsync(response, 200, new
		{
			state,
			progress,
			version,
			path,
			error,
			mandatory,
			currentVersion = LauncherService.LauncherVersion,
			trustNote = UpdateService.TrustModelNote,
			lastApplyResult = applyResult == null ? null : new
			{
				result = applyResult.Result,
				applied = applyResult.Applied,
				version = applyResult.Version,
				writtenAtUtc = applyResult.WrittenAtUtc
			}
		});
	}

	/// <summary>
	/// 一次合法的替换认领。<see cref="Token"/> 非 0 表示认领成功；
	/// 其余分支用 <see cref="Reason"/> 说明为什么没认领。
	/// </summary>
	internal readonly record struct ApplyClaim(int Token, string? Version, string? Path, string? Sha256, string? Reason);

	/// <summary>
	/// v1.0.5 r2：**在 UpdateGate 下原子认领「可应用状态 + 当次下载结果」**。
	///
	/// 为什么必须原子：r1 的检查与置位之间隔着文件检查、SHA 复核和 ApplyUpdate 调用，
	/// 两个同时到达的 apply 请求都能通过检查，于是各起一个替换脚本，同时 copy 同一个 TARGET。
	/// 认领必须发生在**文件校验与启动脚本之前**，否则“校验耗时”就是竞态窗口。
	///
	/// 认领后状态立即是 applying：
	///   · 后续任何失败都要显式 <see cref="ReleaseApplyClaim"/>，把状态落到 failed；
	///   · 下载侧看到 applying 会 409 拒绝（下载与应用互斥），不会改掉当次下载结果。
	/// </summary>
	private static ApplyClaim TryClaimApplyForUpdate()
	{
		lock (UpdateGate)
		{
			if (UpdateApplyClaim != 0)
			{
				return new ApplyClaim(0, null, null, null, "applying");
			}
			if (UpdateState != "completed" && UpdateState != "failed")
			{
				// 只有“有包可装”的状态才允许认领；downloading / idle / applying 都不行。
				return new ApplyClaim(0, null, null, null, UpdateState);
			}
			if (string.IsNullOrWhiteSpace(UpdatePath))
			{
				return new ApplyClaim(0, null, null, null, "no-package");
			}

			UpdateApplyClaimCount++;
			UpdateApplyClaim = UpdateApplyClaimCount;
			UpdateState = "applying";
			UpdateError = string.Empty;
			return new ApplyClaim(UpdateApplyClaim, UpdateVersion, UpdatePath, UpdateExpectedSha256, null);
		}
	}

	/// <summary>认领失败后的收尾：只在令牌仍然有效时回写，避免迟到的失败覆盖新状态。</summary>
	private static void ReleaseApplyClaim(int token, string? error)
	{
		lock (UpdateGate)
		{
			if (token == 0 || UpdateApplyClaim != token)
			{
				return;
			}
			UpdateApplyClaim = 0;
			UpdateState = "failed";
			UpdateError = error ?? string.Empty;
		}
	}

	private static async Task HandleApplyUpdateAsync(HttpListenerResponse response)
	{
		ApplyClaim claim = TryClaimApplyForUpdate();
		if (claim.Token == 0)
		{
			// 已认领 / 状态不对：不再启动第二个替换，也不改任何状态。
			if (claim.Reason == "applying")
			{
				LauncherLog.Info("[update] 重复的 apply 请求已忽略（已在替换中）");
				await WriteJsonAsync(response, 200, new { ok = true, state = "applying", message = "更新已经启动，重启后生效。" });
				return;
			}
			string msg = claim.Reason == "downloading"
				? "更新正在下载，请等下载完成后再应用。"
				: "更新包未就绪，请先下载。";
			await WriteJsonAsync(response, 400, new { ok = false, state = claim.Reason, message = msg });
			return;
		}

		string path = claim.Path!;
		string version = claim.Version ?? string.Empty;
		string expectedSha = claim.Sha256 ?? string.Empty;

		if (!File.Exists(path))
		{
			ReleaseApplyClaim(claim.Token, "更新包不存在，请重新下载。");
			await WriteJsonAsync(response, 400, new { ok = false, state = "failed", message = "更新包不存在，请重新下载。" });
			return;
		}

		// v1.0.5（RED 4）：**替换前再校一次 SHA256**。
		// 下载阶段校验过，但下载完成到点“立即重启更新”之间磁盘上的包可能被截断/改写。
		// 替换脚本只做 fc /b（拷贝完整性），不校验清单声明的哈希，
		// 实测：包被截成 100KB 时脚本仍然写 applied，把可用旧 EXE 换成了无法启动的坏文件。
		// 这里用与下载阶段**同一个** Sha256Matches 复核；不符就拒绝，不写脚本、不替换。
		if (!UpdateService.DownloadedPackageMatchesExpectedHash(path, expectedSha))
		{
			string reason = "更新包校验失败：磁盘上的文件与清单声明的 SHA256 不符（可能在下载后被截断或替换）。请删除后重新下载。";
			LauncherLog.Error("[update] " + reason + " path=" + path);
			ReleaseApplyClaim(claim.Token, reason);
			await WriteJsonAsync(response, 400, new { ok = false, state = "failed", message = reason, sha256Mismatch = true });
			return;
		}

		try
		{
			// 通过可替换的委托启动替换脚本：生产恒为 UpdateService.ApplyUpdate；
			// 测试用受控实现计数，从而在**不触碰任何用户 EXE**的前提下断言“只启动一次”。
			ApplyUpdateInvokerForTest(path, PlatformPaths.GetScriptExtension(), version);
			LauncherLog.Info("[update] 正在应用更新 v" + version + " → " + path);
			await WriteJsonAsync(response, 200, new { ok = true, state = "applying", message = "更新已启动，重启后生效。" });
			// 真实路径是 Delay(1200) 后 Environment.Exit(0)；测试用受控调度器，避免杀掉 testhost。
			ExitSchedulerForTest(0, TimeSpan.FromMilliseconds(1200));
		}
		catch (Exception ex)
		{
			LauncherLog.Error("[update] 应用更新失败: " + ex.Message, ex);
			// 启动失败要把认领释放掉并把状态落到 failed，否则界面会一直停在「正在替换」。
			ReleaseApplyClaim(claim.Token, ex.Message);
			await WriteJsonAsync(response, 500, new { ok = false, state = "failed", message = ex.Message });
		}
	}
}
