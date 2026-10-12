using System;
using System.Text.Json.Serialization;

namespace Pineglow.Launcher.Models;

/// <summary>
/// AI 诊断提供商配置（一套 = 名称 + OpenAI 兼容基址 + 模型 + API Key）。
/// Key 用 <see cref="JsonIgnoreAttribute"/> 不入 config.json，经 SecretStore 持久化（键 AiKey_{Id}）。
/// </summary>
public sealed class AiProviderProfile
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	/// <summary>显示名，如 "DeepSeek"。</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>OpenAI 兼容 chat/completions 基址，如 https://api.deepseek.com/v1。</summary>
	public string BaseUrl { get; set; } = string.Empty;

	/// <summary>模型名，如 deepseek-chat。</summary>
	public string Model { get; set; } = string.Empty;

	[JsonIgnore]
	public string ApiKey { get; set; } = string.Empty;
}