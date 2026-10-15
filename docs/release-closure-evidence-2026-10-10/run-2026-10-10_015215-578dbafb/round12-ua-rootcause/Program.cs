using System.Net;
using System.Net.Http;

string Url = "https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/0.19.5/fabric-loader-0.19.5.jar";

string[] cases = new[] { "<none>", "curl/8.4.0", "PineglowLauncher/1.0", "Mozilla/5.0", "Wget/1.21.4" };

foreach (string ua in cases)
{
    for (int attempt = 1; attempt <= 2; attempt++)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        try { client.DefaultRequestHeaders.UserAgent.Clear(); } catch { }
        if (ua != "<none>") { client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var resp = await client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);
            long bytes = 0;
            await using (var s = await resp.Content.ReadAsStreamAsync())
            {
                var buf = new byte[81920];
                int n;
                while ((n = await s.ReadAsync(buf)) > 0) { bytes += n; }
            }
            sw.Stop();
            Console.WriteLine($"UA={ua,-22} attempt={attempt} status={(int)resp.StatusCode} {resp.StatusCode} bytes={bytes} ms={sw.ElapsedMilliseconds} final={resp.RequestMessage?.RequestUri}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.WriteLine($"UA={ua,-22} attempt={attempt} EXCEPTION {ex.GetType().Name}: {ex.Message} ms={sw.ElapsedMilliseconds}");
        }
    }
}
