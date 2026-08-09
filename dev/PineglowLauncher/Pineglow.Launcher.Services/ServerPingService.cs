using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public static class ServerPingService
{
	private const int ProtocolVersion = -1;

	public static async Task<string> ProbeAsync(string host, int port, CancellationToken ct)
	{
		MinecraftServerStatus minecraftServerStatus = await QueryAsync(host, port, ct);
		return (minecraftServerStatus.LatencyMs >= 0) ? $"可连接 · {minecraftServerStatus.LatencyMs} ms" : "可连接";
	}

	public static async Task<MinecraftServerStatus> QueryAsync(string host, int port, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(host))
		{
			throw new InvalidOperationException("服务器地址为空。");
		}
		host = host.Trim();
		port = ((port <= 0) ? 25565 : port);
		using TcpClient client = new TcpClient();
		await client.ConnectAsync(host, port, ct);
		MinecraftServerStatus result;
		await using (NetworkStream stream = client.GetStream())
		{
			Stopwatch watch = Stopwatch.StartNew();
			await WritePacketAsync(stream, delegate(MemoryStream writer)
			{
				WriteVarInt(writer, 0);
				WriteVarInt(writer, -1);
				WriteString(writer, host);
				InlineArray2<byte> buffer = default(InlineArray2<byte>);
				buffer[0] = (byte)(port >> 8);
				buffer[1] = (byte)port;
				writer.Write(buffer);
				WriteVarInt(writer, 1);
			}, ct);
			await WritePacketAsync(stream, delegate(MemoryStream writer)
			{
				WriteVarInt(writer, 0);
			}, ct);
			using MemoryStream responseStream = new MemoryStream(await ReadPacketAsync(stream, ct));
			if (ReadVarInt(responseStream) != 0)
			{
				throw new InvalidDataException("服务器返回了无法识别的状态响应。");
			}
			string json = ReadString(responseStream);
			long sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			await WritePacketAsync(stream, delegate(MemoryStream writer)
			{
				WriteVarInt(writer, 1);
				Span<byte> span = stackalloc byte[8];
				BinaryPrimitives.WriteInt64BigEndian(span, sentAt);
				writer.Write(span);
			}, ct);
			using MemoryStream memoryStream = new MemoryStream(await ReadPacketAsync(stream, ct));
			if (ReadVarInt(memoryStream) != 1)
			{
				throw new InvalidDataException("服务器没有正确返回延迟响应。");
			}
			if (memoryStream.Length - memoryStream.Position < 8)
			{
				throw new InvalidDataException("服务器延迟响应不完整。");
			}
			watch.Stop();
			result = ParseStatusJson(json, watch.ElapsedMilliseconds);
		}
		return result;
	}

	public static MinecraftServerStatus ParseStatusJson(string json, long latencyMs = -1L)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		JsonElement rootElement = jsonDocument.RootElement;
		JsonElement value;
		JsonElement element = (rootElement.TryGetProperty("version", out value) ? value : default(JsonElement));
		JsonElement value2;
		JsonElement element2 = (rootElement.TryGetProperty("players", out value2) ? value2 : default(JsonElement));
		JsonElement value3;
		byte[] faviconBytes = ((rootElement.TryGetProperty("favicon", out value3) && value3.ValueKind == JsonValueKind.String) ? DecodeFavicon(value3.GetString()) : null);
		JsonElement value4;
		return new MinecraftServerStatus
		{
			Motd = (rootElement.TryGetProperty("description", out value4) ? ReadText(value4) : string.Empty),
			VersionName = ReadString(element, "name"),
			OnlinePlayers = ReadInt(element2, "online", -1),
			MaxPlayers = ReadInt(element2, "max", -1),
			LatencyMs = latencyMs,
			FaviconBytes = faviconBytes
		};
	}

	private static async Task WritePacketAsync(NetworkStream stream, Action<MemoryStream> writePayload, CancellationToken ct)
	{
		using MemoryStream payload = new MemoryStream();
		writePayload(payload);
		byte[] array = payload.ToArray();
		using MemoryStream packet = new MemoryStream();
		WriteVarInt(packet, array.Length);
		packet.Write(array);
		await stream.WriteAsync(packet.ToArray(), ct);
	}

	private static async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken ct)
	{
		int num = await ReadVarIntAsync(stream, ct);
		if (num < 0 || num > 2097152)
		{
			throw new InvalidDataException("服务器状态响应大小异常。");
		}
		byte[] buffer = new byte[num];
		int num2;
		for (int offset = 0; offset < buffer.Length; offset += num2)
		{
			num2 = await stream.ReadAsync(buffer.AsMemory(offset), ct);
			if (num2 == 0)
			{
				throw new EndOfStreamException("服务器在返回状态前断开连接。");
			}
		}
		return buffer;
	}

	private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken ct)
	{
		int value = 0;
		for (int position = 0; position < 5; position++)
		{
			byte[] buffer = new byte[1];
			if (await stream.ReadAsync(buffer, ct) != 1)
			{
				throw new EndOfStreamException("服务器状态响应不完整。");
			}
			value |= (buffer[0] & 0x7F) << 7 * position;
			if ((buffer[0] & 0x80) == 0)
			{
				return value;
			}
		}
		throw new InvalidDataException("服务器状态响应中的整数格式无效。");
	}

	private static void WriteVarInt(Stream stream, int value)
	{
		uint num = (uint)value;
		do
		{
			byte b = (byte)(num & 0x7F);
			num >>= 7;
			if (num != 0)
			{
				b |= 0x80;
			}
			stream.WriteByte(b);
		}
		while (num != 0);
	}

	private static int ReadVarInt(Stream stream)
	{
		int num = 0;
		for (int i = 0; i < 5; i++)
		{
			int num2 = stream.ReadByte();
			if (num2 < 0)
			{
				throw new EndOfStreamException("服务器状态响应不完整。");
			}
			num |= (num2 & 0x7F) << 7 * i;
			if ((num2 & 0x80) == 0)
			{
				return num;
			}
		}
		throw new InvalidDataException("服务器状态响应中的整数格式无效。");
	}

	private static void WriteString(Stream stream, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		WriteVarInt(stream, bytes.Length);
		stream.Write(bytes);
	}

	private static string ReadString(Stream stream)
	{
		int num = ReadVarInt(stream);
		if (num < 0 || num > 1048576)
		{
			throw new InvalidDataException("服务器返回的文本过大。");
		}
		byte[] array = new byte[num];
		if (stream.Read(array, 0, num) != num)
		{
			throw new EndOfStreamException("服务器状态文本不完整。");
		}
		return Encoding.UTF8.GetString(array);
	}

	private static string ReadText(JsonElement value)
	{
		if (value.ValueKind == JsonValueKind.String)
		{
			return StripFormatting(value.GetString() ?? string.Empty);
		}
		if (value.ValueKind != JsonValueKind.Object && value.ValueKind != JsonValueKind.Array)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		AppendText(value, stringBuilder);
		return StripFormatting(stringBuilder.ToString());
	}

	private static void AppendText(JsonElement value, StringBuilder builder)
	{
		if (value.ValueKind == JsonValueKind.String)
		{
			builder.Append(value.GetString());
			return;
		}
		if (value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value.EnumerateArray())
			{
				AppendText(item, builder);
			}
			return;
		}
		if (value.ValueKind == JsonValueKind.Object)
		{
			if (value.TryGetProperty("text", out var value2) && value2.ValueKind == JsonValueKind.String)
			{
				builder.Append(value2.GetString());
			}
			if (value.TryGetProperty("extra", out var value3))
			{
				AppendText(value3, builder);
			}
		}
	}

	private static string StripFormatting(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] == '§' && i + 1 < value.Length)
			{
				i++;
			}
			else
			{
				stringBuilder.Append(value[i]);
			}
		}
		return stringBuilder.ToString().Trim();
	}

	private static string ReadString(JsonElement element, string property)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return string.Empty;
		}
		return value.GetString() ?? string.Empty;
	}

	private static int ReadInt(JsonElement element, string property, int fallback)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value) || !value.TryGetInt32(out var value2))
		{
			return fallback;
		}
		return value2;
	}

	private static byte[]? DecodeFavicon(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		try
		{
			return Convert.FromBase64String(value.Substring("data:image/png;base64,".Length));
		}
		catch (FormatException)
		{
			return null;
		}
	}
}
