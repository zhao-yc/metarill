using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>仅本轮独占空库启用正式宿主验收，普通 CI 无需连接 MySQL。</summary>
public sealed class ScopedCandidateFullMySqlFactAttribute:FactAttribute
{
    public ScopedCandidateFullMySqlFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("METARILL_SCOPED_FULL_SETTINGS")))
            Skip="需要本轮独占 MySQL 和 Redis 配置。";
    }
}

public sealed partial class GlobalScopedCandidateTests
{
    private const string FullSource="/api/admin/auto/cached_consumer/30/";
    private const string FullTarget="/api/admin/auto/global_source/";
    private const string Maintenance="/api/admin/sys/system_maintenance/backfill_join_columns";
    private const string Files="/api/admin/sys/file-library/";

    /// <summary>直接运行原 Full 包的 Production 入口，真实登录后验证单值与多值的 R1 缓存和事务。</summary>
    [ScopedCandidateFullMySqlFact]
    public async Task Scoped_global_candidates_in_full_packages_preserve_login_maintenance_and_import()
    {
        var settingsPath=Environment.GetEnvironmentVariable("METARILL_SCOPED_FULL_SETTINGS")!;
        Assert.Equal(Path.GetFullPath(settingsPath),Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var redis=FreshInstallConfiguration.ReadLoginRedisFile(settingsPath);
        var repository=NonRedundantApplicationFixture.Root();
        var run=settings.RootElement.GetProperty("RunId").GetString()!;
        Assert.Matches("^[a-f0-9]{16}$",run);

        var failures=new List<Exception>();
        foreach(var (multiple,projectType,suffix) in new[]{(false,"string","single"),(true,"int64","array")})
        {
            try{await Verify(multiple,projectType,suffix);}
            catch(Exception error)when(error is not OperationCanceledException){failures.Add(error);}
        }
        if(failures.Count!=0)throw new AggregateException(failures);

        async Task Verify(bool multiple,string projectType,string suffix)
        {
            var options=FreshInstallConfiguration.Read(suffix,suffix);
            var catalog=Catalog(multiple,1,projectType);
            var evidence=CSharpGenerator.SafePath(repository,".local/scoped-candidate-full/"+run+"/"+suffix);
            Assert.False(Directory.Exists(evidence));
            Directory.CreateDirectory(evidence);
            var package=Path.Combine(evidence,"application");
            var stage="生成及严格构建";
            try
            {
                var result=new ApplicationPackager().Create(catalog,package,ApplicationNamespace,repository);
                Assert.Equal(ApplicationPackageMode.FullApplication,result.Mode);
                Assert.True(result.FrontendIncluded);
                var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);
                foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})
                    build.ArgumentList.Add(argument);
                build.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
                await NonRedundantApplicationFixture.RunAsync(build);
                var proof=GlobalCachedSourceNativePreparation.Capture(package);
                using var manifest=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(package,".metarill-package.json")));
                Assert.Equal("web-database-v1",manifest.RootElement.GetProperty("authenticationProfile").GetString());

                stage="空库和显式初始化";
                using var session=new DatabaseSession(options);
                await FreshInstallConfiguration.VerifyEmptyLeaseAsync(session);
                var username="scope-owner-"+suffix;
                var password=settings.RootElement.GetProperty("BootstrapPassword").GetString()!;
                var ordinaryPassword=settings.RootElement.GetProperty("OrdinaryPassword").GetString()!;
                var hostDirectory=CSharpGenerator.SafePath(package,"backend/Application.Host");

                ProcessStartInfo Info(string? operation=null)
                {
                    var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);
                    info.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));
                    if(operation is not null)info.ArgumentList.Add(operation);
                    info.Environment["ASPNETCORE_ENVIRONMENT"]="Production";
                    info.Environment["DOTNET_ENVIRONMENT"]="Production";
                    info.Environment["Logging__LogLevel__Default"]="Warning";
                    info.Environment["Database__Provider"]="mysql";
                    info.Environment["Database__ExpectedDatabase"]=options.ExpectedDatabase;
                    info.Environment["Database__ConnectionString"]=options.ConnectionString;
                    if(operation is null)info.Environment["Authentication__LoginFailures__ConnectionString"]=redis;
                    info.Environment["Authentication__Mode"]="Web";
                    info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
                    info.Environment["Authentication__Bootstrap__Username"]=username;
                    info.Environment["Authentication__Bootstrap__Password"]=password;
                    info.Environment["Ids__WorkerId"]=multiple?"883":"882";
                    info.Environment["Files__PublicContentProjectId"]="100";
                    info.Environment["Files__RootDirectory"]=Path.Combine(evidence,"uploads");
                    info.Environment["Exports__RootDirectory"]=Path.Combine(evidence,"exports");
                    info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
                    return info;
                }

                await NonRedundantApplicationFixture.RunAsync(Info("--schema-preview"));
                await FreshInstallConfiguration.VerifyEmptyLeaseAsync(session);
                await NonRedundantApplicationFixture.RunAsync(Info("--schema-apply"));
                await NonRedundantApplicationFixture.RunAsync(Info("--initialize-security"));

                // 合成目标覆盖同项目、已发布外项目、未发布和软删除四种物理状态。
                var store=new SqlRowStore(session,catalog);
                var security=new SqlRowStore(session,Model(package,"security"));
                var fileStore=new SqlRowStore(session,Model(package,"files"));
                await store.InsertAsync("global_target",Row("id",8L,"name","本项目名称","status",1));
                await store.InsertAsync("global_target",Row("id",9L,"name","公开外项目名称","status",1));
                await store.InsertAsync("global_target",Row("id",10L,"name","未发布名称","status",1));
                await Seed(Own,100,8,1);
                await Seed(Published,101,9,1);
                await Seed(Hidden,101,10,0);
                await Seed(Deleted,101,9,1,new DateTime(2026,1,1));

                const long role=701,account=702;
                const string ordinary="scope-reader";
                await security.InsertAsync("yz_role",Row("id",role,"yz_project_id",100L,"yz_admin_type_id",9L,"name",ordinary,"is_super",0,"is_built_in",0));
                await security.InsertAsync("yz_manager",Row("id",account,"yz_project_id",100L,"yz_admin_type_id",9L,"name",ordinary,
                    "password",PasswordHasher.Hash(ordinaryPassword),"is_disabled",0,"yz_role_ids",JsonSerializer.Serialize(new[]{Text(role)})));
                foreach(var page in catalog.ConfigPages)
                    await security.InsertAsync("yz_role_permission",Row("id",800L+long.Parse(page.Id,CultureInfo.InvariantCulture),
                        "yz_project_id",100L,"yz_role_id",role,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_all\"]"));

                stage="Production 登录和权限";
                await using var host=await FreshHost.StartAsync(Info(),"/health");
                await FreshWebHttp.SendAsync(host.Client,HttpMethod.Post,FullTarget+"10/page",new{},HttpStatusCode.Unauthorized);
                await FreshWebHttp.LoginAsync(host.Client,username,password,form:true);
                using var client=new HttpClient{BaseAddress=host.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(45)};
                await FreshWebHttp.LoginAsync(client,ordinary,ordinaryPassword,form:true);
                var permissions=await FreshWebHttp.SendAsync(client,HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");
                Assert.False(permissions.GetProperty("data").GetProperty("isSuper").GetBoolean());

                var request=new PageRequest{PageNo=1,PageSize=20};
                Assert.Equal(new[]{Text(Published),Text(Own)},Ids((await Post(client,FullTarget+"10/page",request)).GetProperty("records")));
                Assert.Equal(new[]{Text(Own)},Ids((await Post(client,FullTarget+"19/page",request)).GetProperty("records")));
                await BusinessRejected(client,HttpMethod.Get,FullTarget+"19/detail/"+Published,HttpStatusCode.OK);
                var foreignTarget=await store.ReadAsync("global_source",Row("id",Published));
                await BusinessRejected(client,HttpMethod.Post,FullTarget+"19/update",HttpStatusCode.InternalServerError,new{id=Text(Published),status=0});
                Assert.Equal(JsonSerializer.Serialize(foreignTarget),JsonSerializer.Serialize(await store.ReadAsync("global_source",Row("id",Published))));
                if(multiple)
                {
                    var selected=await Post(client,FullTarget+"10/selection/selected",new{selectedIds=new[]{Text(Published)}});
                    Assert.Equal(Text(Published),Ids(selected).Single());
                    await FreshWebHttp.SendAsync(client,HttpMethod.Post,FullTarget+"10/selection/selected",new{selectedIds=new[]{Text(Hidden)}},HttpStatusCode.InternalServerError);
                    Assert.Contains(Text(Published),Ids(await Post(client,FullSource+"findNameIdSelectedGlobalSource38",new{})));
                }
                else
                {
                    var selected=await Post(client,FullSource+"findNameIdGlobalSource34/selected",new{selectedIds=new[]{Text(Published)}});
                    Assert.Equal(Text(Published),Ids(selected).Single());
                    await FreshWebHttp.SendAsync(client,HttpMethod.Post,FullSource+"findNameIdGlobalSource34/selected",new{selectedIds=new[]{Text(Hidden)}},HttpStatusCode.InternalServerError);
                }

                stage="原维护回填与外项目物理隔离";
                object add=multiple?new{name="维护对象",globalSourceIds=new[]{Text(Own)},selectedGlobalSourceIds=new[]{Text(Published)}}
                    :new{name="维护对象",globalSourceId=Text(Own),selectedGlobalSourceId=Text(Published)};
                var created=await Post(client,FullSource+"add",add);
                var recordId=created.GetProperty("id").GetString()!;
                var physicalId=long.Parse(recordId,CultureInfo.InvariantCulture);
                var cache=multiple?"global_source_global_target_names":"global_source_global_target_name";
                var relation=multiple?"global_source_ids":"global_source_id";
                await store.InsertAsync("cached_consumer",Row("id",41L,"yz_project_id",101L,"name","外项目缓存",relation,
                    multiple?JsonSerializer.Serialize(new[]{Text(Own)}):Own,cache,multiple?"[\"保持外项目\"]":"保持外项目"));
                var foreignBefore=JsonSerializer.Serialize(await Physical(41));
                await store.UpdateAsync("cached_consumer",physicalId,Row(cache,multiple?"[\"故意旧名\"]":"故意旧名"));
                Assert.True((await Post(host.Client,Maintenance,new{configTableId="3"})).GetProperty("affectedRows").GetInt32()>0);
                CheckCache(await Physical(physicalId),cache,"本项目名称");
                Assert.Equal(0,(await Post(host.Client,Maintenance,new{configTableId="3"})).GetProperty("affectedRows").GetInt32());

                await Post(client,FullTarget+"19/update",new{id=Text(Own),status=0});
                CheckCache(await Physical(physicalId),cache,null);
                Assert.Equal(foreignBefore,JsonSerializer.Serialize(await Physical(41)));
                await store.UpdateAsync("cached_consumer",physicalId,Row(cache,multiple?"[\"未发布后旧名\"]":"未发布后旧名"));
                Assert.True((await Post(host.Client,Maintenance,new{configTableId="3"})).GetProperty("affectedRows").GetInt32()>0);
                CheckCache(await Physical(physicalId),cache,null);
                Assert.Equal(foreignBefore,JsonSerializer.Serialize(await Physical(41)));

                stage="原 XLSX 和真实末行写入故障回滚";
                var original=new[]{Row("名称","原批量导入","原下拉",multiple?"[\"公开外项目名称\"]":"公开外项目名称",
                    "原单选",multiple?JsonSerializer.Serialize(new[]{Text(Published)}):"公开外项目名称")};
                await Post(client,FullSource+"importDatas30",new{requestJson=original});
                var imported=Assert.Single(await store.ReadAsync("cached_consumer",Row("name","原批量导入")));
                var importedId=Text(SqlRowStore.Id(imported));
                CheckCache(imported,cache,"公开外项目名称");

                var grant=(await Post(client,FullSource+"initExport",new PageRequest{SelectedIds=[importedId]})).GetString()!;
                using(var file=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(grant)))
                {
                    Assert.Equal(HttpStatusCode.OK,file.StatusCode);
                    using var workbook=new XLWorkbook(new MemoryStream(await file.Content.ReadAsByteArrayAsync()));
                    var sheet=workbook.Worksheets.Single();
                    var titles=sheet.Row(1).CellsUsed().ToDictionary(cell=>cell.GetString(),cell=>cell.Address.ColumnNumber);
                    var exported=Assert.Single(sheet.RowsUsed().Skip(1));
                    Assert.Equal("原批量导入",exported.Cell(titles["名称"]).GetString());
                    Assert.Contains("公开外项目名称",exported.Cell(titles["原下拉"]).GetString());
                }
                var fileId=await Upload(client);
                await Sql("ALTER TABLE cached_consumer ADD CONSTRAINT mt_scoped_full_late CHECK (name <> '数据库拒绝末行')");
                try
                {
                    var before=await Snapshot();
                    var first=Row("名称","首行应回滚","原下拉",original[0]["原下拉"],"原单选",original[0]["原单选"],
                        "附件",JsonSerializer.Serialize(new[]{fileId}));
                    var last=Row("名称","数据库拒绝末行","原下拉",original[0]["原下拉"],"原单选",original[0]["原单选"]);
                    await FreshWebHttp.SendAsync(client,HttpMethod.Post,FullSource+"importDatas30",new{requestJson=new[]{first,last}},HttpStatusCode.InternalServerError);
                    Assert.Equal(before,await Snapshot());
                    Assert.Empty(await fileStore.ReadAsync("yz_file_ref",Row("file_id",long.Parse(fileId,CultureInfo.InvariantCulture))));
                    await Post(client,FullSource+"importDatas30",new{requestJson=new[]{first}});
                    Assert.Single(await fileStore.ReadAsync("yz_file_ref",Row("file_id",long.Parse(fileId,CultureInfo.InvariantCulture))));
                }
                finally{await Sql("ALTER TABLE cached_consumer DROP CHECK mt_scoped_full_late");}

                stage="撤权及包摘要核验";
                await security.UpdateAsync("yz_role_permission",830,Row("permission_values","[]"));
                await FreshWebHttp.SendAsync(client,HttpMethod.Post,FullSource+"page",request,HttpStatusCode.Forbidden);
                Assert.Empty((await new SchemaService(session).PreviewAsync(catalog)).Changes);
                foreach(var item in proof.Files)
                    Assert.Equal(item.Value,GlobalCachedSourceNativePreparation.Hash(CSharpGenerator.SafePath(package,item.Key)));
                await Receipt("passed",new{suffix,projectType,multiple,productionWeb=true,ordinaryAccount=true,originalXlsx=true,maintenance=true,repeatZero=true,
                    realBusinessAndFileRollback=true,revocation=true,packageFiles=proof.Files.Count,proof.AssemblySha256,proof.MetadataSha256});

                Task Seed(long id,long project,long target,int status,DateTime? deleted=null)=>store.InsertAsync("global_source",Row("id",id,
                    "yz_project_id",projectType=="string"?Text(project):project,"global_target_id",target,"status",status,"delete_no",deleted));
                async Task<Dictionary<string,object?>> Physical(long id)=>Assert.Single(await store.ReadIncludingDeletedAsync("cached_consumer",Row("id",id)));
                async Task<string> Snapshot()
                {
                    var consumers=(await store.ReadIncludingDeletedAsync("cached_consumer")).OrderBy(row=>SqlRowStore.Id(row)).ToArray();
                    var refs=(await fileStore.ReadIncludingDeletedAsync("yz_file_ref")).OrderBy(row=>SqlRowStore.Id(row)).ToArray();
                    var files=(await fileStore.ReadIncludingDeletedAsync("yz_file_library")).OrderBy(row=>SqlRowStore.Id(row)).ToArray();
                    return JsonSerializer.Serialize(new{consumers,refs,files});
                }
                void CheckCache(Dictionary<string,object?> row,string column,string? expected)
                {
                    if(multiple)Assert.Equal(new[]{expected},JsonSerializer.Deserialize<string?[]>(SqlRowStore.Text(row,column)));
                    else Assert.Equal(expected,row[column]);
                }
                async Task Sql(string statement)
                {
                    await using var connection=session.Provider.CreateConnection(options);
                    await connection.OpenAsync();
                    await using var command=connection.CreateCommand();
                    command.CommandText=statement;
                    await command.ExecuteNonQueryAsync();
                }
            }
            catch(Exception error)
            {
                await Receipt("failure",new{suffix,stage,type=error.GetType().Name});
                throw new InvalidOperationException("正式完整包失败阶段 "+suffix+" / "+stage,error);
            }
            Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value));
        }
    }

    /// <summary>原上传请求建立就绪文件，后续导入才创建业务引用。</summary>
    private static async Task<string> Upload(HttpClient client)
    {
        var content=System.Text.Encoding.UTF8.GetBytes("本轮事务验收附件");
        var plan=await Post(client,Files+"upload-plan",new{originalName="scoped-full.txt",mimeType="text/plain",sizeBytes=content.Length,configColumnId="39"});
        var id=plan.GetProperty("fileId").GetString()!;
        using var form=new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content),"file","scoped-full.txt");
        await FreshWebHttp.SendAsync(client,HttpMethod.Post,Files+"local-upload/"+id,form);
        await Post(client,Files+"complete",new{fileId=id});
        return id;
    }

    private static async Task<JsonElement> Post(HttpClient client,string path,object body)=>
        (await FreshWebHttp.SendAsync(client,HttpMethod.Post,path,body)).GetProperty("data");

    /// <summary>独立包详情无记录时沿用 R 失败，写前找不到行则沿用独立包异常边界。</summary>
    private static async Task BusinessRejected(HttpClient client,HttpMethod method,string path,HttpStatusCode expected,object? body=null)
    {
        using var request=new HttpRequestMessage(method,path);
        if(body is not null)request.Content=System.Net.Http.Json.JsonContent.Create(body);
        using var response=await client.SendAsync(request);
        Assert.Equal(expected,response.StatusCode);
        using var payload=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(500,Code(payload.RootElement));
    }

    private static MetadataCatalog Model(string package,string name)=>JsonSerializer.Deserialize<MetadataCatalog>(
        File.ReadAllText(CSharpGenerator.SafePath(package,"backend/metadata/"+name+".json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static Dictionary<string,object?> Row(params object?[] pairs)
    {
        var row=new Dictionary<string,object?>(StringComparer.Ordinal);
        for(var index=0;index<pairs.Length;index+=2)row.Add((string)pairs[index]!,pairs[index+1]);
        return row;
    }
}
