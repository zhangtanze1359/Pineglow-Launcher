using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

/// <summary>
/// Regression test for the Fabric meta mirror mapping. Nothing in the suite covered
/// MirrorHelper.Rewrite at all, which is why "<base> + path" (404) survived unnoticed.
/// </summary>
public sealed class MirrorFabricMetaMappingTests
{
    [Fact]
    public void Rewrite_MapsFabricMetaUnderFabricMetaPrefix()
    {
        string previous = MirrorHelper.SelectedKey;
        try
        {
            MirrorHelper.SelectedKey = "bmclapi";
            string rewritten = MirrorHelper.Rewrite("https://meta.fabricmc.net/v2/versions/loader");
            Assert.Equal("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader", rewritten);
        }
        finally
        {
            MirrorHelper.SelectedKey = previous;
        }
    }

    [Fact]
    public void Rewrite_QuiltMetaKeepsItsOwnPrefix()
    {
        string previous = MirrorHelper.SelectedKey;
        try
        {
            MirrorHelper.SelectedKey = "bmclapi";
            string rewritten = MirrorHelper.Rewrite("https://meta.quiltmc.org/v3/versions");
            Assert.Equal("https://bmclapi2.bangbang93.com/quilt-meta/v3/versions", rewritten);
        }
        finally
        {
            MirrorHelper.SelectedKey = previous;
        }
    }
}