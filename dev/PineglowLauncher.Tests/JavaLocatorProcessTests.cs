using System.Diagnostics;
using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

[Collection("LocalApiService")]
public sealed class JavaLocatorProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pineglow-java-probe-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FallbackSearch_FindsNormalInstallation_WithoutDescendingUnrelatedDeepTrees()
    {
        string normal = Path.Combine(root, "Vendor", "jdk-21");
        Directory.CreateDirectory(normal);
        for (int i = 0; i < 100; i++)
            Directory.CreateDirectory(Path.Combine(root, "unrelated-" + i, "a", "b", "c", "d", "jdk-cache"));
        var enumerate = typeof(JavaLocator).GetMethod("SafeEnumerateDirectories", BindingFlags.NonPublic | BindingFlags.Static)!;
        var found = ((IEnumerable<string>)enumerate.Invoke(null, new object[] { root, "*jdk*", SearchOption.AllDirectories })!).ToArray();
        Assert.Contains(normal, found);
        Assert.Single(found);
    }

    private string Probe(string mode)
    {
        Directory.CreateDirectory(root);
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "JavaProbe")))
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(root, "mode.txt"), mode);
        return Path.Combine(root, OperatingSystem.IsWindows() ? "JavaProbe.exe" : "JavaProbe");
    }

    [Fact]
    public async Task LocalDiagnosis_WithSlowConfiguredJava_PreservesLocalResultWithoutInventingVersion()
    {
        string probe = Probe("slow");
        var instance = new GameInstance { Id = "slow-java", Name = "slow-java", GameDirectory = root, JavaPath = probe, MinecraftVersion = "1.20.4" };
        var config = new AppConfig { GameDirectory = root, Instances = new() { instance }, SelectedInstanceId = instance.Id };
        await using var harness = await LocalApiTestHarness.StartAsync(config);
        var watch = Stopwatch.StartNew();
        var (status, body) = await harness.SendAsync(HttpMethod.Post, "/api/instances/slow-java/diagnose",
            new { operationId = Guid.NewGuid().ToString("N"), localOnly = true });
        Assert.Equal(200, status);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"local diagnosis took {watch.Elapsed}");
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("rule").GetProperty("summary").GetString()));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("snapshot").GetProperty("context").GetProperty("javaVersion").ValueKind);
        AssertProcessExited();
    }

    [Fact]
    public async Task Cancellation_KillsVersionProcess_BeforeReturning()
    {
        string probe = Probe("slow");
        using var cancellation = new CancellationTokenSource();
        var task = JavaLocator.ValidateInstallationAsync(probe, cancellationToken: cancellation.Token);
        string pidPath = Path.Combine(root, "pid.txt");
        var watch = Stopwatch.StartNew();
        while (!File.Exists(pidPath) && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.True(File.Exists(pidPath), "real child must have started before cancellation");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        AssertProcessExited();
    }

    [Fact]
    public async Task Find_ReturnsFirstValidJava_WithoutEnumeratingTheRest()
    {
        string probe = Probe("normal");
        IEnumerable<string> Candidates()
        {
            yield return probe;
            throw new InvalidOperationException("discovery kept crawling after finding Java");
        }
        var result = await JavaLocator.FindFromCandidatesAsync(Candidates());
        Assert.NotNull(result);
        Assert.Equal(21, result.VersionMajor);
        Assert.Equal(probe, result.Path);
    }

    [Fact]
    public async Task SlowVersionProcess_IsBounded_AndHasExitedOnReturn()
    {
        string probe = Probe("slow");
        var watch = Stopwatch.StartNew();
        var result = await JavaLocator.ValidateInstallationAsync(probe);
        Assert.Null(result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"hung -version took {watch.Elapsed}");
        AssertProcessExited();
    }

    [Fact]
    public async Task BothOutputPipes_AreDrainedConcurrently()
    {
        string probe = Probe("flood");
        var task = JavaLocator.ValidateInstallationAsync(probe);
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(result);
        Assert.Equal(21, result.VersionMajor);
        AssertProcessExited();
    }

    [Theory]
    [InlineData("normal", true)]
    [InlineData("invalid", false)]
    [InlineData("failure", false)]
    public async Task VersionValidation_UsesRealOutputAndExitCode(string mode, bool valid)
    {
        var result = await JavaLocator.ValidateInstallationAsync(Probe(mode));
        Assert.Equal(valid, result != null);
        if (valid) Assert.Equal(21, result!.VersionMajor);
        AssertProcessExited();
    }

    private void AssertProcessExited()
    {
        int pid = int.Parse(File.ReadAllText(Path.Combine(root, "pid.txt")));
        try { using var process = Process.GetProcessById(pid); Assert.True(process.HasExited, "probe still running after validation"); }
        catch (ArgumentException) { }
    }

    public void Dispose()
    {
        string pidPath = Path.Combine(root, "pid.txt");
        if (File.Exists(pidPath))
        {
            try { using var process = Process.GetProcessById(int.Parse(File.ReadAllText(pidPath))); if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
            catch (ArgumentException) { }
        }
        // Process exit is asserted in the test body. Windows can still deny deletion
        // of an exited apphost briefly; retain only this fixture's files in that case.
        try { if (Directory.Exists(root)) Directory.Delete(root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
