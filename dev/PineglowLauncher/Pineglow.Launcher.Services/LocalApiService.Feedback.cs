using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 用户反馈入口（2026-11-24 编排方要求）。
///
/// <para>
/// 产品没有远端服务可收反馈，所以本接口把反馈**写到本地**
/// <c>{数据目录}/feedback/</c> 下的一个 Markdown 文件，并自动附上
/// 版本、时间与最近的启动器日志摘录 —— 用户可以直接把这个文件发给开发者。
/// </para>
/// <para>
/// 界面入口：设置页「反馈」区 + 提交后的 toast 提示文件位置。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/// <summary>单条反馈的正文上限（字符）。</summary>
	internal const int MaxFeedbackChars = 20_000;

	/// <summary>
	/// POST /api/feedback
	/// body: { message, contact? } → { ok, path }
	/// </summary>
	private static async Task HandleSubmitFeedbackAsync(HttpListenerRequest request, HttpListenerResponse response)
	{
		JsonDocument body = await ReadBodyAsync(request);
		string message = (JsonUtil.GetString(body.RootElement, "message") ?? string.Empty).Trim();
		string contact = (JsonUtil.GetString(body.RootElement, "contact") ?? string.Empty).Trim();

		if (message.Length == 0)
		{
			throw new InvalidOperationException("反馈内容不能为空");
		}
		if (message.Length > MaxFeedbackChars)
		{
			throw new InvalidOperationException($"反馈内容过长（上限 {MaxFeedbackChars} 字符）");
		}
		if (contact.Length > 200)
		{
			throw new InvalidOperationException("联系方式过长（上限 200 字符）");
		}

		string root = Path.Combine(PlatformPaths.GetConfigDirectory(), "feedback");
		Directory.CreateDirectory(root);

		string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
		// 文件名只保留安全字符，避免奇怪字符导致路径问题。
		string safe = string.Concat(message.Take(12).Select(c => char.IsLetterOrDigit(c) ? c : '_'));
		string path = Path.Combine(root, $"feedback-{stamp}-{safe}.md");

		var sb = new System.Text.StringBuilder();
		sb.AppendLine("# 用户反馈");
		sb.AppendLine();
		sb.AppendLine($"- 提交时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine($"- 启动器版本：{LauncherService.LauncherVersion}");
		sb.AppendLine($"- 系统：{System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
		if (contact.Length > 0)
		{
			sb.AppendLine($"- 联系方式：{contact}");
		}
		sb.AppendLine();
		sb.AppendLine("## 反馈内容");
		sb.AppendLine();
		sb.AppendLine(message);
		sb.AppendLine();
		sb.AppendLine("## 最近日志（最多 80 行）");
		sb.AppendLine();
		sb.AppendLine("```");
		try
		{
			string logDir = Path.Combine(PlatformPaths.GetConfigDirectory(), "logs");
			if (Directory.Exists(logDir))
			{
				var files = new DirectoryInfo(logDir)
					.EnumerateFiles("*.log")
					.OrderByDescending(f => f.LastWriteTime)
					.ToList();
				if (files.Count > 0)
				{
					foreach (string line in File.ReadLines(files[0].FullName).TakeLast(80))
					{
						sb.AppendLine(line);
					}
				}
				else
				{
					sb.AppendLine("（无日志文件）");
				}
			}
			else
			{
				sb.AppendLine("（日志目录不存在）");
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine($"（读取日志失败：{ex.Message}）");
		}
		sb.AppendLine("```");

		await File.WriteAllTextAsync(path, sb.ToString());

		await WriteJsonAsync(response, 200, new { ok = true, path });
	}

	/// <summary>
	/// GET /api/feedback/last → { ok, path } 最近一条反馈的文件位置（用于界面提示）。
	/// </summary>
	private static async Task HandleLastFeedbackAsync(HttpListenerResponse response)
	{
		string root = Path.Combine(PlatformPaths.GetConfigDirectory(), "feedback");
		string? last = null;
		if (Directory.Exists(root))
		{
			last = new DirectoryInfo(root)
				.EnumerateFiles("feedback-*.md")
				.OrderByDescending(f => f.LastWriteTime)
				.FirstOrDefault()?.FullName;
		}
		await WriteJsonAsync(response, 200, new { ok = last != null, path = last ?? string.Empty });
	}
}
