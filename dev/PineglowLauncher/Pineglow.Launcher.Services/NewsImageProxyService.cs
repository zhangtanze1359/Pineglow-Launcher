using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace Pineglow.Launcher.Services;

/// <summary>图片获取失败。Kind 取值见各抛出点；StatusCode 仅在 http_error 时有效。</summary>
public sealed class NewsImageException : Exception
{
    public string Kind { get; }

    public int StatusCode { get; }

    public NewsImageException(string kind, string message, int statusCode = 0) : base(message)
    {
        Kind = kind;
        StatusCode = statusCode;
    }
}

public sealed record NewsImageResponse(
    int StatusCode,
    string? Location,
    string? ContentType,
    long? ContentLength,
    Func<CancellationToken, Task<byte[]>> ReadBody);

/// <summary>
/// 新闻图片代理（Phase 3A）：
/// 弃用“客户端传任意 URL”的模型——官方 feed 解析成功后由服务端注册合规图片 URL 并生成
/// 不可篡改的图片 ID（SHA-256），前端只提交 ID；服务端从受信映射取回上游地址。
/// 网络层：精确官方 host + HTTPS；DNS 全部地址校验（环回/私网/链路本地/保留/IPv4-mapped 拒绝，
/// 公私混合整体拒绝）；实际连接绑定已验证地址（防 DNS rebinding）；每次重定向重新校验并限跳数；
/// 超时、3MB 上限、MIME、magic bytes 与结构化解码校验。
/// DNS 解析与传输层可注入，便于确定性测试与故障注入。
/// </summary>
public sealed class NewsImageProxyService
{
    public const int MaxImageBytes = 3 * 1024 * 1024;
    public const int MaxRedirectHops = 3;
    public static readonly TimeSpan DefaultImageTimeout = TimeSpan.FromSeconds(12);

    public delegate Task<NewsImageResponse> NewsImageTransport(Uri requestUri, IPAddress pinnedAddress, CancellationToken cancellationToken);

    private static readonly HttpRequestOptionsKey<IPAddress> PinnedAddressKey = new("pineglow.pinned-address");
    private static readonly HttpRequestOptionsKey<bool> PinnedTlsKey = new("pineglow.pinned-tls");

    private readonly string _cacheDirectory;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveDns;
    private readonly NewsImageTransport _transport;
    private readonly TimeSpan _imageTimeout;
    // R-02：198.18/15（fake-ip）兼容开关为显式注入的窄接口，默认 fail closed（拒绝）。
    // 兼容只能来自应用已确认的代理/TUN 状态；当前配置无法可靠证明 fake-ip 模式，生产保持 false。
    private readonly Func<bool> _allowFakeIpCompat;
    private readonly object _gate = new object();
    private readonly Dictionary<string, string> _registered = new(StringComparer.Ordinal);

