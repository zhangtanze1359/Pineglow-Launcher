using System.Net;
using Pineglow.Launcher.Services;

public sealed class SkinImageProxyServiceTests
{
	[Theory]
	[InlineData("https://textures.minecraft.net/texture/a", true)]
	[InlineData("http://textures.minecraft.net/texture/a", false)]
	[InlineData("file:///etc/passwd", false)]
	[InlineData("https://user:pass@example.com/a.png", false)]
	public void IsAllowedUri_OnlyAllowsCredentialFreeHttps(string value, bool expected)
	{
		Assert.Equal(expected, SkinImageProxyService.IsAllowedUri(new Uri(value)));
	}

	[Theory]
	[InlineData("127.0.0.1", false)]
	[InlineData("10.0.0.1", false)]
	[InlineData("172.16.0.1", false)]
	[InlineData("192.168.1.1", false)]
	[InlineData("169.254.1.1", false)]
	[InlineData("8.8.8.8", true)]
	[InlineData("::1", false)]
	[InlineData("fc00::1", false)]
	[InlineData("2606:4700:4700::1111", true)]
	public void IsPublicAddress_RejectsLoopbackPrivateAndLinkLocal(string value, bool expected)
	{
		Assert.Equal(expected, SkinImageProxyService.IsPublicAddress(IPAddress.Parse(value)));
	}

	[Theory]
	[InlineData(64, 64, true)]
	[InlineData(64, 32, true)]
	[InlineData(32, 64, false)]
	[InlineData(128, 128, false)]
	public void TryReadPngDimensions_OnlyAcceptsMinecraftSkinDimensions(int width, int height, bool expected)
	{
		byte[] png = CreateHeader(width, height);
		Assert.Equal(expected, SkinImageProxyService.TryReadPngDimensions(png, out int actualWidth, out int actualHeight));
		Assert.Equal(width, actualWidth);
		Assert.Equal(height, actualHeight);
	}

	[Fact]
	public void TryReadPngDimensions_RejectsNonPng()
	{
		Assert.False(SkinImageProxyService.TryReadPngDimensions(new byte[24], out _, out _));
	}

	[Fact]
	public void ValidateContentMetadata_RejectsHtmlAndDeclaredOversizeResponses()
	{
		Assert.Throws<InvalidOperationException>(() => SkinImageProxyService.ValidateContentMetadata("text/html", 100));
		Assert.Throws<InvalidOperationException>(() => SkinImageProxyService.ValidateContentMetadata("image/png", SkinImageProxyService.MaxBytes + 1L));
		SkinImageProxyService.ValidateContentMetadata("image/png", SkinImageProxyService.MaxBytes);
	}

	[Fact]
	public async Task ReadBoundedAsync_RejectsChunkedBodyOverLimit()
	{
		using MemoryStream stream = new MemoryStream(new byte[SkinImageProxyService.MaxBytes + 1]);
		await Assert.ThrowsAsync<InvalidOperationException>(() => SkinImageProxyService.ReadBoundedAsync(stream, CancellationToken.None));
	}

	private static byte[] CreateHeader(int width, int height)
	{
		byte[] bytes = new byte[24] { 137,80,78,71,13,10,26,10, 0,0,0,13, 73,72,68,82, 0,0,0,0, 0,0,0,0 };
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
		System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
		return bytes;
	}
}
