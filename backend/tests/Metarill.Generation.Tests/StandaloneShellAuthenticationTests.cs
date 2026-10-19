using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实独立包HTTP验证原壳账户操作，不连接数据库、不替用户更改真实密码。</summary>
public sealed class StandaloneShellAuthenticationTests
{
    /// <summary>改密真实持久化并撤销全部旧session；重启、错误身份、损坏和链接不得回退旧配置密码。</summary>
    [Fact]
    public async Task Original_shell_password_contract_persists_privately_and_never_revives_bootstrap_password()
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-shell-auth",Guid.NewGuid().ToString("N"));
        try
        {
            var source=SourceRoot();var metadata=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var output=Path.Combine(root,"application");new ApplicationPackager().Create(metadata,output,"Example.OriginalShell",source);
            await Build(output);var oldPassword=Guid.NewGuid().ToString("N");var nextPassword=Guid.NewGuid().ToString("N");var lastPassword=Guid.NewGuid().ToString("N");
            var host=Path.Combine(output,"backend/Application.Host");var privateFolder=Path.Combine(host,".local/development-auth");var state=Path.Combine(privateFolder,"credentials.json");
            await using(var server=await Server.Start(host,"Development",oldPassword))
            {
                using var anonymous=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/edit_password",new{oldPwd=oldPassword,pwd=nextPassword});Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);Assert.False(File.Exists(state));
                var first=await Login(server.Client,oldPassword);var second=await Login(server.Client,oldPassword);Assert.False(Directory.Exists(privateFolder));
                using(var wrong=await Send(server.Client,first,new{oldPwd="错误的原密码",pwd=nextPassword}))Assert.Equal(HttpStatusCode.Forbidden,wrong.StatusCode);
                using(var weak=await Send(server.Client,first,new{oldPwd=oldPassword,pwd="短"}))Assert.Equal(HttpStatusCode.BadRequest,weak.StatusCode);
                using(var extra=await Send(server.Client,first,new{oldPwd=oldPassword,pwd=nextPassword,accountId="other"}))Assert.Equal(HttpStatusCode.BadRequest,extra.StatusCode);
                using(var request=new HttpRequestMessage(HttpMethod.Post,"/api/admin/sys/yz_manager/edit_password"){Content=new StringContent("{\"oldPwd\":\"x\",\"oldPwd\":\"y\",\"pwd\":\"z\"}",Encoding.UTF8,"application/json")}){request.Headers.Add("loginStateId",first);using var duplicate=await server.Client.SendAsync(request);Assert.Equal(HttpStatusCode.BadRequest,duplicate.StatusCode);}
                using(var unchanged=await Permissions(server.Client,second))Assert.Equal(HttpStatusCode.OK,unchanged.StatusCode);Assert.False(File.Exists(state));
                using(var changed=await Send(server.Client,first,new{oldPwd=oldPassword,pwd=nextPassword})){Assert.Equal(HttpStatusCode.OK,changed.StatusCode);Assert.Equal(200,(await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetInt32());}
                using(var stale=await Permissions(server.Client,first))Assert.Equal(HttpStatusCode.Unauthorized,stale.StatusCode);using(var stale=await Permissions(server.Client,second))Assert.Equal(HttpStatusCode.Unauthorized,stale.StatusCode);
                using(var rejected=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="shell-probe",password=oldPassword}))Assert.Equal(HttpStatusCode.Unauthorized,rejected.StatusCode);
                Assert.NotEmpty(await Login(server.Client,nextPassword));
            }
            // 两个真实进程共享固定私有摘要：另一进程改密后，本进程的既有session同样必须失效。
            await using(var firstServer=await Server.Start(host,"Development",oldPassword))
            await using(var secondServer=await Server.Start(host,"Development",oldPassword))
            {
                var first=await Login(firstServer.Client,nextPassword);var second=await Login(secondServer.Client,nextPassword);
                using(var changed=await Send(firstServer.Client,first,new{oldPwd=nextPassword,pwd=lastPassword}))Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
                using(var stale=await Permissions(secondServer.Client,second))Assert.Equal(HttpStatusCode.Unauthorized,stale.StatusCode);
                Assert.NotEmpty(await Login(secondServer.Client,lastPassword));
                var latest=await Login(firstServer.Client,lastPassword);
                using var restored=await Send(firstServer.Client,latest,new{oldPwd=lastPassword,pwd=nextPassword});Assert.Equal(HttpStatusCode.OK,restored.StatusCode);
            }
            var bytes=File.ReadAllBytes(state);Assert.DoesNotContain(oldPassword,Encoding.UTF8.GetString(bytes));Assert.DoesNotContain(nextPassword,Encoding.UTF8.GetString(bytes));Assert.DoesNotContain("credentials.json",File.ReadAllText(Path.Combine(output,".metarill-package.json")));Assert.Single(Directory.GetFiles(privateFolder));
            using(var json=JsonDocument.Parse(bytes)){Assert.Equal("PBKDF2-SHA256",json.RootElement.GetProperty("algorithm").GetString());Assert.Equal(600000,json.RootElement.GetProperty("iterations").GetInt32());Assert.Equal(metadata.YzProjectId,json.RootElement.GetProperty("projectId").GetString());}
            await using(var server=await Server.Start(host,"Development",oldPassword))
            {
                var current=await Login(server.Client,nextPassword);Assert.Equal(bytes,File.ReadAllBytes(state));
                // 旧GET合同仅保留兼容；原页面新请求已用POST，不在普通交互中把密码放进URL。
                using var request=new HttpRequestMessage(HttpMethod.Get,"/api/admin/sys/yz_manager/edit_password?oldPwd="+Uri.EscapeDataString(nextPassword)+"&pwd="+Uri.EscapeDataString(lastPassword));request.Headers.Add("loginStateId",current);using var changed=await server.Client.SendAsync(request);Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
                using var stale=await Permissions(server.Client,current);Assert.Equal(HttpStatusCode.Unauthorized,stale.StatusCode);Assert.NotEmpty(await Login(server.Client,lastPassword));
            }
            bytes=File.ReadAllBytes(state);
            // 私有摘要的现有权限过宽必须拒绝，不能默默重置权限后接受不可信状态。
            BroadenTestFile(state);
            await using(var server=await Server.Start(host,"Development",oldPassword)){using var login=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="shell-probe",password=oldPassword});Assert.False(login.IsSuccessStatusCode);Assert.Equal(bytes,File.ReadAllBytes(state));}SecureTestFile(state);
            foreach(var scenario in new[]{"Production","disabled","different-user"})
            {
                await using var server=await Server.Start(host,scenario=="Production"?"Production":"Development",oldPassword,enabled:scenario!="disabled",username:scenario=="different-user"?"other-probe":"shell-probe");
                using var login=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username=scenario=="different-user"?"other-probe":"shell-probe",password=oldPassword});Assert.False(login.IsSuccessStatusCode);Assert.Equal(bytes,File.ReadAllBytes(state));
            }
            // 同目录替换成另一项目元数据：旧账户摘要不能继承到新配置项目。
            var metadataPath=Path.Combine(output,"backend/metadata/project.json");var originalMetadata=File.ReadAllText(metadataPath);var other=System.Text.Json.Nodes.JsonNode.Parse(originalMetadata)!.AsObject();other["yzProjectId"]="2200000000000999999";
            foreach(var property in other.Where(item=>item.Value is System.Text.Json.Nodes.JsonArray).ToArray())foreach(var item in property.Value!.AsArray())if(item is System.Text.Json.Nodes.JsonObject row&&row.ContainsKey("yzProjectId"))row["yzProjectId"]="2200000000000999999";
            File.WriteAllText(metadataPath,other.ToJsonString());
            await using(var server=await Server.Start(host,"Development",oldPassword)){using var login=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="shell-probe",password=oldPassword});Assert.False(login.IsSuccessStatusCode);Assert.Equal(bytes,File.ReadAllBytes(state));}File.WriteAllText(metadataPath,originalMetadata);
            File.WriteAllText(state,"{损坏状态");
            await using(var server=await Server.Start(host,"Development",oldPassword)){using var login=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="shell-probe",password=oldPassword});Assert.False(login.IsSuccessStatusCode);Assert.Equal("{损坏状态",File.ReadAllText(state));}File.WriteAllBytes(state,bytes);
            File.Delete(state);
            await using(var server=await Server.Start(host,"Development",oldPassword)){using var login=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="shell-probe",password=oldPassword});Assert.False(login.IsSuccessStatusCode);Assert.False(File.Exists(state));}File.WriteAllBytes(state,bytes);SecureTestFile(state);
            // 实际目录链接不能把密码更新写到外部目录，保持原私有目录和外部空目录。
            var preserved=privateFolder+"-preserved";var outside=Path.Combine(root,"outside");Directory.CreateDirectory(outside);Directory.Move(privateFolder,preserved);CreateLink(privateFolder,outside);
            try{await using var server=await Server.Start(host,"Development",oldPassword);using var login=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="shell-probe",password=oldPassword});Assert.False(login.IsSuccessStatusCode);Assert.Empty(Directory.EnumerateFileSystemEntries(outside));}
            finally{RemoveTestLink(privateFolder);Directory.Move(preserved,privateFolder);}
            // 断链同样不能回退原配置密码，外部目标不能因登录而被创建。
            Directory.Move(privateFolder,preserved);var missing=Path.Combine(root,"missing-target");CreateLink(privateFolder,missing);
            try{await using var server=await Server.Start(host,"Development",oldPassword);using var login=await server.Client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="shell-probe",password=oldPassword});Assert.False(login.IsSuccessStatusCode);Assert.False(Directory.Exists(missing));}
            finally{RemoveTestLink(privateFolder);Directory.Move(preserved,privateFolder);}
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,Path.Combine(Path.GetTempPath(),"metarill-shell-auth"));}
    }
    /// <summary>测试删除后重建的凭据只恢复当前用户权限，不放宽实际产品写入策略。</summary>
    private static void SecureTestFile(string path)
    {
        if(OperatingSystem.IsWindows()){var owner=WindowsIdentity.GetCurrent().User!;var security=new FileSecurity();security.SetOwner(owner);security.SetAccessRuleProtection(true,false);security.AddAccessRule(new FileSystemAccessRule(owner,FileSystemRights.FullControl,AccessControlType.Allow));new FileInfo(path).SetAccessControl(security);}
        else File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }
    /// <summary>仅给自己的随机测试摘要增加一个外部读取主体，验证产品不会接收过宽权限。</summary>
    private static void BroadenTestFile(string path)
    {
        if(OperatingSystem.IsWindows()){var security=new FileInfo(path).GetAccessControl();security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.Read,AccessControlType.Allow));new FileInfo(path).SetAccessControl(security);}
        else File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead);
    }
    /// <summary>只移除本fixture刚创建的链接本身；Unix断链须按文件unlink，不能递归删除或跟随目标。</summary>
    private static void RemoveTestLink(string path)
    {
        if(new DirectoryInfo(path).LinkTarget is null)throw new InvalidOperationException("测试清理路径不是链接，已保留现场。");
        if(OperatingSystem.IsWindows())Directory.Delete(path,false);else File.Delete(path);
    }
    /// <summary>链接仅位于本fixture随机目录；Windows使用junction并拒绝shell元字符。</summary>
    private static void CreateLink(string path,string target)
    {
        if(!OperatingSystem.IsWindows()){Directory.CreateSymbolicLink(path,target);return;}
        if((path+target).IndexOfAny(['&','|','<','>','^','%','!','"','\r','\n'])>=0)throw new InvalidOperationException("测试路径无效。");
        using var process=Process.Start(new ProcessStartInfo("cmd.exe"){Arguments="/c mklink /J \""+path+"\" \""+target+"\"",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})!;process.WaitForExit();Assert.Equal(0,process.ExitCode);
    }
    /// <summary>真实会话只从头传输，不打印密码、令牌或私有摘要。</summary>
    private static async Task<string> Login(HttpClient client,string password){using var result=await client.PostAsJsonAsync("/api/admin/sys/yz_manager/login",new{username="shell-probe",password});Assert.Equal(HttpStatusCode.OK,result.StatusCode);return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("map").GetProperty("loginStateId").GetString()!;}
    private static Task<HttpResponseMessage> Send(HttpClient client,string token,object body){var request=new HttpRequestMessage(HttpMethod.Post,"/api/admin/sys/yz_manager/edit_password"){Content=JsonContent.Create(body)};request.Headers.Add("loginStateId",token);return client.SendAsync(request);}
    private static Task<HttpResponseMessage> Permissions(HttpClient client,string token){var request=new HttpRequestMessage(HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");request.Headers.Add("loginStateId",token);return client.SendAsync(request);}
    /// <summary>只构建刚生成的临时独立包，完全从自己的源编译。</summary>
    private static async Task Build(string output){var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){WorkingDirectory=output,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","--nologo","--disable-build-servers","--verbosity","quiet","-p:WarningsAsErrors=CA1416"})start.ArgumentList.Add(arg);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);}
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    /// <summary>临时回环真实宿主，固定Warning日志不记录含测试密码的旧GET；退出终止整棵测试进程。</summary>
    private sealed class Server(Process process,HttpClient client,Task<string> stdout,Task<string> stderr):IAsyncDisposable
    {
        public HttpClient Client=>client;
        public static async Task<Server> Start(string host,string environment,string password,bool enabled=true,string username="shell-probe")
        {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){WorkingDirectory=host,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));info.ArgumentList.Add("--urls");info.ArgumentList.Add("http://127.0.0.1:"+port);info.Environment["ASPNETCORE_ENVIRONMENT"]=environment;info.Environment["Authentication__DevelopmentBootstrap__Enabled"]=enabled?"true":"false";info.Environment["Authentication__DevelopmentBootstrap__Username"]=username;info.Environment["Authentication__DevelopmentBootstrap__Password"]=password;info.Environment["Logging__LogLevel__Default"]="Warning";
            var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var client=new HttpClient{BaseAddress=new("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(5)};var result=new Server(process,client,stdout,stderr);
            try{for(var attempt=0;attempt<100&&!process.HasExited;attempt++){try{using var health=await client.GetAsync("/health");if(health.IsSuccessStatusCode)return result;}catch(HttpRequestException){}await Task.Delay(100);}throw new InvalidOperationException("临时独立认证宿主未就绪。");}catch{await result.DisposeAsync();throw;}
        }
        public async ValueTask DisposeAsync(){client.Dispose();if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await stdout;await stderr;process.Dispose();}
    }
}
