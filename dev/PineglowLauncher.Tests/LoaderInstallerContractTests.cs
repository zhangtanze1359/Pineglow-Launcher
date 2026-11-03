using System.Net;
using System.Text.Json;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

public sealed class LoaderInstallerContractTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pineglow-installer-" + Guid.NewGuid().ToString("N"));

    private sealed class MetadataHandler(string version) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<metadata><versioning><versions><version>" + version + "</version></versions></versioning></metadata>") });
    }

    [Theory]
    [InlineData(false, "1.20.1", "1.20.1-47.4.0", "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.0/forge-1.20.1-47.4.0-installer.jar")]
    [InlineData(true, "1.21.1", "21.1.219", "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.219/neoforge-21.1.219-installer.jar")]
    public async Task GetLoaderVersionEntriesAsync_OfficialMavenLayout_DoesNotDuplicateArtifact(bool neo, string mc, string version, string expected)
    {
        using var http = new HttpClient(new MetadataHandler(version));
        var downloader = new MinecraftDownloader(http, _ => { });
        InstallerLoaderService service = neo ? new NeoForgeService(http, root, downloader, _ => { }) : new ForgeService(http, root, downloader, _ => { });
        Assert.Equal(expected, Assert.Single(await service.GetLoaderVersionEntriesAsync(mc)).DownloadUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunInstallerAsync_PreparesMissingProfileAndPreservesExistingBytes(bool existing)
    {
        Directory.CreateDirectory(root);
        string profile = Path.Combine(root, "launcher_profiles.json");
        const string original = "{\"profiles\":{\"user\":{\"name\":\"Keep me\"}},\"settings\":{\"custom\":true}}\n";
        if (existing) File.WriteAllText(profile, original);
        using var http = new HttpClient();
        var helper = new LoaderInstallHelper(http, root, new MinecraftDownloader(http, _ => { }), _ => { });
        // Exercise the actual pre-launch path with a native process that rejects Java arguments.
        string rejectingExecutable = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe") : "/usr/bin/false";
        await Assert.ThrowsAsync<InvalidOperationException>(() => helper.RunInstallerAsync(rejectingExecutable, Path.Combine(root, "missing.jar"), "Forge"));
        Assert.True(File.Exists(profile));
        if (existing) Assert.Equal(original, File.ReadAllText(profile));
        else
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(profile));
            Assert.Empty(doc.RootElement.GetProperty("profiles").EnumerateObject());
            Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("settings").ValueKind);
            Assert.Equal(3, doc.RootElement.GetProperty("version").GetInt32());
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
