using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Security;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>没有专属设置只跳过；设置存在仍须在任何连接前核对真实批准租约和固定包。</summary>
public sealed class CrossTableExcelNativeFactAttribute:FactAttribute
{
    public CrossTableExcelNativeFactAttribute(){if(!File.Exists(Environment.GetEnvironmentVariable("METARILL_CROSS_EXCEL_NATIVE_SETTINGS")))Skip="缺专属批准租约，跨表Excel正式SQL／身份／事务验收未执行。";}
}

/// <summary>八个普通物理目标组合共用一个独占新库；运行生产Web宿主，不注入认证／SQL替身。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class CrossTableExcelNativeMySqlTests
{
    internal const int WorkerId=940;
    private const long Project=9007199254740993,First=9007199254745001;
    /// <summary>真正路径持久化及原XLSX回读，所有副作用仅限已批准租约，清库由外部协调器执行。</summary>
    [CrossTableExcelNativeFact]
    public async Task Fixed_eight_model_application_roundtrips_paths_and_rolls_back_real_batch_and_files()
    {
        var settingsPath=Environment.GetEnvironmentVariable("METARILL_CROSS_EXCEL_NATIVE_SETTINGS")!;
        Assert.Equal(Path.GetFullPath(settingsPath),Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));var settings=document.RootElement;
        var source=NonRedundantApplicationFixture.Root();var run=CrossTableExcelNativeLease.Validate(settings,CSharpGenerator.SafePath(source,".local/cross-table-excel-native/prepared"));
        var proof=settings.GetProperty("CrossTableExcelPreparation").Deserialize<CrossTableExcelPackageProof>()!;
        var catalog=CrossTableExcelNativePreparation.Verify(proof);var options=FreshInstallConfiguration.Read("AgentDatabase","agent");
        var copied=CrossTableExcelNativePreparation.CopyForExecution(proof,CSharpGenerator.SafePath(source,".local/cross-table-excel-native/"+run+"/execution/application"));
        var evidence=CSharpGenerator.SafePath(source,".local/cross-table-excel-native/"+run+"/evidence");Directory.CreateDirectory(evidence);
        var storageBoundary=Path.Combine(evidence,"storage");var storage=Path.Combine(storageBoundary,Guid.NewGuid().ToString("N"));var hooks=Path.Combine(evidence,"real-transaction-hooks.jsonl");
        var hostDirectory=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host");
        using var session=new DatabaseSession(options);await Pin(session,settings.GetProperty("InstancePin"));
        var store=new SqlRowStore(session,catalog);var securityCatalog=await Model("security");var fileCatalog=await Model("files");var security=new SqlRowStore(session,securityCatalog);var files=new SqlRowStore(session,fileCatalog);
        var physical=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(catalog,securityCatalog),fileCatalog),AgentFoundationCatalog.ForApplication(catalog));
        var owner="cross-excel-owner";var password=Guid.NewGuid().ToString("N");var readerPassword=Guid.NewGuid().ToString("N");
        FreshHost? host=null;HttpClient? client=null;var stage="零表检查";var passed=false;var completed=new List<string>();var permissions=new Dictionary<string,long>(StringComparer.Ordinal);
        ProcessStartInfo Info(string? operation=null)
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);
            info.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));if(operation is not null)info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";info.Environment["DOTNET_ENVIRONMENT"]="Production";info.Environment["Logging__LogLevel__Default"]="Warning";
            info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;info.Environment["Database__ConnectionString"]=options.ConnectionString;
            info.Environment["Authentication__Mode"]="Web";info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";info.Environment["Authentication__Bootstrap__Username"]=owner;info.Environment["Authentication__Bootstrap__Password"]=password;
            info.Environment["Ids__WorkerId"]=WorkerId.ToString(CultureInfo.InvariantCulture);info.Environment["Agent__VerificationUri"]="https://agent.example/#/pages/custom/agent_authorize";
            info.Environment["Files__RootDirectory"]=Path.Combine(storage,"uploads");info.Environment["Files__LegacyPublicBaseUrl"]="";info.Environment["Files__PublicContentProjectId"]=catalog.YzProjectId;
            info.Environment["Exports__RootDirectory"]=Path.Combine(storage,"exports");info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;info.Environment["METARILL_CROSS_EXCEL_HOOK_RECEIPT"]=hooks;return info;
        }
        try
        {
            Assert.Equal(0,await TableCount(session));host=await FreshHost.StartAsync(Info(),"/health");Assert.Equal(0,await TableCount(session));await host.DisposeAsync();host=null;
            stage="显式预览／重复安装";await Run(Info("--schema-preview"),"schema-preview");Assert.Equal(0,await TableCount(session));
            await Run(Info("--schema-apply"),"schema-first");await Run(Info("--schema-apply"),"schema-repeat");Assert.Equal(FreshInstallConfiguration.ExpectedFullTableCount(catalog),await TableCount(session));Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes);
            stage="真实账号／角色";await Run(Info("--initialize-security"),"initialize-first");await Run(Info("--initialize-security"),"initialize-repeat");host=await FreshHost.StartAsync(Info(),"/health");
            await FreshWebHttp.LoginAsync(host.Client,owner,password,true);
            var ids=new SnowflakeIdGenerator(WorkerId+1);var role=ids.NextId();var manager=ids.NextId();
            await session.InTransactionAsync(async()=>
            {
                await security.ReadForUpdateAsync("yz_project",Row("id",Project));
                await security.InsertAsync("yz_role",Row("id",role,"yz_project_id",Project,"yz_admin_type_id",5L,"name","cross-excel-reader","is_super",0,"is_built_in",0));
                await security.InsertAsync("yz_manager",Row("id",manager,"yz_project_id",Project,"yz_admin_type_id",5L,"name","cross-excel-reader","password",PasswordHasher.Hash(readerPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{Id(role)})));
                foreach(var page in catalog.ConfigPages){var id=ids.NextId();permissions.Add(page.Id,id);await security.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",Project,"yz_role_id",role,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_all\"]"));}
                return true;
            });
            client=new(){BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(60)};var token=await FreshWebHttp.LoginAsync(client,"cross-excel-reader",readerPassword);
            Assert.False((await FreshWebHttp.SendAsync(client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            foreach(var table in catalog.ConfigTables.Where(table=>table.TableName.EndsWith("_source",StringComparison.Ordinal)))
            {
                var page=catalog.ConfigPages.Single(page=>page.ConfigTableId==table.Id);var prefix=table.TableName[..^7];var route="/api/admin/auto/"+table.TableName+"/"+page.Id+"/";
                stage=prefix+"种子与普通身份";await Seed(prefix);
                using(var anonymous=new HttpClient{BaseAddress=client.BaseAddress})await FreshWebHttp.SendAsync(anonymous,HttpMethod.Post,route+"importDatas"+page.Id,new{requestJson=Array.Empty<object>()},HttpStatusCode.Unauthorized);
                async Task<JsonElement> Import(Dictionary<string,object?>[] rows,HttpStatusCode expected=HttpStatusCode.OK)=>await FreshWebHttp.SendAsync(client,HttpMethod.Post,route+"importDatas"+page.Id,new{requestJson=rows},expected);
                async Task<Dictionary<string,object?>> Record(string name)
                {
                    using var independent=new DatabaseSession(options);
                    return Assert.Single(await new SqlRowStore(independent,catalog).ReadAsync(table.TableName,Row("name",name)));
                }
                async Task Slots(string name,int depth,long[] selected)
                {
                    var record=await Record(name);var names=new[]{"country_id","area_id","place_id"};
                    for(var index=0;index<3;index++)
                    {
                        var actual=record[names[index]];var stored=index<=depth&&(table.IsRongyu==1||index==depth);
                        if(stored)Assert.Equal(Id(selected[index]),Convert.ToString(actual,CultureInfo.InvariantCulture));
                        else Assert.True(actual is null||actual is string{Length:0},"非选中物理位必须为空，不能用逻辑VO代替实际存储。");
                    }
                    if(table.IsRongyu==0)Assert.DoesNotContain(record.Keys,name=>name.EndsWith("_name",StringComparison.Ordinal));
                }
                var pathIds=new[]{First,First+1,First+2};
                stage=prefix+"完整／根／中层／唯一短写";
                foreach(var item in new[]{("完整","国甲/区甲/城甲",2),("根","国甲//",0),("中层","国甲/区甲/",1),("旧短写","城甲",2),("数字名称","00123//",0)})
                {await Import([Input(item.Item1,item.Item2)]);await Slots(item.Item1,item.Item3,item.Item1=="数字名称"?new[]{First+20}:pathIds);}
                // 显式祖先与路径先后次序均保持；导入不会偷偷扩大到隐式字段名。
                await Import([Row("祖先ID",Id(First),"级联","国甲/区甲/城甲","名称","逆序","金额",5)]);
                await Slots("逆序",2,pathIds);var before=await Snapshot();
                foreach(var invalid in new[]{Input("错误层","国甲/城甲/"),Input("不存在","不存在/错误/路径"),Input("跨层重名","同名"),Input("隐藏目标","国甲/区甲/隐藏城"),Input("外项目目标","国甲/区甲/外项目城"),Row("名称","冲突","级联","国甲/区甲/城甲","祖先ID",Id(First+10)),Row("名称","隐藏列","countryId",Id(First))})
                {await Import([Input("无效批首行","国甲//"),invalid],HttpStatusCode.BadRequest);Assert.Equal(before,await Snapshot());}
                stage=prefix+"同父重名";
                await store.InsertAsync(prefix+"_place",Row("id",First+50,"name","城甲","yz_project_id",Project,"status",1,prefix+"_area_id",Parent(prefix,First+1)));
                var ambiguous=await Snapshot();await Import([Input("同父重名","国甲/区甲/城甲")],HttpStatusCode.BadRequest);Assert.Equal(ambiguous,await Snapshot());
                await store.UpdateAsync(prefix+"_place",First+50,Row("delete_no",new DateTime(2026,1,1)));
                stage=prefix+"省略与明确清空";
                await Import([Row("名称","完整","金额",42)]);await Slots("完整",2,pathIds);
                await Import([Input("完整","")]);await Slots("完整",-1,[]);
                await Import([Input("完整","国甲/区甲/城甲")]);await Slots("完整",2,pathIds);
                stage=prefix+"实际XLSX及原选择方法回读";
                var expected=new Dictionary<string,string>{{"完整","国甲/区甲/城甲"},{"根","国甲//"},{"中层","国甲/区甲/"},{"旧短写","国甲/区甲/城甲"},{"数字名称","00123//"},{"逆序","国甲/区甲/城甲"}};
                var selectedIds=new List<string>();foreach(var name in expected.Keys)selectedIds.Add(Id(SqlRowStore.Id(await Record(name))));
                var grant=(await FreshWebHttp.SendAsync(client,HttpMethod.Post,route+"initExport",new{selectedIds,pageNo=99,pageSize=1})).GetProperty("data").GetString()!;
                var exported=new Dictionary<string,string>(StringComparer.Ordinal);
                using(var response=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+grant))
                {
                    Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(ExcelPageExportService.ExcelContentType,response.Content.Headers.ContentType?.MediaType);
                    var bytes=await response.Content.ReadAsByteArrayAsync();await File.WriteAllBytesAsync(Path.Combine(evidence,prefix+"-export.xlsx"),bytes);
                    using var workbook=new XLWorkbook(new MemoryStream(bytes));var sheet=workbook.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);
                    Assert.Equal(expected.Count,sheet.RowsUsed().Count()-1);
                    foreach(var row in sheet.RowsUsed().Skip(1))
                    {var cell=row.Cell(headers["级联"]);Assert.Equal(XLDataType.Text,cell.DataType);Assert.False(cell.HasFormula);var name=row.Cell(headers["名称"]).GetString();Assert.Equal(expected[name],cell.GetString());exported.Add(name,cell.GetString());}
                }
                var importBook=Path.Combine(evidence,prefix+"-roundtrip.xlsx");var roundtrip=exported.OrderBy(item=>item.Key,StringComparer.Ordinal).Select(item=>Input(item.Key+"往返",item.Value)).ToArray();
                using(var workbook=new XLWorkbook())
                {var sheet=workbook.AddWorksheet("原导入列");var headers=new[]{"名称","级联","金额"};for(var column=0;column<headers.Length;column++)sheet.Cell(1,column+1).Value=headers[column];for(var row=0;row<roundtrip.Length;row++)for(var column=0;column<headers.Length;column++)sheet.Cell(row+2,column+1).Value=XLCellValue.FromObject(roundtrip[row][headers[column]]);workbook.SaveAs(importBook);}
                var expectedPath=Path.Combine(evidence,prefix+"-roundtrip-expected.json");var receivedPath=Path.Combine(evidence,prefix+"-roundtrip-received.json");
                await File.WriteAllTextAsync(expectedPath,JsonSerializer.Serialize(roundtrip));var node=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node",source);
                foreach(var argument in new[]{Path.Combine(source,"backend/tests/Metarill.Generation.Tests/OriginalExcelImportProbe.mjs"),source,importBook,expectedPath,receivedPath})node.ArgumentList.Add(argument);
                await NonRedundantApplicationFixture.RunAsync(node);
                using(var received=JsonDocument.Parse(await File.ReadAllTextAsync(receivedPath)))await FreshWebHttp.SendAsync(client,HttpMethod.Post,route+"importDatas"+page.Id,new{requestJson=received.RootElement});
                foreach(var name in expected.Keys)await Slots(name+"往返",name=="根"||name=="数字名称"?0:name=="中层"?1:2,name=="数字名称"?new[]{First+20}:pathIds);
                stage=prefix+"真实文件及已写入后整批回滚";
                var fileColumn=catalog.ConfigColumns.Single(column=>column.ConfigTableId==table.Id&&column.ControlKind=="FileLibrary");
                var uploadBytes=Encoding.UTF8.GetBytes("cross-excel-"+prefix);var fileId=await Upload(fileColumn.Id,uploadBytes);
                var firstRow=Input("回滚首行","国甲/区甲/城甲");firstRow["附件"]=JsonSerializer.Serialize(new[]{fileId});before=await Snapshot();
                await Import([firstRow,Input("后置拒绝","国甲/区甲/城甲")],HttpStatusCode.InternalServerError);
                Assert.Equal(before,await Snapshot());var observed=File.ReadAllLines(hooks).Select(line=>JsonSerializer.Deserialize<JsonElement>(line)).Where(value=>value.GetProperty("table").GetString()==table.TableName).ToArray();
                var observation=Assert.Single(observed);Assert.Equal(1,observation.GetProperty("references").GetInt64());Assert.True(observation.GetProperty("insideTransaction").GetBoolean());Assert.DoesNotContain(await store.ReadAsync(table.TableName),record=>SqlRowStore.Text(record,"name")=="回滚首行");
                firstRow["名称"]="文件成功";await Import([firstRow]);await Slots("文件成功",2,pathIds);var fileRecord=await Record("文件成功");
                Assert.Single(await files.ReadAsync("yz_file_ref",Row("file_id",long.Parse(fileId,CultureInfo.InvariantCulture),"record_id",SqlRowStore.Id(fileRecord))));
                using(var download=await client.GetAsync("/api/admin/sys/file-library/protected-content/"+fileId)){Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal(uploadBytes,await download.Content.ReadAsByteArrayAsync());}
                stage=prefix+"撤权且不写入";before=await Snapshot();await Grant(page.Id,"[\"is_query\"]");await Import([Input("权限拒绝","国甲//")],HttpStatusCode.Unauthorized);Assert.Equal(before,await Snapshot());await Grant(page.Id,"[\"is_all\"]");
                completed.Add(prefix);await Receipt(prefix,new{run,passed=true,storage=table.IsRongyu,formalWeb=true,ordinaryAccount=true,fullPartialLegacy=true,originalXlsxRoundtrip=true,realTransactionHook=true,fileReferenceRollback=true,permissionRevoked=true,browserExecuted=false,performanceMeasured=false});
            }
            stage="重启保持正式会话";await host.DisposeAsync();host=null;host=await FreshHost.StartAsync(Info(),"/health");host.Client.DefaultRequestHeaders.Add("loginStateId",token);
            var firstTable=catalog.ConfigTables.First(table=>table.TableName.EndsWith("_source",StringComparison.Ordinal));var firstPage=catalog.ConfigPages.Single(page=>page.ConfigTableId==firstTable.Id);
            await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/auto/"+firstTable.TableName+"/"+firstPage.Id+"/page",new PageRequest());
            await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});
            await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,"/api/admin/auto/"+firstTable.TableName+"/"+firstPage.Id+"/page",new PageRequest(),HttpStatusCode.Unauthorized);
            await host.DisposeAsync();host=null;CrossTableExcelNativePreparation.Verify(copied,true);CrossTableExcelNativePreparation.Verify(proof);Assert.Equal(8,completed.Count);passed=true;
            await Receipt("verified",new{run,passed,completed,productionWeb=true,preparedNotPublishedFull=true,tableCount=await TableCount(session),browserExecuted=false,performanceMeasured=false,cleanupByLease=true});
        }
        catch(Exception error){await Receipt("failure",new{run,stage,completed,passed=false,errorType=error.GetType().Name,cleanupByLease=true});throw;}
        finally
        {
            client?.Dispose();var stopped=host is null;try{if(host is not null){await host.DisposeAsync();stopped=true;}if(stopped&&Directory.Exists(storage))await TestDirectoryCleanup.DeleteAsync(storage,storageBoundary);if(!stopped)throw new InvalidOperationException("Excel自有宿主未确认停止，保留存储现场。");await Receipt("local-cleanup",new{run,stopped,storageRemoved=!Directory.Exists(storage),databaseCleanupByLease=true});}
            catch(Exception error){await Receipt("local-cleanup-failure",new{run,errorType=error.GetType().Name});if(passed)throw;}
        }
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        async Task<MetadataCatalog> Model(string name)=>JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(CSharpGenerator.SafePath(copied.PackageRoot,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Task Run(ProcessStartInfo info,string name)=>RunOwned(info,evidence,name,options.ConnectionString,password,readerPassword);
        Task Grant(string page,string values)=>security.UpdateAsync("yz_role_permission",permissions[page],Row("permission_values",values));
        object Parent(string prefix,long id)=>catalog.ConfigColumns.Single(column=>column.ColumnName==prefix+"_country_id").LogicalType=="string"?Id(id):id;
        async Task Seed(string prefix)
        {
            async Task Target(string level,long id,string name,string? parentField=null,long? parent=null,int status=1,long project=Project)
            {var values=Row("id",id,"name",name,"yz_project_id",project,"status",status);if(parentField is not null)values[parentField]=Parent(prefix,parent!.Value);await store.InsertAsync(prefix+"_"+level,values);}
            await Target("country",First,"国甲");await Target("country",First+10,"国乙");await Target("country",First+20,"00123");await Target("country",First+30,"同名");
            await Target("area",First+1,"区甲",prefix+"_country_id",First);await Target("area",First+11,"区乙",prefix+"_country_id",First+10);await Target("area",First+31,"同名",prefix+"_country_id",First);
            await Target("place",First+2,"城甲",prefix+"_area_id",First+1);await Target("place",First+12,"城乙",prefix+"_area_id",First+11);
            await Target("place",First+40,"隐藏城",prefix+"_area_id",First+1,status:0);await Target("place",First+41,"外项目城",prefix+"_area_id",First+1,project:Project+1);
        }
        async Task<string> Snapshot()
        {
            using var independent=new DatabaseSession(options);var business=new SqlRowStore(independent,catalog);var fileStore=new SqlRowStore(independent,fileCatalog);var rows=new Dictionary<string,object>(StringComparer.Ordinal);
            foreach(var table in catalog.ConfigTables.OrderBy(table=>table.TableName,StringComparer.Ordinal))rows[table.TableName]=(await business.ReadIncludingDeletedAsync(table.TableName)).OrderBy(row=>SqlRowStore.Id(row)).ToArray();
            foreach(var table in fileCatalog.ConfigTables.OrderBy(table=>table.TableName,StringComparer.Ordinal))rows[table.TableName]=(await fileStore.ReadIncludingDeletedAsync(table.TableName)).OrderBy(row=>SqlRowStore.Id(row)).ToArray();
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));
        }
        async Task<string> Upload(string column,byte[] bytes)
        {
            const string route="/api/admin/sys/file-library/";var fileId=(await FreshWebHttp.SendAsync(client!,HttpMethod.Post,route+"upload-plan",new{originalName="cross-excel.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId=column})).GetProperty("data").GetProperty("fileId").GetString()!;
            using var content=new MultipartFormDataContent();content.Add(new ByteArrayContent(bytes),"file","cross-excel.txt");await FreshWebHttp.SendAsync(client!,HttpMethod.Post,route+"local-upload/"+fileId,content);await FreshWebHttp.SendAsync(client!,HttpMethod.Post,route+"complete",new{fileId});return fileId;
        }
    }
    /// <summary>只用原显式中文表头，数值保持JSON数值，级联文本完全保留。</summary>
    private static Dictionary<string,object?> Input(string name,string path)=>Row("名称",name,"级联",path,"金额",5);
    /// <summary>只构造本轮合成记录，SQL参数化交给原SqlRowStore。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var result=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<values.Length;index+=2)result.Add((string)values[index]!,values[index+1]);return result;}
    /// <summary>HTTP和文件中长ID始终使用精确字符串。</summary>
    private static string Id(long value)=>value.ToString(CultureInfo.InvariantCulture);
    /// <summary>任何DDL前固定数据库、实例以及真正登录的本轮临时账号。</summary>
    private static async Task Pin(DatabaseSession session,JsonElement pin)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid,CURRENT_USER()";await using var reader=await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());Assert.Equal(session.Options.ExpectedDatabase,reader.GetString(0));Assert.Equal(pin.GetProperty("expectedHostname").GetString(),reader.GetString(1));Assert.Equal(pin.GetProperty("expectedServerUuid").GetString(),reader.GetString(2));Assert.StartsWith(new MySqlConnectionStringBuilder(session.Options.ConnectionString).UserID+"@",reader.GetString(3));
    }
    /// <summary>只读当前独占库表数，不跨库扫描业务数据。</summary>
    private static async Task<int> TableCount(DatabaseSession session)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()";return Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture);
    }
    /// <summary>单次执行自有初始化进程；输出脱敏，失败不自动重试，保留日志和原阶段。</summary>
    private static async Task RunOwned(ProcessStartInfo info,string evidence,string name,params string[] secrets)
    {
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var timedOut=false;
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){timedOut=true;if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();}
        var output=await stdout;var error=await stderr;var builder=new MySqlConnectionStringBuilder(info.Environment["Database__ConnectionString"]??throw new InvalidOperationException("缺少本轮连接。"));
        foreach(var secret in secrets.Append(builder.Password).Where(value=>!string.IsNullOrEmpty(value)).OrderByDescending(value=>value.Length)){output=output.Replace(secret,"[已脱敏]",StringComparison.Ordinal);error=error.Replace(secret,"[已脱敏]",StringComparison.Ordinal);}
        await File.WriteAllTextAsync(Path.Combine(evidence,name+".log"),output+"\n"+error);if(timedOut)throw new TimeoutException("Excel原生命令超时，脱敏证据保留。");Assert.True(process.ExitCode==0,"Excel原生命令失败，详见私有脱敏日志。");
    }
}
