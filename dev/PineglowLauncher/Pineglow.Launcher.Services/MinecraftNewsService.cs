using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pineglow.Launcher.Services;

public sealed class MinecraftNewsItem
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Date { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    public string Link { get; set; } = string.Empty;
}

/// <summary>
/// 新闻内容状态（Phase 3A 状态机）：
/// live=本次 200 新内容；revalidated=本次 304 验证未变；memory=30 分钟内内存新鲜期；
/// cached=上游失败，展示 6 小时内的官方磁盘缓存；empty=没有可展示内容；error=无可用内容。
/// </summary>
public enum NewsContentState
{
    Live,
    Revalidated,
    Memory,
    Cached,
    Empty,
    Error
}

public static class NewsContentStateText
{
    public static string ToWire(NewsContentState state) => state switch
    {
        NewsContentState.Live => "live",
        NewsContentState.Revalidated => "revalidated",
        NewsContentState.Memory => "memory",
        NewsContentState.Cached => "cached",
        NewsContentState.Empty => "empty",
        _ => "error"
    };
}

/// <summary>
/// 取数结果与可信元数据。errorKind 取值：rate_limited / http_server / http_client / network /
/// timeout / parse / empty_response / internal。Error 只包含对外安全的简要原因，
/// 不含堆栈、内部路径或上游响应正文。
/// </summary>
public sealed record NewsFetchResult(
    IReadOnlyList<MinecraftNewsItem> Items,
    string Source,
    NewsContentState State,
    DateTimeOffset ValidatedAt,
    DateTimeOffset FetchedAt,
    string? ETag,
    string? LastModified,
    bool IsStale,
    bool CanRetry,
    string? ErrorKind,
    string? Error)
{
    public static NewsFetchResult Failure(string source, string errorKind, string message, DateTimeOffset at) =>
        new(Array.Empty<MinecraftNewsItem>(), source, NewsContentState.Error, DateTimeOffset.MinValue, DateTimeOffset.MinValue, null, null, true, true, errorKind, message);
}

public sealed record NewsProviderResult(
    IReadOnlyList<MinecraftNewsItem>? Items,
    bool NotModified,
    string? ETag,
    string? LastModified,
    int? StatusCode,
    string? ErrorKind,
    string? Error);

public interface INewsProvider
{
    string Name { get; }

    Task<NewsProviderResult> FetchAsync(string? etag, string? lastModified, CancellationToken cancellationToken);
}

public sealed class LauncherContentNewsProvider : INewsProvider
{
    public const string DefaultEndpoint = "https://launchercontent.mojang.com/news.json";
    private const string ImageBase = "https://launchercontent.mojang.com";

    private readonly Func<HttpClient> _httpProvider;
    private readonly string _endpoint;

    public string Name => "launchercontent.mojang.com";

    public static string UserAgent => "PineglowLauncher/" + Pineglow.Launcher.Services.LauncherService.LauncherVersion;

    public LauncherContentNewsProvider(HttpClient http, string? endpoint = null)
        : this(() => http, endpoint)
    {
    }

    public LauncherContentNewsProvider(Func<HttpClient> httpProvider, string? endpoint = null)
    {
        _httpProvider = httpProvider;
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint;
        if (!NewsEndpointPolicy.IsAllowedFeedEndpoint(_endpoint))
        {
            throw new ArgumentException("news feed endpoint is not an approved official host", nameof(endpoint));
        }
    }

