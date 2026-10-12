using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;

namespace Pineglow.Launcher.Services;

/// <summary>
/// 新闻模块 URL 策略（Phase 3A）。
/// 主机清单来自真实官方 payload 证据（docs/phase3a-news-evidence-2026-09-05/baseline/real-feed-snapshot.json）：
/// - 外链 host（100 条 entries 实测）：www.minecraft.net、minecraft.net、aka.ms、www.youtube.com；
/// - feed 与图片 host：launchercontent.mojang.com。
/// 全部为精确匹配：仅 HTTPS、无 userinfo、无非默认端口、拒绝 IP literal / 末尾点滥用 /
/// IDN（punycode）/ Unicode 主机；绝不使用 Contains 或后缀匹配。
/// </summary>
public static class NewsEndpointPolicy
{
    public const string FeedHost = "launchercontent.mojang.com";

    public static readonly IReadOnlyList<string> AllowedLinkHosts = new[]
    {
        "www.minecraft.net",
        "minecraft.net",
        "aka.ms",
        "www.youtube.com"
    };

    public static readonly IReadOnlyList<string> AllowedImageHosts = new[]
    {
        FeedHost
    };

    public static bool IsAllowedFeedEndpoint(string? url) => Check(url, new[] { FeedHost });

    public static bool IsAllowedImageUrl(string? url) => Check(url, AllowedImageHosts);

    public static bool IsAllowedLink(string? url) => Check(url, AllowedLinkHosts);

    private static bool Check(string? url, IReadOnlyList<string> allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (!uri.IsDefaultPort)
        {
            return false;
        }

        string host = NormalizeHost(uri.IdnHost);
        if (host == null)
        {
            return false;
        }

        return allowedHosts.Contains(host, StringComparer.Ordinal);
    }

    /// <summary>小写化并规范化单个末尾点；规范化后仍以点结尾、非 ASCII、punycode 或 IP literal 一律拒绝。</summary>
    private static string? NormalizeHost(string idnHost)
    {
        if (string.IsNullOrWhiteSpace(idnHost))
        {
            return null;
        }

        string host = idnHost.Trim().ToLowerInvariant();
        if (host.EndsWith('.'))
        {
            host = host.TrimEnd('.');
            if (host.EndsWith('.') || host.Length == 0)
            {
                return null;
            }
        }

        if (!IsAscii(host))
        {
            return null;
        }

        if (host.Contains("xn--", StringComparison.Ordinal))
        {
            return null;
        }

        if (IPAddress.TryParse(host, out _))
        {
            return null;
        }

        return host;
    }

    private static bool IsAscii(string value)
    {
        foreach (char ch in value)
        {
            if (ch > 127)
            {
                return false;
            }
        }

        return true;
    }
}
