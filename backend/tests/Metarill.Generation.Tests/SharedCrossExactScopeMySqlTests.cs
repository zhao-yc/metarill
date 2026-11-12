using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>用真实发布目录、普通账号及新库验证共享级联和逐动作精确范围。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SharedCrossExactScopeMySqlTests
{
    /// <summary>同首列的两个来源只有一个可覆盖，同时核对导出及写前拒绝。</summary>
    [FreshInstallMySqlFact]
    public Task Production_shared_cascade_limits_reads_and_overwrite_by_exact_role()=>Exercise("scopeKey");

    /// <summary>物理祖先位为空时按完整现有路径授权，并对失效同伴拒权。</summary>
    [FreshInstallMySqlFact]
    public Task Production_shared_cascade_matches_logical_ancestor_and_files()=>Exercise("countryId");

    /// <summary>名称按目标当前值重算，真实文件引用与记录使用同一范围。</summary>
    [FreshInstallMySqlFact]
    public Task Production_shared_cascade_matches_derived_name_and_files()=>Exercise("countryName");

    /// <summary>真实普通账号同时受物理精确字段与共享逻辑父范围约束。</summary>
    [FreshInstallMySqlFact]
    public Task Production_shared_parent_and_physical_exact_allow_only_matching_records_and_files()=>Exercise("scopeKey",parentScoped:true);

    /// <summary>共享派生名称和独立逻辑父范围分别核对，并验证附件引用随事务回滚。</summary>
    [FreshInstallMySqlFact]
    public Task Production_shared_parent_and_logical_exact_allow_only_matching_records_and_files()=>Exercise("countryName",parentScoped:true);

    /// <summary>各配置共用相同宿主、原HTTP和独占MySQL验证。</summary>
    private static async Task Exercise(string scope,bool parentScoped=false)
    {
        var logical=scope!="scopeKey";
        var includeFiles=logical||parentScoped;
        var loginRedis = FreshInstallConfiguration.ReadLoginRedis();
        var options = FreshInstallConfiguration.Read("AgentDatabase", "agent");
        using var session = new DatabaseSession(options);
        await FreshInstallConfiguration.VerifyEmptyLeaseAsync(session);
        var catalog = SharedCrossExactScopeGenerationTests.Catalog(scope);
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        if(parentScoped)
        {
            sourcePage.WhereSql+=" AND area_id=@parent_id";
            sourcePage.IsImport=0;
            sourcePage.IsExport=0;
            sourcePage.Extra.Remove("importMethod");
        }
        if(includeFiles)
        {
            catalog.ConfigColumns.Add(new(){Id="9001",ConfigTableId="2",ColumnName="attachments",Name="附件",SortNo=100,
                LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\"}"});
            catalog.ConfigPageColumns.Add(new(){Id="2019001",ConfigPageId="201",ConfigColumnId="9001",SortNo=100});
        }
        var repository = NonRedundantApplicationFixture.Root();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-shared-exact-mysql");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var password = Guid.NewGuid().ToString("N");
        var project = long.Parse(catalog.YzProjectId, CultureInfo.InvariantCulture);
        const long country = 9007199254742001, area = country + 1, place = country + 2;
        const long firstWarehouse = country + 3, secondWarehouse = country + 4;
        const long allowed = country + 5, denied = country + 6, conflict = country + 7;
        const long otherCountry = country + 10, otherArea = country + 11, otherPlace = country + 12, otherWarehouse = country + 13;
        const long siblingArea=country+14,siblingPlace=country+15,siblingWarehouse=country+16,sibling=country+17;
        var allowedParent=parentScoped?Id(area):null;
        var deniedParent=parentScoped?Id(logical?otherArea:area):null;
        const string ownerName="shared-exact-owner";
        const string source = "shared_cross_source";
        const string route = "/api/admin/auto/" + source + "/201";
        const string username = "shared-exact-user";

        try
        {
            new ApplicationPackager().Create(catalog, directory, "Example.SharedExactScope", repository);
            await NonRedundantApplicationFixture.BuildAsync(directory, "restore");
            await NonRedundantApplicationFixture.BuildAsync(directory, "build");
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));
            var securityPath = Path.Combine(directory, "backend/metadata/security.json");
            var securityCatalog = JsonSerializer.Deserialize<MetadataCatalog>(
                await File.ReadAllTextAsync(securityPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var security = new SqlRowStore(session, securityCatalog);
            var ids = new SnowflakeIdGenerator(933);
            var role = ids.NextId();
            await security.InsertAsync("yz_role", new Dictionary<string, object?>
            {
                ["id"] = role, ["yz_project_id"] = project, ["yz_admin_type_id"] = 5L,
                ["name"] = username, ["is_scope"] = 1, ["is_super"] = 0, ["is_built_in"] = 0
            });
            await security.InsertAsync("yz_manager", new Dictionary<string, object?>
            {
                ["id"] = ids.NextId(), ["yz_project_id"] = project, ["yz_admin_type_id"] = 5L,
                ["name"] = username, ["real_name"] = scope=="countryId"?Id(country):scope=="countryName"?"country":"scope-a",
                ["password"] = PasswordHasher.Hash(password),
                ["is_disabled"] = 0, ["yz_role_ids"] = JsonSerializer.Serialize(new[] { Id(role) })
            });
            foreach (var page in catalog.ConfigPages)
                await security.InsertAsync("yz_role_permission", new Dictionary<string, object?>
                {
                    ["id"] = ids.NextId(), ["yz_project_id"] = project, ["yz_role_id"] = role,
                    ["config_page_id"] = long.Parse(page.Id, CultureInfo.InvariantCulture),
                    ["permission_values"] = "[\"is_all\"]"
                });

            var business = new SqlRowStore(session, catalog);
            await business.InsertAsync("shared_cross_country", Target(country, "country"));
            await business.InsertAsync("shared_cross_area", Target(area, "area", "shared_cross_country_id", country));
            await business.InsertAsync("shared_cross_place", Target(place, "place", "shared_cross_area_id", area));
            await business.InsertAsync("shared_cross_warehouse", Target(firstWarehouse, "first", "shared_cross_area_id", area));
            await business.InsertAsync("shared_cross_warehouse", Target(secondWarehouse, "second", "shared_cross_area_id", area));
            if(logical)
            {
                await business.InsertAsync("shared_cross_country", Target(otherCountry, "other-country"));
                await business.InsertAsync("shared_cross_area", Target(otherArea, "other-area", "shared_cross_country_id", otherCountry));
                await business.InsertAsync("shared_cross_place", Target(otherPlace, "other-place", "shared_cross_area_id", otherArea));
                await business.InsertAsync("shared_cross_warehouse", Target(otherWarehouse, "other-warehouse", "shared_cross_area_id", otherArea));
            }
            if(parentScoped)
            {
                await business.InsertAsync("shared_cross_area",Target(siblingArea,"sibling-area","shared_cross_country_id",country));
                await business.InsertAsync("shared_cross_place",Target(siblingPlace,"sibling-place","shared_cross_area_id",siblingArea));
                await business.InsertAsync("shared_cross_warehouse",Target(siblingWarehouse,"sibling-warehouse","shared_cross_area_id",siblingArea));
                await business.InsertAsync(source,Source(sibling,"row-sibling","scope-a",siblingPlace,siblingWarehouse));
            }
            await business.InsertAsync(source, Source(allowed, "row-allowed", "scope-a", place, firstWarehouse));
            await business.InsertAsync(source, Source(denied, "row-denied", "scope-b",
                logical?otherPlace:place,logical?otherWarehouse:firstWarehouse));

            await using var host = await FreshHost.StartAsync(HostInfo(), "/health");
            using var client = new HttpClient{BaseAddress=host.Client.BaseAddress};
            var files=Array.Empty<string>();
            if(includeFiles)
            {
                await FreshWebHttp.LoginAsync(host.Client,ownerName,password,form:true);
                files=[await Attach(host.Client,allowed,allowedParent),await Attach(host.Client,denied,deniedParent)];
                if(logical)await business.InsertAsync(source, Source(conflict,"row-conflict","scope-a",place,otherWarehouse));
            }
            await FreshWebHttp.LoginAsync(client, username, password, form: true);
            var permissions = await FreshWebHttp.SendAsync(client, HttpMethod.Get, "/api/admin/sys/yz_manager/getCurrentPermissions");
            Assert.False(permissions.GetProperty("data").GetProperty("isSuper").GetBoolean());
            var pageData = (await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/page", new PageRequest { ParentId=allowedParent }))
                .GetProperty("data");
            Assert.Equal(1, pageData.GetProperty("total").GetInt64());
            var visible = Assert.Single(pageData.GetProperty("records").EnumerateArray());
            Assert.Equal(Id(allowed), visible.GetProperty("id").GetString());
            Assert.False(visible.TryGetProperty("scopeKey", out _));
            var count = await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/count", new PageRequest { ParentId=allowedParent });
            Assert.Equal(1, count.GetProperty("data").GetInt64());
            var detail = await FreshWebHttp.SendAsync(client, HttpMethod.Get, WithParent(route + "/detail/" + Id(allowed),allowedParent));
            Assert.Equal("country", detail.GetProperty("data").GetProperty("countryName").GetString());
            using var forbiddenDetail = await client.GetAsync(WithParent(route + "/detail/" + Id(denied),deniedParent));
            Assert.Equal(HttpStatusCode.OK, forbiddenDetail.StatusCode);
            Assert.Equal(500, (await forbiddenDetail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetInt32());
            if(includeFiles)
            {
                await Download(files[0],HttpStatusCode.OK);
                await Download(files[1],HttpStatusCode.Forbidden);
            }
            if(parentScoped)
            {
                var siblingPage=(await FreshWebHttp.SendAsync(client,HttpMethod.Post,route+"/page",
                    new PageRequest{ParentId=Id(siblingArea)})).GetProperty("data");
                Assert.Equal(Id(sibling),Assert.Single(siblingPage.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());
                await DeniedMutations(allowed,Id(siblingArea));
                await DeniedMutations(denied,deniedParent);
                await CheckAttachmentRollback();
                await FreshWebHttp.SendAsync(client,HttpMethod.Post,"/api/admin/sys/yz_manager/exit_login",new{});
                return;
            }

            var ticket = (await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/initExport", new PageRequest()))
                .GetProperty("data").GetString();
            using var export = await client.GetAsync(ExportDownloadAccess.DownloadPath + "?exportId=" + ticket);
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            using var workbook = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
            var text = workbook.Worksheet(1).RowsUsed().SelectMany(row => row.CellsUsed()).Select(cell => cell.GetString()).ToArray();
            Assert.Contains("row-allowed", text);
            Assert.DoesNotContain("row-denied", text);

            await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/update",
                new { id = Id(denied), warehouseId = Id(secondWarehouse) }, HttpStatusCode.InternalServerError);
            await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/copy/" + Id(denied),
                new { }, HttpStatusCode.InternalServerError);
            await FreshWebHttp.SendAsync(client, HttpMethod.Delete, route,
                new { id = Id(denied) }, HttpStatusCode.InternalServerError);
            await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/importDatas201", new
            {
                requestJson = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["placeId"] = "country/area/place", ["warehouseId"] = "country/area/second"
                    }
                }
            });
            var rows = await business.ReadAsync(source, new Dictionary<string, object?> { ["yz_project_id"] = project });
            Assert.Equal(logical?3:2, rows.Count);
            Assert.Equal(Id(secondWarehouse), SqlRowStore.Text(Assert.Single(rows, row => SqlRowStore.Id(row) == allowed), "warehouse_id"));
            Assert.Equal(Id(logical?otherWarehouse:firstWarehouse), SqlRowStore.Text(Assert.Single(rows, row => SqlRowStore.Id(row) == denied), "warehouse_id"));
            Assert.All(rows, row => Assert.Null(row["country_id"]));
            if(logical)
            {
                // 城市分支仍可恢复祖先，仓库目标失效却必须使整个来源失去权限。
                await business.UpdateAsync("shared_cross_warehouse",secondWarehouse,new Dictionary<string,object?>{["status"]=0});
                await RequireHidden();
                await business.UpdateAsync("shared_cross_warehouse",secondWarehouse,new Dictionary<string,object?>{["status"]=1});
                await Download(files[0],HttpStatusCode.OK);
                if(scope=="countryName")
                {
                    await business.UpdateAsync("shared_cross_country",country,new Dictionary<string,object?>{["name"]="changed-country"});
                    await RequireHidden();
                }
            }
            await FreshWebHttp.SendAsync(client, HttpMethod.Post, "/api/admin/sys/yz_manager/exit_login", new { });

            /// <summary>同一个真实文件引用随当前关系范围失效，旧账号值不能继续放行。</summary>
            async Task RequireHidden()
            {
                var page=await FreshWebHttp.SendAsync(client,HttpMethod.Post,route+"/page",new PageRequest());
                Assert.Equal(0,page.GetProperty("data").GetProperty("total").GetInt64());
                await FreshWebHttp.SendAsync(client,HttpMethod.Post,route+"/update",
                    new{id=Id(allowed),warehouseId=Id(firstWarehouse)},HttpStatusCode.InternalServerError);
                await Download(files[0],HttpStatusCode.Forbidden);
            }

            /// <summary>修改、复制和删除都必须拒绝错误父范围或错误精确值。</summary>
            async Task DeniedMutations(long record,string? parent)
            {
                await FreshWebHttp.SendAsync(client,HttpMethod.Post,WithParent(route+"/update",parent),
                    new{id=Id(record),warehouseId=Id(secondWarehouse)},HttpStatusCode.InternalServerError);
                await FreshWebHttp.SendAsync(client,HttpMethod.Post,WithParent(route+"/copy/"+Id(record),parent),
                    new{},HttpStatusCode.InternalServerError);
                await FreshWebHttp.SendAsync(client,HttpMethod.Delete,WithParent(route,parent),
                    new{id=Id(record)},HttpStatusCode.InternalServerError);
            }

            /// <summary>文件引用插入后段失败时，业务仓库和原引用须同事务回滚。</summary>
            async Task CheckAttachmentRollback()
            {
                var fileMetadata=JsonSerializer.Deserialize<MetadataCatalog>(
                    await File.ReadAllTextAsync(Path.Combine(directory,"backend/metadata/files.json")),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                var references=new SqlRowStore(session,fileMetadata);
                var update=WithParent(route+"/update",allowedParent);
                var replacement=await Upload(client);
                var change=new{id=Id(allowed),warehouseId=Id(secondWarehouse),attachments=new[]{replacement}};
                const string constraint="mt_shared_exact_file_rollback";
                await ExecuteSql("ALTER TABLE `yz_file_ref` ADD CONSTRAINT `"+constraint+"` CHECK (`file_id` <> "+Id(long.Parse(replacement,CultureInfo.InvariantCulture))+")");
                try
                {
                    await FreshWebHttp.SendAsync(client,HttpMethod.Post,update,change,HttpStatusCode.InternalServerError);
                    await AssertRecord(firstWarehouse,files[0],replacement);
                }
                finally
                {
                    await ExecuteSql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");
                }
                await FreshWebHttp.SendAsync(client,HttpMethod.Post,update,change);
                await AssertRecord(secondWarehouse,replacement,files[0]);
                await Download(replacement,HttpStatusCode.OK);
                await Download(files[0],HttpStatusCode.Forbidden);

                /// <summary>独立业务和文件读取分别核对事务结局。</summary>
                async Task AssertRecord(long expectedWarehouse,string currentFile,string detachedFile)
                {
                    var row=Assert.Single(await business.ReadAsync(source,new Dictionary<string,object?>{["id"]=allowed}));
                    Assert.Equal(Id(expectedWarehouse),SqlRowStore.Text(row,"warehouse_id"));
                    Assert.Null(row["area_id"]);
                    var linked=await references.ReadAsync("yz_file_ref",new Dictionary<string,object?>{["record_id"]=allowed});
                    var live=linked.Where(reference=>reference.GetValueOrDefault("delete_no") is null).ToArray();
                    Assert.Equal(currentFile,SqlRowStore.Text(Assert.Single(live),"file_id"));
                    Assert.DoesNotContain(live,reference=>SqlRowStore.Text(reference,"file_id")==detachedFile);
                }
            }

            /// <summary>仅在本轮独占库安装与撤回真实故障条件。</summary>
            async Task ExecuteSql(string statement)
            {
                await using var connection=session.Provider.CreateConnection(session.Options);
                await connection.OpenAsync();
                await using var command=connection.CreateCommand();
                command.CommandText=statement;
                await command.ExecuteNonQueryAsync();
            }

            /// <summary>只通过原上传和保存入口建立真实记录引用。</summary>
            async Task<string> Attach(HttpClient owner,long record,string? parent)
            {
                var id=await Upload(owner);
                await FreshWebHttp.SendAsync(owner,HttpMethod.Post,WithParent(route+"/update",parent),new{id=Id(record),attachments=new[]{id}});
                return id;
            }

            /// <summary>分离上传和保存，以便附件引用后段故障可独立注入。</summary>
            async Task<string> Upload(HttpClient owner)
            {
                const string path="/api/admin/sys/file-library/";
                var bytes=Encoding.UTF8.GetBytes("共享逻辑权限附件");
                var plan=await FreshWebHttp.SendAsync(owner,HttpMethod.Post,path+"upload-plan",
                    new{originalName="shared-exact.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId="9001"});
                var id=plan.GetProperty("data").GetProperty("fileId").GetString()!;
                using var content=new MultipartFormDataContent();
                content.Add(new ByteArrayContent(bytes),"file","shared-exact.txt");
                await FreshWebHttp.SendAsync(owner,HttpMethod.Post,path+"local-upload/"+id,content);
                await FreshWebHttp.SendAsync(owner,HttpMethod.Post,path+"complete",new{fileId=id});
                return id;
            }

            /// <summary>验证受保护下载的实际状态与内容。</summary>
            async Task Download(string id,HttpStatusCode expected)
            {
                using var response=await client.GetAsync("/api/admin/sys/file-library/protected-content/"+id);
                Assert.Equal(expected,response.StatusCode);
                if(expected==HttpStatusCode.OK)Assert.Equal("共享逻辑权限附件",await response.Content.ReadAsStringAsync());
            }
        }
        finally
        {
            await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }

        /// <summary>私有连接参数只传环境，宿主按真实 Production Web 认证启动。</summary>
        ProcessStartInfo HostInfo(string? operation = null)
        {
            var hostDirectory = Path.Combine(directory, "backend/Application.Host");
            var info = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", hostDirectory);
            info.ArgumentList.Add(Path.Combine(hostDirectory, "bin/Debug/net10.0/Application.Host.dll"));
            if (operation is not null) info.ArgumentList.Add(operation);
            info.Environment["Authentication__LoginFailures__ConnectionString"] = loginRedis;
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            info.Environment["Database__Provider"] = "mysql";
            info.Environment["Database__ExpectedDatabase"] = options.ExpectedDatabase;
            info.Environment["Database__ConnectionString"] = options.ConnectionString;
            info.Environment["Authentication__Mode"] = "Web";
            info.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false";
            info.Environment["Authentication__Bootstrap__Username"] = ownerName;
            info.Environment["Authentication__Bootstrap__Password"] = password;
            info.Environment["Ids__WorkerId"] = "932";
            info.Environment["Files__RootDirectory"] = Path.Combine(directory, "files");
            info.Environment["Exports__RootDirectory"] = Path.Combine(directory, "exports");
            return info;
        }

        /// <summary>两条来源共享同一个原导入键，仅物理精确范围列取不同值。</summary>
        Dictionary<string, object?> Source(long id, string name, string marker,long placeId,long warehouseId) => new()
        {
            ["id"] = id, ["name"] = name, ["yz_project_id"] = project, ["scope_key"] = marker,
            ["place_id"] = Id(placeId), ["warehouse_id"] = Id(warehouseId)
        };

        /// <summary>所有目标由本轮独占库插入，父外键保持真实 bigint。</summary>
        Dictionary<string, object?> Target(long id, string name, string? parentField = null, long? parent = null)
        {
            var row = new Dictionary<string, object?> { ["id"] = id, ["name"] = name, ["yz_project_id"] = project, ["status"] = 1 };
            if (parentField is not null) row.Add(parentField, parent);
            return row;
        }
    }

    /// <summary>沿用原页面路由的父 ID 查询参数。</summary>
    private static string WithParent(string route,string? parent)=>parent is null?route:route+"?parentId="+Uri.EscapeDataString(parent);

    /// <summary>超过 JavaScript 安全整数的业务 ID 保持字符串边界。</summary>
    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
}
