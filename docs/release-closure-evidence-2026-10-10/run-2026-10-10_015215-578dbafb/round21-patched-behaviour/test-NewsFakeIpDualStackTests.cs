using System.Net;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Regression tests for the Clash-style TUN/fake-ip compatibility switch on a DUAL-STACK machine.
/// The resolver returns one IPv4 from 198.18.0.0/15 and one IPv6 from 2001:2::/48 (RFC 5180);
/// before the fix only the IPv4 one counted as "fake-ip", so the opt-in switch could never help.
/// </summary>
public sealed class NewsFakeIpDualStackTests
{
    private static IPAddress[] DualStack() => new[]
    {
        IPAddress.Parse("198.18.0.33"),
        IPAddress.Parse("2001:2::21"),
    };

    [Fact]
    public void DualStackFakeIp_IsRejectedWhenCompatIsOff()
    {
        // After the fix BOTH addresses count as benchmark ranges, so the "every address is a
        // benchmark range" branch fires and the kind becomes dns_private. Before the fix only the
        // IPv4 one was recognised, so the same input produced dns_mixed ("mixed with other
        // classes"). Either way the request is REJECTED while the opt-in switch is off.
        NewsImageException ex = Assert.Throws<NewsImageException>(
            () => NewsSsrfGuard.SelectConnectableAddress(DualStack(), allowFakeIpRanges: false));
        Assert.Equal("dns_private", ex.Kind);
    }

    [Fact]
    public void DualStackFakeIp_ResolvesToTheIpv4AddressWhenCompatIsOn()
    {
        IPAddress picked = NewsSsrfGuard.SelectConnectableAddress(DualStack(), allowFakeIpRanges: true);
        Assert.Equal(IPAddress.Parse("198.18.0.33"), picked);
    }

    [Fact]
    public void RealPrivateAddressMixedWithFakeIp_IsStillRejected()
    {
        IPAddress[] mixed = new[] { IPAddress.Parse("192.168.1.10"), IPAddress.Parse("198.18.0.33") };
        NewsImageException ex = Assert.Throws<NewsImageException>(
            () => NewsSsrfGuard.SelectConnectableAddress(mixed, allowFakeIpRanges: true));
        Assert.Equal("dns_mixed", ex.Kind);
    }

    [Fact]
    public void PublicAddressMixedWithFakeIp_IsStillRejected()
    {
        IPAddress[] mixed = new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Parse("198.18.0.33") };
        NewsImageException ex = Assert.Throws<NewsImageException>(
            () => NewsSsrfGuard.SelectConnectableAddress(mixed, allowFakeIpRanges: true));
        Assert.Equal("dns_mixed", ex.Kind);
    }
}