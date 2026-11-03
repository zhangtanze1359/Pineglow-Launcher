using System.Text.Json;
using Pineglow.Launcher.Services;

namespace Pineglow.Launcher.Tests;

public sealed class VersionResolverInheritanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pineglow-inherit-" + Guid.NewGuid().ToString("N"));

    private void Write(string id, string json)
    {
        string path = VersionResolver.GetVersionJsonPath(root, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    [Theory]
    [InlineData("fabric")]
    [InlineData("forge")]
    [InlineData("neoforge")]
    [InlineData("quilt")]
    public async Task ResolveAsync_LoaderChild_RetainsNativeClasspathGameAndRuleArguments(string loader)
    {
        Write("base", """{"id":"base","mainClass":"Vanilla","arguments":{"jvm":["-Djava.library.path=${natives_directory}","-cp","${classpath}",{"rules":[{"action":"allow","os":{"name":"windows"}}],"value":"-Dos=windows"}],"game":["--username","${auth_player_name}"]},"libraries":[{"name":"parent"}]}""");
        Write(loader, JsonSerializer.Serialize(new { id = loader, inheritsFrom = "base", mainClass = "Loader", arguments = new { jvm = new[] { "-Dloader=" + loader }, game = new[] { "--loader", loader } }, libraries = new[] { new { name = "child" } } }));
        var resolved = await VersionResolver.ResolveAsync(root, loader);
        var args = resolved.Arguments!.Value;
        Assert.Equal("-Djava.library.path=${natives_directory}", args.GetProperty("jvm")[0].GetString());
        Assert.Equal("-cp", args.GetProperty("jvm")[1].GetString());
        Assert.Equal("${classpath}", args.GetProperty("jvm")[2].GetString());
        Assert.Equal("allow", args.GetProperty("jvm")[3].GetProperty("rules")[0].GetProperty("action").GetString());
        Assert.Equal("-Dloader=" + loader, args.GetProperty("jvm")[4].GetString());
        Assert.Equal(new[] { "--username", "${auth_player_name}", "--loader", loader }, args.GetProperty("game").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("Loader", resolved.MainClass);
        Assert.Equal(new[] { "parent", "child" }, resolved.Libraries.Select(l => l.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ResolveAsync_MultipleLevelsAndGameOnlyChild_KeepsAllAncestorJvmArguments()
    {
        Write("base", """{"id":"base","arguments":{"jvm":["base"],"game":["game"]}}""");
        Write("middle", """{"id":"middle","inheritsFrom":"base","arguments":{"jvm":["middle"]}}""");
        Write("leaf", """{"id":"leaf","inheritsFrom":"middle","arguments":{"game":["leaf"]}}""");
        var args = (await VersionResolver.ResolveAsync(root, "leaf")).Arguments!.Value;
        Assert.Equal(new[] { "base", "middle" }, args.GetProperty("jvm").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(new[] { "game", "leaf" }, args.GetProperty("game").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("base", (await VersionResolver.ResolveAsync(root, "base")).Arguments!.Value.GetProperty("jvm")[0].GetString());
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
