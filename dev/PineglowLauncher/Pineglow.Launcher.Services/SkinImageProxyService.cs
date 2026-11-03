using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public static class SkinImageProxyService
{
	public const int MaxBytes = 1024 * 1024;
	private const int MaxRedirects = 3;
	private static readonly byte[] PngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

	public static bool IsAllowedUri(Uri? uri) => uri is not null
		&& uri.Scheme == Uri.UriSchemeHttps
		&& !string.IsNullOrWhiteSpace(uri.Host)
		&& string.IsNullOrEmpty(uri.UserInfo);

	public static bool IsPublicAddress(IPAddress address)
	{
		if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
		if (address.AddressFamily == AddressFamily.InterNetwork)
		{
			byte[] b = address.GetAddressBytes();
			return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || b[0] >= 224
				|| (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31)
				|| (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127));
		}
		if (address.AddressFamily == AddressFamily.InterNetworkV6)
		{
			// IPv4-mapped IPv6（::ffff:a.b.c.d）按内嵌 IPv4 的公网性判定，避免绕过私网检查。
			if (address.IsIPv4MappedToIPv6)
			{
				return IsPublicAddress(address.MapToIPv4());
			}
			if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return false;
			byte[] b = address.GetAddressBytes();
			return (b[0] & 0xFE) != 0xFC;
		}
		return false;
	}

	public static bool TryReadPngDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
	{
		width = 0; height = 0;
		if (bytes.Length < 24 || !bytes[..8].SequenceEqual(PngSignature) || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
		width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
		height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
		return width == 64 && (height == 64 || height == 32);
	}

	public static void ValidateContentMetadata(string? mediaType, long? contentLength)
	{
		if (!string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("远端内容不是 PNG 图片");
		if (contentLength > MaxBytes) throw new InvalidOperationException("皮肤图片超过 1 MiB 上限");
	}

	public static async Task<byte[]> ReadBoundedAsync(Stream input, CancellationToken cancellationToken)
	{
		using MemoryStream output = new MemoryStream(); byte[] buffer = new byte[16384]; int read;
		while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
		{
			if (output.Length + read > MaxBytes) throw new InvalidOperationException("皮肤图片超过 1 MiB 上限");
			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		}
		return output.ToArray();
	}

	public static async Task<byte[]> FetchAsync(string url, CancellationToken cancellationToken)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? current) || !IsAllowedUri(current)) throw new InvalidOperationException("皮肤地址必须是安全的 HTTPS URL");
		using SocketsHttpHandler handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5), UseProxy = false };
		using HttpClient http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
		for (int redirect = 0; redirect <= MaxRedirects; redirect++)
		{
			await ValidateDestinationAsync(current, cancellationToken);
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, current);
			using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if ((int)response.StatusCode is >= 300 and < 400)
			{
				if (redirect == MaxRedirects || response.Headers.Location is null) throw new InvalidOperationException("皮肤图片重定向次数过多");
				current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
				if (!IsAllowedUri(current)) throw new InvalidOperationException("皮肤图片重定向到了不安全地址");
				continue;
			}
			response.EnsureSuccessStatusCode();
			ValidateContentMetadata(response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentLength);
			await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
			byte[] bytes = await ReadBoundedAsync(input, cancellationToken);
			if (!TryReadPngDimensions(bytes, out _, out _)) throw new InvalidOperationException("皮肤图片必须是 64×64 或 64×32 PNG");
			return bytes;
		}
		throw new InvalidOperationException("无法获取皮肤图片");
	}

	private static async Task ValidateDestinationAsync(Uri uri, CancellationToken cancellationToken)
	{
		IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
		if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address))) throw new InvalidOperationException("皮肤地址解析到了本机或内网地址");
	}
}
