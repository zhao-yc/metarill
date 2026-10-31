using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>仅消费新精确单库租约；真实Production/Web/OAuth/CLI/生成Service及MySQL，不注入身份或数据库替身。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class FreshSampleActionsMySqlTests
{
    private const string Contract="/api/admin/auto/sample_contract/81002/",Central="/api/admin/sys/config_button/execute",Files="/api/admin/sys/file-library/";
    /// <summary>原业务三按钮、普通按钮身份、文件保持及两个宿主消费同一租约；租约管理器负责库/账号独立清理。</summary>
    [SampleActionsNativeMySqlFact]
    public async Task Fixed_original_actions_use_real_web_cli_transactions_and_two_hosts()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));var config=settings.RootElement;
        var options=FreshInstallConfiguration.Read("AgentDatabase","agent");var run=config.GetProperty("RunId").GetString()!;
        if(Environment.GetEnvironmentVariable(SampleActionsNativeMySqlFactAttribute.RunVariable)!=run)throw new InvalidOperationException("缺少本轮独立正式验收运行标识，未连接数据库。");
        Assert.Equal(SampleActionsNativePreparation.ProductCommit,config.GetProperty("SampleActionsProductCommit").GetString());
        var source=Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_SAMPLE_ACTION_NATIVE_SOURCE_ROOT")??NonRedundantApplicationFixture.Root());
        var prepared=config.GetProperty("SampleActionsPreparation").Deserialize<GlobalCachedPackageProof>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(CSharpGenerator.SafePath(source,".local/sample-actions-native/prepared"),prepared.PackageRoot);
        var catalog=SampleActionsNativePreparation.Verify(prepared,source);var evidence=CSharpGenerator.SafePath(source,".local/sample-actions-native/"+run);Directory.CreateDirectory(evidence);
        var runtime=SampleActionsNativePreparation.CopyForExecution(prepared,source,Path.Combine(evidence,"application"));var hostDirectory=CSharpGenerator.SafePath(runtime.PackageRoot,"backend/Application.Host");var assembly=CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll");
        using var session=new DatabaseSession(options);await Pin(session,config.GetProperty("InstancePin"));Assert.Equal(0,await CountTables(session));
        var security=ReadCatalog(runtime.PackageRoot,"security");var fileMetadata=ReadCatalog(runtime.PackageRoot,"files");var all=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(catalog,security),fileMetadata),AgentFoundationCatalog.ForApplication(catalog));
        var store=new SqlRowStore(session,catalog);var securityStore=new SqlRowStore(session,security);var fileStore=new SqlRowStore(session,fileMetadata);
        var owner="sample-actions-owner";var password=Guid.NewGuid().ToString("N");var ordinaryPassword=Guid.NewGuid().ToString("N");var privateCli=Path.Combine(evidence,"cli-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(privateCli);
        var cliConfig=Path.Combine(privateCli,"config.json");var stage="普通启动保持空库";FreshHost? first=null,second=null;var passed=false;string ownerToken="";
        // 秘密只传进程环境，两个宿主用不同worker；文件、导出、凭据和日志均在固定包外。
        ProcessStartInfo Host(int worker=1007,string? operation=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(assembly);if(operation is not null)info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
            info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            info.Environment["Authentication__Bootstrap__Username"]=owner;info.Environment["Authentication__Bootstrap__Password"]=password;info.Environment["Agent__VerificationUri"]="https://agent.example/#/pages/custom/agent_authorize";info.Environment["Ids__WorkerId"]=worker.ToString(CultureInfo.InvariantCulture);
            info.Environment["Files__RootDirectory"]=Path.Combine(evidence,"files");info.Environment["Exports__RootDirectory"]=Path.Combine(evidence,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;return info;
        }
        try
        {
            first=await FreshHost.StartAsync(Host(),"/health");Assert.Equal(0,await CountTables(session));await first.DisposeAsync();first=null;
            stage="显式预览安装与初始化";await Process(Host(operation:"--schema-preview"),"schema-preview");Assert.Equal(0,await CountTables(session));await Process(Host(operation:"--schema-apply"),"schema-apply");Assert.Equal(all.ConfigTables.Count,await CountTables(session));Assert.Empty((await new SchemaService(session).PreviewAsync(all)).Changes);await Process(Host(operation:"--initialize-security"),"security-initialize");
            first=await FreshHost.StartAsync(Host(),"/health");second=await FreshHost.StartAsync(Host(1008),"/health");ownerToken=await FreshWebHttp.LoginAsync(first.Client,owner,password,form:true);second.Client.DefaultRequestHeaders.Add("loginStateId",ownerToken);
            await FreshWebHttp.SendAsync(second.Client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");
            var ids=new SnowflakeIdGenerator(1009);var customer=ids.NextId();await store.InsertAsync("sample_customer",Row("id",customer,"name","原三按钮客户"));
            stage="原文件与业务准备";var bytes=Encoding.UTF8.GetBytes("原三按钮正式文件");var file=(await Post(first.Client,Files+"upload-plan",new{originalName="sample-actions.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId="82007"})).GetProperty("data").GetProperty("fileId").GetString()!;
            using(var content=new MultipartFormDataContent()){content.Add(new ByteArrayContent(bytes),"file","sample-actions.txt");await FreshWebHttp.SendAsync(first.Client,HttpMethod.Post,Files+"local-upload/"+file,content);}await Post(first.Client,Files+"complete",new{fileId=file});
            var records=new List<JsonElement>();for(var index=0;index<3;index++)
            {
                var record=(await Post(first.Client,Contract+"add",new{name="原三按钮合同"+index,sampleCustomerId=Id(customer),amount=10.00m,remarks="待处理",attachments=new[]{file}})).GetProperty("data").Clone();
                Assert.Equal(JsonValueKind.String,record.GetProperty("id").ValueKind);Assert.True(long.Parse(record.GetProperty("id").GetString()!,CultureInfo.InvariantCulture)>9007199254740991);records.Add(record);
            }
            var recordsPath=Path.Combine(evidence,"records.json");await File.WriteAllTextAsync(recordsPath,JsonSerializer.Serialize(records));
            stage="真实OAuth及CLI三动作";await Probe("login");await Probe("exercise");await Physical(records[0],"已处理");await References(file,3);
            stage="普通账号仅按钮权限";var role=ids.NextId();var manager=ids.NextId();var ordinary="sample-actions-ordinary";var grants=new Dictionary<string,long>();
            await session.InTransactionAsync(async()=>
            {
                await securityStore.ReadForUpdateAsync("yz_project",Row("id",long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture)));
                await securityStore.InsertAsync("yz_role",Row("id",role,"yz_project_id",long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture),"yz_admin_type_id",85001L,"name",ordinary,"is_super",0,"is_scope",0,"is_built_in",0));
                await securityStore.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture),"yz_admin_type_id",85001L,"name",ordinary,"password",PasswordHasher.Hash(ordinaryPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{Id(role)})));
                foreach(var button in new[]{"86000","86001","86002"}){var id=ids.NextId();grants.Add(button,id);await securityStore.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",long.Parse(catalog.YzProjectId,CultureInfo.InvariantCulture),"yz_role_id",role,"config_button_id",long.Parse(button,CultureInfo.InvariantCulture),"permission_values","[]"));}return true;
            });
            using var ordinaryClient=new HttpClient{BaseAddress=first.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(30)};await FreshWebHttp.LoginAsync(ordinaryClient,ordinary,ordinaryPassword,form:true);
            Assert.False((await FreshWebHttp.SendAsync(ordinaryClient,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            var secondId=records[1].GetProperty("id").GetString()!;await FreshWebHttp.SendAsync(ordinaryClient,HttpMethod.Get,Contract+"detail/"+secondId,expected:HttpStatusCode.Forbidden);
            await Button(ordinaryClient,"86000",records[1]);var before=await Snapshot();await Button(ordinaryClient,"86002",records[1],code:500);Assert.Equal(before,await Snapshot());await Physical(records[1],"待处理");
            await Button(ordinaryClient,"86001",records[1]);await Physical(records[1],"已处理");await References(file,3);
            before=await Snapshot();await securityStore.UpdateAsync("yz_role_permission",grants["86000"],Row("config_button_id",86002L));await Button(ordinaryClient,"86000",records[1],http:HttpStatusCode.Forbidden,code:403);Assert.Equal(before,await Snapshot());
            stage="原Web旧值拒绝";var third=records[2];await Post(first.Client,Contract+"update",new{id=third.GetProperty("id").GetString(),name="Web预览后改名"});before=await Snapshot();await Button(first.Client,"86001",third,HttpStatusCode.Conflict,409);Assert.Equal(before,await Snapshot());await Physical(third,"待处理");
            stage="两个真实宿主及Agent共用占用拒绝";before=await Snapshot();
            await using(var held=await session.Provider.TryAcquireExecutionLeaseAsync(session.Options,"original-button:"+catalog.YzProjectId+":86000",TimeSpan.FromMinutes(30),TimeProvider.System)??throw new InvalidOperationException("本轮独占按钮锁未取得。"))
            {
                var busy1=await Button(first.Client,"86000",third,code:500);var busy2=await Button(second.Client,"86000",third,code:500);Assert.Equal("按钮正在执行中，请稍后再试",busy1.GetProperty("msg").GetString());Assert.Equal(busy1.GetProperty("msg").GetString(),busy2.GetProperty("msg").GetString());await Probe("busy");Assert.Equal(before,await Snapshot());
            }
            // 用新的公开业务请求验证两个进程恢复，不以本机租约对象Disposed充当服务器释放证明。
            await Button(first.Client,"86000",third);await Button(second.Client,"86000",third);Assert.Equal(before,await Snapshot());
            stage="真实CLI撤权与重新批准";await Probe("revoke");using var cached=JsonDocument.Parse(await File.ReadAllTextAsync(cliConfig));var bearer=cached.RootElement.GetProperty("profiles").GetProperty("sample-actions").GetProperty("credentials").GetProperty("accessToken").GetString()!;
            using var agentSecond=new HttpClient{BaseAddress=second.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(20)};agentSecond.DefaultRequestHeaders.Add("Authorization","Bearer "+bearer);await FreshWebHttp.SendAsync(agentSecond,HttpMethod.Get,"/api/admin/sys/agent/me");
            stage="同一Agent及Web身份跨宿主重启";await first.DisposeAsync();first=null;first=await FreshHost.StartAsync(Host(),"/health");first.Client.DefaultRequestHeaders.Add("loginStateId",ownerToken);await FreshWebHttp.SendAsync(first.Client,HttpMethod.Get,"/api/admin/sys/agent/grants");
            using var agent=new HttpClient{BaseAddress=first.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(20)};agent.DefaultRequestHeaders.Add("Authorization","Bearer "+bearer);var me=(await FreshWebHttp.SendAsync(agent,HttpMethod.Get,"/api/admin/sys/agent/me")).GetProperty("data");
            await Post(first.Client,"/api/admin/sys/agent/revoke",new{grantId=me.GetProperty("grantId").ToString()});await FreshWebHttp.SendAsync(agent,HttpMethod.Get,"/api/admin/sys/agent/me",expected:HttpStatusCode.Unauthorized);
            await References(file,3);using(var downloaded=await first.Client.GetAsync(Files+"protected-content/"+file)){Assert.Equal(HttpStatusCode.OK,downloaded.StatusCode);Assert.Equal(bytes,await downloaded.Content.ReadAsByteArrayAsync());}Assert.Empty((await new SchemaService(session).PreviewAsync(all)).Changes);SampleActionsNativePreparation.Verify(prepared,source);SampleActionsNativePreparation.Verify(runtime,source,true);passed=true;
            await Receipt("receipt",new{run,passed,formalWeb=true,realCli=true,developmentAuthentication=false,injectedAuthorization=false,ordinaryButtonOnly=true,ordinaryRevocation=true,webStaleConflict=true,realRollback=true,fileReferencesPreserved=true,twoProcessesBusy=true,agentBusyTaskUnknown=true,webAndAgentRestart=true,browserApprovalExecuted=false,crossDatabaseExecuted=false,businessHoldingLockRaceExecuted=false,performanceExecuted=false});

            // 只消费真实CLI标准入口；秘密环境和CLI配置从不进入参数或回执。
            async Task Probe(string mode)
            {
                var node=NonRedundantApplicationFixture.Start("node",source);foreach(var value in new[]{CSharpGenerator.SafePath(source,"backend/tests/Metarill.Generation.Tests/SampleActionsCliLiveProbe.mjs"),mode,new Uri(first!.Client.BaseAddress!,"/api").ToString(),source,evidence,recordsPath})node.ArgumentList.Add(value);node.Environment["METARILL_AGENT_WEB_TOKEN"]=ownerToken;node.Environment["METARILL_AGENT_CONFIG"]=cliConfig;
                var output=await Process(node,"cli-"+mode);using var result=JsonDocument.Parse(output);var required=mode switch{"login"=>new[]{"realCli","deviceApproved"},"exercise"=>new[]{"realCli","threeActions","previewReadOnly","staleConflict","rollbackUnknown","rollbackReadback","completeApplied","duplicateUuid","stringId"},"busy"=>new[]{"busyBeforeBusiness","taskUnknown"},_=>new[]{"revoked","reapprovedForRestart"}};foreach(var flag in required)Assert.True(result.RootElement.GetProperty(flag).GetBoolean());
            }
        }
        catch(Exception error){await Receipt("failure",new{run,stage,passed=false,errorType=error.GetType().Name,stackTrace=error.StackTrace});throw new InvalidOperationException("原三按钮正式验收未通过，阶段："+stage+"；类型："+error.GetType().Name+"。详见本轮私有证据，不自动重试。");}
        finally
        {
            try{if(first is not null)await first.DisposeAsync();}
            finally
            {
                try{if(second is not null)await second.DisposeAsync();}
                finally
                {
                    try{if(Directory.Exists(privateCli))await TestDirectoryCleanup.DeleteAsync(privateCli,evidence);}
                    finally
                    {
                        foreach(var folder in new[]{"files","exports"}){var owned=Path.Combine(evidence,folder);if(Directory.Exists(owned))await TestDirectoryCleanup.DeleteAsync(owned,evidence);}
                        if(!passed)await Receipt("failed",new{run,passed=false,cleanupByLease=true});
                    }
                }
            }
        }
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        // 子进程只使用自有句柄；超时停止自有树，日志脱敏，业务请求不自动重试。
        async Task<string> Process(ProcessStartInfo info,string name)
        {
            using var process=System.Diagnostics.Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();var expired=false;
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));}catch(TimeoutException){expired=true;if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();}
            var text=await stdout;var errors=await stderr;foreach(var secret in new[]{password,ordinaryPassword,ownerToken,options.ConnectionString,new MySqlConnectionStringBuilder(options.ConnectionString).Password}.Where(value=>value.Length>0).OrderByDescending(value=>value.Length)){text=text.Replace(secret,"[已脱敏]",StringComparison.Ordinal);errors=errors.Replace(secret,"[已脱敏]",StringComparison.Ordinal);}
            await File.WriteAllTextAsync(Path.Combine(evidence,name+".log"),text+"\n"+errors);if(expired||process.ExitCode!=0)throw new InvalidOperationException("本轮自有验收进程失败或超时，未自动重试。");return text;
        }
        // 独立查本轮物理行，验证失败回滚或成功提交的最终金额和备注。
        async Task Physical(JsonElement record,string remarks){var row=Assert.Single(await store.ReadAsync("sample_contract",Row("id",long.Parse(record.GetProperty("id").GetString()!,CultureInfo.InvariantCulture))));Assert.Equal(remarks,SqlRowStore.Text(row,"remarks"));Assert.Equal(10m,Convert.ToDecimal(row["amount"],CultureInfo.InvariantCulture));}
        // 文件引用条数和项目归属都必须来自本轮实际库。
        async Task References(string file,int count){var refs=await fileStore.ReadAsync("yz_file_ref",Row("file_id",long.Parse(file,CultureInfo.InvariantCulture)));Assert.Equal(count,refs.Count);Assert.All(refs,row=>Assert.Equal(catalog.YzProjectId,SqlRowStore.Text(row,"yz_project_id")));}
        // 只对本轮业务表和文件引用作摘要，不包含账号、密码或令牌。
        async Task<string> Snapshot(){var state=new Dictionary<string,object>();foreach(var table in catalog.ConfigTables)state[table.TableName]=(await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(row=>SqlRowStore.Id(row)).ToArray();state["fileRefs"]=(await fileStore.ReadIncludingDeletedAsync("yz_file_ref")).OrderBy(row=>SqlRowStore.Id(row)).ToArray();return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state))));}
    }
    /// <summary>原中央请求及原单层R；状态与业务码分别验证，错误正文不回显。</summary>
    private static Task<JsonElement> Button(HttpClient client,string button,JsonElement record,HttpStatusCode http=HttpStatusCode.OK,int code=200)=>Post(client,Central,new{buttonId=button,entityId=record.GetProperty("id").GetString(),entity=record},http,code);
    /// <summary>分别核对HTTP状态及原R业务码，失败不打印响应正文。</summary>
    private static async Task<JsonElement> Post(HttpClient client,string route,object body,HttpStatusCode http=HttpStatusCode.OK,int code=200){using var response=await client.PostAsJsonAsync(route,body);Assert.Equal(http,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(code,json.RootElement.GetProperty("code").GetInt32());return json.RootElement.Clone();}
    /// <summary>只读取原完整包的固定基础设施目录，不从客户端选择资源来源。</summary>
    private static MetadataCatalog ReadCatalog(string package,string name)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(package,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>仅为固定测试种子字段建立参数字典，不拼接请求SQL。</summary>
    private static Dictionary<string,object?> Row(params object?[] parts){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<parts.Length;i+=2)row.Add((string)parts[i]!,parts[i+1]);return row;}
    /// <summary>标识只按十进制字符串编码，不经double。</summary>
    private static string Id(long id)=>id.ToString(CultureInfo.InvariantCulture);
    /// <summary>DDL前同时固定库、服务器容器及UUID，连接错误不能试建或补建。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var value=await command.ExecuteReaderAsync();Assert.True(await value.ReadAsync()&&value.GetString(0)==session.Options.ExpectedDatabase&&value.GetString(1)==pin.GetProperty("hostname").GetString()&&value.GetString(2)==pin.GetProperty("serverUuid").GetString(),"正式业务验收实例不符，未执行DDL。");}
    /// <summary>只读本轮当前库的实际表计数。</summary>
    private static async Task<long> CountTables(DatabaseSession session){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()";return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
}

/// <summary>新正式场景必须有独立显式运行标识，不能继承通用租约变量而意外运行。</summary>
internal sealed class SampleActionsNativeMySqlFactAttribute:FactAttribute
{
    internal const string RunVariable="METARILL_SAMPLE_ACTION_NATIVE_RUN";
    /// <summary>没有本轮独立标识或设置时跳过，不尝试连接数据库。</summary>
    public SampleActionsNativeMySqlFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RunVariable))||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")))Skip="未配置本轮原三按钮的独立精确租约运行标识和设置。";
    }
}
