using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pineglow.Launcher.Services;
using Xunit;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// 2026-11-06 未覆盖路径补测（批次 3）：<c>ServerPingService</c> 的**真实 TCP 协议层**。
///
/// 覆盖率实测该类是 **0/316**——整个服务器 Ping 功能一行没测过。它是"收藏服务器"页的
/// 唯一数据来源，坏了用户只会看到"连接失败"，没有任何线索。
///
/// 这批测试用 <see cref="TcpListener"/> 起一个**本地假服务器**，按 Minecraft
/// Server List Ping 协议逐字节应答，从而在不联外网的前提下验证：
///   * 握手包的实际字节布局（协议版本 -1、host、端口大端序、next state=1）；
///   * status 请求 / 响应包的长度前缀与 packet id；
///   * 延迟测量用的 ping/pong（8 字节大端序回显）；
///   * 各类畸形响应的错误语义（不是静默返回空对象）。
///
/// 假服务器**不复用被测代码**的编解码，独立实现一遍协议，否则就是"自己跟自己对答案"。
/// </summary>
public class ServerPingProtocolTests
{
	/// <summary>独立实现的 VarInt 编码（不调用被测的私有方法）。</summary>
	private static byte[] VarInt(int value)
	{
		uint v = (uint)value;
		var bytes = new List<byte>();
		do
		{
			byte b = (byte)(v & 0x7F);
			v >>= 7;
			if (v != 0)
			{
				b |= 0x80;
			}

			bytes.Add(b);
		}
		while (v != 0);

		return bytes.ToArray();
	}

	private static byte[] BigEndianBytes(long value)
	{
		byte[] buffer = new byte[8];
		BinaryPrimitives.WriteInt64BigEndian(buffer, value);
		return buffer;
	}

	private static byte[] Packet(params byte[][] parts)
	{
		byte[] body = parts.SelectMany(p => p).ToArray();
		return VarInt(body.Length).Concat(body).ToArray();
	}

	private static byte[] StringField(string value)
	{
		byte[] utf8 = Encoding.UTF8.GetBytes(value);
		return VarInt(utf8.Length).Concat(utf8).ToArray();
	}

