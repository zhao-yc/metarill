using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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

/// <summary>只有专属随机空库配置存在时执行；普通CI的跳过不能计作正式宿主通过。</summary>
public sealed class FreshCrossMySqlTheoryAttribute:TheoryAttribute
{
    public FreshCrossMySqlTheoryAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")))Skip="尚未提供专属Server-200跨表空库，正式宿主验收未执行。";}
}

/// <summary>实际Full包、Production/Web宿主及真实MySQL身份，不注入固定授权器。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class FreshCrossTableMySqlTests
{
    private const long Project=9007199254740993,First=9007199254742001;
    /// <summary>每种来源／父键组合使用各自新库，独立证明首次安装、权限、稀疏值和文件事务。</summary>
    [FreshCrossMySqlTheory]
    [InlineData("string","string","CrossStringStringDatabase","cross_ss",940)]
    [InlineData("string","int64","CrossStringLongDatabase","cross_sl",941)]
    [InlineData("int64","string","CrossLongStringDatabase","cross_ls",942)]
    [InlineData("int64","int64","CrossLongLongDatabase","cross_ll",943)]
    public async Task Full_cross_installs_and_executes_original_routes_with_real_web_identity(string type,string parentType,string property,string suffix,int worker)
    {
        var options=FreshInstallConfiguration.Read(property,suffix);using var session=new DatabaseSession(options);
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));var run=settings.RootElement.GetProperty("RunId").GetString()!;var pin=settings.RootElement.GetProperty("InstancePin");await Pin(session,pin);Assert.Equal(0,await CountTables(session));
        var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/nonredundant-cross-table-fresh",run+"-"+suffix);Directory.CreateDirectory(evidence);
        var catalog=NonRedundantCrossFullTests.Catalog(type,parentType,true);foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId!="2"))page.IsSave=1;
        foreach(var page in catalog.ConfigPages.Where(page=>page.Id is "201" or "301" or "401" or "501"))catalog.ConfigMenus.Add(new(){Id=(900+int.Parse(page.Id,CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture),YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="跨表验收"+page.Id,Kind="GENERATED_PAGE",ConfigPageId=page.Id});
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-fresh");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var application=Path.Combine(directory,"application");var username="cross-owner-"+suffix;var password=Guid.NewGuid().ToString("N");FreshHost? host=null;var passed=false;Exception? primary=null;var stage="准备完整包";
        try
        {
            new ApplicationPackager().Create(catalog,application,"Example.CrossFresh",root);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(application,operation);
            var hostDirectory=Path.Combine(application,"backend/Application.Host");
            /// <summary>所有私有配置通过环境传入，命令行不带连接串、密码或登录令牌。</summary>
            ProcessStartInfo HostInfo(string? operation=null)
            {
                var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Debug/net10.0/Application.Host.dll"));if(operation is not null)info.ArgumentList.Add(operation);
                info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=username;info.Environment["Authentication__Bootstrap__Password"]=password;info.Environment["Ids__WorkerId"]=worker.ToString(CultureInfo.InvariantCulture);info.Environment["Files__RootDirectory"]=Path.Combine(directory,"files");info.Environment["Files__MaxFileBytes"]="4096";info.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;return info;
            }
            stage="显式空库初始化";host=await FreshHost.StartAsync(HostInfo(),"/health");Assert.Equal(0,await CountTables(session));await host.DisposeAsync();host=null;await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-preview"));Assert.Equal(0,await CountTables(session));await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));Assert.Equal(13,await CountTables(session));await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));Assert.Equal(13,await CountTables(session));Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            var security=await Catalog(Path.Combine(application,"backend/metadata/security.json"));var files=await Catalog(Path.Combine(application,"backend/metadata/files.json"));var securityStore=new SqlRowStore(session,security);var store=new SqlRowStore(session,catalog);var library=new SqlRowStore(session,files);
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));var owner=Assert.Single(await securityStore.ReadAsync("yz_manager",Row("name",username)));var hash=SqlRowStore.Text(owner,"password");Assert.True(PasswordHasher.Verify(password,hash));await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));Assert.Equal(hash,SqlRowStore.Text(Assert.Single(await securityStore.ReadAsync("yz_manager",Row("name",username))),"password"));
            // 种子只写本轮独占的新库；所有业务表来自真实元数据／SchemaService，不手写DDL。
            stage="固定种子与真实登录";foreach(var branch in new[]{0L,10L})for(var level=0;level<3;level++){var values=Row("id",First+branch,"yz_project_id",Project,"name",new[]{"国","区","城"}[level]+(branch==0?"甲":"乙"),"status",1);if(level>0)values.Add("nr_cross_full_"+(level==1?"country":"area")+"_id",parentType=="string"?(First+branch).ToString(CultureInfo.InvariantCulture):(object)(First+branch));await store.InsertAsync("nr_cross_full_"+new[]{"country","area","place"}[level],values);}
            host=await FreshHost.StartAsync(HostInfo(),"/health");var client=host.Client;var route="/api/admin/auto/nr_cross_full_source/202";var sourceRoute="/api/admin/auto/nr_cross_full_source/201";var parent=First.ToString(CultureInfo.InvariantCulture);var other=(First+10).ToString(CultureInfo.InvariantCulture);await Send(client,HttpMethod.Post,route+"/page",new PageRequest{ParentId=parent},HttpStatusCode.Unauthorized);var token=await FreshWebHttp.LoginAsync(client,username,password,form:true);var menu=await Send(client,HttpMethod.Get,"/api/admin/sys/yz_manager/getMenu");Assert.True(HasPage(menu.GetProperty("menu"),"201"));
            stage="根中末层与双控件";var selections=new List<string>();foreach(var slot in new[]{"countryId","areaId","placeId"}){var data=await Send(client,HttpMethod.Post,sourceRoute+"/add",new Dictionary<string,object?>{{"name","部分选择"+slot},{slot,parent}});var id=data.GetProperty("data").GetProperty("id").GetString()!;selections.Add(id);await Sparse(id,slot=="countryId"?0:slot=="areaId"?1:2);}
            var mixed=await Send(client,HttpMethod.Post,"/api/admin/auto/nr_cross_full_source/205/add?parentId="+parent,new{name="双层位",placeId=parent,secondaryAreaId=parent});await Sparse(mixed.GetProperty("data").GetProperty("id").GetString()!,2,1);
            var before=await Snapshot();await Send(client,HttpMethod.Post,"/api/admin/auto/nr_cross_full_source/205/add?parentId="+parent,new{name="越界双父",placeId=parent,secondaryPlaceId=other},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());
            await Send(client,HttpMethod.Post,sourceRoute+"/update",new{id=selections[2],countryId=(string?)null,areaId=(string?)null,placeId=(string?)null});await Sparse(selections[2],-1);
            stage="原文件上传与事务";var content=Encoding.UTF8.GetBytes("完整跨表正式宿主文件");var file=await Upload(client,content);var added=(await Send(client,HttpMethod.Post,route+"/add?parentId="+parent,new{name="父范围文件",placeId=parent,attachments=new[]{file}})).GetProperty("data");var record=added.GetProperty("id").GetString()!;await Sparse(record,2);await References(file,1);await Download(client,file,content);
            var copy=(await Send(client,HttpMethod.Post,route+"/copy/"+record+"?parentId="+parent,new{})).GetProperty("data").GetProperty("id").GetString()!;await References(file,2);await Sparse(copy,2);
            before=await Snapshot();await Send(client,HttpMethod.Post,route+"/update?parentId="+other,new{id=record,name="越界写"},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());await Send(client,HttpMethod.Post,route+"/update?parentId="+parent,new{id=record,name="非法文件不得提交",attachments=new[]{long.MaxValue.ToString(CultureInfo.InvariantCulture)}},HttpStatusCode.Unauthorized);Assert.Equal(before,await Snapshot());
            stage="真实普通账号及撤权";var ids=new SnowflakeIdGenerator(worker+10);var role=ids.NextId();var manager=ids.NextId();var permission=ids.NextId();var reader="cross-reader-"+suffix;var readerPassword=Guid.NewGuid().ToString("N");await session.InTransactionAsync(async()=>{await securityStore.ReadForUpdateAsync("yz_project",Row("id",Project));await securityStore.InsertAsync("yz_role",Row("id",role,"yz_project_id",Project,"yz_admin_type_id",5L,"name",reader,"is_super",0,"is_built_in",0));await securityStore.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",Project,"yz_admin_type_id",5L,"name",reader,"password",PasswordHasher.Hash(readerPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{role.ToString(CultureInfo.InvariantCulture)})));await securityStore.InsertAsync("yz_role_permission",Row("id",permission,"yz_project_id",Project,"yz_role_id",role,"config_page_id",202L,"permission_values","[\"is_query\"]"));return true;});
            using var ordinary=new HttpClient{BaseAddress=client.BaseAddress};await FreshWebHttp.LoginAsync(ordinary,reader,readerPassword);await Send(ordinary,HttpMethod.Get,route+"/detail/"+record+"?parentId="+parent);await Download(ordinary,file,content);await Send(ordinary,HttpMethod.Post,route+"/add?parentId="+parent,new{name="禁止新增",placeId=parent},HttpStatusCode.Forbidden);await Send(ordinary,HttpMethod.Post,route+"/cascadeTreeOptions25",new{levelKey="country",selectedPath=new{}},HttpStatusCode.Forbidden);
            var targetPermissions=new List<long>();foreach(var page in new[]{301L,401L,501L}){var targetPermission=ids.NextId();targetPermissions.Add(targetPermission);await securityStore.InsertAsync("yz_role_permission",Row("id",targetPermission,"yz_project_id",Project,"yz_role_id",role,"config_page_id",page,"permission_values","[\"is_query\"]"));}var optionsResponse=await Send(ordinary,HttpMethod.Post,route+"/cascadeTreeOptions25",new{levelKey="地区-二",selectedPath=new{countryId=parent,areaId=parent,placeId=parent}});Assert.Contains(optionsResponse.GetProperty("data").EnumerateArray(),node=>node.GetProperty("id").GetString()==parent);var path=await Send(ordinary,HttpMethod.Post,route+"/cascadeTreePath25",new{leafId=parent});Assert.Equal(3,path.GetProperty("data").GetProperty("path").GetArrayLength());
            // 同一持久会话中撤销目标查询，候选必须立即拒绝；来源已存关系和文件读取仍只按来源权限。
            await securityStore.UpdateAsync("yz_role_permission",targetPermissions[0],Row("permission_values","[]"));await Send(ordinary,HttpMethod.Post,route+"/cascadeTreeOptions25",new{levelKey="country",selectedPath=new{}},HttpStatusCode.Forbidden);await Send(ordinary,HttpMethod.Get,route+"/detail/"+record+"?parentId="+parent);await Download(ordinary,file,content);await securityStore.UpdateAsync("yz_role_permission",targetPermissions[0],Row("permission_values","[\"is_query\"]"));
            before=await Snapshot();await securityStore.UpdateAsync("yz_role_permission",permission,Row("permission_values","[]"));await Send(ordinary,HttpMethod.Get,route+"/detail/"+record+"?parentId="+parent,expected:HttpStatusCode.Forbidden);using(var denied=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+file))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(before,await Snapshot());await securityStore.UpdateAsync("yz_role_permission",permission,Row("permission_values","[\"is_query\"]"));
            stage="真实目标变化与稀疏回读";await Send(client,HttpMethod.Post,"/api/admin/auto/nr_cross_full_country/301/update",new{id=parent,name="实时新国"});var detail=(await Send(ordinary,HttpMethod.Get,route+"/detail/"+record+"?parentId="+parent)).GetProperty("data");Assert.Equal("实时新国",detail.GetProperty("countryName").GetString());await Sparse(record,2);await store.UpdateAsync("nr_cross_full_country",First,Row("status",0));using(var denied=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+file))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);await store.UpdateAsync("nr_cross_full_country",First,Row("status",1));await Download(ordinary,file,content);
            stage="软删与持久会话";await Delete(client,route+"?parentId="+parent,record);await References(file,1);await Delete(client,route+"?parentId="+parent,copy);await References(file,0);using(var denied=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+file))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
            await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(HostInfo(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);await Send(host.Client,HttpMethod.Get,sourceRoute+"/detail/"+selections[0]);await Send(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await Send(host.Client,HttpMethod.Get,sourceRoute+"/detail/"+selections[0],expected:HttpStatusCode.Unauthorized);Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);Assert.Equal(13,await CountTables(session));
            passed=true;await Receipt("receipt",new{run,suffix,type,parentType,passed,formalWeb=true,developmentAuthentication=false,fixedAuthorizationInjected=false,tableCount=13,ordinaryAccount=true,sourceAndTargetPermission=true,targetRevocation=true,scopedMenu=true,referenceReadAndRevocation=true,sparseStorage=true,partialAndMixedSelection=true,realFileUploadDownload=true,copyAndSoftDelete=true,transactionRollback=true,liveRename=true,persistentSession=true,browserExecuted=false,deviceExecuted=false,performanceExecuted=false,cleanupByFreshLease=true});
            /// <summary>观察真实物理槽，不将逻辑回显祖先误当作存储证明。</summary>
            async Task Sparse(string id,int depth,int second=-1){var row=Assert.Single(await store.ReadIncludingDeletedAsync("nr_cross_full_source",Row("id",long.Parse(id,CultureInfo.InvariantCulture))));foreach(var prefix in new[]{"","secondary_"})for(var index=0;index<3;index++){var value=row[prefix+new[]{"country_id","area_id","place_id"}[index]];var selected=index==(prefix.Length==0?depth:second);if(selected)Assert.Equal(parent,Convert.ToString(value,CultureInfo.InvariantCulture));else Assert.Null(value);}Assert.DoesNotContain((await new SchemaService(session).InspectAsync(catalog)).Tables.Single(table=>table.Name=="nr_cross_full_source").Columns,column=>column.Name.EndsWith("_name",StringComparison.Ordinal));}
            /// <summary>所有当前引用必须属于真实父页和当前项目，墓碑另参与事务快照。</summary>
            async Task References(string fileId,int count){var rows=await library.ReadAsync("yz_file_ref",Row("file_id",long.Parse(fileId,CultureInfo.InvariantCulture)));Assert.Equal(count,rows.Count);Assert.All(rows,row=>{Assert.Equal("202",SqlRowStore.Text(row,"page_id"));Assert.Equal(Project.ToString(CultureInfo.InvariantCulture),SqlRowStore.Text(row,"yz_project_id"));});}
            /// <summary>来源和全部引用／墓碑一起比较，防止只回滚表单而漏掉文件。</summary>
            async Task<string> Snapshot()=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{source=(await store.ReadIncludingDeletedAsync("nr_cross_full_source")).OrderBy(row=>SqlRowStore.Id(row)),references=(await library.ReadIncludingDeletedAsync("yz_file_ref")).OrderBy(row=>SqlRowStore.Id(row))}))));
        }
        catch(Exception error){primary=error;await Receipt("failure",new{run,suffix,type,parentType,stage,errorType=error.GetType().FullName,error.Message,error.StackTrace,passed=false,cleanupByFreshLease=true});throw;}
        finally{try{if(host is not null)await host.DisposeAsync();await TestDirectoryCleanup.DeleteAsync(directory,boundary);}catch(Exception cleanup)when(primary is not null){throw new AggregateException("正式跨表验收及本地清理同时失败，首因已保留。",primary,cleanup);}if(!passed)await Receipt("failed",new{run,suffix,passed=false,cleanupByFreshLease=true});}
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>递归核对真实包项目／后台与目标页，避免数字子串被误计为正确菜单。</summary>
    private static bool HasPage(JsonElement nodes,string page)=>nodes.EnumerateArray().Any(node=>node.GetProperty("projectId").GetString()==Project.ToString(CultureInfo.InvariantCulture)&&node.GetProperty("adminTypeId").GetString()=="5"&&(node.TryGetProperty("target",out var target)&&target.ValueKind==JsonValueKind.Object&&target.GetProperty("pageId").GetString()==page||HasPage(node.GetProperty("children"),page)));
    /// <summary>严格验证真实随机库／实例，任何DDL和初始化之前完成。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin){await using var connection=new MySqlConnection(session.Options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==session.Options.ExpectedDatabase&&reader.GetString(1)==pin.GetProperty("hostname").GetString()&&reader.GetString(2)==pin.GetProperty("serverUuid").GetString(),"跨表专属空库实例pin不符。");}
    /// <summary>只读实际表数，不把复用基础表计为首次安装。</summary>
    private static async Task<long> CountTables(DatabaseSession session){await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()";return Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);}
    /// <summary>包内元数据只从实际交付目录读取。</summary>
    private static async Task<MetadataCatalog> Catalog(string path)=>JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>沿原HTTP与R合同，错误不回显响应正文或凭据。</summary>
    private static Task<JsonElement> Send(HttpClient client,HttpMethod method,string path,object? body=null,HttpStatusCode expected=HttpStatusCode.OK)=>FreshWebHttp.SendAsync(client,method,path,body,expected);
    /// <summary>实际原上传计划、multipart及完成端点，不替换文件服务。</summary>
    private static async Task<string> Upload(HttpClient client,byte[] bytes){var route="/api/admin/sys/file-library";var plan=(await Send(client,HttpMethod.Post,route+"/upload-plan",new{originalName="cross.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId="9001"})).GetProperty("data");var id=plan.GetProperty("fileId").GetString()!;using var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(bytes),"file","cross.txt");await Send(client,HttpMethod.Post,route+"/local-upload/"+id,content);await Send(client,HttpMethod.Post,route+"/complete",new{fileId=id});return id;}
    /// <summary>保护文件必须实际返回完全一致的文件流。</summary>
    private static async Task Download(HttpClient client,string file,byte[] bytes){using var response=await client.GetAsync("/api/admin/sys/file-library/protected-content/"+file);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());}
    /// <summary>删除沿原DELETE正文和父上下文。</summary>
    private static async Task Delete(HttpClient client,string route,string id)=>await Send(client,HttpMethod.Delete,route,new{id});
    /// <summary>只构造本轮种子字段，数据库写入仍使用元数据SqlRowStore。</summary>
    private static Dictionary<string,object?> Row(params object?[] fields){var row=new Dictionary<string,object?>();for(var index=0;index<fields.Length;index+=2)row.Add((string)fields[index]!,fields[index+1]);return row;}
}
