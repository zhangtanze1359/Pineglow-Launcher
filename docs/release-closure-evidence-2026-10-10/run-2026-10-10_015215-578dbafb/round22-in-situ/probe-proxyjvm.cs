using System.Collections;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;

string dllPath = args[0];
string fakeJava = args[1];
string dir = Path.GetDirectoryName(dllPath)!;
var alc = new AssemblyLoadContext("probe", isCollectible: false);
alc.Resolving += (ctx, name) => { string p = Path.Combine(dir, name.Name + ".dll"); return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null; };
var asm = alc.LoadFromAssemblyPath(dllPath);

var lasType = asm.GetType("Pineglow.Launcher.Services.LocalApiService")!;
var appCfgType = asm.GetType("Pineglow.Launcher.Models.AppConfig")!;
var helperType = asm.GetType("Pineglow.Launcher.Services.LoaderInstallHelper")!;
var dlType = asm.GetType("Pineglow.Launcher.Services.MinecraftDownloader")!;

var build = lasType.GetMethod("BuildLoaderInstallerJvmArgs", BindingFlags.NonPublic | BindingFlags.Static);
if (build == null) { Console.WriteLine("BuildLoaderInstallerJvmArgs: ABSENT (pre-fix build)"); return 0; }

string Show(IEnumerable seq)
{
    var parts = new List<string>();
    foreach (var o in seq) parts.Add(o?.ToString() ?? "<null>");
    return parts.Count == 0 ? "(empty)" : string.Join(" ", parts);
}

List<string> Args(bool on)
{
    var cfg = Activator.CreateInstance(appCfgType)!;
    appCfgType.GetProperty("ProxyEnabled")!.SetValue(cfg, on);
    appCfgType.GetProperty("ProxyHost")!.SetValue(cfg, "127.0.0.1");
    appCfgType.GetProperty("ProxyPort")!.SetValue(cfg, 7897);
    var r = (IEnumerable)build.Invoke(null, new object[] { cfg })!;
    return r.Cast<object>().Select(o => o.ToString()!).ToList();
}

Console.WriteLine("=== BuildLoaderInstallerJvmArgs(config) ===");
Console.WriteLine($"proxyEnabled=true  -> {Show(Args(true))}");
Console.WriteLine($"proxyEnabled=false -> {Show(Args(false))}");

// MinecraftDownloader's 3rd parameter is Action<MinecraftDownloadProgress>; build a typed no-op.
var progType = asm.GetType("Pineglow.Launcher.Services.MinecraftDownloadProgress")!;
var pParam = Expression.Parameter(progType, "p");
var noopProgress = Expression.Lambda(typeof(Action<>).MakeGenericType(progType), Expression.Empty(), pParam).Compile();

async Task RunInstaller(IReadOnlyList<string> extra)
{
    var lines = new List<string>();
    Action<string> log = s => lines.Add(s);
    var ctorHelper = helperType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
    var ctorDl = dlType.GetConstructors().First();
    string gameDir = Path.Combine(Path.GetTempPath(), "pineglow-proxyjvm-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(gameDir);
    var http = new HttpClient();
    object dl = ctorDl.Invoke(new object[] { http, log, noopProgress });
    object helper = ctorHelper.Invoke(new object[] { http, gameDir, dl, log, extra });
    var m = helperType.GetMethod("RunInstallerAsync")!;
    await (Task)m.Invoke(helper, new object[] { fakeJava, "dummy-installer.jar", "Forge" })!;
    foreach (var l in lines.Where(l => l.StartsWith("ARG|"))) Console.WriteLine("  " + l);
}

Console.WriteLine();
Console.WriteLine("=== child command line WITH the args BuildLoaderInstallerJvmArgs produced ===");
await RunInstaller(Args(true));
Console.WriteLine();
Console.WriteLine("=== child command line with NO extra args (pre-fix behaviour) ===");
await RunInstaller(Array.Empty<string>());
return 0;
