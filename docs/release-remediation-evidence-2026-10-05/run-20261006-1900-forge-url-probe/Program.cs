// 第 61 轮：**用产品自己的代码**复现"Forge 安装器 404"。
// 目的：把第 51/52 轮那个"未定因"钉死——到底是 URL 拼错、镜像改写，还是环境/客户端差异。
// 做法：引用构建出来的 Pineglow.Launcher.dll，直接调用
//   ① 反射拿 InstallerLoaderService.GetInstallerUrl()（**产品会用的权威 URL**）
//   ② LoaderInstallHelper.DownloadFileAsync()（**产品自己的下载代码路径**）
//   ③ 裸 HttpClient GET（对照）
using System.Net.Http;
using System.Reflection;
using Pineglow.Launcher.Services;

string url = "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.4.3/forge-1.20.1-47.4.3-installer.jar";
Console.WriteLine("[1] 我手算的 URL: " + url);

// ---- 镜像改写行为
foreach (string key in new[] { "official", "bmclapi" })
{
    MirrorHelper.SelectedKey = key;
    Console.WriteLine($"[2] SelectedKey={key} Enabled={MirrorHelper.Enabled}");
    Console.WriteLine("    Rewrite(url)   = " + MirrorHelper.Rewrite(url));
    foreach (string c in MirrorHelper.GetCandidates(url)) Console.WriteLine("    candidate      = " + c);
}
MirrorHelper.SelectedKey = "official";

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var dl = new MinecraftDownloader(http, s => Console.WriteLine("    [downloader] " + s));
string gameDir = Path.Combine(Path.GetTempPath(), "forge-probe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(gameDir);
var forge = new ForgeService(http, gameDir, dl, s => Console.WriteLine("    [forge] " + s));

// ---- 产品权威 URL（反射调私有方法）
MethodInfo? mi = typeof(InstallerLoaderService).GetMethod("GetInstallerUrl", BindingFlags.NonPublic | BindingFlags.Instance);
Console.WriteLine("[3] 反射调用 GetInstallerUrl：");
foreach (string mv in new[] { "1.20.1-47.4.3", "47.4.3" })
{
    try { Console.WriteLine($"    GetInstallerUrl(\"{mv}\") = " + mi!.Invoke(forge, new object[] { mv })); }
    catch (Exception ex) { Console.WriteLine($"    GetInstallerUrl(\"{mv}\") 抛错: {ex.GetType().Name}: {ex.Message}"); }
}

// ---- 裸 GET（对照）
Console.WriteLine("[4] 裸 HttpClient GET：");
foreach (string u in new[] { url, MirrorHelper.Rewrite(url) })
{
    try
    {
        using HttpResponseMessage r = await http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead);
        Console.WriteLine($"    {(int)r.StatusCode}  {u}");
    }
    catch (Exception ex) { Console.WriteLine($"    FAIL {ex.GetType().Name}: {ex.Message}  {u}"); }
}

// ---- 产品自己的下载代码路径
Console.WriteLine("[5] 产品代码路径 LoaderInstallHelper.DownloadFileAsync：");
var helper = new LoaderInstallHelper(http, gameDir, dl, s => Console.WriteLine("    [helper] " + s));
string outPath = Path.Combine(gameDir, "forge-1.20.1-47.4.3-installer.jar");
try
{
    await helper.DownloadFileAsync(url, outPath, null, "Forge installer");
    Console.WriteLine("    RESULT=PASS 下载成功 " + (new FileInfo(outPath).Length) + " B");
}
catch (Exception ex)
{
    Console.WriteLine("    RESULT=FAIL " + ex.GetType().Name + ": " + ex.Message);
}
Console.WriteLine("[6] 结论行：PROBE-DONE");
