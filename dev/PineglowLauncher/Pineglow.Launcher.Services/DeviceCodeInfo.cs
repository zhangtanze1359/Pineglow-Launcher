using System.Text.Json.Serialization;

namespace Pineglow.Launcher.Services;

public sealed class DeviceCodeInfo
{
	[JsonPropertyName("user_code")]
	public string UserCode { get; set; } = string.Empty;

	[JsonPropertyName("device_code")]
	public string DeviceCode { get; set; } = string.Empty;

	[JsonPropertyName("verification_uri")]
	public string VerificationUri { get; set; } = string.Empty;

	[JsonPropertyName("verification_uri_complete")]
	public string VerificationUriComplete { get; set; } = string.Empty;

	[JsonPropertyName("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonPropertyName("interval")]
	public int Interval { get; set; } = 5;

	[JsonPropertyName("message")]
	public string Message { get; set; } = string.Empty;
}
