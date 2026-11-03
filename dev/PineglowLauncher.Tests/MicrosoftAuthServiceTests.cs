using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

public class MicrosoftAuthServiceTests
{
	private sealed class FakeHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

		public HttpRequestMessage? LastRequest { get; private set; }

		public string? LastBody { get; private set; }

		public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
		{
			_respond = respond;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastRequest = request;
			LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			return _respond(request);
		}
	}

	private static MicrosoftAuthService CreateService(FakeHandler handler) => new MicrosoftAuthService(new HttpClient(handler));

	private static DeviceCodeInfo DeviceCode() => new DeviceCodeInfo { DeviceCode = "DEVICE-CODE-123", UserCode = "ABCDEFG" };

	[Fact]
	public async Task PollDeviceCodeOnceAsync_Success_ReturnsTokenAndPostsForm()
	{
		const string body = """{"access_token":"ACCESS-1","refresh_token":"REFRESH-1","expires_in":3600}""";
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		});
		MicrosoftAuthService service = CreateService(handler);

		MicrosoftOAuthToken token = await service.PollDeviceCodeOnceAsync("client-1", DeviceCode(), CancellationToken.None);

		Assert.Equal("ACCESS-1", token.AccessToken);
		Assert.Equal("REFRESH-1", token.RefreshToken);
		Assert.Equal(3600, token.ExpiresIn);
		Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
		Assert.Contains("login.microsoftonline.com/consumers/oauth2/v2.0/token", handler.LastRequest?.RequestUri?.ToString() ?? string.Empty);
		Assert.Contains("device_code=DEVICE-CODE-123", handler.LastBody ?? string.Empty);
		Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", handler.LastBody ?? string.Empty);
	}

	[Fact]
	public async Task PollDeviceCodeOnceAsync_AuthorizationPending_ThrowsOperationCanceled()
	{
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("""{"error":"authorization_pending"}""", Encoding.UTF8, "application/json")
		});
		MicrosoftAuthService service = CreateService(handler);

		await Assert.ThrowsAsync<OperationCanceledException>(() => service.PollDeviceCodeOnceAsync("client-1", DeviceCode(), CancellationToken.None));
	}

	[Fact]
	public async Task PollDeviceCodeOnceAsync_SlowDown_ThrowsOperationCanceled()
	{
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("""{"error":"slow_down"}""", Encoding.UTF8, "application/json")
		});
		MicrosoftAuthService service = CreateService(handler);

		await Assert.ThrowsAsync<OperationCanceledException>(() => service.PollDeviceCodeOnceAsync("client-1", DeviceCode(), CancellationToken.None));
	}

	[Fact]
	public async Task PollDeviceCodeOnceAsync_Declined_ThrowsInvalidOperation()
	{
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("""{"error":"authorization_declined"}""", Encoding.UTF8, "application/json")
		});
		MicrosoftAuthService service = CreateService(handler);

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PollDeviceCodeOnceAsync("client-1", DeviceCode(), CancellationToken.None));
		Assert.Contains("取消", ex.Message);
	}

	[Fact]
	public async Task PollDeviceCodeOnceAsync_Expired_ThrowsInvalidOperation()
	{
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
		{
			Content = new StringContent("""{"error":"expired_token"}""", Encoding.UTF8, "application/json")
		});
		MicrosoftAuthService service = CreateService(handler);

		InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PollDeviceCodeOnceAsync("client-1", DeviceCode(), CancellationToken.None));
		Assert.Contains("过期", ex.Message);
	}

	[Fact]
	public async Task PollDeviceCodeOnceAsync_EmptyClientId_ThrowsWithoutHttp()
	{
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
		MicrosoftAuthService service = CreateService(handler);

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.PollDeviceCodeOnceAsync("  ", DeviceCode(), CancellationToken.None));
		Assert.Null(handler.LastRequest);
	}
}
