// 第 53 轮：把"哪些加载器受影响"从"看 profile 内容"推进到"按现有代码逻辑推演最终命令行"。
// 用 .NET 10 file-based app 跑：`dotnet run fallback-check.cs`
// 逻辑抄自 LauncherService.ResolveJvmArguments（179–197 行）：
//   if (version.Arguments 存在 && 其中 jvm 是数组)  { 只用 jvm 里的参数；goto 结束 }   // ← 不补库路径
//   否则                                            { 补 -Djava.library.path / brand / version / -cp }
using System.Text.Json;

string[] cases = { "fabric", "quilt", "forge", "neoforge", "vanilla" };

// 各加载器 profile 里 arguments 的真实形态（第 51/53 轮实测取回）
string fabricChild = """
{"id":"fabric-loader-0.19.3-1.18.2","inheritsFrom":"1.18.2","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
 "arguments":{"jvm":["-DFabricMcEmu= net.minecraft.client.main.Main "],"game":[]}}
""";
string quiltChild = """
{"id":"quilt-loader-0.20.0-beta.9-1.20.1","inheritsFrom":"1.20.1","mainClass":"org.quiltmc.loader.impl.launch.knot.KnotClient",
 "arguments":{"game":["--width","${resolution_width}"]}}
""";
string forgeChild = """
{"id":"1.20.1-forge-47.4.3","inheritsFrom":"1.20.1","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
 "arguments":{"jvm":["-DignoreList=bootstraplauncher,securejarhandler","-p","${library_directory}","--add-modules","ALL-MODULE-PATH"],
              "game":["--launchTarget","forge_client"]}}
""";
string neoChild = """
{"id":"neoforge-21.1.254","inheritsFrom":"1.21.1","mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
 "arguments":{"jvm":["-DignoreList=bootstraplauncher","-p","${library_directory}"],"game":["--launchTarget","neoforge_client"]}}
""";
string vanillaChild = """
{"id":"1.18.2","mainClass":"net.minecraft.client.main.Main",
 "arguments":{"jvm":["-Djava.library.path=${natives_directory}","-cp","${class_path}"],"game":["--width","${resolution_width}"]}}
""";

string nativesDir = @"C:\game\versions\X\natives";
int affected = 0;
foreach (string name in cases)
{
    string json = name switch
    {
        "fabric" => fabricChild,
        "quilt" => quiltChild,
        "forge" => forgeChild,
        "neoforge" => neoChild,
        _ => vanillaChild
    };
    using JsonDocument doc = JsonDocument.Parse(json);
    JsonElement version = doc.RootElement;
    var jvmArgs = new List<string> { "-Xmx4096M", "-Dfile.encoding=UTF-8" };

    // —— 抄自产品逻辑：是否走 arguments.jvm 分支
    bool tookJvmBranch = false;
    if (version.TryGetProperty("arguments", out JsonElement arguments) && arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty("jvm", out JsonElement jvm) && jvm.ValueKind == JsonValueKind.Array)
    {
        tookJvmBranch = true;
        foreach (JsonElement item in jvm.EnumerateArray()) jvmArgs.Add(item.GetString() ?? "");
    }
    else
    {
        jvmArgs.Add("-Djava.library.path=" + nativesDir);
        jvmArgs.Add("-Dminecraft.launcher.brand=PineglowLauncher");
        jvmArgs.Add("-Dminecraft.launcher.version=1.0.0");
        jvmArgs.Add("-cp");
        jvmArgs.Add("<classpath>");
    }
    bool hasLibPath = jvmArgs.Any(a => a.StartsWith("-Djava.library.path"));
    bool affectedCase = !hasLibPath;
    if (affectedCase) affected++;
    Console.WriteLine($"{name,-9} 走 jvm 分支={tookJvmBranch,-5} 命令行含 -Djava.library.path={hasLibPath,-5} ⇒ {(affectedCase ? "受影响（会崩）" : "不受影响")}");
}
Console.WriteLine($"FALLBACK-CHECK RESULT={(affected == 3 ? "PASS" : "UNEXPECTED")}  受影响加载器数={affected}（期望 3：fabric/forge/neoforge）");
return affected == 3 ? 0 : 1;
