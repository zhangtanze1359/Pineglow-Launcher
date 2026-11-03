using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Pineglow.Launcher.Services;

namespace VerifyHost
{
	/// <summary>
	/// Mod Bisect 端到端冒烟自检：--bisect-smoke [--port N]
	/// 自建 portable 沙盒（不回写真实配置），走完二分定位全流程（定位 + 放弃 + 无会话拦截），
	/// 结束后清理沙盒，退出码 0=通过 / 1=失败。
	/// </summary>
	internal static class BisectSmoke
	{
		private sealed class Session
		{
			public List<string> Candidates = new List<string>();
			public List<string> DisabledHalf = new List<string>();
			public int Step;
			public bool WaitingForReport;
			public bool Finished;
			public string Culprit = string.Empty;
			public string FinishReason = string.Empty;
			public bool Ok = true;
			public bool NeedsRecovery;
			public string RecoveryError = string.Empty;
			public int FailedFileCount;
		}

		public static int Run(string[] args)
		{
			int port = 48123;
			for (int i = 1; i < args.Length; i++)
			{
				if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed))
				{
					port = parsed;
					i++;
				}
			}

			string baseDir = AppContext.BaseDirectory;
			string flagPath = Path.Combine(baseDir, "portable.flag");
			string dataDir = Path.Combine(baseDir, "data");
			bool hadFlag = File.Exists(flagPath);
			bool hadData = Directory.Exists(dataDir);
			int failures = 0;

			// 凭据后端隔离（独立验收遗留项的收口）：
			// 原来这个冒烟只做了 portable 沙盒 —— 那只能证明**配置**没回写真实目录，
			// 不能证明 **Windows 命名凭据**没被动过（生产后端是 CredReadW/CredWriteW/CredDeleteW）。
			// 这里把 SecretStore 整个换成纯内存后端，并在收尾断言"生产后端调用次数 = 0"：
			// 于是"没碰真实凭据"从一句声明变成可判定的事实，任何环境（含 CI runner）都能安全跑。
			InMemorySecretStoreBackend isolatedSecrets = new InMemorySecretStoreBackend("verifyhost-bisect-smoke");
			SecretStore.ResetProductionBackendCallCountForTest();
			IDisposable secretScope = SecretStore.OverrideBackendForTest(isolatedSecrets);

