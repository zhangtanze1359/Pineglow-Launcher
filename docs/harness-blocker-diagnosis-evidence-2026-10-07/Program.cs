using System.Text.Json;
using Pineglow.Launcher.Services;

string root = Path.Combine(args[1], "fixture");
string parent = VersionResolver.GetVersionJsonPath(root, "1.21.1");
string child = VersionResolver.GetVersionJsonPath(root, "neoforge-21.1.230");
Directory.CreateDirectory(Path.GetDirectoryName(parent)!);
Directory.CreateDirectory(Path.GetDirectoryName(child)!);
File.WriteAllText(parent, """{"id":"1.21.1","javaVersion":{"majorVersion":21}}""");
File.WriteAllText(child, """{"id":"neoforge-21.1.230","inheritsFrom":"1.21.1"}""");
string[] jvm = ["--add-opens", "java.base/java.util.jar=cpw.mods.securejarhandler", "--add-opens", "java.base/java.lang.invoke=cpw.mods.securejarhandler"];
var deduplicated = jvm.DistinctPreserveOrder().ToArray();
int stringOnly = JavaVersionMatcher.GetRequiredJavaMajor("neoforge-21.1.230");
int metadata = JavaVersionMatcher.GetRequiredJavaMajor("neoforge-21.1.230", root);
bool reproduced = stringOnly == 17 && metadata == 21 && deduplicated.Count(a => a == "--add-opens") == 1;
var result = new
{
    scope = "Actual current source helpers; synthetic inputs; no live Java/network/game invocation",
    stringOnlyJavaMajor = stringOnly,
    metadataJavaMajor = metadata,
    inputOptionCount = jvm.Count(a => a == "--add-opens"),
    outputOptionCount = deduplicated.Count(a => a == "--add-opens"),
    output = deduplicated,
    defectsReproduced = reproduced
};
string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(args[0], json);
Console.WriteLine(json);
return reproduced ? 0 : 1;
