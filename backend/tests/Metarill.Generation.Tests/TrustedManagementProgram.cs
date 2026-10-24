using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text.Json;
using Metarill.Data;

namespace Metarill.Generation.Tests;

/// <summary>以真实独立Program及其固定模板运行内部管理目录；不发布完整包或替换认证中间件。</summary>
internal sealed class TrustedManagementProgram:IAsyncDisposable
{
    private readonly Process process;private readonly Task<string> output,error;private readonly string directory,settings;
    internal string Address{get;}
    /// <summary>记录本夹具唯一子进程及其私有输出，释放时不触及其他测试或服务。</summary>
    private TrustedManagementProgram(Process process,Task<string> output,Task<string> error,string directory,string settings,string address)
    {this.process=process;this.output=output;this.error=error;this.directory=directory;this.settings=settings;Address=address;}
    /// <summary>连接只写私有配置，源码直接取当前模板及同一生成批次；先编译再启动，正常启动不执行结构或初始化命令。</summary>
    internal static async Task<TrustedManagementProgram> StartAsync(TrustedSecurityServiceFixture fixture,DatabaseOptions options,string evidence)
    {
        var directory=Path.Combine(evidence,"program");Directory.CreateDirectory(directory);Directory.CreateDirectory(Path.Combine(directory,"metadata"));
        foreach(var name in new[]{"Program.cs","Authentication.cs","PasswordStore.cs","Navigation.cs","WebAuthentication.cs","SecurityInitialization.cs","PackageSecurityCatalog.cs","SessionPolicy.cs","WebSecurityCoordination.cs"})
            await File.WriteAllTextAsync(Path.Combine(directory,name),File.ReadAllText(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"templates/standalone",name+".tpl")).Replace("{{NAMESPACE}}",TrustedSecurityServiceFixture.Namespace));
        await File.WriteAllTextAsync(Path.Combine(directory,"PackageSecurityCatalogBinding.g.cs"),ApplicationPackager.SecurityCatalogBindingSource(TrustedSecurityServiceFixture.Namespace,Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fixture.CatalogBytes))));
        await File.WriteAllBytesAsync(Path.Combine(directory,"metadata/project.json"),fixture.CatalogBytes);
        foreach(var name in new[]{"files","security"})File.Copy(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"metadata",name+".json"),Path.Combine(directory,"metadata",name+".json"));
        var references=string.Join("",new[]{"Core","Data","Security","Files","Exports"}.Select(name=>"<ProjectReference Include=\""+SecurityElement.Escape(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"src/Metarill."+name,"Metarill."+name+".csproj"))+"\"/>"));
        var assemblyName="TrustedProgram_"+Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(directory,"ProgramHost.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>"+assemblyName+"</AssemblyName></PropertyGroup><ItemGroup>"+references+"<Compile Include=\""+SecurityElement.Escape(Path.Combine(fixture.DirectoryPath,"generated/**/*.cs"))+"\"/><Content Update=\"metadata/*.json\" CopyToOutputDirectory=\"PreserveNewest\"/></ItemGroup></Project>");
        var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",directory);build.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=directory;foreach(var arg in new[]{"build","ProgramHost.csproj","--nologo","--disable-build-servers","-v","quiet","-warnaserror"})build.ArgumentList.Add(arg);
        using(await SharedSourceBuildGate.Shared.EnterAsync())await NonRedundantApplicationFixture.RunAsync(build);
        var settings=Path.Combine(directory,"appsettings.local.json");await File.WriteAllTextAsync(settings,JsonSerializer.Serialize(new{Database=options,Authentication=new{Mode="Web"},Ids=new{WorkerId=947},Files=new{RootDirectory=Path.Combine(directory,"files")},Exports=new{RootDirectory=Path.Combine(directory,"exports")},Logging=new{LogLevel=new{Default="Warning"}}}));
        using var reservation=new TcpListener(IPAddress.Loopback,0);reservation.Start();var port=((IPEndPoint)reservation.LocalEndpoint).Port;reservation.Stop();var address="http://127.0.0.1:"+port;
        var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",directory);start.ArgumentList.Add(Path.Combine(directory,"bin/Debug/net10.0",assemblyName+".dll"));start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["ASPNETCORE_URLS"]=address;
        var process=Process.Start(start)!;var instance=new TrustedManagementProgram(process,process.StandardOutput.ReadToEndAsync(),process.StandardError.ReadToEndAsync(),directory,settings,address);
        try
        {
            using var client=new HttpClient{BaseAddress=new(address),Timeout=TimeSpan.FromSeconds(2)};
            for(var attempt=0;attempt<100;attempt++)
            {
                if(process.HasExited)throw new InvalidOperationException("真实管理Program提前退出，私有日志保留。");
                try{using var response=await client.GetAsync("/health");if(response.IsSuccessStatusCode)return instance;}catch(HttpRequestException){}catch(TaskCanceledException){}
                await Task.Delay(100);
            }
            throw new TimeoutException("真实管理Program启动未在观察窗口完成。");
        }
        catch{await instance.DisposeAsync();throw;}
    }
    /// <summary>只终止本夹具的Program；确认退出及输出排空后删除连接配置，保留脱敏前的私有诊断日志。</summary>
    public async ValueTask DisposeAsync()
    {
        try{await SharedSourceBuildGate.StopOwnProcessAsync(process,output,error);await File.WriteAllTextAsync(Path.Combine(directory,"stdout.log"),await output);await File.WriteAllTextAsync(Path.Combine(directory,"stderr.log"),await error);}
        finally{if(process.HasExited){process.Dispose();if(File.Exists(settings))File.Delete(settings);}}
    }
}