    public async Task<NewsProviderResult> FetchAsync(string? etag, string? lastModified, CancellationToken cancellationToken)
    {
        HttpClient http = _httpProvider();
        using HttpRequestMessage request = new(HttpMethod.Get, _endpoint);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (!string.IsNullOrEmpty(etag))
        {
            // 原样透传上游 ETag（如 Azure Blob 的 0x8D... 无引号形式）；加引号会导致 304 匹配失败
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        if (!string.IsNullOrEmpty(lastModified) && DateTimeOffset.TryParse(lastModified, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset since))
        {
            request.Headers.IfModifiedSince = since;
        }

        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        int status = (int)response.StatusCode;
        if (status == 304)
        {
            return new NewsProviderResult(null, true, etag, lastModified, 304, null, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            string kind = status == 429 ? "rate_limited" : (status >= 500 ? "http_server" : "http_client");
            return new NewsProviderResult(null, false, null, null, status, kind, "HTTP " + status);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return new NewsProviderResult(Array.Empty<MinecraftNewsItem>(), false, ReadHeader(response.Headers, response.Content.Headers, "ETag"), ReadHeader(response.Headers, response.Content.Headers, "Last-Modified"), 200, "parse", "feed root is not an entries array");
        }

        List<MinecraftNewsItem> items = new List<MinecraftNewsItem>();
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            MinecraftNewsItem item = ParseEntry(entry);
            if (!string.IsNullOrWhiteSpace(item.Title) && !items.Any(existing => SameItem(existing, item)))
            {
                items.Add(item);
            }
        }

        return new NewsProviderResult(items.OrderByDescending(item => MinecraftNewsDate.ParseOrMin(item.Date)).ToList(), false, ReadHeader(response.Headers, response.Content.Headers, "ETag"), ReadHeader(response.Headers, response.Content.Headers, "Last-Modified"), 200, null, null);
    }

    private static string? ReadHeader(System.Net.Http.Headers.HttpHeaders primary, System.Net.Http.Headers.HttpHeaders secondary, string name)
    {
        if (primary.TryGetValues(name, out IEnumerable<string>? values))
        {
            string? value = values.FirstOrDefault();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        if (secondary.TryGetValues(name, out IEnumerable<string>? secondaryValues))
        {
            string? value = secondaryValues.FirstOrDefault();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static bool SameItem(MinecraftNewsItem left, MinecraftNewsItem right)
    {
        return string.Equals(NormalizeTitle(left.Title), NormalizeTitle(right.Title), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTitle(string value) => value.Trim().ToLowerInvariant();

    private static MinecraftNewsItem ParseEntry(JsonElement entry)
    {
        MinecraftNewsItem item = new MinecraftNewsItem
        {
            Id = ReadString(entry, "id"),
            Title = ReadString(entry, "title"),
            Category = ReadString(entry, "category"),
            Date = ReadString(entry, "date"),
            Text = ReadString(entry, "text"),
            Link = ReadString(entry, "readMoreLink")
        };

        if (entry.TryGetProperty("playPageImage", out JsonElement image) && image.ValueKind == JsonValueKind.Object)
        {
            string path = ReadString(image, "url");
            if (!string.IsNullOrWhiteSpace(path))
            {
                item.ImageUrl = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : ImageBase + path;
            }
        }

        return item;
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

public static class MinecraftNewsDate
{
    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats = { "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-ddTHH:mm:ss'Z'", "yyyy-MM-dd" };
        if (DateTimeOffset.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset result))
        {
            return result;
        }

        return null;
    }

    public static DateTimeOffset ParseOrMin(string? value) => Parse(value) ?? DateTimeOffset.MinValue;
}

/// <summary>
/// 新闻服务（Phase 3A）：
/// - 30 分钟内存新鲜期（按最后成功验证时间 ValidatedAt 计算）；
/// - 到达 30 分钟后发条件请求（ETag/If-Modified-Since），304 沿用内容并更新验证时间；
/// - 手动刷新（forceRefresh）绕过内存但仍携带 validators；
/// - 上游失败时只展示最后成功验证后 6 小时内的官方磁盘缓存（cached）；
/// - 失败结果不写内存、不写磁盘；空结果同样不落盘，防止坏数据覆盖好缓存；
/// - 并发普通请求共享一次上游刷新，等待者取消不影响共享刷新；
/// - 提交代际号保证乱序完成时旧结果不覆盖新结果；
/// - 缓存信封版本化（schemaVersion=2）+ 同目录临时文件原子替换。
/// </summary>
public sealed class MinecraftNewsService
{
    public static readonly TimeSpan MemoryFreshness = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DiskDisplayWindow = TimeSpan.FromHours(6);
    public const int CacheSchemaVersion = 2;

    private readonly INewsProvider _provider;
    private readonly string _cacheDirectory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _fetchTimeout;
    private readonly object _gate = new object();
    private NewsFetchResult? _memoryCache;
    private Task<NewsFetchResult>? _inFlight;
    private long _generationCounter;
    private long _committedGeneration;

    public MinecraftNewsService(
        INewsProvider provider,
        string? cacheDirectory = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? fetchTimeout = null)
    {
        _provider = provider;
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(PlatformPaths.GetConfigDirectory(), "cache")
            : cacheDirectory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _fetchTimeout = fetchTimeout ?? TimeSpan.FromSeconds(15);
    }

    internal string TestCacheDirectory => _cacheDirectory;

    public Task<NewsFetchResult> FetchAsync(int limit = 12, CancellationToken ct = default, bool forceRefresh = false)
    {
        DateTimeOffset now = _clock();
        Task<NewsFetchResult>? flight;
        lock (_gate)
        {
            if (!forceRefresh)
            {
                NewsFetchResult? memory = _memoryCache;
                if (memory != null && now - memory.ValidatedAt < MemoryFreshness)
                {
                    return Task.FromResult(Serve(memory, NewsContentState.Memory, limit));
                }

                flight = _inFlight;
                if (flight != null)
                {
                    return AwaitSharedAsync(flight, ct, limit);
                }
            }

            long generation = ++_generationCounter;
            TaskCompletionSource<NewsFetchResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = completion.Task;
            StartLeader(generation, completion);
            flight = completion.Task;
        }

        return AwaitSharedAsync(flight, ct, limit);
    }

    public NewsFetchResult? PeekMemory() => _memoryCache;

    private void StartLeader(long generation, TaskCompletionSource<NewsFetchResult> completion)
    {
        // 内联启动：leader 在调用方线程同步执行到首个真正 await。
        // 若经 Task.Run 排队，线程池被阻塞型操作占满时 leader 可能长期不运行，
        // 导致请求计数与提交时序不确定（并行测试环境实测）。
        _ = RunLeaderAsync(generation, completion);
    }

    private async Task RunLeaderAsync(long generation, TaskCompletionSource<NewsFetchResult> completion)
    {
        NewsFetchResult result;
        try
        {
            result = await FetchCoreAsync(generation).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            result = ClassifyException(exception);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, completion.Task))
                {
                    _inFlight = null;
                }
            }
        }

        completion.TrySetResult(result);
    }

    private async Task<NewsFetchResult> FetchCoreAsync(long generation)
    {
        DiskEnvelope? disk = ReadDiskEnvelope();
        NewsFetchResult? memory = _memoryCache;
        bool memoryIsNewer = memory != null && (disk == null || memory.ValidatedAt >= disk.ValidatedAt);
        string? etag = memoryIsNewer ? memory!.ETag : disk?.ETag;
        string? lastModified = memoryIsNewer ? memory!.LastModified : disk?.LastModified;

        NewsProviderResult upstream;
        DateTimeOffset now = _clock();
        try
        {
            using CancellationTokenSource timeout = new CancellationTokenSource(_fetchTimeout);
            upstream = await _provider.FetchAsync(etag, lastModified, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException or IOException)
        {
            return BuildFailure(ClassifyException(exception).ErrorKind!, ClassifyException(exception).Error, disk, now);
        }

        if (upstream.NotModified)
        {
            IReadOnlyList<MinecraftNewsItem>? items = memory != null && memory.Items.Count > 0 ? memory.Items : disk?.Items;
            if (items == null || items.Count == 0)
            {
                return new NewsFetchResult(Array.Empty<MinecraftNewsItem>(), _provider.Name, NewsContentState.Error, now, now, etag, lastModified, true, true, "parse", "304 without local content");
            }

            NewsFetchResult revalidated = new NewsFetchResult(
                items, _provider.Name, NewsContentState.Revalidated, now,
                memoryIsNewer ? memory!.FetchedAt : disk?.FetchedAt ?? now,
                upstream.ETag ?? etag, upstream.LastModified ?? lastModified, false, false, null, null);
            Commit(generation, revalidated);
            return revalidated;
        }

        if (upstream.Items == null)
        {
            return BuildFailure(upstream.ErrorKind ?? "internal", upstream.Error, disk, now);
        }

        if (upstream.Items.Count == 0)
        {
            // 空结果不落盘、不进内存：坏数据不得覆盖好缓存；空状态可重试。
            return new NewsFetchResult(Array.Empty<MinecraftNewsItem>(), _provider.Name, NewsContentState.Empty, now, now, upstream.ETag ?? etag, upstream.LastModified ?? lastModified, true, true, "empty_response", null);
        }

        NewsFetchResult fresh = new NewsFetchResult(
            upstream.Items, _provider.Name, NewsContentState.Live, now, now,
            upstream.ETag, upstream.LastModified, false, false, null, null);
        Commit(generation, fresh);
        return fresh;
    }

    private NewsFetchResult BuildFailure(string errorKind, string? error, DiskEnvelope? disk, DateTimeOffset now)
    {
        if (disk != null && disk.Items.Count > 0 && disk.Source == _provider.Name && now - disk.ValidatedAt < DiskDisplayWindow)
        {
            return new NewsFetchResult(disk.Items, _provider.Name, NewsContentState.Cached, disk.ValidatedAt, disk.FetchedAt, disk.ETag, disk.LastModified, true, true, errorKind, error);
        }

        return new NewsFetchResult(Array.Empty<MinecraftNewsItem>(), _provider.Name, NewsContentState.Error, DateTimeOffset.MinValue, DateTimeOffset.MinValue, null, null, true, true, errorKind, error);
    }

    private NewsFetchResult ClassifyException(Exception exception)
    {
        string kind = exception switch
        {
            OperationCanceledException => "timeout",
            HttpRequestException => "network",
            JsonException => "parse",
            IOException => "network",
            _ => "internal"
        };
        string message = Truncate(exception.Message);
        return NewsFetchResult.Failure(_provider.Name, kind, message, _clock());
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "news request failed";
        }

        return value.Length <= 200 ? value : value.Substring(0, 200);
    }

    private bool Commit(long generation, NewsFetchResult result)
    {
        lock (_gate)
        {
            if (generation < _committedGeneration)
            {
                return false;
            }

            _committedGeneration = generation;
            _memoryCache = result;
        }

        WriteDiskCache(result);
        return true;
    }

    private static NewsFetchResult Serve(NewsFetchResult cached, NewsContentState state, int limit)
    {
        return cached with
        {
            State = state,
            Items = cached.Items.Take(limit).ToList(),
            IsStale = false,
            CanRetry = false
        };
    }

    private static async Task<NewsFetchResult> AwaitSharedAsync(Task<NewsFetchResult> flight, CancellationToken ct, int limit)
    {
        try
        {
            NewsFetchResult result = await flight.WaitAsync(ct).ConfigureAwait(false);
            return result with { Items = result.Items.Take(limit).ToList() };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
    }

    private sealed record DiskEnvelope(
        IReadOnlyList<MinecraftNewsItem> Items,
        string Source,
        string? ETag,
        string? LastModified,
        DateTimeOffset FetchedAt,
        DateTimeOffset ValidatedAt);

    private DiskEnvelope? ReadDiskEnvelope()
    {
        try
        {
            string path = GetDiskCachePath();
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out JsonElement version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != CacheSchemaVersion)
            {
                return null;
            }

            string source = ReadString(root, "source");
            if (!string.Equals(source, _provider.Name, StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("items", out JsonElement itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            List<MinecraftNewsItem> items = new List<MinecraftNewsItem>();
            foreach (JsonElement element in itemsElement.EnumerateArray())
            {
                items.Add(new MinecraftNewsItem
                {
                    Id = ReadString(element, "id"),
                    Title = ReadString(element, "title"),
                    Category = ReadString(element, "category"),
                    Date = ReadString(element, "date"),
                    Text = ReadString(element, "text"),
                    ImageUrl = ReadString(element, "imageUrl"),
                    Link = ReadString(element, "link")
                });
            }

            return new DiskEnvelope(
                items,
                source,
                NullIfEmpty(ReadString(root, "etag")),
                NullIfEmpty(ReadString(root, "lastModified")),
                ParseTimestamp(ReadString(root, "fetchedAt")),
                ParseTimestamp(ReadString(root, "validatedAt")));
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private void WriteDiskCache(NewsFetchResult result)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            object payload = new Dictionary<string, object>
            {
                ["schemaVersion"] = CacheSchemaVersion,
                ["source"] = result.Source,
                ["etag"] = result.ETag ?? string.Empty,
                ["lastModified"] = result.LastModified ?? string.Empty,
                ["fetchedAt"] = result.FetchedAt,
                ["validatedAt"] = result.ValidatedAt,
                ["items"] = result.Items.Select(item => new Dictionary<string, object>
                {
                    ["id"] = item.Id,
                    ["title"] = item.Title,
                    ["category"] = item.Category,
                    ["date"] = item.Date,
                    ["text"] = item.Text,
                    ["imageUrl"] = item.ImageUrl,
                    ["link"] = item.Link
                }).ToList()
            };
            string cachePath = GetDiskCachePath();
            string temporary = cachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload), Encoding.UTF8);
            File.Move(temporary, cachePath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 写失败不能破坏旧缓存（原子替换失败时保留原文件）。
        }
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTimeOffset.MinValue;
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private string GetDiskCachePath() => Path.Combine(_cacheDirectory, "news-cache.json");

    private static string ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