    public NewsImageProxyService(
        string? cacheDirectory = null,
        Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null,
        NewsImageTransport? transport = null,
        TimeSpan? imageTimeout = null,
        Func<bool>? fakeIpCompat = null)
    {
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(PlatformPaths.GetConfigDirectory(), "cache", "news-images")
            : cacheDirectory;
        _resolveDns = dnsResolver ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));
        _transport = transport ?? PinnedTransportAsync;
        _imageTimeout = imageTimeout ?? DefaultImageTimeout;
        _allowFakeIpCompat = fakeIpCompat ?? (() => false);
    }

    /// <summary>测试专用：运行时切换 fake-ip 兼容开关。</summary>
    internal bool TestFakeIpCompat { get; set; }

    /// <summary>注册官方图片 URL，返回稳定 ID；URL 未通过策略时返回 null。</summary>
    public string? RegisterImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !NewsEndpointPolicy.IsAllowedImageUrl(url))
        {
            return null;
        }

        string id = ImageId(url);
        lock (_gate)
        {
            _registered[id] = url;
        }

        return id;
    }

    public string? TryGetImageUrl(string id)
    {
        lock (_gate)
        {
            return _registered.TryGetValue(id, out string? url) ? url : null;
        }
    }

    public static string ImageId(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

    public async Task<byte[]> FetchImageAsync(string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new NewsImageException("unknown_image", "image id is required");
        }

        string? url = TryGetImageUrl(id);
        if (url == null)
        {
            throw new NewsImageException("unknown_image", "unknown image id");
        }

        if (!NewsEndpointPolicy.IsAllowedImageUrl(url))
        {
            throw new NewsImageException("policy_rejected", "image url is not an approved official host");
        }

        string cachePath = Path.Combine(_cacheDirectory, ImageId(url) + ".img");
        if (File.Exists(cachePath))
        {
            try
            {
                return await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }

        bool fakeIpCompat = _allowFakeIpCompat() || TestFakeIpCompat;
        Uri current = new Uri(url);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_imageTimeout);

        try
        {
            for (int hop = 0; hop <= MaxRedirectHops; hop++)
            {
                if (!NewsEndpointPolicy.IsAllowedImageUrl(current.ToString()))
                {
                    throw new NewsImageException("policy_rejected", "redirect target is not an approved official host");
                }

                IPAddress[] addresses;
                try
                {
                    addresses = await _resolveDns(current.IdnHost, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is SocketException or OperationCanceledException)
                {
                    throw new NewsImageException("dns_failed", "dns resolution failed: " + Truncate(exception.Message));
                }

                IPAddress pinned = NewsSsrfGuard.SelectConnectableAddress(addresses, fakeIpCompat);
                NewsImageResponse response = await _transport(current, pinned, timeout.Token).ConfigureAwait(false);

                if (response.StatusCode >= 300 && response.StatusCode < 400)
                {
                    if (string.IsNullOrWhiteSpace(response.Location))
                    {
                        throw new NewsImageException("redirect_invalid", "redirect without location");
                    }

                    if (hop == MaxRedirectHops)
                    {
                        throw new NewsImageException("redirect_limit", "too many redirects");
                    }

                    current = new Uri(current, response.Location);
                    continue;
                }

                if (response.StatusCode < 200 || response.StatusCode >= 300)
                {
                    throw new NewsImageException("http_error", "image request failed: HTTP " + response.StatusCode, response.StatusCode);
                }

                string? mediaType = response.ContentType;
                if (string.IsNullOrEmpty(mediaType) || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NewsImageException("mime_rejected", "image response is not an image");
                }

                if (response.ContentLength is long length && length > MaxImageBytes)
                {
                    throw new NewsImageException("size_rejected", "image exceeds size limit");
                }

                byte[] bytes = await response.ReadBody(timeout.Token).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
                {
                    throw new NewsImageException("size_rejected", "image size is outside the allowed range");
                }

                if (!NewsImageValidator.IsStructurallyValidImage(bytes))
                {
                    throw new NewsImageException("decode_rejected", "image payload failed structural validation");
                }

                // R-01：最终准入必须完整像素解码（SkiaSharp GetPixels），
                // 解码前限制宽/高/总像素/内存（checked 计算）；失败载荷不落盘。
                if (!NewsImageValidator.TryFullDecode(bytes, out string decodeReason))
                {
                    throw new NewsImageException("decode_rejected", "image failed full decode: " + decodeReason);
                }

                WriteCacheAtomically(cachePath, bytes);
                return bytes;
            }

            throw new NewsImageException("redirect_limit", "too many redirects");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new NewsImageException("timeout", "image request timed out");
        }
    }

    private void WriteCacheAtomically(string cachePath, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            string temporary = cachePath + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, cachePath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "request failed";
        }

        return value.Length <= 160 ? value : value.Substring(0, 160);
    }

    /* ---------------- 生产传输层：固定已验证地址 + 禁止自动重定向 ---------------- */

    private static async Task<NewsImageResponse> PinnedTransportAsync(Uri requestUri, IPAddress pinnedAddress, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd(LauncherContentNewsProvider.UserAgent);
        request.Options.Set(PinnedAddressKey, pinnedAddress);
        request.Options.Set(PinnedTlsKey, string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        using HttpResponseMessage response = await PinnedClient.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        int status = (int)response.StatusCode;
        string? location = response.Headers.Location?.ToString();
        string? contentType = response.Content.Headers.ContentType?.MediaType;
        long? contentLength = response.Content.Headers.ContentLength;

        // 响应（含底层连接流）必须在本方法内完整消费并释放，不得跨出 transport 生命周期；
        // 因此在传输层内缓冲全部字节（强制 3MB 上限）。
        if (contentLength is long declared && declared > MaxImageBytes + 1)
        {
            return new NewsImageResponse(status, location, contentType, contentLength,
                _ => throw new NewsImageException("size_rejected", "image exceeds size limit"));
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream output = new MemoryStream();
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxImageBytes + 1)
            {
                throw new NewsImageException("size_rejected", "image exceeds size limit");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        byte[] bytes = output.ToArray();
        return new NewsImageResponse(status, location, contentType, contentLength, _ => Task.FromResult(bytes));
    }

    private static readonly Lazy<HttpClient> PinnedClient = new Lazy<HttpClient>(() =>
    {
        SocketsHttpHandler handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!context.InitialRequestMessage.Options.TryGetValue(PinnedAddressKey, out IPAddress? pinned) || pinned == null)
                {
                    throw new HttpRequestException("pinned validated address is missing");
                }

                bool useTls = context.InitialRequestMessage.Options.TryGetValue(PinnedTlsKey, out bool tls) && tls;
                return await ConnectPinnedAsync(
                    context.DnsEndPoint.Host,
                    pinned,
                    context.DnsEndPoint.Port,
                    useTls,
                    cancellationToken).ConfigureAwait(false);
            }
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    });

    /// <summary>连接到已验证的地址（不再二次解析，防 rebinding）；HTTPS 时以目标主机名做 TLS 校验。</summary>
    internal static async Task<Stream> ConnectPinnedAsync(string host, IPAddress pinnedAddress, int port, bool useTls, CancellationToken cancellationToken)
    {
        Socket socket = new Socket(pinnedAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(pinnedAddress, port), cancellationToken).ConfigureAwait(false);
            NetworkStream network = new NetworkStream(socket, ownsSocket: true);
            if (!useTls)
            {
                return network;
            }

            SslStream ssl = new SslStream(network, false);
            try
            {
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    RemoteCertificateValidationCallback = null // 默认 OS 证书与主机名校验
                }, cancellationToken).ConfigureAwait(false);
                return ssl;
            }
            catch
            {
                ssl.Dispose();
                throw;
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>DNS 解析结果的 SSRF 防护：全部地址必须是公网单播地址。</summary>
public static class NewsSsrfGuard
{
    public static IPAddress SelectConnectableAddress(IReadOnlyList<IPAddress> addresses, bool allowFakeIpRanges = false)
    {
        if (addresses == null || addresses.Count == 0)
        {
            throw new NewsImageException("dns_failed", "dns returned no addresses");
        }

        List<IPAddress> forbidden = addresses.Where(IsForbiddenAddress).ToList();
        if (forbidden.Count == addresses.Count)
        {
            throw new NewsImageException("dns_private", "dns resolved only to non-public addresses");
        }

        if (forbidden.Count > 0)
        {
            throw new NewsImageException("dns_mixed", "dns mixed public and non-public addresses");
        }

        // R-02：fake-ip（198.18/15）默认为受限地址；仅显式兼容模式下、且解析结果全部为
        // fake-ip（与公网/私网/环回等任何其他类别混合即整体拒绝）时才允许选择。
        List<IPAddress> fakeIp = addresses.Where(IsFakeIpBenchmarkAddress).ToList();
        if (fakeIp.Count > 0)
        {
            if (!allowFakeIpRanges)
            {
                throw fakeIp.Count == addresses.Count
                    ? new NewsImageException("dns_private", "dns resolved only to non-public addresses (fake-ip range rejected by default)")
                    : new NewsImageException("dns_mixed", "dns mixed public and non-public addresses");
            }

            if (fakeIp.Count != addresses.Count)
            {
                throw new NewsImageException("dns_mixed", "fake-ip mixed with other address classes");
            }

            return fakeIp[0];
        }

        return addresses.First(address => !IsForbiddenAddress(address));
    }

    /// <summary>危险地址：环回/私网/链路本地（含云元数据）/CGNAT/保留/组播等；198.18/15 由 fake-ip 策略单独处理。</summary>
    public static bool IsForbiddenAddress(IPAddress address)
    {
        IPAddress candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (candidate.AddressFamily == AddressFamily.InterNetwork)
        {
            return IsForbiddenIPv4(candidate.GetAddressBytes());
        }

        if (candidate.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IsForbiddenIPv6(candidate.GetAddressBytes());
        }

        return true;
    }

    /// <summary>198.18.0.0/15（含 IPv4-mapped 形式）：基准测试保留段，默认拒绝。</summary>
    public static bool IsFakeIpBenchmarkAddress(IPAddress address)
    {
        IPAddress candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (candidate.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] b = candidate.GetAddressBytes();
        return b[0] == 198 && (b[1] & 0xFE) == 18;
    }

    private static bool IsForbiddenIPv4(byte[] b)
    {
        uint v = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        uint top = v >> 24;
        if (b[0] == 0) return true;                       // 0.0.0.0/8
        if (b[0] == 10) return true;                      // RFC1918
        if (b[0] == 127) return true;                     // 环回
        if (b[0] == 169 && b[1] == 254) return true;      // 链路本地（含云元数据）
        if (b[0] == 172 && (b[1] & 0xF0) == 16) return true; // 172.16/12
        if (b[0] == 192 && b[1] == 168) return true;      // RFC1918
        if (b[0] == 192 && b[1] == 0 && b[2] == 0) return true;   // 192.0.0.0/24
        if (b[0] == 192 && b[1] == 0 && b[2] == 2) return true;   // TEST-NET-1
        if (b[0] == 198 && b[1] == 51 && b[2] == 100) return true; // TEST-NET-2
        if (b[0] == 203 && b[1] == 0 && b[2] == 113) return true;  // TEST-NET-3
        if (b[0] == 100 && (b[1] & 0xC0) == 64) return true;      // 100.64/10 CGNAT
        if (top >= 224) return true;                      // 组播 + 保留 240/4 + 广播
        return false;
    }

    private static bool IsForbiddenIPv6(byte[] b)
    {
        // R2-01 修正：
        //  真正 Teredo = 2001:0000::/32（RFC 4380），前 4 字节 0x20 0x01 0x00 0x00。
        //  2001:db8::/32 是文档前缀，与 Teredo 无关。
        //  2002::/16 是 6to4（RFC 3056），不是 Teredo；为防 6to4 隧道绕过整体拒绝。
        //  64:ff9b::/96 (RFC 6052 well-known) 与 64:ff9b:1::/48 (RFC 8215 local) 都是
        //  NAT64 IPv4 转换前缀，必须整体拒绝。旧实现用 b[0]==0x64 错位 1 字节，导致漏判。
        //  IPv6 字节序为大端（网络字节序），64:ff9b:: 实际首字节是 0x00（64 是 0064 完整 16 位）。
        bool allZero = b.All(x => x == 0);
        if (allZero) return true;                                   // ::
        if (b[15] == 1 && b.Take(15).All(x => x == 0)) return true; // ::1
        if ((b[0] & 0xFE) == 0xFC) return true;                     // fc00::/7 ULA
        if ((b[0] & 0xFF) == 0xFE && (b[1] & 0xC0) == 0x80) return true; // fe80::/10 链路本地
        if ((b[0] & 0xFF) == 0xFF) return true;                     // 组播 ff00::/8
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) return true; // 2001:0000::/32 真正 Teredo
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return true; // 2001:db8::/32 文档前缀
        if (b[0] == 0x20 && b[1] == 0x02) return true;              // 2002::/16 6to4（防 6to4 隧道绕过；IPv4 嵌入在第 17-32 位）
        // R2-01b 修正：NAT64 转换前缀必须只拒两个精确目标段的并集，
        //   64:ff9b::/96 (RFC 6052 well-known)：完整比较前 12 字节 00 64 FF 9B 00..00
        //   64:ff9b:1::/48 (RFC 8215 local-use)：完整比较前 6 字节 00 64 FF 9B 00 01
        // 旧实现用前 4 字节等同于 64:ff9b::/32，覆盖聚合过宽，误拒 64:ff9b:2::/48 等未分配/未来可能另作他用的地址。
        if (IsNat64WellKnownPrefix(b) || IsNat64LocalUsePrefix(b)) return true;
        return false;
    }

    private static bool IsNat64WellKnownPrefix(byte[] b) =>
        b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B
        && b[4] == 0x00 && b[5] == 0x00 && b[6] == 0x00 && b[7] == 0x00
        && b[8] == 0x00 && b[9] == 0x00 && b[10] == 0x00 && b[11] == 0x00;

    private static bool IsNat64LocalUsePrefix(byte[] b) =>
        b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B
        && b[4] == 0x00 && b[5] == 0x01;
}

