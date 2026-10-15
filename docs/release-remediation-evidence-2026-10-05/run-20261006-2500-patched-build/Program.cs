// 第 74 轮：对**打过补丁的构建**做代码级复现验证（B1 合并 + B2 安装器 URL）。
// 用法：把编译出的 probe dll 放进打过补丁的输出目录，`dotnet exec probe.dll <gameDirForB1>`
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Pineglow.Launcher.Services;

string gameDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "probe-game");

// ---------- B2：安装器 URL 是否还是"多一段"
var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var dl = new MinecraftDownloader(http, _ => { });
var forge = new ForgeService(http, gameDir, dl, _ => { });
MethodInfo? mi = typeof(InstallerLoaderService).GetMethod("GetInstallerUrl", BindingFlags.NonPublic | BindingFlags.Instance);
string url = mi!.Invoke(forge, new object[] { "1.20.1-47.4.3" })!.ToString()!;
Console.WriteLine("[B2] GetInstallerUrl = " + url);
Console.WriteLine("[B2] 段数检查：'/forge/forge/' 出现=" + url.Contains("/forge/forge/"));
try
{
    using HttpResponseMessage r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    Console.WriteLine("[B2] HEAD/GET 状态 = " + (int)r.StatusCode + " 长度=" + r.Content.Headers.ContentLength);
}
catch (Exception ex) { Console.WriteLine("[B2] 请求失败: " + ex.Message); }

// ---------- B1：加载器（继承型）profile 的 arguments 是否与父版本合并
Directory.CreateDirectory(Path.Combine(gameDir, "versions", "1.18.2"));
Directory.CreateDirectory(Path.Combine(gameDir, "versions", "fabric-loader-0.19.3-1.18.2"));
File.WriteAllText(Path.Combine(gameDir, "versions", "1.18.2", "1.18.2.json"),
    """{"id":"1.18.2","mainClass":"net.minecraft.client.main.Main","arguments":{"jvm":["-Djava.library.path=${natives_directory}","-cp","${class_path}"],"game":["--width","${resolution_width}"]},"libraries":[{"name":"com.mojang:logging:1.0.0"}]}""");
File.WriteAllText(Path.Combine(gameDir, "versions", "fabric-loader-0.19.3-1.18.2", "fabric-loader-0.19.3-1.18.2.json"),
    """{"id":"fabric-loader-0.19.3-1.18.2","inheritsFrom":"1.18.2","mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient","arguments":{"jvm":["-DFabricMcEmu= net.minecraft.client.main.Main "],"game":[]},"libraries":[{"name":"net.fabricmc:fabric-loader:0.19.3"}]}""");
MinecraftVersion resolved = await VersionResolver.ResolveAsync(gameDir, "fabric-loader-0.19.3-1.18.2");
string jvm = resolved.Arguments?.GetProperty("jvm").GetRawText() ?? "(无 arguments.jvm)";
Console.WriteLine("[B1] 合并后的 arguments.jvm = " + jvm);
Console.WriteLine("[B1] 含 -Djava.library.path=" + jvm.Contains("-Djava.library.path=${natives_directory}"));
Console.WriteLine("[B1] 含 Fabric 自己的条目=" + jvm.Contains("-DFabricMcEmu"));
Console.WriteLine("[B1] mainClass = " + resolved.MainClass);
Console.WriteLine("[B1] libraries = " + resolved.Libraries.Count);
Console.WriteLine("PROBE-DONE");
