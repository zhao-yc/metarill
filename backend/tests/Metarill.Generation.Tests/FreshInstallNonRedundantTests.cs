using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Metarill.Runtime.Exports;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class FreshInstallMySqlTests
{
    /// <summary>正式完整包从零安装，再用持久Web身份和真实原路由验证非冗余关系，不注入固定授权器。</summary>
    [FreshInstallMySqlFact]
    public async Task Nonredundant_full_package_installs_and_executes_original_routes_with_persistent_web_authorization()
    {
        var options=FreshInstallConfiguration.Read("NonRedundantDatabase","nonred");using var session=new DatabaseSession(options);Assert.Equal(0L,await CountTablesAsync(session));
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));var run=settings.RootElement.GetProperty("RunId").GetString()!;var pin=settings.RootElement.GetProperty("InstancePin");
        await using(var connection=new MySqlConnection(options.ConnectionString))
        {await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.GetProperty("hostname").GetString()&&reader.GetString(2)==pin.GetProperty("serverUuid").GetString(),"专属空库实例pin不符。");}
        var catalog=NonRedundantApplicationFixture.Catalog("mt_nrp_"+run,true);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-fresh");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var outputDirectory=Path.Combine(directory,"application");var evidence=Path.Combine(root,".local/nonredundant-application/fresh-"+run);Directory.CreateDirectory(evidence);
        ExactDataManageMySqlProbe.Configure(catalog);DerivedExactDataManageMySqlProbe.Configure(catalog);
        var password=Guid.NewGuid().ToString("N");var username="nonred-owner";FreshHost? host=null;var passed=false;Exception? primaryFailure=null;
        try
        {
            new ApplicationPackager().Create(catalog,outputDirectory,"Example.NonRedFresh",root);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(outputDirectory,operation);
            var hostDirectory=Path.Combine(outputDirectory,"backend/Application.Host");
            /// <summary>正式Web配置仅经私有进程环境提供，默认Production且不启用开发认证。</summary>
            ProcessStartInfo HostInfo(string? operation=null)
            {
                var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);info.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Debug/net10.0/Application.Host.dll"));if(operation is not null)info.ArgumentList.Add(operation);
                info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
                info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=username;info.Environment["Authentication__Bootstrap__Password"]=password;
                info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;info.Environment["Ids__WorkerId"]="934";info.Environment["Files__RootDirectory"]=Path.Combine(directory,"files");info.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");info.Environment["Exports__MaxRows"]="20";return info;
            }
            // 普通Production启动和显式预览都不能建表；只有包内schema apply安装业务3+文件3+安全6表。
            host=await FreshHost.StartAsync(HostInfo(),"/health");Assert.Equal(0L,await CountTablesAsync(session));await host.DisposeAsync();host=null;
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-preview"));Assert.Equal(0L,await CountTablesAsync(session));
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));Assert.Equal(12L,await CountTablesAsync(session));Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));Assert.Equal(12L,await CountTablesAsync(session));
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));
            var security=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(outputDirectory,"backend/metadata/security.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;var securityStore=new SqlRowStore(session,security);var owner=Assert.Single(await securityStore.ReadAsync("yz_manager",new Dictionary<string,object?>{{"name",username}}));var passwordHash=SqlRowStore.Text(owner,"password");Assert.True(PasswordHasher.Verify(password,passwordHash));
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));Assert.Equal(passwordHash,SqlRowStore.Text(Assert.Single(await securityStore.ReadAsync("yz_manager",new Dictionary<string,object?>{{"name",username}})),"password"));
            var store=new SqlRowStore(session,catalog);var order=catalog.ConfigTables.Single(table=>table.Id=="1").TableName;var party=catalog.ConfigTables.Single(table=>table.Id=="2").TableName;var group=catalog.ConfigTables.Single(table=>table.Id=="3").TableName;
            await session.InTransactionAsync(async()=>
            {
                await store.InsertAsync(group,Row("id",71L,"yz_project_id",54L,"name","父甲"));await store.InsertAsync(group,Row("id",72L,"yz_project_id",54L,"name","父乙"));await store.InsertAsync(group,Row("id",73L,"yz_project_id",55L,"name","外项目父"));
                await store.InsertAsync(party,Row("id",101L,"yz_project_id",54L,"group_id",71L,"name","A甲","notes","A注释","amount",10m));await store.InsertAsync(party,Row("id",102L,"yz_project_id",54L,"group_id",71L,"name","B乙","notes","B注释","amount",20m));await store.InsertAsync(party,Row("id",105L,"yz_project_id",54L,"group_id",72L,"name","其它父候选","notes","乙组","amount",30m));
                await store.InsertAsync(order,Row("id",9007199254741001L,"yz_project_id",54L,"group_id",71L,"customer_id",101L,"name","包内记录甲","status",2));await store.InsertAsync(order,Row("id",9007199254741002L,"yz_project_id",54L,"group_id",71L,"customer_id",102L,"name","包内记录乙","status",1));await store.InsertAsync(order,Row("id",9007199254741003L,"yz_project_id",55L,"group_id",71L,"customer_id",101L,"name","外项目记录","status",2));return true;
            });
            host=await FreshHost.StartAsync(HostInfo(),"/health");var client=host.Client;var source="/api/admin/auto/"+order+"/101";var mapped="/api/admin/auto/"+order+"/104";var overwrite="/api/admin/auto/"+order+"/105";
            await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/page",new PageRequest{ParentId="71"},HttpStatusCode.Unauthorized);var token=await FreshWebHttp.LoginAsync(client,username,password,form:true);
            var menu=await FreshWebHttp.SendAsync(client,HttpMethod.Get,"/api/admin/sys/yz_manager/getMenu");Assert.True(HasPage(menu.GetProperty("menu"),"101","54","5"));
            var filter=new Dictionary<string,JsonElement>{{"customerName",JsonSerializer.SerializeToElement("B")}};var page=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/page",new PageRequest{ParentId="71",PageSize=1,Filters=filter});Assert.Equal("1",page.GetProperty("data").GetProperty("total").ToString());Assert.Equal("9007199254741002",Assert.Single(page.GetProperty("data").GetProperty("records").EnumerateArray()).GetProperty("id").GetString());Assert.Equal(20m,page.GetProperty("map").GetProperty("statistics").GetProperty("customerAmount").GetProperty("total").GetDecimal());
            var added=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/add?parentId=71",new{name="完整包新增",customerId="101"});var id=added.GetProperty("data").GetProperty("id").GetString()!;Assert.Equal("A注释",added.GetProperty("data").GetProperty("customerNotes").GetString());
            var updated=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/update?parentId=71",new{id,customerId="102"});Assert.Equal("B注释",updated.GetProperty("data").GetProperty("customerNotes").GetString());
            await FreshWebHttp.SendAsync(client,HttpMethod.Post,mapped+"/importDatas104",new{parentId="73",requestJson=new[]{Row("customer_id","其它父候选","记录名","完整包跨父","group_id","父乙")}});
            await FreshWebHttp.SendAsync(client,HttpMethod.Post,overwrite+"/importDatas105",new{requestJson=new[]{Row("记录名","完整包跨父","customer_id","其它父候选","status",1)}});
            var imported=Assert.Single(await store.ReadAsync(order,new Dictionary<string,object?>{{"name","完整包跨父"}}));Assert.Equal(72L,Convert.ToInt64(imported["group_id"],CultureInfo.InvariantCulture));Assert.Equal(105L,Convert.ToInt64(imported["customer_id"],CultureInfo.InvariantCulture));Assert.False(imported.ContainsKey("customer_notes"));
            var before=await HashRows();var error=await FreshWebHttp.SendAsync(client,HttpMethod.Post,overwrite+"/importDatas105",new{requestJson=new[]{Row("记录名","完整包跨父","status",2),Row("记录名","错误行","customer_id","A甲","group_id",72)}},HttpStatusCode.BadRequest);Assert.Contains("第2行",error.GetProperty("msg").GetString());Assert.Equal(before,await HashRows());
            var grant=(await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/initExport",new PageRequest{ParentId="71",Filters=filter})).GetProperty("data").GetString();
            using(var response=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+grant)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));Assert.Contains(book.Worksheet(1).RowsUsed().SelectMany(row=>row.CellsUsed()),cell=>cell.GetString()=="B注释");}
            var selection=await FreshWebHttp.SendAsync(client,HttpMethod.Post,"/api/admin/auto/"+order+"/102/selection/selected",new{parentId="71",selectedIds=new[]{id,"9007199254741002"}});Assert.Equal(new[]{id,"9007199254741002"},selection.GetProperty("data").EnumerateArray().Select(row=>row.GetProperty("id").GetString()));
            var files=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(outputDirectory,"backend/metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var protectedFile=await NonRedundantFilesMySqlProbe.VerifyAsync(client,session,catalog,files);
            // 普通账号只授来源查询，列表及真实选择查询继承，新增、导入和导出按各自原动作拒绝。
            var ids=new SnowflakeIdGenerator(933);var role=ids.NextId();var manager=ids.NextId();var permission=ids.NextId();var readerName="nonred_reader_"+run;var readerPassword=Guid.NewGuid().ToString("N");
            await session.InTransactionAsync(async()=>{await securityStore.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",54L}});await securityStore.InsertAsync("yz_role",Row("id",role,"yz_project_id",54L,"yz_admin_type_id",5L,"name",readerName,"is_super",0,"is_built_in",0));await securityStore.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",54L,"yz_admin_type_id",5L,"name",readerName,"password",PasswordHasher.Hash(readerPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{role.ToString(CultureInfo.InvariantCulture)})));await securityStore.InsertAsync("yz_role_permission",Row("id",permission,"yz_project_id",54L,"yz_role_id",role,"config_page_id",101L,"permission_values","[\"is_query\"]"));return true;});
            using var ordinary=new HttpClient{BaseAddress=client.BaseAddress};await FreshWebHttp.LoginAsync(ordinary,readerName,readerPassword);await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,source+"/page",new PageRequest{ParentId="71",Filters=filter});
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Get,source+"/detail/"+id+"?parentId=71");
            var unboundOwnedFile=await NonRedundantFileStateMySqlProbe.VerifyAsync(ordinary,client,session,catalog,files,securityStore,permission,protectedFile);
            await NonRedundantFileManagementMySqlProbe.VerifyAsync(ordinary,client,session,catalog,files,protectedFile,unboundOwnedFile);
            await ExactDataManageMySqlProbe.VerifyAsync(ordinary,client,session,catalog,files,securityStore,role,manager,permission,protectedFile);
            await DerivedExactDataManageMySqlProbe.VerifyAsync(ordinary,client,session,catalog,files,securityStore,role,manager,protectedFile);
            using(var file=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+protectedFile))Assert.Equal(HttpStatusCode.OK,file.StatusCode);
            var library=new SqlRowStore(session,files);var reference=Assert.Single(await library.ReadAsync("yz_file_ref",Row("file_id",protectedFile)));var fileRecord=SqlRowStore.Id(reference,"record_id");var storedFileRecord=Assert.Single(await store.ReadAsync(order,Row("id",fileRecord)));
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,"/api/admin/sys/file-library/resolve",new{fileIds=new[]{protectedFile.ToString(CultureInfo.InvariantCulture)},pageId="101",recordId=fileRecord.ToString(CultureInfo.InvariantCulture)});
            // 构造仅在本轮独占新库内的旧坏引用：真实字段、父范围或软删任一失配均不能凭引用行放行。
            foreach(var bad in new[]{Row("attachments",null),Row("group_id",73L),Row("delete_no",DateTime.UtcNow)})
            {
                await store.UpdateAsync(order,fileRecord,bad);
                using(var denied=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+protectedFile))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
                await store.UpdateAsync(order,fileRecord,bad.Keys.ToDictionary(key=>key,key=>storedFileRecord[key],StringComparer.Ordinal));
            }
            using(var restored=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+protectedFile))Assert.Equal(HttpStatusCode.OK,restored.StatusCode);
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,source+"/add?parentId=71",new{name="无权新增"},HttpStatusCode.Forbidden);
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,source+"/importDatas101",new{parentId="71",requestJson=Array.Empty<object>()},HttpStatusCode.Forbidden);
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,source+"/initExport",new PageRequest{ParentId="71"},HttpStatusCode.Forbidden);
            // 原资源模式允许真实选择引用继承查询，仍限定父71；不能把旧缺失继承的403当作合同。
            var targetPage=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,"/api/admin/auto/"+party+"/201/page",new PageRequest{ParentId="71"})).GetProperty("data");
            Assert.Equal("2",targetPage.GetProperty("total").ToString());Assert.Equal(new[]{"101","102"},targetPage.GetProperty("records").EnumerateArray().Select(row=>row.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
            var model=GenerationModel.Create(catalog,"Example.NonRedFresh");var sourceTable=model.Tables.Single(table=>table.Source.Id=="1");var relation=RelationCompilation.ForPage(model,sourceTable.Pages.Single(page=>page.Source.Id=="101")).Single(relation=>relation.SourceColumn.Source.Id=="15");
            // 原查询候选需要来源与目标查询；目标查询现在由真实选择引用继承，仍核验实际父范围。
            var candidates=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,source+"/"+relation.LegacyEndpoint+"/page",new{parentId="71",pageNo=1,pageSize=20})).GetProperty("data");
            Assert.Equal("2",candidates.GetProperty("total").ToString());Assert.Equal(new[]{"101","102"},candidates.GetProperty("records").EnumerateArray().Select(row=>row.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));
            before=await HashRows();await securityStore.UpdateAsync("yz_role_permission",permission,Row("permission_values","[]"));await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,source+"/page",new PageRequest{ParentId="71"},HttpStatusCode.Forbidden);Assert.Equal(before,await HashRows());using(var file=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+protectedFile))Assert.Equal(HttpStatusCode.Unauthorized,file.StatusCode);await securityStore.UpdateAsync("yz_role_permission",permission,Row("permission_values","[\"is_query\"]"));
            await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(HostInfo(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,source+"/detail/"+id+"?parentId=71");await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});await FreshWebHttp.SendAsync(host.Client,HttpMethod.Get,source+"/detail/"+id+"?parentId=71",expected:HttpStatusCode.Unauthorized);
            Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);passed=true;
            await File.WriteAllTextAsync(Path.Combine(evidence,"receipt.json"),JsonSerializer.Serialize(new{passed,formalWeb=true,developmentAuthentication=false,fixedAuthorizationInjected=false,tableCount=12,originalMenus=true,scalarRelations=true,crossParentImport=true,batchRollback=true,xlsxDownload=true,persistentSessionRestart=true,queryOnlyAndRevocation=true,nonredundantPhysicalFiles=true,fileReferenceTransactions=true,protectedFileReadAndRevocation=true,exactDataManage=true,inheritedFileSources=true,derivedExactDataManage=true,exactNumericRepresentation=true,browserExecuted=false,deviceExecuted=false,performanceExecuted=false,cleanupByFreshLease=true},new JsonSerializerOptions{WriteIndented=true})+"\n");
            output.WriteLine("完整非冗余包：0表启动/预览、正式安装12表、正式Web/菜单/查询权限与撤权、跨父导入回滚、XLSX及重启会话通过；非浏览器、真机或性能验收。");
            /// <summary>仅对本轮三张业务表做整行摘要，失败回滚不以响应或日志推定。</summary>
            async Task<string> HashRows(){var rows=new List<object>();foreach(var table in catalog.ConfigTables)rows.Add((await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(row=>SqlRowStore.Id(row)).ToArray());return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));}
        }
        catch(Exception error)
        {
            primaryFailure=error;
            await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{type=error.GetType().FullName,error.Message,error.StackTrace,passed=false,cleanupByFreshLease=true},new JsonSerializerOptions{WriteIndented=true})+"\n");
            throw;
        }
        finally
        {
            try{if(host is not null)await host.DisposeAsync();await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
            catch(Exception cleanupError)when(primaryFailure is not null){throw new AggregateException("新增完整包用例和本地清理同时失败，首因已保留。",primaryFailure,cleanupError);}
            if(!passed)await File.WriteAllTextAsync(Path.Combine(evidence,"failed.json"),"{\"passed\":false,\"cleanupByFreshLease\":true}\n");
        }
    }
    /// <summary>测试种子和Excel行均保留明确键及显式空值，只写专属随机空库。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<values.Length;index+=2)row.Add((string)values[index]!,values[index+1]);return row;}
}
