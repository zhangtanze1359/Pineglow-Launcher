using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit.Abstractions;
namespace Pineglow.Launcher.Tests;

[Collection("LocalApiService")]
public class RemediationBoundaryProbes
{
    private readonly ITestOutputHelper output;
    public RemediationBoundaryProbes(ITestOutputHelper output) => this.output = output;
    private sealed class Fx : IDisposable
    {
        public string Root = Path.Combine(Path.GetTempPath(), "pineglow-boundary-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "game");
        public string Mods => Path.Combine(Game, "mods");
        public string Outside => Path.Combine(Root, "outside");
        public string Recovery => Path.Combine(Root, "recovery");
        public GameInstance Instance;
        public AppConfig Config;
        public string Api(string suffix) => "/api/instances/" + Instance.Id + suffix;
        public Fx()
        {
            Directory.CreateDirectory(Mods); Directory.CreateDirectory(Outside);
            Instance = new() { Id="boundary-"+Guid.NewGuid().ToString("N"), GameDirectory=Game, Name="synthetic", MinecraftVersion="1.20.4" };
            Config = new() { GameDirectory=Game, Instances=new(){Instance}, SelectedInstanceId=Instance.Id };
            File.WriteAllText(Path.Combine(Mods,"a.jar"),"old-A");
        }
        public void Dispose()
        {
            foreach (string p in new[]{Mods,Game})
                if(Directory.Exists(p) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) Directory.Delete(p,false);
            // GUID sandbox retained for evidence. No recursive deletion and no real data paths.
        }
    }
    private static async Task Junction(string link, string target)
    {
        var psi=new ProcessStartInfo("powershell.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-Command",$"New-Item -ItemType Junction -Path '{link}' -Target '{target}' -ErrorAction Stop | Out-Null"}) psi.ArgumentList.Add(arg);
        using var p=Process.Start(psi)!;
        var stdout=await p.StandardOutput.ReadToEndAsync();var stderr=await p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();
        Assert.True(p.ExitCode==0,stdout+stderr);
        Assert.True((File.GetAttributes(link)&FileAttributes.ReparsePoint)!=0);
    }
    [Fact]
    public async Task ModReplaceUndo_MustRejectJunctionAboveRecordedModsRoot()
    {
        using var fx=new Fx(); await using var h=await LocalApiTestHarness.StartAsync(fx.Config);
        var (status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/mods/a.jar/replace"),new {newFileBase64=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("new-A"))});
        Assert.Equal(200,status);var operation=JsonDocument.Parse(body).RootElement.GetProperty("operationId").GetString()!;
        Directory.Move(fx.Game,Path.Combine(fx.Root,"original-game"));
        Directory.CreateDirectory(Path.Combine(fx.Outside,"mods"));
        await Junction(fx.Game,fx.Outside);
        (status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/recovery/"+operation+"/restore"));
        bool outside=File.Exists(Path.Combine(fx.Outside,"mods/a.jar"));
        output.WriteLine($"sandbox={fx.Root}; restoreHTTP={status}; body={body}; outsideFileExists={outside}");
        Assert.False(outside,"Undo wrote through the game junction above record.GameDirectory=game/mods");
        Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("ok").GetBoolean());
    }
    [Fact]
    public async Task StoredPath_MustBelongToSameInstanceAndOperation()
    {
        using var fx=new Fx();var service=new FileRecoveryService(fx.Recovery);
        var a=await service.QuarantineAsync(fx.Instance.Id,fx.Game,"A",new[]{Path.Combine(fx.Mods,"a.jar")});
        var otherGame=Path.Combine(fx.Root,"other-game");Directory.CreateDirectory(otherGame);
        var bfile=Path.Combine(otherGame,"b.jar");File.WriteAllText(bfile,"other-instance-B");
        var b=await service.QuarantineAsync("other-instance",otherGame,"B",new[]{bfile});
        string originalAStored=a.Files[0].StoredPath;
        a.Files[0].StoredPath=b.Files[0].StoredPath;a.Files[0].Sha256=b.Files[0].Sha256;
        var restored=await service.RestoreAsync(a);
        output.WriteLine($"sandbox={fx.Root}; status={restored.Status}; target={(File.Exists(a.Files[0].OriginalPath)?File.ReadAllText(a.Files[0].OriginalPath):"absent")}; BStoredExists={File.Exists(b.Files[0].StoredPath)}; AStoredExists={File.Exists(originalAStored)}");
        Assert.True(File.Exists(b.Files[0].StoredPath),"Another instance's isolated file was consumed by a forged record");
        Assert.False(File.Exists(a.Files[0].OriginalPath));
    }
    [Fact]
    public async Task HttpForgedStoredPath_MustNotConsumeOtherInstanceFile()
    {
        using var fx=new Fx();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);
        var (status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/mods/a.jar/delete"));Assert.Equal(200,status);
        var operation=JsonDocument.Parse(body).RootElement.GetProperty("operationId").GetString()!;
        var service=new FileRecoveryService(h.RecoveryRoot);var a=(await service.LoadAsync(fx.Instance.Id,operation))!;
        var otherGame=Path.Combine(fx.Root,"http-other-game");Directory.CreateDirectory(otherGame);
        string bfile=Path.Combine(otherGame,"b.jar");File.WriteAllText(bfile,"http-other-instance-B");
        var b=await service.QuarantineAsync("other-instance",otherGame,"B",new[]{bfile});
        a.Files[0].StoredPath=b.Files[0].StoredPath;a.Files[0].Sha256=b.Files[0].Sha256;
        await File.WriteAllTextAsync(Path.Combine(service.GetOperationDirectory(fx.Instance.Id,operation),"record.json"),JsonSerializer.Serialize(a,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        (status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/recovery/"+operation+"/restore"));
        output.WriteLine($"sandbox={fx.Root}; HTTP={status}; body={body}; BStoredExists={File.Exists(b.Files[0].StoredPath)}; target={(File.Exists(Path.Combine(fx.Mods,"a.jar"))?File.ReadAllText(Path.Combine(fx.Mods,"a.jar")):"absent")}");
        Assert.True(File.Exists(b.Files[0].StoredPath),"HTTP accepted a record pointing to another instance's isolated file");
        Assert.False(File.Exists(Path.Combine(fx.Mods,"a.jar")));
    }
    [Fact]
    public async Task RollbackFailure_HttpMustExposeRecoveryFields()
    {
        using var fx=new Fx();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);
        ModReplacementService.BeforeCommitForTest=path=>{File.WriteAllText(path,"foreign-readonly");File.SetAttributes(path,FileAttributes.ReadOnly);throw new IOException("injected commit failure");};
        try
        {
            var(status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/mods/a.jar/replace"),new{newFileBase64=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("new-A"))});
            output.WriteLine($"sandbox={fx.Root}; HTTP={status}; body={body}");
            Assert.Equal(400,status);var json=JsonDocument.Parse(body).RootElement;
            Assert.True(json.TryGetProperty("needsRecovery",out var needs),"needsRecovery only appears inside error prose, not a JSON field");
            Assert.True(needs.GetBoolean());Assert.True(json.TryGetProperty("operationId",out _));Assert.True(json.TryGetProperty("recoveryStatus",out _));
        }
        finally { ModReplacementService.BeforeCommitForTest=null;File.SetAttributes(Path.Combine(fx.Mods,"a.jar"),FileAttributes.Normal); }
    }
    [Fact]
    public async Task SecretBackend_ConcurrentAsyncFlowsStayIsolated()
    {
        long before=SecretStore.ProductionBackendCallCount;
        var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);int count=0;
        async Task<string> Work(string value)
        {
            using var scope=SecretStore.OverrideBackendForTest(new InMemorySecretStoreBackend(value));
            SecretStore.Save(SecretStore.ProxyPassword,value);
            if(Interlocked.Increment(ref count)==2) ready.SetResult(true);
            await ready.Task;await Task.Yield();return SecretStore.Load(SecretStore.ProxyPassword);
        }
        var values=await Task.WhenAll(Task.Run(()=>Work("one")),Task.Run(()=>Work("two")));
        Assert.Equal(new[]{"one","two"},values);Assert.Equal(before,SecretStore.ProductionBackendCallCount);
        output.WriteLine("two overlapping async scopes stayed isolated; native delta=0");
    }
}
