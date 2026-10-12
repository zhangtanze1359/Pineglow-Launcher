using System.Text.Json;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

[Collection("LocalApiService")]
public sealed class ConfigDefaultIdentityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pineglow-default-" + Guid.NewGuid().ToString("N"));
    private readonly InMemorySecretStoreBackend secrets = new("default-identity");
    private readonly IDisposable scope;
    private string ConfigPath => Path.Combine(root, "nested", "config.json");

    public ConfigDefaultIdentityTests()
    {
        Directory.CreateDirectory(root);
        ConfigStore.ConfigPathOverrideForTest = ConfigPath;
        scope = SecretStore.OverrideBackendForTest(secrets);
    }

    [Fact]
    public async Task LoadAsync_FirstRun_PersistsStableInstanceAndAccountIds()
    {
        var first = await ConfigStore.LoadAsync();
        Assert.True(File.Exists(ConfigPath));
        var second = await ConfigStore.LoadAsync();
        Assert.Equal(Assert.Single(first.Instances).Id, Assert.Single(second.Instances).Id);
        Assert.Equal(Assert.Single(first.Accounts).Id, Assert.Single(second.Accounts).Id);
        Assert.Equal(first.SelectedInstanceId, second.SelectedInstanceId);
    }

    [Fact]
    public async Task LoadAsync_FirstRun_DoesNotWriteOrDeleteExistingCredentials()
    {
        SecretStore.Save("CurseForgeApiKey", "preserved");
        SecretStore.Save("MicrosoftRefreshToken", "preserved");
        SecretStore.Save("AiApiKey", "preserved");
        int before = secrets.Operations.Count;
        await ConfigStore.LoadAsync();
        Assert.Equal(before, secrets.Operations.Count);
        Assert.Equal("preserved", SecretStore.Load("CurseForgeApiKey"));
        Assert.Equal("preserved", SecretStore.Load("MicrosoftRefreshToken"));
        Assert.Equal("preserved", SecretStore.Load("AiApiKey"));
    }

    [Fact]
    public async Task LoadAsync_ConcurrentFirstRun_ReturnsOneIdentity()
    {
        var configs = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(ConfigStore.LoadAsync)));
        Assert.Single(configs.Select(c => Assert.Single(c.Instances).Id).Distinct());
        Assert.Single(configs.Select(c => Assert.Single(c.Accounts).Id).Distinct());
    }

    [Fact]
    public async Task LoadAsync_ExistingConfigWithoutInstance_PersistsGeneratedId()
    {
        var config = await ConfigStore.LoadAsync();
        config.Instances.Clear();
        config.SelectedInstanceId = "";
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        await File.WriteAllTextAsync(ConfigPath, JsonSerializer.Serialize(config));
        var first = await ConfigStore.LoadAsync();
        var second = await ConfigStore.LoadAsync();
        Assert.Equal(Assert.Single(first.Instances).Id, Assert.Single(second.Instances).Id);
        Assert.Equal(config.Accounts[0].Id, first.Accounts[0].Id);
    }

    [Fact]
    public async Task LoadAsync_UnwritableInitialPath_ReportsFailureInsteadOfTransientIdentity()
    {
        File.WriteAllText(Path.Combine(root, "not-a-directory"), "sentinel");
        ConfigStore.ConfigPathOverrideForTest = Path.Combine(root, "not-a-directory", "config.json");
        await Assert.ThrowsAnyAsync<IOException>(() => ConfigStore.LoadAsync());
    }

    public void Dispose()
    {
        scope.Dispose();
        ConfigStore.ConfigPathOverrideForTest = null;
        Directory.Delete(root, true);
    }
}
