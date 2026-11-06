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
/// 静态资源托管。
/// <para>
/// ui/ 目录的静态文件托管（含 SPA 回退与路径越界校验）。
/// </para>
/// </summary>
public static partial class LocalApiService
{
	/* ================= 静态文件 ================= */

	private static async Task ServeStaticAsync(HttpListenerContext context, string path)
	{
		HttpListenerResponse response = context.Response;
		string relative = path.TrimStart('/');
		if (string.IsNullOrEmpty(relative))
		{
			relative = "index.html";
		}

		string full = Path.GetFullPath(Path.Combine(_uiRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
		if (!IsPathWithinRoot(_uiRoot, full) || !File.Exists(full))
		{
			await WriteJsonAsync(response, 404, new { error = "Not Found: " + path });
			return;
		}

		string contentType = "application/octet-stream";
		string ext = Path.GetExtension(full).ToLowerInvariant();
		switch (ext)
		{
			case ".html":
				contentType = "text/html; charset=utf-8";
				break;
			case ".css":
				contentType = "text/css; charset=utf-8";
				break;
			case ".js":
				contentType = "application/javascript; charset=utf-8";
				break;
			case ".json":
				contentType = "application/json; charset=utf-8";
				break;
			case ".png":
				contentType = "image/png";
				break;
			case ".jpg":
			case ".jpeg":
				contentType = "image/jpeg";
				break;
			case ".svg":
				contentType = "image/svg+xml";
				break;
			case ".ico":
				contentType = "image/x-icon";
				break;
			case ".woff2":
				contentType = "font/woff2";
				break;
		}

		response.ContentType = contentType;
		byte[] bytes = await File.ReadAllBytesAsync(full);
		if (Path.GetFileName(full).Equals("index.html", StringComparison.OrdinalIgnoreCase))
		{
			bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("__PINEGLOW_SESSION_TOKEN__", SessionToken, StringComparison.Ordinal));
		}
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
	}
}
