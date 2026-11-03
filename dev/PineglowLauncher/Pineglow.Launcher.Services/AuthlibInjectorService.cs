using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public sealed class AuthlibInjectorService(HttpClient http)
{
	private static readonly string[] AgentMirrorUrls = new string[]
	{
		"https://authlib-injector.yushi.moe/artifact/53/authlib-injector-1.2.5.jar",
		"https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/53/authlib-injector-1.2.5.jar",
	};

	public async Task<AuthlibAccount> LoginAsync(string serverUrl, string userName, string password, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
		{
			throw new InvalidOperationException("请填写认证服务地址、账号和密码。");
		}
		string normalizedUrl = NormalizeServerUrl(serverUrl);
		string requestUri = normalizedUrl + "/authserver/authenticate";
		string content = JsonSerializer.Serialize(new
		{
			agent = new
			{
				name = "Minecraft",
				version = 1
			},
			username = userName.Trim(),
			password = password,
			requestUser = true
		});
		using HttpResponseMessage response = await http.PostAsync(requestUri, new StringContent(content, Encoding.UTF8, "application/json"), ct);
		string text = await response.Content.ReadAsStringAsync(ct);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException("第三方登录失败：" + text.Substring(0, Math.Min(200, text.Length)));
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(text);
		JsonElement rootElement = jsonDocument.RootElement;
		string text2 = rootElement.GetProperty("accessToken").GetString() ?? string.Empty;
		JsonElement property = rootElement.GetProperty("selectedProfile");
		string text3 = property.GetProperty("id").GetString() ?? string.Empty;
		string? userName2 = property.GetProperty("name").GetString() ?? userName.Trim();
		if (string.IsNullOrWhiteSpace(text2) || string.IsNullOrWhiteSpace(text3))
		{
			throw new InvalidOperationException("第三方认证没有返回可用角色。");
		}
		string skinUrl = string.Empty;
		string capeUrl = string.Empty;
		bool isSlim = false;
		if (property.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement p in properties.EnumerateArray())
			{
				if (p.TryGetProperty("name", out JsonElement pName) && pName.GetString() == "textures" &&
					p.TryGetProperty("value", out JsonElement pValue))
				{
					try
					{
						string texturesJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(pValue.GetString() ?? string.Empty));
						using JsonDocument texDoc = JsonDocument.Parse(texturesJson);
						if (texDoc.RootElement.TryGetProperty("textures", out JsonElement texRoot))
						{
							if (texRoot.TryGetProperty("SKIN", out JsonElement skin) && skin.TryGetProperty("url", out JsonElement skinUrlEl))
							{
								skinUrl = skinUrlEl.GetString() ?? string.Empty;
								isSlim = skin.TryGetProperty("metadata", out JsonElement meta) && meta.TryGetProperty("model", out JsonElement modelEl) && modelEl.GetString() == "slim";
							}
							if (texRoot.TryGetProperty("CAPE", out JsonElement cape) && cape.TryGetProperty("url", out JsonElement capeUrlEl))
							{
								capeUrl = capeUrlEl.GetString() ?? string.Empty;
							}
						}
					}
					catch
					{
						// textures 解析失败不影响登录
					}
				}
			}
		}
		return new AuthlibAccount(userName2, text3, text2, normalizedUrl, skinUrl, capeUrl, isSlim);
	}

	public async Task<string> EnsureAgentAsync(CancellationToken ct)
	{
		string text = Path.Combine(PlatformPaths.GetConfigDirectory(), "authlib-injector");
		string output = Path.Combine(text, "authlib-injector.jar");
		if (File.Exists(output) && new FileInfo(output).Length > 1024)
		{
			return output;
		}
		Directory.CreateDirectory(text);
		string temporary = output + ".part";
		try
		{
			byte[] array = await DownloadAgentAsync(ct);
			if (array.Length <= 1024)
			{
				throw new InvalidOperationException("Authlib Injector 组件下载内容无效。");
			}
			await File.WriteAllBytesAsync(temporary, array, ct);
			File.Move(temporary, output, overwrite: true);
			return output;
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}

	private async Task<byte[]> DownloadAgentAsync(CancellationToken ct)
	{
		Exception? lastError = null;
		foreach (string url in AgentMirrorUrls)
		{
			try
			{
				byte[] array = await http.GetByteArrayAsync(url, ct);
				if (array.Length > 1024)
				{
					return array;
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				lastError = ex;
			}
		}
		throw new InvalidOperationException("无法下载 Authlib Injector 组件（所有镜像均失败）。" + (lastError?.Message ?? string.Empty), lastError);
	}

	private static string NormalizeServerUrl(string value)
	{
		string text = value.Trim().TrimEnd('/');
		if (!Uri.TryCreate(text, UriKind.Absolute, out Uri result) || (result.Scheme != Uri.UriSchemeHttp && result.Scheme != Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException("认证服务地址必须是以 http:// 或 https:// 开头的完整地址。");
		}
		return text;
	}
}
