using System.Diagnostics;
using System.Reflection;
using Pineglow.Launcher.Services;
using Xunit.Abstractions;

namespace Pineglow.Launcher.Tests;

[Collection("LocalApiService")]
public sealed class JavaDiscoveryTimingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CandidateEnumeration_AndVersionProcesses_AreMeasuredSeparately()
    {
        var enumerate = typeof(JavaLocator).GetMethod("EnumerateCandidates", BindingFlags.NonPublic | BindingFlags.Static)!;
        var watch = Stopwatch.StartNew();
        var candidates = ((IEnumerable<string>)enumerate.Invoke(null, null)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        output.WriteLine($"JAVA_TIMING enumeration_ms={watch.Elapsed.TotalMilliseconds:F1} candidates={candidates.Length}");
        int valid = 0;
        for (int i = 0; i < candidates.Length; i++)
        {
            watch.Restart();
            var installation = await JavaLocator.ValidateInstallationAsync(candidates[i]);
            if (installation != null) valid++;
            output.WriteLine($"JAVA_TIMING candidate_index={i} validation_ms={watch.Elapsed.TotalMilliseconds:F1} valid={installation != null}");
        }
        output.WriteLine($"JAVA_TIMING valid_count={valid}");
    }
}
