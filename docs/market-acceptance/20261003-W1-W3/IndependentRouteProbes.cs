using System.Net.Http;
using System.Text.Json;
using Pineglow.Launcher.Models;
using Xunit.Abstractions;
namespace Pineglow.Launcher.Tests;
[Collection("LocalApiService")]
public sealed class IndependentRouteProbes
{
    private readonly ITestOutputHelper output;
    public IndependentRouteProbes(ITestOutputHelper output) => this.output=output;
    private sealed class Fixture : IDisposable
    {
        public string Root=Path.Combine(Path.GetTempPath(), "pineglow-independent-probe-"+Guid.NewGuid().ToString("N"));
        public string Id="acceptance-"+Guid.NewGuid().ToString("N");
        public AppConfig Config;
        public string PathFor(string suffix)=>"/api/instances/"+Id+suffix;
        public string Mod=>Path.Combine(Root,"mods","a.jar");
        public Fixture(){Directory.CreateDirectory(Path.Combine(Root,"mods"));File.WriteAllText(Mod,"original-A");File.WriteAllText(Path.Combine(Root,"mods","b.jar"),"original-B");
            foreach(var w in new[]{"worldA","worldB"}){Directory.CreateDirectory(Path.Combine(Root,"saves",w));File.WriteAllText(Path.Combine(Root,"saves",w,"level.dat"),"original-"+w);}
            Config=new AppConfig {GameDirectory=Root,SelectedInstanceId=Id,Instances=new(){new GameInstance{Id=Id,Name="acceptance",MinecraftVersion="1.20.4",GameDirectory=Root}}};}
        public string[] Files()=>Directory.GetFiles(Root,"*",SearchOption.AllDirectories).Select(p=>System.IO.Path.GetRelativePath(Root,p)+":"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))).Order().ToArray();
        public void Dispose(){if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
    [Theory]
    [InlineData("/delete/extra","POST")]
    [InlineData("/launch/extra","POST")]
    [InlineData("/mods/a.jar/delete/extra","POST")]
    [InlineData("/saves/worldA/delete/extra","POST")]
    [InlineData("/mods/a.jar/delete","GET")]
    [InlineData("/saves/worldA/delete","GET")]
    [InlineData("/bisect/start","GET")]
    [InlineData("/mods/a.jar/toggle","DELETE")]
    public async Task InvalidShapeOrMethodCannotMutate(string suffix,string method)
    {using var fx=new Fixture();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);var before=fx.Files();var (status,body)=await h.SendAsync(new HttpMethod(method),fx.PathFor(suffix));output.WriteLine($"{method} {suffix}: {status} {body}");Assert.Equal(404,status);Assert.Single(fx.Config.Instances);Assert.Equal(before,fx.Files());}
    [Theory][InlineData(null)][InlineData("incorrect-token")]
    public async Task MissingOrInvalidTokenCannotDelete(string? token)
    {using var fx=new Fixture();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);var before=fx.Files();using var req=new HttpRequestMessage(HttpMethod.Post,fx.PathFor("/mods/a.jar/delete"));if(token!=null)req.Headers.Add("X-Pineglow-Token",token);using var res=await h.Client.SendAsync(req);Assert.Equal(401,(int)res.StatusCode);Assert.Equal(before,fx.Files());}
    [Fact]
    public async Task EncodedUnicodeFilenameTogglePreservesBytes()
    {using var fx=new Fixture();var name="测试 模组.jar";var path=Path.Combine(fx.Root,"mods",name);File.WriteAllText(path,"unicode-sentinel");await using var h=await LocalApiTestHarness.StartAsync(fx.Config);var (status,_)=await h.SendAsync(HttpMethod.Post,fx.PathFor("/mods/"+Uri.EscapeDataString(name)+"/toggle"));Assert.Equal(200,status);Assert.False(File.Exists(path));Assert.Equal("unicode-sentinel",File.ReadAllText(path+".disabled"));}
    private static async Task<string> Backup(LocalApiTestHarness h, Fixture fx)
    {var (status,body)=await h.SendAsync(HttpMethod.Post,fx.PathFor("/saves/worldA/backup"));Assert.Equal(200,status);return Path.GetFileName(JsonDocument.Parse(body).RootElement.GetProperty("path").GetString())!;}
    [Fact]
    public async Task RestoreProducesOriginalBytesAndDeleteRemovesOnlyRequestedWorld()
    {using var fx=new Fixture();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);var name=await Backup(h,fx);File.WriteAllText(Path.Combine(fx.Root,"saves","worldA","level.dat"),"changed-A");var (status,body)=await h.SendAsync(HttpMethod.Post,fx.PathFor("/saves/worldA/backups/"+name+"/restore"));Assert.Equal(200,status);var target=JsonDocument.Parse(body).RootElement.GetProperty("path").GetString()!;output.WriteLine("restore target="+target);Assert.Equal("original-worldA",File.ReadAllText(Path.Combine(target,"level.dat")));Assert.Equal("changed-A",File.ReadAllText(Path.Combine(fx.Root,"saves","worldA","level.dat")));(status,_)=await h.SendAsync(HttpMethod.Post,fx.PathFor("/saves/worldA/delete"));Assert.Equal(200,status);Assert.False(Directory.Exists(Path.Combine(fx.Root,"saves","worldA")));Assert.Equal("original-worldB",File.ReadAllText(Path.Combine(fx.Root,"saves","worldB","level.dat")));}
    [Fact]
    public async Task RestoreRejectsBackupFromDifferentWorld()
    {using var fx=new Fixture();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);var name=await Backup(h,fx);var before=fx.Files();var (status,body)=await h.SendAsync(HttpMethod.Post,fx.PathFor("/saves/worldB/backups/"+name+"/restore"));output.WriteLine($"worldB request with worldA backup => {status}: {body}");Assert.True(status>=400,$"Cross-world restore accepted: {status} {body}");Assert.Equal(before,fx.Files());}
    [Fact]
    public async Task BackupsListIsScopedToRequestedWorld()
    {using var fx=new Fixture();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);await Backup(h,fx);var (status,body)=await h.SendAsync(HttpMethod.Get,fx.PathFor("/saves/worldB/backups"));output.WriteLine("worldB backup list="+body);Assert.Equal(200,status);Assert.Empty(JsonDocument.Parse(body).RootElement.GetProperty("backups").EnumerateArray());}
    [Fact]
    public async Task BisectGiveupPreservesPreviouslyDisabledModAndOriginalContents()
    {using var fx=new Fixture();var disabled=Path.Combine(fx.Root,"mods","prior.jar.disabled");File.WriteAllText(disabled,"previously-disabled");await using var h=await LocalApiTestHarness.StartAsync(fx.Config);var before=fx.Files();var (status,_)=await h.SendAsync(HttpMethod.Post,fx.PathFor("/bisect/start"));Assert.Equal(200,status);(status,_)=await h.SendAsync(HttpMethod.Post,fx.PathFor("/bisect/giveup"));Assert.Equal(200,status);Assert.Equal(before,fx.Files());}
}