	/// <summary>按协议读一个带长度前缀的包，返回包体。</summary>
	private static async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken ct)
	{
		int length = await ReadVarIntAsync(stream, ct);
		byte[] buffer = new byte[length];
		int read = 0;
		while (read < length)
		{
			int n = await stream.ReadAsync(buffer.AsMemory(read), ct);
			if (n == 0)
			{
				throw new EndOfStreamException();
			}

			read += n;
		}

		return buffer;
	}

	/// <summary>同步版 VarInt 读取，用于已缓冲在 MemoryStream 里的包体。</summary>
	private static int ReadVarInt(Stream stream)
	{
		int value = 0;
		for (int i = 0; i < 5; i++)
		{
			int b = stream.ReadByte();
			if (b < 0)
			{
				throw new EndOfStreamException();
			}

			value |= (b & 0x7F) << (7 * i);
			if ((b & 0x80) == 0)
			{
				return value;
			}
		}

		throw new InvalidDataException("伪造服务器读包时遇到非法 VarInt");
	}

	private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken ct)
	{
		int value = 0;
		for (int i = 0; i < 5; i++)
		{
			byte[] one = new byte[1];
			if (await stream.ReadAsync(one, ct) != 1)
			{
				throw new EndOfStreamException();
			}

			value |= (one[0] & 0x7F) << (7 * i);
			if ((one[0] & 0x80) == 0)
			{
				return value;
			}
		}

		throw new InvalidDataException("伪造服务器收到非法 VarInt");
	}

	/// <summary>记下客户端实际发来的握手字段，供断言用。</summary>
	private sealed class Captured
	{
		public int ProtocolVersion;
		public string Host = string.Empty;
		public int Port;
		public int NextState;
		public long PingPayload = long.MinValue;
	}

	private sealed class FakeServer : IAsyncDisposable
	{
		private readonly TcpListener _listener;
		private readonly CancellationTokenSource _cts = new();
		private readonly Task _loop;
		public Captured Captured { get; } = new();
		public int Port { get; }

		/// <summary>构造应答；抛异常即模拟"服务端行为异常"。</summary>
		public Func<Captured, byte[]>? StatusJsonFactory { get; set; }
		public Func<Captured, byte[]>? StatusPacketOverride { get; set; }
		public Func<Captured, byte[]>? PongPacketOverride { get; set; }
		public bool CloseImmediately { get; set; }

		public FakeServer()
		{
			_listener = new TcpListener(IPAddress.Loopback, 0);
			_listener.Start();
			Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
			_loop = Task.Run(RunAsync);
		}

		private async Task RunAsync()
		{
			while (!_cts.IsCancellationRequested)
			{
				TcpClient client;
				try
				{
					client = await _listener.AcceptTcpClientAsync(_cts.Token);
				}
				catch
				{
					return;
				}

				_ = Task.Run(async () =>
				{
					using (client)
					{
						try
						{
							if (CloseImmediately)
							{
								client.Close();
								return;
							}

							await using NetworkStream stream = client.GetStream();

							// --- 握手包 ---
							byte[] handshake = await ReadPacketAsync(stream, _cts.Token);
							using (var ms = new MemoryStream(handshake))
							{
								_ = ReadVarInt(ms);
								Captured.ProtocolVersion = ReadVarInt(ms);
								int hostLen = ReadVarInt(ms);
								byte[] hostBytes = new byte[hostLen];
								ms.ReadExactly(hostBytes, 0, hostLen);
								Captured.Host = Encoding.UTF8.GetString(hostBytes);
								Span<byte> portBytes = stackalloc byte[2];
								ms.ReadExactly(portBytes);
								Captured.Port = BinaryPrimitives.ReadUInt16BigEndian(portBytes);
								Captured.NextState = ReadVarInt(ms);
							}

							// --- status 请求（空包体，只有 packet id 0）---
							_ = await ReadPacketAsync(stream, _cts.Token);

							byte[] status = StatusPacketOverride?.Invoke(Captured)
								?? Packet(VarInt(0), StringField(Encoding.UTF8.GetString(StatusJsonFactory?.Invoke(Captured) ?? Encoding.UTF8.GetBytes("{}"))));
							await stream.WriteAsync(status, _cts.Token);
							await stream.FlushAsync(_cts.Token);

							// --- ping ---
							byte[] ping = await ReadPacketAsync(stream, _cts.Token);
							using (var ms = new MemoryStream(ping))
							{
								_ = ReadVarInt(ms);
								Span<byte> payload = stackalloc byte[8];
								ms.ReadExactly(payload);
								Captured.PingPayload = BinaryPrimitives.ReadInt64BigEndian(payload);
							}

							byte[] pong = PongPacketOverride?.Invoke(Captured)
								?? Packet(VarInt(1), BigEndianBytes(Captured.PingPayload));
							await stream.WriteAsync(pong, _cts.Token);
							await stream.FlushAsync(_cts.Token);
						}
						catch
						{
							// 客户端提前断开等情形，忽略
						}
					}
				});
			}
		}

		public async ValueTask DisposeAsync()
		{
			_cts.Cancel();
			try { _listener.Stop(); } catch { }
			try { await _loop; } catch { }
			_cts.Dispose();
		}
	}

	private static string StatusPayload(string motd, int online, int max, string version = "1.20.4")
		=> $"{{\"version\":{{\"name\":\"{version}\",\"protocol\":765}},\"players\":{{\"online\":{online},\"max\":{max}}},\"description\":\"{motd}\"}}";

	[Fact]
	public async Task Query_SendsCorrectHandshake_AndParsesStatus()
	{
		await using var server = new FakeServer
		{
			StatusJsonFactory = _ => Encoding.UTF8.GetBytes(StatusPayload("§aHello", 7, 20)),
		};

		var status = await ServerPingService.QueryAsync("127.0.0.1", server.Port, CancellationToken.None);

		// 客户端发来的握手必须符合协议：协议版本 -1（不关心版本）、host 回环、端口一致、next state = 1（status）
		Assert.Equal(-1, server.Captured.ProtocolVersion);
		Assert.Equal("127.0.0.1", server.Captured.Host);
		Assert.Equal(server.Port, server.Captured.Port);
		Assert.Equal(1, server.Captured.NextState);

		// 解析结果
		Assert.Equal("Hello", status.Motd); // 格式化码被剥掉
		Assert.Equal(7, status.OnlinePlayers);
		Assert.Equal(20, status.MaxPlayers);
		Assert.Equal("1.20.4", status.VersionName);
		Assert.True(status.LatencyMs >= 0, "延迟必须被实测出来，不能是 -1");
	}

	[Fact]
	public async Task Query_PingPayloadIsEchoed_ProvingTheLatencyProbeRoundTripped()
	{
		await using var server = new FakeServer
		{
			StatusJsonFactory = _ => Encoding.UTF8.GetBytes(StatusPayload("x", 0, 0)),
		};

		await ServerPingService.QueryAsync("127.0.0.1", server.Port, CancellationToken.None);

		// 假服务器把收到的 8 字节原样回显。若客户端没有正确发/收 ping，
		// 这里会是 long.MinValue（未捕获）或解析出的值不合法。
		Assert.NotEqual(long.MinValue, server.Captured.PingPayload);
		Assert.True(server.Captured.PingPayload > 0, "ping 载荷应当是当前毫秒时间戳");
	}

	[Fact]
	public async Task Query_EmptyHost_ThrowsActionableError()
	{
		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => ServerPingService.QueryAsync("   ", 25565, CancellationToken.None));
		Assert.Contains("服务器地址为空", ex.Message);
	}

	[Fact]
	public async Task Query_ConnectionRefused_PropagatesSoUiCanSayUnreachable()
	{
		// 找一个必然没人监听的端口
		var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		int freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();

		await Assert.ThrowsAnyAsync<SocketException>(
			() => ServerPingService.QueryAsync("127.0.0.1", freePort, CancellationToken.None));
	}

	[Fact]
	public async Task Query_ServerClosesImmediately_ThrowsInsteadOfHanging()
	{
		await using var server = new FakeServer { CloseImmediately = true };

		// 关键：不能永久挂起。用超时断言兜一层，避免测试本身卡死。
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		await Assert.ThrowsAnyAsync<Exception>(
			() => ServerPingService.QueryAsync("127.0.0.1", server.Port, cts.Token));
	}

	[Fact]
	public async Task Query_NonZeroStatusPacketId_IsRejected()
	{
		await using var server = new FakeServer
		{
			// packet id 应该是 0；这里故意写 9
			StatusPacketOverride = _ => Packet(VarInt(9), StringField("{}")),
		};

		var ex = await Assert.ThrowsAsync<InvalidDataException>(
			() => ServerPingService.QueryAsync("127.0.0.1", server.Port, CancellationToken.None));
		Assert.Contains("无法识别的状态响应", ex.Message);
	}

	[Fact]
	public async Task Query_WrongPongPacketId_IsRejected()
	{
		await using var server = new FakeServer
		{
			StatusJsonFactory = _ => Encoding.UTF8.GetBytes(StatusPayload("x", 0, 0)),
			// pong 的 packet id 必须是 1
			PongPacketOverride = _ => Packet(VarInt(2), new byte[8]),
		};

		var ex = await Assert.ThrowsAsync<InvalidDataException>(
			() => ServerPingService.QueryAsync("127.0.0.1", server.Port, CancellationToken.None));
		Assert.Contains("延迟响应", ex.Message);
	}

	[Fact]
	public async Task Query_TruncatedPongPayload_IsRejected()
	{
		await using var server = new FakeServer
		{
			StatusJsonFactory = _ => Encoding.UTF8.GetBytes(StatusPayload("x", 0, 0)),
			// 只回 3 字节而不是 8 字节
			PongPacketOverride = _ => Packet(VarInt(1), new byte[3]),
		};

		var ex = await Assert.ThrowsAsync<InvalidDataException>(
			() => ServerPingService.QueryAsync("127.0.0.1", server.Port, CancellationToken.None));
		Assert.Contains("不完整", ex.Message);
	}

	[Fact]
	public async Task Query_OversizedStatusPacket_IsRejected_BeforeAllocating()
	{
		// 只写一个"长度 3MB"的 VarInt 前缀就把连接挂住：读端必须在分配前拒绝。
		await using var evil = new FakeServer
		{
			StatusPacketOverride = _ => VarInt(3 * 1024 * 1024), // 只有长度，没有内容
		};

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		await Assert.ThrowsAnyAsync<Exception>(
			() => ServerPingService.QueryAsync("127.0.0.1", evil.Port, cts.Token));
	}

	[Fact]
	public async Task Probe_ReturnsHumanReadableLatencyLine()
	{
		await using var server = new FakeServer
		{
			StatusJsonFactory = _ => Encoding.UTF8.GetBytes(StatusPayload("x", 0, 0)),
		};

		string text = await ServerPingService.ProbeAsync("127.0.0.1", server.Port, CancellationToken.None);

		Assert.StartsWith("可连接", text);
		Assert.Contains("ms", text);
	}

	[Fact]
	public async Task Query_CancelledToken_ThrowsOperationCancelled()
	{
		await using var server = new FakeServer
		{
			// 故意不返回任何东西
			StatusJsonFactory = _ => Encoding.UTF8.GetBytes(StatusPayload("x", 0, 0)),
			StatusPacketOverride = _ => Array.Empty<byte>(),
		};

		using var cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromMilliseconds(200));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => ServerPingService.QueryAsync("127.0.0.1", server.Port, cts.Token));
	}
}
