using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
using Pineglow.Launcher.Models;
using Pineglow.Launcher.Services;
using Xunit.Abstractions;
namespace Pineglow.Launcher.Tests;
[Collection("LocalApiService")]
public sealed class FinalAcceptanceProbes
{
    readonly ITestOutputHelper output;
    public FinalAcceptanceProbes(ITestOutputHelper output)=>this.output=output;
    sealed class Fx : IDisposable
    {
        public string Root=Path.Combine(Path.GetTempPath(),"pineglow-final-probe-"+Guid.NewGuid().ToString("N"));
        public string Game=>Path.Combine(Root,"game"); public string Mods=>Path.Combine(Game,"mods");
        public string Recovery=>Path.Combine(Root,"recovery");public string Outside=>Path.Combine(Root,"outside");
        public GameInstance Instance;public AppConfig Config;public string Api(string suffix)=>"/api/instances/"+Instance.Id+suffix;
        public Fx(){Directory.CreateDirectory(Mods);Directory.CreateDirectory(Outside);Instance=new(){Id="final-"+Guid.NewGuid().ToString("N"),Name="final",GameDirectory=Game,MinecraftVersion="1.20.4"};Config=new(){GameDirectory=Game,Instances=new(){Instance},SelectedInstanceId=Instance.Id};File.WriteAllText(Path.Combine(Mods,"a.jar"),"old-A");File.WriteAllText(Path.Combine(Mods,"b.jar"),"old-B");}
        public void Dispose(){RunningInstanceRegistry.MarkExited(Instance.Id);ModBisectService.Remove(Instance.Id);if(Directory.Exists(Mods)&&(File.GetAttributes(Mods)&FileAttributes.ReparsePoint)!=0)Directory.Delete(Mods,false);if(Directory.Exists(Root)){foreach(var file in Directory.GetFiles(Root,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);Directory.Delete(Root,true);}}
    }
    [Fact]
    public async Task RestoreRejectsReparseAncestorOutsideGameRoot()
    {
        using var fx=new Fx();string path=Path.Combine(fx.Mods,"a.jar");File.Delete(Path.Combine(fx.Mods,"b.jar"));var service=new FileRecoveryService(fx.Recovery);
        var rec=await service.QuarantineAsync(fx.Instance.Id,fx.Game,"acceptance",new[]{path});Assert.Equal("quarantined",rec.Status);Directory.Delete(fx.Mods);
        var psi=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        psi.ArgumentList.Add("-NoProfile");psi.ArgumentList.Add("-NonInteractive");psi.ArgumentList.Add("-Command");psi.ArgumentList.Add($"New-Item -ItemType Junction -Path '{fx.Mods}' -Target '{fx.Outside}' -ErrorAction Stop | Out-Null");
        using(var p=Process.Start(psi)!){string stdout=await p.StandardOutput.ReadToEndAsync();string stderr=await p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();Assert.True(p.ExitCode==0,"junction setup failed: "+stderr+stdout);}
        Assert.True((File.GetAttributes(fx.Mods)&FileAttributes.ReparsePoint)!=0);rec=await service.RestoreAsync(rec);
        output.WriteLine($"state={rec.Status}, outsideFileExists={File.Exists(Path.Combine(fx.Outside,"a.jar"))}, storedExists={File.Exists(rec.Files[0].StoredPath)}");
        Assert.False(File.Exists(Path.Combine(fx.Outside,"a.jar")),"Recovery wrote outside the game directory through a junction");
        Assert.Equal("failed",rec.Files[0].State);Assert.True(File.Exists(rec.Files[0].StoredPath));
    }
    [Fact]
    public async Task ApiRecoveryMustNotWriteOutsideGameViaJunction()
    {
        using var fx=new Fx();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);
        var (status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/mods/a.jar/delete"));Assert.Equal(200,status);
        string operation=JsonDocument.Parse(body).RootElement.GetProperty("operationId").GetString()!;
        File.Delete(Path.Combine(fx.Mods,"b.jar"));Directory.Delete(fx.Mods);
        var psi=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        psi.ArgumentList.Add("-NoProfile");psi.ArgumentList.Add("-NonInteractive");psi.ArgumentList.Add("-Command");psi.ArgumentList.Add($"New-Item -ItemType Junction -Path '{fx.Mods}' -Target '{fx.Outside}' -ErrorAction Stop | Out-Null");
        using(var p=Process.Start(psi)!){string stderr=await p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();Assert.True(p.ExitCode==0,"junction setup failed: "+stderr);}
        (status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/recovery/"+operation+"/restore"));
        output.WriteLine($"API restore => {status}: {body}; outsideFileExists={File.Exists(Path.Combine(fx.Outside,"a.jar"))}");
        Assert.False(File.Exists(Path.Combine(fx.Outside,"a.jar")),"HTTP recovery wrote outside configured game root");
        Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("ok").GetBoolean());
    }
    [Fact]
    public async Task RollbackFailureMustNotClaimOldVersionWasRestored()
    {
        using var fx=new Fx();string target=Path.Combine(fx.Mods,"a.jar"),staged=Path.Combine(fx.Root,"staged.jar");File.WriteAllText(staged,"new-stage");var recovery=new FileRecoveryService(fx.Recovery);
        ModReplacementService.BeforeCommitForTest=path=>{File.WriteAllText(path,"foreign-readonly");File.SetAttributes(path,FileAttributes.ReadOnly);throw new IOException("injected commit failure");};
        try{var result=await new ModReplacementService(recovery).ReplaceAsync(fx.Instance,new ModService(fx.Game),"a.jar",staged);var rec=await recovery.LoadAsync(fx.Instance.Id,result.OperationId!);output.WriteLine($"status={result.Status}, error={result.Error}, targetBytes={File.ReadAllText(target)}, recoveryStatus={rec?.Status}");Assert.False(result.Ok);Assert.DoesNotContain("已回滚",result.Error??"");Assert.True(rec!.Files.Any(x=>x.State=="failed"));Assert.True(rec.Files.Any(x=>File.Exists(x.StoredPath)));}
        finally{ModReplacementService.BeforeCommitForTest=null;}
    }
    [Fact]
    public async Task ReadOnlyModsListingAvailableWhileGameIsRunning()
    {
        using var fx=new Fx();await using var h=await LocalApiTestHarness.StartAsync(fx.Config);RunningInstanceRegistry.MarkRunningPid(fx.Instance.Id,Environment.ProcessId);var (status,body)=await h.SendAsync(HttpMethod.Get,fx.Api("/mods"));output.WriteLine($"running instance GET mods => {status}: {body}");Assert.Equal(200,status);Assert.Equal("old-A",File.ReadAllText(Path.Combine(fx.Mods,"a.jar")));
    }
    [Fact]
    public async Task DeletedWorldBackupRestoresChineseSpaceHyphenNameAndBytes()
    {
        using var fx=new Fx();string world="世界 A-测试";string wd=Path.Combine(fx.Game,"saves",world);Directory.CreateDirectory(wd);File.WriteAllText(Path.Combine(wd,"level.dat"),"original-world-bytes");await using var h=await LocalApiTestHarness.StartAsync(fx.Config);var (status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/saves/"+Uri.EscapeDataString(world)+"/backup"));Assert.Equal(200,status);string name=Path.GetFileName(JsonDocument.Parse(body).RootElement.GetProperty("path").GetString())!;Directory.Delete(wd,true);(status,body)=await h.SendAsync(HttpMethod.Post,fx.Api("/saves/"+Uri.EscapeDataString(world)+"/backups/"+Uri.EscapeDataString(name)+"/restore"));output.WriteLine($"deleted world restore => {status}: {body}");Assert.Equal(200,status);Assert.Equal("original-world-bytes",File.ReadAllText(Path.Combine(JsonDocument.Parse(body).RootElement.GetProperty("path").GetString()!,"level.dat")));
    }
}
