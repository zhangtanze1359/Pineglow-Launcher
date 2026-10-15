using System.IO.Compression;
using System.Text.Json;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;

string root = args[1];
var results = new List<object>();
int failures = 0;
foreach (var (name, strayRelative) in new[]
{
    ("unrelated-extra", "libraries/net/minecraftforge/unrelated-tool/1.0/unrelated-tool-extra.jar"),
    ("stale-client", "libraries/net/minecraft/client/1.20.1/client-1.20.1-extra.jar")
})
{
    string dir = Path.Combine(root, name);
    string vanilla = VersionResolver.GetVersionJarPath(dir, "1.21.1");
    Directory.CreateDirectory(Path.GetDirectoryName(vanilla)!);
    using (var zip = ZipFile.Open(vanilla, ZipArchiveMode.Create)) zip.CreateEntry("META-INF/MANIFEST.MF");
    string parent = VersionResolver.GetVersionJsonPath(dir, "1.21.1");
    File.WriteAllText(parent, """{"id":"1.21.1","mainClass":"net.minecraft.client.main.Main","javaVersion":{"majorVersion":21}}""");
    string profileJson = VersionResolver.GetVersionJsonPath(dir, "neoforge-21.1.230");
    Directory.CreateDirectory(Path.GetDirectoryName(profileJson)!);
    File.WriteAllText(profileJson, """{"id":"neoforge-21.1.230","inheritsFrom":"1.21.1","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher","arguments":{"jvm":["-DignoreList=client-extra,${version_name}.jar","-cp","${classpath}"]},"libraries":[]}""");
    var profile = new LaunchProfile { GameDirectory = dir, JavaPath = "java.exe", VersionId = "neoforge-21.1.230", PlayerName = "Acceptance", MaxMemoryMb = 1024 };
    var launcher = new LauncherService(_ => {});
    var before = await launcher.BuildCommandAsync(profile);
    string stray = Path.Combine(dir, strayRelative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
    using (var zip = ZipFile.Open(stray, ZipArchiveMode.Create)) zip.CreateEntry("META-INF/MANIFEST.MF");
    var after = await launcher.BuildCommandAsync(profile);
    string GetClasspath(LaunchCommand cmd) => cmd.Arguments[cmd.Arguments.IndexOf("-cp") + 1];
    bool retainedBefore = GetClasspath(before).Split(Path.PathSeparator).Contains(vanilla);
    bool retainedAfter = GetClasspath(after).Split(Path.PathSeparator).Contains(vanilla);
    bool pass = retainedBefore && retainedAfter;
    if (!pass) failures++;
    results.Add(new { name, currentVersion = "1.21.1", strayRelative, strayIsNotReferencedByProfile = true, retainedBefore, retainedAfter, beforeClasspath = GetClasspath(before), afterClasspath = GetClasspath(after), pass });
}
var report = new { scope = "Actual BuildCommandAsync from frozen current product DLL; synthetic isolated fixtures; no product edits", failures, results };
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[0], json);
Console.WriteLine(json);
return failures == 0 ? 0 : 1;