			Console.WriteLine("[BisectSmoke] start, portable sandbox=" + dataDir);
			try
			{
				if (!hadFlag)
				{
					File.WriteAllText(flagPath, "portable");
				}

				LocalApiService.Start(port);
				using (HttpClient http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port + "/"), Timeout = TimeSpan.FromSeconds(10) })
				{
					WaitHealthy(http);
					http.DefaultRequestHeaders.Add("X-Pineglow-Token", LocalApiService.SessionTokenForTests);

					string instanceId = CreateInstance(http);
					string gameDir = GetInstanceGameDirectory(http, instanceId);
					string modsDir = Path.Combine(gameDir, "mods");
					Directory.CreateDirectory(modsDir);
					for (int i = 1; i <= 8; i++)
					{
						File.WriteAllText(Path.Combine(modsDir, string.Format("mod{0:D2}-1.0.0.jar", i)), "fake mod placeholder");
					}

					// ---- 故障注入（MR06 计划要求："以故障注入补充现有 8→4→2→1 正常路径"） ----
					// 契约：**一个文件操作失败时 API 不得仍报告"本轮就绪"**。
					// 注入方式：为每个"待禁用目标"预先建一个**同名目录**，让"把 .jar 改名为 .jar.disabled"
					// 这一步真实失败（目标被目录占住）。候选清单只枚举**文件**，因此不受这些目录影响 ——
					// 于是失败发生在文件操作上，而不是"Mod 数量变了"。
					for (int i = 1; i <= 8; i++)
					{
						Directory.CreateDirectory(Path.Combine(modsDir, string.Format("mod{0:D2}-1.0.0.jar.disabled", i)));
					}

					int injectStatus = PostJsonAllowFailure(http, "api/instances/" + instanceId + "/bisect/start", "{}", out Session injected);
					Console.WriteLine("  [info] 注入后 start 状态码 = " + injectStatus + "，recoveryError = " + (injected.RecoveryError ?? "(空)"));

					Check(ref failures, injectStatus == 409, "fault-injection: 文件操作失败必须返回 409，实际 " + injectStatus);
					Check(ref failures, !injected.Ok, "fault-injection: 响应必须 ok=false");
					Check(ref failures, !injected.WaitingForReport, "fault-injection: **不得**报告本轮就绪（waitingForReport=false）");
					Check(ref failures, !string.IsNullOrWhiteSpace(injected.RecoveryError), "fault-injection: 必须报出可读的 recoveryError");
					Check(ref failures, injected.NeedsRecovery, "fault-injection: 必须标记 needsRecovery=true");
					Check(ref failures, injected.FailedFileCount > 0, "fault-injection: 必须逐项列出失败文件");
					Check(ref failures, injected.DisabledHalf.Count == 0, "fault-injection: 失败后不得声称已禁用半区");

					// 清理注入物并结束会话，让后续正常路径在干净状态下运行
					for (int i = 1; i <= 8; i++)
					{
						string dir = Path.Combine(modsDir, string.Format("mod{0:D2}-1.0.0.jar.disabled", i));
						if (Directory.Exists(dir))
						{
							Directory.Delete(dir, recursive: true);
						}
					}
					Session afterInject = GiveUpBisect(http, instanceId);
					Check(ref failures, afterInject.Finished, "fault-injection: 清理后可正常放弃会话");
					Check(ref failures, DisabledOnDisk(modsDir) == 0, "fault-injection: 清理后磁盘上没有残留禁用文件");

					// 定位全流程：8 -> 4 -> 2 -> 1
					Session s = StartBisect(http, instanceId);
					Check(ref failures, s.Step == 1 && s.DisabledHalf.Count == 4 && s.WaitingForReport, "start: step=1, disabled half=4, waiting for report");
					Check(ref failures, DisabledOnDisk(modsDir) == 4, "start: 4 jars disabled on disk");

					s = TestBisect(http, instanceId, crashed: true);
					Check(ref failures, s.Candidates.Count == 4 && s.Candidates[0].StartsWith("mod05"), "test(crashed=true): candidates narrowed to mod05-08");
					Check(ref failures, s.DisabledHalf.Count == 2 && DisabledOnDisk(modsDir) == 6, "round2: disable mod05-06, 6 disabled on disk");

					s = TestBisect(http, instanceId, crashed: true);
					Check(ref failures, s.Candidates.Count == 2 && s.DisabledHalf.Count == 1 && DisabledOnDisk(modsDir) == 7, "round3: candidates mod07-08, disable mod07, 7 disabled on disk");

					s = TestBisect(http, instanceId, crashed: false);
					Check(ref failures, s.Finished && s.Culprit == "mod07-1.0.0.jar", "test(crashed=false): culprit=mod07-1.0.0.jar");
					Check(ref failures, DisabledOnDisk(modsDir) == 0, "finish: all mods restored");

					// 放弃分支
					StartBisect(http, instanceId);
					s = GiveUpBisect(http, instanceId);
					Check(ref failures, s.Finished && s.FinishReason == "用户放弃", "giveup: session finished with reason");
					Check(ref failures, DisabledOnDisk(modsDir) == 0, "giveup: all mods restored");

					// 无会话拦截
					Check(ref failures, ExpectBadRequest(http, instanceId), "test without session rejected with 400");


				}
			}
			catch (Exception ex)
			{
				failures++;
				Console.WriteLine("[BisectSmoke] exception: " + ex.Message);
			}
			finally
			{
				try
				{
					LocalApiService.Stop();
				}
				catch
				{
				}

				try
				{
					if (!hadFlag && File.Exists(flagPath))
					{
						File.Delete(flagPath);
					}
					if (!hadData && Directory.Exists(dataDir))
					{
						Directory.Delete(dataDir, recursive: true);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("[BisectSmoke] cleanup warning: " + ex.Message);
				}

				try
				{
					long productionCalls = SecretStore.ProductionBackendCallCount;
					// 标签刻意用**纯 ASCII**：门槛脚本按它判定，不能依赖控制台代码页/本地化
					// （历史上已经因为中文标签在不同编码下匹配不上而误判过一次）。
					Check(ref failures, productionCalls == 0,
						"credential-isolation: production credential backend calls must be 0 (actual " + productionCalls + ")");
					Console.WriteLine("  [info] 隔离凭据后端操作 " + isolatedSecrets.Operations.Count
						+ " 次（label=" + isolatedSecrets.Label + "，未接触磁盘/注册表/凭据管理器）");
					if (isolatedSecrets.DeletedNames.Count > 0)
					{
						Console.WriteLine("  [info] 被隔离接住的删除意图 " + isolatedSecrets.DeletedNames.Count + " 个");
					}
				}
				catch (Exception ex)
				{
					failures++;
					Console.WriteLine("[BisectSmoke] credential isolation check error: " + ex.Message);
				}
				finally
				{
					secretScope.Dispose();
				}
			}

			Console.WriteLine(failures == 0 ? "[BisectSmoke] PASS" : "[BisectSmoke] FAIL (" + failures + " checks failed)");
			return failures == 0 ? 0 : 1;
		}


		private static void Check(ref int failures, bool ok, string label)
		{
			Console.WriteLine(ok ? "  [PASS] " + label : "  [FAIL] " + label);
			if (!ok)
			{
				failures++;
			}
		}

		private static void WaitHealthy(HttpClient http)
		{
			for (int i = 0; i < 50; i++)
			{
				try
				{
					using HttpResponseMessage res = http.GetAsync("api/health").GetAwaiter().GetResult();
					if (res.IsSuccessStatusCode)
					{
						return;
					}
				}
				catch
				{
				}
				Thread.Sleep(100);
			}
			throw new InvalidOperationException("API health check timed out");
		}

		private static string CreateInstance(HttpClient http)
		{
			using JsonDocument doc = PostJson(http, "api/instances", "{\"name\":\"Bisect Smoke\",\"version\":\"1.21.1\"}");
			JsonElement root = doc.RootElement;
			if (!root.TryGetProperty("ok", out JsonElement ok) || ok.ValueKind != JsonValueKind.True)
			{
				throw new InvalidOperationException("create instance failed: " + root.GetRawText());
			}
			return root.GetProperty("instance").GetProperty("id").GetString() ?? string.Empty;
		}

		private static string GetInstanceGameDirectory(HttpClient http, string instanceId)
		{
			using JsonDocument doc = GetJson(http, "api/instances/" + instanceId);
			return doc.RootElement.GetProperty("instance").GetProperty("gameDirectory").GetString() ?? string.Empty;
		}

		private static Session StartBisect(HttpClient http, string instanceId)
		{
			using JsonDocument doc = PostJson(http, "api/instances/" + instanceId + "/bisect/start", "{}");
			return ParseSession(doc.RootElement);
		}

		private static Session TestBisect(HttpClient http, string instanceId, bool crashed)
		{
			string body = crashed ? "{\"crashed\":true}" : "{\"crashed\":false}";
			using JsonDocument doc = PostJson(http, "api/instances/" + instanceId + "/bisect/test", body);
			return ParseSession(doc.RootElement);
		}

		private static Session GiveUpBisect(HttpClient http, string instanceId)
		{
			using JsonDocument doc = PostJson(http, "api/instances/" + instanceId + "/bisect/giveup", "{}");
			return ParseSession(doc.RootElement);
		}

		private static bool ExpectBadRequest(HttpClient http, string instanceId)
		{
			try
			{
				HttpResponseMessage res = http.PostAsync("api/instances/" + instanceId + "/bisect/test", new StringContent("{\"crashed\":false}", System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
				using (res)
				{
					return (int)res.StatusCode == 400;
				}
			}
			catch
			{
				return false;
			}
		}

		private static Session ParseSession(JsonElement root)
		{
			Session s = new Session();
			if (root.TryGetProperty("candidates", out JsonElement candidates) && candidates.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in candidates.EnumerateArray())
				{
					s.Candidates.Add(item.GetString() ?? string.Empty);
				}
			}
			if (root.TryGetProperty("disabledHalf", out JsonElement half) && half.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in half.EnumerateArray())
				{
					s.DisabledHalf.Add(item.GetString() ?? string.Empty);
				}
			}
			if (root.TryGetProperty("step", out JsonElement step))
			{
				s.Step = step.GetInt32();
			}
			if (root.TryGetProperty("waitingForReport", out JsonElement waiting))
			{
				s.WaitingForReport = waiting.ValueKind == JsonValueKind.True;
			}
			if (root.TryGetProperty("finished", out JsonElement finished))
			{
				s.Finished = finished.ValueKind == JsonValueKind.True;
			}
			if (root.TryGetProperty("culprit", out JsonElement culprit) && culprit.ValueKind == JsonValueKind.String)
			{
				s.Culprit = culprit.GetString() ?? string.Empty;
			}
			if (root.TryGetProperty("finishReason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String)
			{
				s.FinishReason = reason.GetString() ?? string.Empty;
			}
			// 故障注入阶段用到的字段（MR06 的失败契约）
			if (root.TryGetProperty("ok", out JsonElement okFlag) && okFlag.ValueKind == JsonValueKind.False)
			{
				s.Ok = false;
			}
			if (root.TryGetProperty("needsRecovery", out JsonElement needs) && needs.ValueKind == JsonValueKind.True)
			{
				s.NeedsRecovery = true;
			}
			if (root.TryGetProperty("recoveryError", out JsonElement recErr) && recErr.ValueKind == JsonValueKind.String)
			{
				s.RecoveryError = recErr.GetString() ?? string.Empty;
			}
			if (root.TryGetProperty("files", out JsonElement files) && files.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement f in files.EnumerateArray())
				{
					if (f.TryGetProperty("state", out JsonElement st) && st.ValueKind == JsonValueKind.String
						&& string.Equals(st.GetString(), "failed", StringComparison.OrdinalIgnoreCase))
					{
						s.FailedFileCount++;
					}
				}
			}
			return s;
		}

		private static JsonDocument GetJson(HttpClient http, string uri)
		{
			HttpResponseMessage res = http.GetAsync(uri).GetAwaiter().GetResult();
			using (res)
			{
				res.EnsureSuccessStatusCode();
				string json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				return JsonDocument.Parse(json);
			}
		}

		private static JsonDocument PostJson(HttpClient http, string uri, string body)
		{
			HttpResponseMessage res = http.PostAsync(uri, new StringContent(body, System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
			using (res)
			{
				res.EnsureSuccessStatusCode();
				string json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				return JsonDocument.Parse(json);
			}
		}

		/// <summary>故障注入用：允许非 2xx（失败轮次会返回 409），仍然解析响应体。</summary>
		private static int PostJsonAllowFailure(HttpClient http, string uri, string body, out Session session)
		{
			HttpResponseMessage res = http.PostAsync(uri, new StringContent(body, System.Text.Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
			using (res)
			{
				string json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				session = ParseSession(JsonDocument.Parse(json).RootElement);
				return (int)res.StatusCode;
			}
		}

		private static int DisabledOnDisk(string modsDir)
		{
			return Directory.GetFiles(modsDir, "*.jar.disabled").Length;
		}
	}
}
