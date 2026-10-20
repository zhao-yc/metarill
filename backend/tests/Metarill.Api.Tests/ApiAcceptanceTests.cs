using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Metarill.Data;
using Metarill.Runtime;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly:CollectionBehavior(DisableTestParallelization=true)]
namespace Metarill.Api.Tests;

public sealed class MySqlApiFactAttribute:FactAttribute
{
    public MySqlApiFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")))Skip="需要独立的 Server-200 MySQL 测试配置。";}
}

/// <summary>使用真实测试库验证 HTTP 边界；不存在测试配置时明确跳过。</summary>
public sealed partial class ApiAcceptanceTests
{
    private const string Project="2200000000000100000";
    private static JsonElement Json(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
    private static async Task<JsonElement> Body(HttpResponseMessage response){var content=await response.Content.ReadAsStringAsync();Assert.True(content.TrimStart().StartsWith("{",StringComparison.Ordinal),content[..Math.Min(content.Length,2500)]);return Json(content);}
    private static async Task<JsonElement> Post(HttpClient client,string path,object body)=>await Body(await client.PostAsJsonAsync("/api"+path,body));
    private static JsonElement Data(JsonElement json){Assert.True(json.GetProperty("code").GetInt32()==200,json.ToString());return json.GetProperty("data");}
    /// <summary>测试库名称必须显式含 test，禁止误用开发库或生产库做验收清理。</summary>
    private static async Task<(WebApplicationFactory<Program>,HttpClient)> Login(Func<WebApplicationFactory<Program>,WebApplicationFactory<Program>>? customize=null)
    {
        var settings=Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!;
        var config=Json(await File.ReadAllTextAsync(settings));
        foreach(var key in new[]{"Database","BusinessDatabase"})
            Assert.Contains("test",config.GetProperty("Metarill").GetProperty(key).GetProperty("ExpectedDatabase").GetString()!,StringComparison.OrdinalIgnoreCase);
        Environment.SetEnvironmentVariable("METARILL_SETTINGS",settings);
        var factory=new WebApplicationFactory<Program>();if(customize is not null)factory=customize(factory);var client=factory.CreateClient();
        var bootstrap=config.GetProperty("Metarill").GetProperty("Bootstrap");
        var login=await Body(await client.PostAsync("/api/admin/sys/yz_manager/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"username",bootstrap.GetProperty("Username").GetString()!},{"password",bootstrap.GetProperty("Password").GetString()!}})));
        Data(login);client.DefaultRequestHeaders.Add("loginStateId",login.GetProperty("map").GetProperty("loginStateId").GetString());
        client.DefaultRequestHeaders.Add("sysParams",Uri.EscapeDataString(JsonSerializer.Serialize(new{yzProjectId=Project})));
        return(factory,client);
    }
    [MySqlApiFact]
    public async Task Crud_preserves_presence_and_protects_system_fields_with_legacy_echoes()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        const string route="/admin/auto/demo_customer/2200000000000100020";var ids=new List<string>();
        try
        {
            var added=Data(await Post(client,route+"/add",new{name="接口验收-"+Guid.NewGuid().ToString("N"),notes="省略保留",amount=12.34,isEnabled=1}));
            var id=added.GetProperty("id").GetString()!;ids.Add(id);
            // 旧整entity允许已知只读回显，任意未知扩展字段仍须在删除前拒绝。
            var forged=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(added.GetRawText())!;forged["notAllowed"]=JsonSerializer.SerializeToElement("不可接受的扩展");
            using var invalidDelete=await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api"+route){Content=JsonContent.Create(forged)});
            Assert.Equal(500,(await Body(invalidDelete)).GetProperty("code").GetInt32());
            Assert.Equal(id,Data(await Body(await client.GetAsync("/api"+route+"/detail/"+id))).GetProperty("id").GetString());
            var updated=Data(await Post(client,route+"/update",new{id,amount=45.67}));
            Assert.Equal("省略保留",updated.GetProperty("notes").GetString());
            Assert.Equal(JsonValueKind.Null,Data(await Post(client,route+"/update",new{id,notes=(string?)null})).GetProperty("notes").ValueKind);
            // 本页没有发布insertManagerId回显，仍按未知请求字段拒绝；不能将所有系统列一概剥离。
            Assert.Equal(500,(await Post(client,route+"/update",new{id,insertManagerId="99"})).GetProperty("code").GetInt32());
            Assert.Equal(500,(await Post(client,route+"/update",new{id,notAllowed="99"})).GetProperty("code").GetInt32());
            Assert.Equal(500,(await Post(client,route+"/page",new{filters=new{notAllowed="x"}})).GetProperty("code").GetInt32());
            Assert.True(Data(await Post(client,route+"/page",new{pageNo=1,pageSize=20,filters=new{name=added.GetProperty("name").GetString()}})).GetProperty("total").ToString() is{} total&&long.Parse(total)>=1);
            var copy=Data(await Post(client,route+"/copy/"+id,new{}));var copyId=copy.GetProperty("id").GetString()!;Assert.NotEqual(id,copyId);ids.Add(copyId);
            // 原删除操作提交完整实体仍应成功；先从待清理表移除已删除ID，避免二次删除掩盖前面的断言。
            Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api"+route){Content=JsonContent.Create(copy)})));ids.Remove(copyId);
        }
        finally{foreach(var id in ids)Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api"+route){Content=JsonContent.Create(new{id})})));}
        using var anonymous=factory.CreateClient();Assert.Equal(500,(await Body(await anonymous.PostAsJsonAsync("/api"+route+"/page",new{filters=new{}}))).GetProperty("code").GetInt32());
    }
    [MySqlApiFact]
    public async Task Metadata_schema_and_generation_form_a_real_vertical_slice()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        var tableName="acceptance_"+Guid.NewGuid().ToString("N")[..16];string? tableId=null;
        const string tableRoute="/admin/auto/config_table/6191083005411328";
        try
        {
            var table=Data(await Post(client,tableRoute+"/add",new{name="验收配置",tableName,yzAdminTypeId="2200000000000100001",isRongyu=1}));
            tableId=table.GetProperty("id").GetString();
            var field=Data(await Post(client,"/admin/custom/index/save_batch_create_columns",new{config_table_id=tableId,content="测试数量 quantity"}));
            Assert.Equal(1,field.GetProperty("savedCount").GetInt32());
            var plan=Data(await Post(client,"/admin/sys/database/preview",new{projectId=Project,tableId}));
            Assert.Contains(plan.GetProperty("changes").EnumerateArray(),c=>c.GetProperty("sql").GetString()!.Contains(tableName,StringComparison.Ordinal));
            var applied=Data(await Post(client,"/admin/sys/database/apply",new{projectId=Project,plan}));
            Assert.True(applied.GetProperty("succeeded").GetBoolean());
            // 计划不可重放；第二次预览必须无差异。
            Assert.Equal(500,(await Post(client,"/admin/sys/database/apply",new{projectId=Project,plan})).GetProperty("code").GetInt32());
            Assert.Empty(Data(await Post(client,"/admin/sys/database/preview",new{projectId=Project,tableId})).GetProperty("changes").EnumerateArray());
            using var generated=await client.PostAsJsonAsync("/api/admin/sys/generation/generate",new{projectId=Project,rootNamespace="Acceptance.App"});
            Assert.Equal("application/zip",generated.Content.Headers.ContentType?.MediaType);
            using var zip=new System.IO.Compression.ZipArchive(await generated.Content.ReadAsStreamAsync());
            Assert.Contains(zip.Entries,e=>e.FullName.StartsWith("backend/generated/Services/",StringComparison.Ordinal)&&e.FullName.EndsWith(".g.cs",StringComparison.Ordinal));
            Assert.Contains(zip.Entries,e=>e.FullName=="backend/Application.Host/Program.cs");
            Assert.Contains(zip.Entries,e=>e.FullName=="src/Metarill.Data/Metarill.Data.csproj");
            Assert.Equal("FullApplication",Assert.Single(generated.Headers.GetValues("X-Metarill-Package-Mode")));
            Assert.Equal("true",Assert.Single(generated.Headers.GetValues("X-Metarill-Frontend-Included")));
            Assert.Contains(zip.Entries,e=>e.FullName=="frontend/pages/login.vue");
            using var packageManifest=JsonDocument.Parse(await new StreamReader(zip.GetEntry(".metarill-package.json")!.Open()).ReadToEndAsync());
            Assert.True(packageManifest.RootElement.GetProperty("frontendIncluded").GetBoolean());
        }
        finally
        {
            if(tableId is not null)Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api"+tableRoute){Content=JsonContent.Create(new{id=tableId})})));
            // 只清理本测试随机创建的物理表；期望测试库已在登录前核验。
            var session=factory.Services.GetRequiredService<TargetDatabaseSessions>().Business;
            await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();
            await using var command=connection.CreateCommand();command.CommandText="DROP TABLE IF EXISTS "+session.Provider.QuoteIdentifier(tableName);await command.ExecuteNonQueryAsync();
        }
    }
    /// <summary>平台导出由服务端已发布身份选择仅后端模式，保持原能力并明确告知前端缺口。</summary>
    [MySqlApiFact]
    public async Task Published_platform_export_explicitly_reports_backend_only()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        var registry=factory.Services.GetRequiredService<Metarill.Runtime.Metadata.MetadataRegistry>();
        using var response=await client.PostAsJsonAsync("/api/admin/sys/generation/generate",new{projectId=registry.Platform.YzProjectId,rootNamespace="Acceptance.Platform"});
        Assert.True(response.Content.Headers.ContentType?.MediaType=="application/zip",response.Content.Headers.ContentType?.MediaType=="application/json"?await response.Content.ReadAsStringAsync():"平台导出没有返回ZIP内容类型。");
        Assert.Equal("BackendOnly",Assert.Single(response.Headers.GetValues("X-Metarill-Package-Mode")));
        Assert.Equal("false",Assert.Single(response.Headers.GetValues("X-Metarill-Frontend-Included")));
        var warnings=JsonSerializer.Deserialize<string[]>(Uri.UnescapeDataString(Assert.Single(response.Headers.GetValues("X-Metarill-Package-Warnings"))))!;
        Assert.Contains(warnings,warning=>warning.Contains("BackendOnly",StringComparison.Ordinal));
        using var zip=new System.IO.Compression.ZipArchive(await response.Content.ReadAsStreamAsync());
        Assert.DoesNotContain(zip.Entries,entry=>entry.FullName.StartsWith("frontend/",StringComparison.Ordinal));
        using var manifest=JsonDocument.Parse(await new StreamReader(zip.GetEntry(".metarill-package.json")!.Open()).ReadToEndAsync());
        Assert.False(manifest.RootElement.GetProperty("frontendIncluded").GetBoolean());
        Assert.Equal("BackendOnly",manifest.RootElement.GetProperty("packageMode").GetString());
    }

    [MySqlApiFact]
    public async Task Published_platform_metadata_keeps_its_structural_identity()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        var registry=factory.Services.GetRequiredService<Metarill.Runtime.Metadata.MetadataRegistry>();
        var repository=factory.Services.GetRequiredService<Metarill.Runtime.Metadata.MetadataRepository>();
        var store=factory.Services.GetRequiredService<Metarill.Runtime.Persistence.SqlRowStore>();
        var filter=new Dictionary<string,object?>{{"yz_project_id",long.Parse(registry.Platform.YzProjectId)}};
        var pages=await store.ReadAsync("config_page",filter);var columns=await store.ReadAsync("config_column",filter);
        var fields=await store.ReadAsync("config_page_column",filter);
        var invalid=fields.Where(f=>!pages.Any(p=>p["id"]!.ToString()==f["config_page_id"]?.ToString())||!columns.Any(c=>c["id"]!.ToString()==f["config_column_id"]?.ToString())).Select(f=>new{id=f["id"],page=f["config_page_id"],column=f["config_column_id"],name=f["name"]}).ToArray();
        Assert.True(invalid.Length==0,JsonSerializer.Serialize(invalid.Take(10)));
        var current=await repository.LoadProjectAsync(registry.Platform.YzProjectId);
        foreach(var expected in registry.Platform.ConfigTables)Assert.Contains(current.ConfigTables,t=>t.Id==expected.Id&&t.TableName==expected.TableName);
        foreach(var expected in registry.Platform.ConfigColumns)Assert.Contains(current.ConfigColumns,c=>c.Id==expected.Id&&c.ConfigTableId==expected.ConfigTableId&&c.ColumnName==expected.ColumnName);
    }
    [MySqlApiFact]
    public async Task Resource_permissions_do_not_turn_menu_visibility_into_write_access()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        var store=factory.Services.GetRequiredService<Metarill.Runtime.Persistence.SqlRowStore>();
        var ids=factory.Services.GetRequiredService<Metarill.Core.Security.IIdGenerator>();
        var role=ids.NextId();var account=ids.NextId();var username="permission-"+account;var password=Guid.NewGuid().ToString("N");
        try
        {
            await store.InsertAsync("yz_role",new Dictionary<string,object?>{{"id",role},{"name","验收只读角色"},{"is_super",0},{"yz_project_id",long.Parse(Project)},{"yz_admin_type_id",2200000000000100001L}});
            await store.InsertAsync("yz_manager",new Dictionary<string,object?>{{"id",account},{"name",username},{"password",Metarill.Runtime.Security.PasswordHasher.Hash(password)},{"yz_role_ids",JsonSerializer.Serialize(new[]{role.ToString()})},{"is_disabled",0},{"yz_project_id",long.Parse(Project)},{"yz_admin_type_id",2200000000000100001L}});
            var snapshot=Data(await Post(client,"/admin/sys/yzp_permission/snapshot",new{roleId=role.ToString()}));
            var revision=snapshot.GetProperty("revision").GetString();
            var grants=new{menuIds=new[]{"2200000000000100031"},pagePermissions=new Dictionary<string,string[]>{{"2200000000000100020",["is_query"]}},buttonIds=Array.Empty<string>()};
            Data(await Post(client,"/admin/sys/yzp_permission/save",new{roleId=role.ToString(),revision,grants}));
            Assert.Equal(500,(await Post(client,"/admin/sys/yzp_permission/save",new{roleId=role.ToString(),revision,grants})).GetProperty("code").GetInt32());
            using var ordinary=factory.CreateClient();
            var login=await Body(await ordinary.PostAsync("/api/admin/sys/yz_manager/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"username",username},{"password",password}})));Data(login);
            ordinary.DefaultRequestHeaders.Add("loginStateId",login.GetProperty("map").GetProperty("loginStateId").GetString());
            ordinary.DefaultRequestHeaders.Add("sysParams",Uri.EscapeDataString(JsonSerializer.Serialize(new{yzProjectId=Project})));
            Data(await Post(ordinary,"/admin/auto/demo_customer/2200000000000100020/page",new{filters=new{}}));
            Assert.Equal(500,(await Post(ordinary,"/admin/auto/demo_customer/2200000000000100020/add",new{name="不能创建"})).GetProperty("code").GetInt32());
            Assert.Equal(500,(await Post(ordinary,"/admin/sys/yzp_permission/snapshot",new{roleId=role.ToString()})).GetProperty("code").GetInt32());
            var state=Data(await Body(await ordinary.GetAsync("/api/admin/sys/yz_manager/getCurrentPermissions")));
            Assert.Contains("is_query",state.GetProperty("pagePermissions").GetProperty("2200000000000100020").EnumerateArray().Select(x=>x.GetString()));
            var noPages=new{menuIds=new[]{"2200000000000100031"},pagePermissions=new Dictionary<string,string[]>(),buttonIds=Array.Empty<string>()};
            var current=Data(await Post(client,"/admin/sys/yzp_permission/snapshot",new{roleId=role.ToString()}));
            Data(await Post(client,"/admin/sys/yzp_permission/save",new{roleId=role.ToString(),revision=current.GetProperty("revision").GetString(),grants=noPages}));
            Assert.Equal(500,(await Post(ordinary,"/admin/auto/demo_customer/2200000000000100020/page",new{filters=new{}})).GetProperty("code").GetInt32());
        }
        finally
        {
            foreach(var permission in await store.ReadAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",role}}))await store.UpdateAsync("yz_role_permission",Metarill.Runtime.Persistence.SqlRowStore.Id(permission),new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            foreach(var session in await store.ReadAsync("sys_session",new Dictionary<string,object?>{{"account_id",account}}))await store.UpdateAsync("sys_session",Metarill.Runtime.Persistence.SqlRowStore.Id(session),new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            await store.UpdateAsync("yz_manager",account,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
            await store.UpdateAsync("yz_role",role,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
        }
    }
    [MySqlApiFact]
    public async Task Import_is_atomic_and_export_is_bound_to_authenticated_project()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        const string route="/admin/auto/demo_customer/2200000000000100020";var marker="导入验收-"+Guid.NewGuid().ToString("N");var ids=new List<string>();
        try
        {
            var imported=await Post(client,route+"/importDatas2200000000000100020",new{requestJson=new[]{new Dictionary<string,object?>{{"名称",marker},{"金额","123.45"},{"备注","=SUM(1,2)"}}}});
            Data(imported);
            var page=Data(await Post(client,route+"/page",new{filters=new{name=marker}}));
            ids.AddRange(page.GetProperty("records").EnumerateArray().Select(r=>r.GetProperty("id").GetString()!));Assert.Single(ids);
            var failed=await Post(client,route+"/importDatas2200000000000100020",new{requestJson=new[]{new Dictionary<string,object?>{{"名称",marker+"回滚"}},new Dictionary<string,object?>{{"名称",""},{"金额","无法转换"}}}});
            Assert.Equal(500,failed.GetProperty("code").GetInt32());
            var rollback=Data(await Post(client,route+"/page",new{filters=new{name=marker+"回滚"}}));Assert.Equal("0",rollback.GetProperty("total").ToString());
            var ticket=Data(await Post(client,route+"/initExport",new{filters=new{name=marker}})).GetString();
            var url="/api/admin/sys/export/export?exportId="+ticket;
            using var download=await client.GetAsync(url);Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",download.Content.Headers.ContentType?.MediaType);
            using var archive=new System.IO.Compression.ZipArchive(await download.Content.ReadAsStreamAsync());Assert.Contains(archive.Entries,e=>e.FullName=="xl/workbook.xml");
            using var anonymous=factory.CreateClient();
            using(var direct=await anonymous.GetAsync(url)){Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",direct.Content.Headers.ContentType?.MediaType);Assert.Contains("no-referrer",direct.Headers.GetValues("Referrer-Policy"));}
            using(var invalid=new HttpRequestMessage(HttpMethod.Get,url)){invalid.Headers.Add("loginStateId","invalid-session");Assert.Equal(500,(await Body(await anonymous.SendAsync(invalid))).GetProperty("code").GetInt32());}
            Assert.Equal(500,(await Body(await anonymous.GetAsync("/api/admin/sys/export/export?exportId="+new string('f',64)))).GetProperty("code").GetInt32());
            client.DefaultRequestHeaders.Remove("sysParams");client.DefaultRequestHeaders.Add("sysParams",Uri.EscapeDataString("{\"yzProjectId\":\"1608097412978577409\"}"));
            Assert.Equal(500,(await Body(await client.GetAsync(url))).GetProperty("code").GetInt32());
        }
        finally
        {
            client.DefaultRequestHeaders.Remove("sysParams");client.DefaultRequestHeaders.Add("sysParams",Uri.EscapeDataString(JsonSerializer.Serialize(new{yzProjectId=Project})));
            foreach(var id in ids)Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api"+route){Content=JsonContent.Create(new{id})})));
        }
    }
    [MySqlApiFact]
    public async Task Mcp_transport_lists_tools_and_checks_the_same_page_permissions()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        await using var transport=new ModelContextProtocol.Client.HttpClientTransport(new(){Endpoint=new Uri(client.BaseAddress!,"api/mcp"),TransportMode=ModelContextProtocol.Client.HttpTransportMode.StreamableHttp},client,ownsHttpClient:false);
        await using var mcp=await ModelContextProtocol.Client.McpClient.CreateAsync(transport);
        var tools=await mcp.ListToolsAsync();Assert.Contains(tools,t=>t.Name=="get_project_metadata");Assert.Contains(tools,t=>t.Name=="call_generated_page");
        var result=await mcp.CallToolAsync("call_generated_page",new Dictionary<string,object?>{{"pageId","2200000000000100020"},{"operation","list"},{"data",new{filters=new{}}},{"confirmed",false}});
        Assert.NotEqual(true,result.IsError);Assert.Contains("records",JsonSerializer.Serialize(result),StringComparison.Ordinal);
        var preview=await mcp.CallToolAsync("preview_database_schema",new Dictionary<string,object?>());
        Assert.NotEqual(true,preview.IsError);
        var text=Assert.Single(preview.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()).Text;
        var mcpPlan=Json(text);
        var httpPlan=Data(await Post(client,"/admin/sys/database/preview",new{projectId=Project}));
        Assert.Equal(httpPlan.GetProperty("desiredFingerprint").GetString(),mcpPlan.GetProperty("desiredFingerprint").GetString());
        Assert.Contains(tools,tool=>tool.Name=="inspect_database_schema");
        var inspection=await mcp.CallToolAsync("inspect_database_schema",new Dictionary<string,object?>{{"data",new{}}});
        Assert.NotEqual(true,inspection.IsError);
        var inspected=Json(Assert.Single(inspection.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()).Text);
        Assert.False(inspected.GetProperty("executable").GetBoolean());
        var httpInspection=Data(await Post(client,"/admin/sys/database/inspect",new{}));
        Assert.Equal(httpInspection.GetProperty("snapshot").GetProperty("fingerprint").GetString(),inspected.GetProperty("snapshot").GetProperty("fingerprint").GetString());
        var invalidInspection=await mcp.CallToolAsync("inspect_database_schema",new Dictionary<string,object?>{{"data",new{sql="SELECT 1"}}});
        Assert.True(invalidInspection.IsError);
        using var anonymous=factory.CreateClient();using var denied=await anonymous.PostAsJsonAsync("/api/mcp",new{jsonrpc="2.0",id=3,method="tools/list",@params=new{}});Assert.Equal(System.Net.HttpStatusCode.Unauthorized,denied.StatusCode);
    }
    [MySqlApiFact]
    public async Task File_upload_completion_and_protected_stream_are_real()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;string? id=null;
        var content=Encoding.UTF8.GetBytes("文件验收-"+Guid.NewGuid());
        try
        {
            var plan=Data(await Post(client,"/admin/sys/file-library/upload-plan",new{originalName="验收.txt",mimeType="text/html",sizeBytes=content.Length,folderId=""}));
            id=plan.GetProperty("fileId").GetString();
            using var multipart=new MultipartFormDataContent();multipart.Add(new ByteArrayContent(content),"file","验收.txt");
            Data(await Body(await client.PostAsync("/api"+plan.GetProperty("plan").GetProperty("uploadUrl").GetString(),multipart)));
            var ready=Data(await Post(client,"/admin/sys/file-library/complete",new{fileId=id}));Assert.Equal("PROTECTED",ready.GetProperty("accessMode").GetString());
            var url="/api"+ready.GetProperty("url").GetString();
            using var stream=await client.GetAsync(url);Assert.Equal(content,await stream.Content.ReadAsByteArrayAsync());Assert.Equal("text/plain",stream.Content.Headers.ContentType?.MediaType);
            using var anonymous=factory.CreateClient();Assert.Equal(500,(await Body(await anonymous.GetAsync(url))).GetProperty("code").GetInt32());
            using var rangeRequest=new HttpRequestMessage(HttpMethod.Get,url);rangeRequest.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(0,3);
            using var range=await client.SendAsync(rangeRequest);Assert.Equal(System.Net.HttpStatusCode.PartialContent,range.StatusCode);Assert.Equal(4,(await range.Content.ReadAsByteArrayAsync()).Length);
            Assert.Equal(500,(await Body(await client.GetAsync("/api/admin/sys/file-library/public-content/"+id))).GetProperty("code").GetInt32());
        }
        finally{if(id is not null)Data(await Body(await client.DeleteAsync("/api/admin/sys/file-library/"+id)));}
    }
}