/// <summary>结构化图片校验：magic bytes + 头部结构（PNG IHDR / JPEG SOFn / GIF / WebP），不引入解码依赖。</summary>
public static class NewsImageValidator
{
    public static bool IsStructurallyValidImage(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 12)
        {
            return false;
        }

        if (IsPng(bytes)) return HasPngHeader(bytes);
        if (IsJpeg(bytes)) return HasJpegFrameHeader(bytes);
        if (IsGif(bytes)) return true;
        if (IsWebP(bytes)) return bytes.Length >= 30;
        return false;
    }

    private static bool IsPng(byte[] b) => b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    private static bool HasPngHeader(byte[] b)
    {
        // 签名后第一个块必须是 IHDR，长度 13，且宽高在 1..65535
        if (b.Length < 33) return false;
        int length = b[8] << 24 | b[9] << 16 | b[10] << 8 | b[11];
        bool ihdr = b[12] == (byte)'I' && b[13] == (byte)'H' && b[14] == (byte)'D' && b[15] == (byte)'R';
        int width = b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19];
        int height = b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23];
        return ihdr && length == 13 && width > 0 && height > 0;
    }

    private static bool IsJpeg(byte[] b) => b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static bool HasJpegFrameHeader(byte[] b)
    {
        // 扫描前若干标记段，必须找到 SOF0-SOF3/SOF9-SOF15（帧头）才承认结构
        int i = 2;
        while (i + 4 < b.Length && i < 64 * 1024)
        {
            if (b[i] != 0xFF) return false;
            byte marker = b[i + 1];
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
            int segLength = (b[i + 2] << 8) | b[i + 3];
            if (segLength < 2) return false;
            bool isSof = (marker >= 0xC0 && marker <= 0xCF) && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSof) return b[i + 4] > 0 && segLength >= 6;
            i += 2 + segLength;
        }

        return false;
    }

    private static bool IsGif(byte[] b) => b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8';

    private static bool IsWebP(byte[] b) => b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P';

    /* ---------------- R-01：SkiaSharp 完整像素解码（最终准入） ---------------- */

    public const int MaxDecodeDimension = 8192;
    public const long MaxDecodePixels = 16_777_216;      // 16MP
    public const long MaxDecodeMemoryBytes = 64 * 1024 * 1024; // Bgra8888 = 4 字节/像素

    /// <summary>
    /// 完整像素解码验证：SKCodec → 编码格式与魔数一致 → 宽/高/像素量/内存上限（checked）→
    /// 分配受限缓冲 → GetPixels。只有 SKCodecResult.Success 才接受；
    /// IncompleteInput/InvalidInput/ErrorInInput 等一律拒绝。
    /// </summary>
    public static bool TryFullDecode(byte[] bytes, out string reason)
    {
        reason = "unknown";
        if (bytes == null || bytes.Length == 0)
        {
            reason = "empty";
            return false;
        }

        MemoryStream? stream = null;
        SKCodec? codec = null;
        SKBitmap? bitmap = null;
        try
        {
            stream = new MemoryStream(bytes, writable: false);
            codec = SKCodec.Create(stream);
            if (codec == null)
            {
                reason = "codec_create_failed";
                return false;
            }

            string detected = DetectImageFormat(bytes) ?? string.Empty;
            SKEncodedImageFormat encoded = codec.EncodedFormat;
            bool formatMatches = detected switch
            {
                "png" => encoded == SKEncodedImageFormat.Png,
                "jpg" => encoded == SKEncodedImageFormat.Jpeg,
                "gif" => encoded == SKEncodedImageFormat.Gif,
                "webp" => encoded == SKEncodedImageFormat.Webp,
                _ => false,
            };
            if (!formatMatches)
            {
                reason = "format_mismatch";
                return false;
            }

            int width = codec.Info.Width;
            int height = codec.Info.Height;
            if (width <= 0 || height <= 0)
            {
                reason = "invalid_dimensions";
                return false;
            }

            if (width > MaxDecodeDimension || height > MaxDecodeDimension)
            {
                reason = "dimension_limit";
                return false;
            }

            long pixels = checked((long)width * height);
            if (pixels > MaxDecodePixels)
            {
                reason = "pixel_limit";
                return false;
            }

            long decodeBytes = checked(pixels * 4);
            if (decodeBytes > MaxDecodeMemoryBytes)
            {
                reason = "memory_limit";
                return false;
            }

            bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            SKImageInfo target = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            SKCodecResult result = codec.GetPixels(target, bitmap.GetPixels());
            if (result == SKCodecResult.Success)
            {
                reason = string.Empty;
                return true;
            }

            reason = result.ToString();
            return false;
        }
        catch (Exception exception)
        {
            reason = exception.GetType().Name;
            return false;
        }
        finally
        {
            bitmap?.Dispose();
            codec?.Dispose();
            stream?.Dispose();
        }
    }

    internal static string? DetectImageFormat(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 12) return null;
        if (IsPng(bytes)) return "png";
        if (IsJpeg(bytes)) return "jpg";
        if (IsGif(bytes)) return "gif";
        if (IsWebP(bytes)) return "webp";
        return null;
    }
}
