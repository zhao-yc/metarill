using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1重复来源使用独占新库、实际完整包和Production/Web，不注入认证替身。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed partial class FreshRedundantCrossMySqlTests
{
    private const long Project=9007199254740993,First=9007199254745001;
    /// <summary>四种重复角色交叉四种键，真实普通账号及文件事务使用各自独占新库。</summary>
    [FreshCrossMySqlTheory]
    [InlineData("cross3","CrossStringStringDatabase","cross_ss",950)]
    [InlineData("cross5","CrossStringLongDatabase","cross_sl",951)]
    [InlineData("cross6","CrossLongStringDatabase","cross_ls",952)]
    [InlineData("cross7","CrossLongLongDatabase","cross_ll",953)]
    public Task Redundant_cross_uses_real_identity_and_fresh_database(string profile,string property,string suffix,int worker)=>Exercise(profile,property,suffix,worker,SeedCross,VerifyCross);
    /// <summary>每个场景从零表安装，真实普通账号授权／撤权，重启后继续同一持久会话。</summary>
    private static async Task Exercise(string profile,string property,string suffix,int worker,Func<Probe,Task> seed,Func<Probe,Task> verify)
    {
        var options=FreshInstallConfiguration.Read(property,suffix);using var session=new DatabaseSession(options);using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));var run=settings.RootElement.GetProperty("RunId").GetString()!;await Pin(session,settings.RootElement.GetProperty("InstancePin"));Assert.Equal(0,await CountTables(session));
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/redundant-cross-fresh",run+"-"+suffix);Directory.CreateDirectory(evidence);var catalog=RedundantCrossFreshCatalogTests.Catalog(profile);var expectedTables=catalog.ConfigTables.Count+9;var boundary=Path.Combine(Path.GetTempPath(),"metarill-redundant-cross-fresh");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var application=Path.Combine(directory,"application");var username="r1-owner-"+suffix;var password=Guid.NewGuid().ToString("N");FreshHost? host=null;Probe? p=null;Exception? primary=null;var stage="实际Full构建";var passed=false;
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog,application,"Example.RedundantCrossFresh",root).FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(application,operation);var hostDirectory=Path.Combine(application,"backend/Application.Host");
            /// <summary>秘密仅经环境传递；禁用开发身份，实际运行包内Foundation和宿主。</summary>
            ProcessStartInfo HostInfo(string? operation=null)
            {
                var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Debug/net10.0/Application.Host.dll"));if(operation is not null)info.ArgumentList.Add(operation);info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=username;info.Environment["Authentication__Bootstrap__Password"]=password;info.Environment["Ids__WorkerId"]=worker.ToString(CultureInfo.InvariantCulture);info.Environment["Files__RootDirectory"]=Path.Combine(directory,"files");info.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;return info;
            }
            stage="零表启动／预览";host=await FreshHost.StartAsync(HostInfo(),"/health");Assert.Equal(0,await CountTables(session));await host.DisposeAsync();host=null;await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-preview"));Assert.Equal(0,await CountTables(session));stage="首次及重复显式结构应用";await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));Assert.Equal(expectedTables,await CountTables(session));await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));Assert.Equal(expectedTables,await CountTables(session));Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            var security=await ReadCatalog(Path.Combine(application,"backend/metadata/security.json"));var files=await ReadCatalog(Path.Combine(application,"backend/metadata/files.json"));var securityStore=new SqlRowStore(session,security);stage="首次及重复安全初始化";await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));var hash=SqlRowStore.Text(Assert.Single(await securityStore.ReadAsync("yz_manager",Row("name",username))),"password");Assert.True(PasswordHasher.Verify(password,hash));await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));Assert.Equal(hash,SqlRowStore.Text(Assert.Single(await securityStore.ReadAsync("yz_manager",Row("name",username))),"password"));
            p=new(profile,catalog,session,securityStore,new SqlRowStore(session,files));stage="本轮种子";await seed(p);host=await FreshHost.StartAsync(HostInfo(),"/health");p.Owner=host.Client;await FreshWebHttp.SendAsync(p.Owner,HttpMethod.Post,p.Route+"page",new PageRequest(),HttpStatusCode.Unauthorized);await FreshWebHttp.LoginAsync(p.Owner,username,password,form:true);var menu=await FreshWebHttp.SendAsync(p.Owner,HttpMethod.Get,"/api/admin/sys/yz_manager/getMenu");Assert.True(HasPage(menu.GetProperty("menu"),p.SourcePage));
            stage="真实普通账号";var ids=new SnowflakeIdGenerator(worker+10);var role=ids.NextId();var manager=ids.NextId();var reader="r1-reader-"+suffix;var readerPassword=Guid.NewGuid().ToString("N");await session.InTransactionAsync(async()=>{await securityStore.ReadForUpdateAsync("yz_project",Row("id",Project));await securityStore.InsertAsync("yz_role",Row("id",role,"yz_project_id",Project,"yz_admin_type_id",5L,"name",reader,"is_super",0,"is_built_in",0));await securityStore.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",Project,"yz_admin_type_id",5L,"name",reader,"password",PasswordHasher.Hash(readerPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{role.ToString(CultureInfo.InvariantCulture)})));foreach(var page in catalog.ConfigPages){var id=ids.NextId();p.Permissions.Add(page.Id,id);await securityStore.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",Project,"yz_role_id",role,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_all\"]"));}return true;});p.Reader=new HttpClient{BaseAddress=p.Owner.BaseAddress};var token=await FreshWebHttp.LoginAsync(p.Reader,reader,readerPassword);Assert.False((await FreshWebHttp.SendAsync(p.Reader,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            stage="正式候选／路径／CRUD／文件／撤权";await verify(p);stage="正式宿主重启和同会话注销";await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(HostInfo(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,p.Route+"detail/"+p.RecordId);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,p.Route+"detail/"+p.RecordId,expected:HttpStatusCode.Unauthorized);Assert.Equal(expectedTables,await CountTables(session));Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            passed=true;await Receipt("receipt",new{run,suffix,profile,passed,formalWeb=true,productionEnvironment=true,developmentAuthentication=false,fixedAuthorizationInjected=false,tableCount=expectedTables,ordinaryAccount=true,realCandidatesAndPath=true,sourceAndTargetRevocation=true,realFileUploadDownload=true,copyAndRollback=true,liveNames=true,partialSelection=true,redundantStoredIdsAndNames=true,repeatedSourceRoles=true,lateFileFailureRollback=true,persistentSession=true,browserExecuted=false,deviceExecuted=false,performanceExecuted=false,cleanupByFreshLease=true});
        }
        catch(Exception error){primary=error;await Receipt("failure",new{run,suffix,profile,stage,lastOperation=p?.Stage,errorType=error.GetType().FullName,error.Message,error.StackTrace,passed=false,cleanupByFreshLease=true});throw;}
        finally{p?.Reader?.Dispose();try{if(host is not null)await host.DisposeAsync();await TestDirectoryCleanup.DeleteAsync(directory,boundary);}catch(Exception cleanup)when(primary is not null){throw new AggregateException("正式图验收与本地清理均失败，首因已保留。",primary,cleanup);}if(!passed)await Receipt("failed",new{run,suffix,profile,passed=false,cleanupByFreshLease=true});}
        /// <summary>只将无凭据业务证据写入私有目录。</summary>
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>只封装测试共享会话和标准HTTP；不改变实际宿主的认证、权限或业务组件。</summary>
    private sealed class Probe(string profile,MetadataCatalog catalog,DatabaseSession session,SqlRowStore security,SqlRowStore files)
    {
        internal readonly MetadataCatalog Catalog=catalog;internal readonly DatabaseSession Session=session;internal readonly SqlRowStore Store=new(session,catalog),Security=security,Files=files;internal readonly Dictionary<string,long> Permissions=new(StringComparer.Ordinal);internal HttpClient Owner=null!,Reader=null!;internal string RecordId="",Stage="";
        internal readonly int Mask=RedundantCrossFreshCatalogTests.Definition(profile).Mask;internal readonly string ParentType=RedundantCrossFreshCatalogTests.Definition(profile).ParentType;
        internal string Source=>"rr_full_source";
        internal string SourcePage=>"201";
        internal string Route=>"/api/admin/auto/"+Source+"/"+SourcePage+"/";
        /// <summary>每次记录实际HTTP动作，异常不回显正文、令牌及密码。</summary>
        internal async Task<JsonElement> Send(HttpClient client,HttpMethod method,string route,object? body=null,HttpStatusCode expected=HttpStatusCode.OK){Stage=method.Method+" "+route;return await FreshWebHttp.SendAsync(client,method,route,body,expected);}
        /// <summary>只从已验证R中读取data。</summary>
        internal async Task<JsonElement> Post(string action,object body,HttpStatusCode expected=HttpStatusCode.OK){var response=await Send(Reader,HttpMethod.Post,Route+action,body,expected);return expected==HttpStatusCode.OK?response.GetProperty("data"):response;}
        /// <summary>撤权只修改本轮新库角色的明确页面许可。</summary>
        internal Task Grant(string page,string values)=>Security.UpdateAsync("yz_role_permission",Permissions[page],Row("permission_values",values));
        /// <summary>真实标量父键按模型类型保存，外部接口仍传精确字符串ID。</summary>
        internal object? Parent(long? id)=>id is null?null:ParentType=="string"?id.Value.ToString(CultureInfo.InvariantCulture):id.Value;
        /// <summary>失败与改名对比包含墓碑及文件引用，不用逻辑回显代替物理数据。</summary>
        internal async Task<string> Snapshot()=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{rows=await AllBusinessRows(),refs=(await Files.ReadIncludingDeletedAsync("yz_file_ref")).OrderBy(x=>SqlRowStore.Id(x))}))));
        /// <summary>快照包含所有业务表及墓碑，验证目标、消费者缓存和文件引用一起回滚。</summary>
        private async Task<object> AllBusinessRows(){var rows=new Dictionary<string,object>();foreach(var table in Catalog.ConfigTables.OrderBy(x=>x.TableName,StringComparer.Ordinal))rows[table.TableName]=(await Store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(x=>SqlRowStore.Id(x)).ToArray();return rows;}
        /// <summary>使用普通账号原上传计划、multipart和完成入口。</summary>
        internal async Task<string> Upload(byte[] bytes)
        {
            const string path="/api/admin/sys/file-library/";var id=(await Send(Reader,HttpMethod.Post,path+"upload-plan",new{originalName="redundant-cross.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId="9001"})).GetProperty("data").GetProperty("fileId").GetString()!;using var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(bytes),"file","redundant-cross.txt");await Send(Reader,HttpMethod.Post,path+"local-upload/"+id,content);await Send(Reader,HttpMethod.Post,path+"complete",new{fileId=id});return id;
        }
        /// <summary>保护下载实际比较文件流，撤权时按原401合同拒绝。</summary>
        internal async Task Download(string id,byte[] bytes,HttpStatusCode status=HttpStatusCode.OK){Stage="保护下载";using var response=await Reader.GetAsync("/api/admin/sys/file-library/protected-content/"+id);Assert.Equal(status,response.StatusCode);if(status==HttpStatusCode.OK)Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());}
        /// <summary>引用必须留在本源页及当前项目。</summary>
        internal async Task References(string id,int count){var rows=await Files.ReadAsync("yz_file_ref",Row("file_id",long.Parse(id,CultureInfo.InvariantCulture)));Assert.Equal(count,rows.Count);Assert.All(rows,x=>{Assert.Equal(SourcePage,SqlRowStore.Text(x,"page_id"));Assert.Equal(Project.ToString(CultureInfo.InvariantCulture),SqlRowStore.Text(x,"yz_project_id"));});}
    }
    /// <summary>固定项目／后台菜单递归匹配真实页ID。</summary>
    private static bool HasPage(JsonElement menu,string page)=>menu.EnumerateArray().Any(x=>x.GetProperty("projectId").GetString()==Project.ToString(CultureInfo.InvariantCulture)&&x.GetProperty("adminTypeId").GetString()=="5"&&(x.TryGetProperty("target",out var t)&&t.ValueKind==JsonValueKind.Object&&t.GetProperty("pageId").GetString()==page||HasPage(x.GetProperty("children"),page)));
    /// <summary>任何DDL之前同时核对随机库、Server-200实例指纹。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin){await using var c=new MySqlConnection(session.Options.ConnectionString);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var rows=await cmd.ExecuteReaderAsync();Assert.True(await rows.ReadAsync()&&rows.GetString(0)==session.Options.ExpectedDatabase&&rows.GetString(1)==pin.GetProperty("hostname").GetString()&&rows.GetString(2)==pin.GetProperty("serverUuid").GetString(),"新图正式库实例pin不符，未进行DDL。");}
    /// <summary>从information_schema只读实际表数，普通启动不能自动建表。</summary>
    private static async Task<long> CountTables(DatabaseSession session){await using var c=session.Provider.CreateConnection(session.Options);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()";return Convert.ToInt64(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>只读取本轮完整包实际交付的元数据。</summary>
    private static async Task<MetadataCatalog> ReadCatalog(string path)=>JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>所有种子和值通过标准SqlRowStore参数化保存。</summary>
    private static Dictionary<string,object?> Row(params object?[] pairs){var d=new Dictionary<string,object?>();for(var i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i]!,pairs[i+1]);return d;}
    /// <summary>精确ID只在HTTP边界转换为字符串。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
