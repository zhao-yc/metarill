using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    private const string CandidateRoute = "/admin/custom/config_column/";
    private const string CandidateTableRoute = "/admin/auto/config_table/6191083005411328";

    [MySqlApiFact]
    public async Task Configuration_candidates_and_references_keep_the_original_shapes_and_current_project_boundary()
    {
        var (factory, client) = await Login(); await using var disposable = factory; using var http = client;
        var store = factory.Services.GetRequiredService<SqlRowStore>(); CandidateTable? fixture = null;
        try
        {
            fixture = await CreateCandidateTable(client, store);
            await store.UpdateAsync("config_page", fixture.PageId, new Dictionary<string, object?> { ["type"] = 1 });
            var definition = JsonSerializer.Serialize(new { tableId = fixture.TableId.ToString(), table = fixture.TableName, nameField = "candidate_value", whereSql = "PRIVATE_SQL_SENTINEL" });
            await store.UpdateAsync("config_column", fixture.LookupId, new Dictionary<string, object?> { ["control_kind"] = "List", ["control_detail"] = definition });
            var snapshot = Data(await Post(client, "/admin/custom/config_table/index_config", new { configTableId = fixture.TableId.ToString() }));
            Data(await Post(client, "/admin/custom/config_table/save_indexes", new
            {
                configTableId = fixture.TableId.ToString(), revision = snapshot.GetProperty("revision").GetString(),
                indexes = new[] { new { id = "", indexName = "yzp_idx_candidate_value", indexType = "NORMAL", isEnabled = 1,
                    columns = new[] { new { id = "", configColumnId = fixture.ColumnId.ToString(), columnName = "candidate_value", sortNo = 10, sortDirection = "ASC" } } } }
            }));
            var tables = Data(await Post(client, CandidateRoute + "list_control_tables", new { })).EnumerateArray().ToArray();
            Assert.Contains(tables, option => option.GetProperty("id").GetString() == fixture.TableId.ToString());
            Assert.All(tables, option => Assert.Equal(new[] { "id", "name", "text", "value" }, option.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray()));
            Assert.Equal(tables.Select(option => long.Parse(option.GetProperty("id").GetString()!)).OrderDescending(), tables.Select(option => long.Parse(option.GetProperty("id").GetString()!)));
            var pages = Data(await Post(client, CandidateRoute + "select_page_options", new { })).EnumerateArray().ToArray();
            Assert.Contains(pages, option => option.GetProperty("id").GetString() == fixture.PageId.ToString());
            // 旧工作台发送表单 id；响应仅带定位信息，不带原控件 JSON 和 SQL。
            var references = Data(await Body(await client.PostAsync("/api" + CandidateRoute + "references", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = fixture.ColumnId.ToString() }))));
            Assert.Equal(Project, references.GetProperty("projectId").GetString()); Assert.Equal(fixture.ColumnId.ToString(), references.GetProperty("columnId").GetString());
            Assert.Contains(references.GetProperty("pages").EnumerateArray(), field => field.GetProperty("pageColumnId").GetString() == fixture.FieldId.ToString());
            Assert.Contains(references.GetProperty("indexes").EnumerateArray(), index => index.GetProperty("indexName").GetString() == "yzp_idx_candidate_value");
            Assert.Contains(references.GetProperty("controls").EnumerateArray(), control => control.GetProperty("columnId").GetString() == fixture.LookupId.ToString() && control.GetProperty("referencePath").GetString() == "nameField");
            Assert.DoesNotContain("PRIVATE_SQL_SENTINEL", references.ToString()); Assert.NotEmpty(references.GetProperty("coverage").EnumerateArray());
            Assert.Equal(500, (await Post(client, CandidateRoute + "list_control_tables", new { projectId = Project })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, CandidateRoute + "references", new { id = fixture.ColumnId.ToString(), sql = "ignored" })).GetProperty("code").GetInt32());
            using var duplicate = await client.PostAsync("/api" + CandidateRoute + "references", new StringContent("{\"id\":\"" + fixture.ColumnId + "\",\"id\":\"" + fixture.ColumnId + "\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(500, (await Body(duplicate)).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, CandidateRoute + "references", new { id = "1 OR 1=1" })).GetProperty("code").GetInt32());
            SetCandidateProject(client, factory.Services.GetRequiredService<MetadataRegistry>().Platform.YzProjectId);
            Assert.Equal(500, (await Post(client, CandidateRoute + "references", new { id = fixture.ColumnId.ToString() })).GetProperty("code").GetInt32());
            Assert.DoesNotContain(Data(await Post(client, CandidateRoute + "list_control_tables", new { })).EnumerateArray(), option => option.GetProperty("id").GetString() == fixture.TableId.ToString());
            using var anonymous = factory.CreateClient();
            foreach (var action in new[] { "list_control_tables", "select_page_options", "references", "clear_page_control_detail", "control_metadata" })
                Assert.Equal(500, (await Post(anonymous, CandidateRoute + action, action is "references" or "clear_page_control_detail" or "control_metadata" ? new { id = fixture.ColumnId.ToString() } : (object)new { })).GetProperty("code").GetInt32());
        }
        finally
        {
            SetCandidateProject(client, Project);
            if (fixture is not null) await DeleteCandidateTable(client, fixture.TableId);
        }
    }

    [MySqlApiFact]
    public async Task Clearing_control_overrides_validates_every_owner_and_preserves_the_base_and_other_fields()
    {
        var (factory, client) = await Login(); await using var disposable = factory; using var http = client;
        var store = factory.Services.GetRequiredService<SqlRowStore>(); CandidateTable? fixture = null; long extraField = 0;
        try
        {
            fixture = await CreateCandidateTable(client, store); var ids = factory.Services.GetRequiredService<IIdGenerator>();
            var first = (await store.ReadAsync("config_page_column", new Dictionary<string, object?> { ["id"] = fixture.FieldId })).Single();
            var page = (await store.ReadAsync("config_page", new Dictionary<string, object?> { ["id"] = fixture.PageId })).Single();
            var secondPage = new Dictionary<string, object?>(page) { ["id"] = ids.NextId(), ["name"] = "清空验收副页", ["type"] = 0 };
            await store.InsertAsync("config_page", secondPage);
            extraField = ids.NextId(); var second = new Dictionary<string, object?>(first) { ["id"] = extraField, ["config_page_id"] = secondPage["id"], ["control_detail"] = "second-override" };
            await store.InsertAsync("config_page_column", second);
            await store.UpdateAsync("config_page_column", fixture.FieldId, new Dictionary<string, object?> { ["control_detail"] = "first-override", ["is_in_filter"] = 1 });
            await store.UpdateAsync("config_column", fixture.ColumnId, new Dictionary<string, object?> { ["control_detail"] = "base-must-remain" });
            var lookupField = (await store.ReadAsync("config_page_column", new Dictionary<string, object?> { ["config_column_id"] = fixture.LookupId })).Single();
            await store.UpdateAsync("config_page_column", SqlRowStore.Id(lookupField), new Dictionary<string, object?> { ["control_detail"] = "unrelated-must-remain" });
            // 后项项目冲突也不能清空前项；本测试恢复脏引用后才允许正常清空。
            var foreignProject = factory.Services.GetRequiredService<MetadataRegistry>().Platform.YzProjectId;
            await store.UpdateAsync("config_page_column", extraField, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(foreignProject) });
            Assert.Equal(500, (await Post(client, CandidateRoute + "clear_page_control_detail", new { id = fixture.ColumnId.ToString() })).GetProperty("code").GetInt32());
            Assert.Equal("first-override", SqlRowStore.Text((await store.ReadAsync("config_page_column", new Dictionary<string, object?> { ["id"] = fixture.FieldId })).Single(), "control_detail"));
            Assert.Equal("second-override", SqlRowStore.Text((await store.ReadAsync("config_page_column", new Dictionary<string, object?> { ["id"] = extraField })).Single(), "control_detail"));
            await store.UpdateAsync("config_page_column", extraField, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(Project) });
            SetCandidateProject(client, foreignProject);
            Assert.Equal(500, (await Post(client, CandidateRoute + "clear_page_control_detail", new { id = fixture.ColumnId.ToString() })).GetProperty("code").GetInt32());
            SetCandidateProject(client, Project);
            var cleared = Data(await Body(await client.PostAsync("/api" + CandidateRoute + "clear_page_control_detail", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = fixture.ColumnId.ToString() }))));
            Assert.Equal("清空成功，共处理 2 条页面字段", cleared.GetString());
            Assert.All(await store.ReadAsync("config_page_column", new Dictionary<string, object?> { ["config_column_id"] = fixture.ColumnId }), row => Assert.Equal("", SqlRowStore.Text(row, "control_detail")));
            Assert.Equal("base-must-remain", SqlRowStore.Text((await store.ReadAsync("config_column", new Dictionary<string, object?> { ["id"] = fixture.ColumnId })).Single(), "control_detail"));
            Assert.Equal("unrelated-must-remain", SqlRowStore.Text((await store.ReadAsync("config_page_column", new Dictionary<string, object?> { ["id"] = SqlRowStore.Id(lookupField) })).Single(), "control_detail"));
            Assert.Equal(1, SqlRowStore.Id((await store.ReadAsync("config_page_column", new Dictionary<string, object?> { ["id"] = fixture.FieldId })).Single(), "is_in_filter"));
        }
        finally
        {
            SetCandidateProject(client, Project);
            if (extraField > 0) await store.UpdateAsync("config_page_column", extraField, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(Project) });
            if (fixture is not null) await DeleteCandidateTable(client, fixture.TableId);
        }
    }

    [MySqlApiFact]
    public async Task Editor_metadata_preserves_the_saved_definition_and_rejects_inactive_or_foreign_owners()
    {
        var (factory, client) = await Login(); await using var disposable = factory; using var http = client;
        var store = factory.Services.GetRequiredService<SqlRowStore>(); var ids = factory.Services.GetRequiredService<IIdGenerator>();
        CandidateTable? fixture = null; var role = ids.NextId(); var account = ids.NextId();
        try
        {
            fixture = await CreateCandidateTable(client, store);
            var original = " {\n  \"table\":\"" + fixture.TableName + "\", \"whereSql\":\"is_enabled = 1 AND name = '原条件'\", \"orderBy\":\"sort_no asc,id desc\", \"futureOption\":{\"保留\":true}\n } ";
            await store.UpdateAsync("config_column", fixture.ColumnId, new Dictionary<string, object?> { ["control_kind"] = "List", ["control_detail"] = original });
            var detail = Data(await Post(client, CandidateRoute + "control_metadata", new { id = fixture.ColumnId.ToString() }));
            Assert.Equal(new[] { "columnName", "columnType", "controlDetail", "controlKind", "name" }, detail.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal("candidate_value", detail.GetProperty("columnName").GetString()); Assert.Equal("List", detail.GetProperty("controlKind").GetString());
            Assert.Equal(original, detail.GetProperty("controlDetail").GetString());
            // 专用字段继承不能成为绕过标准页面父范围或接受客户端项目/连接的入口。
            Assert.Equal(500, (await Body(await client.GetAsync("/api/admin/auto/config_column/6217715532627968/detail/" + fixture.ColumnId))).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, CandidateRoute + "control_metadata", new { id = fixture.ColumnId.ToString(), projectId = Project })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, CandidateRoute + "control_metadata", new { id = ids.NextId().ToString() })).GetProperty("code").GetInt32());
            var foreign = factory.Services.GetRequiredService<MetadataRegistry>().Platform.YzProjectId;
            SetCandidateProject(client, foreign);
            Assert.Equal(500, (await Post(client, CandidateRoute + "control_metadata", new { id = fixture.ColumnId.ToString() })).GetProperty("code").GetInt32());
            SetCandidateProject(client, Project);
            // 只改本测试的唯一配置行，分别覆盖字段软删、父表软删与父表项目冲突。
            await store.UpdateAsync("config_column", fixture.ColumnId, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
            Assert.Equal(500, (await Post(client, CandidateRoute + "control_metadata", new { id = fixture.ColumnId.ToString() })).GetProperty("code").GetInt32());
            await store.UpdateAsync("config_column", fixture.ColumnId, new Dictionary<string, object?> { ["delete_no"] = null });
            await store.UpdateAsync("config_table", fixture.TableId, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
            Assert.Equal(500, (await Post(client, CandidateRoute + "control_metadata", new { id = fixture.ColumnId.ToString() })).GetProperty("code").GetInt32());
            await store.UpdateAsync("config_table", fixture.TableId, new Dictionary<string, object?> { ["delete_no"] = null, ["yz_project_id"] = long.Parse(foreign) });
            Assert.Equal(500, (await Post(client, CandidateRoute + "control_metadata", new { id = fixture.ColumnId.ToString() })).GetProperty("code").GetInt32());
            await store.UpdateAsync("config_table", fixture.TableId, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(Project) });
            Assert.Equal(original, Data(await Post(client, CandidateRoute + "control_metadata", new { id = fixture.ColumnId.ToString() })).GetProperty("controlDetail").GetString());
            var username = "candidate-ordinary-" + account; var password = Guid.NewGuid().ToString("N");
            await store.InsertAsync("yz_role", new Dictionary<string, object?> { ["id"] = role, ["name"] = "候选接口普通角色", ["is_super"] = 0, ["yz_project_id"] = long.Parse(Project), ["yz_admin_type_id"] = 2200000000000100001L });
            await store.InsertAsync("yz_manager", new Dictionary<string, object?> { ["id"] = account, ["name"] = username, ["password"] = Metarill.Runtime.Security.PasswordHasher.Hash(password), ["yz_role_ids"] = JsonSerializer.Serialize(new[] { role.ToString() }), ["is_disabled"] = 0, ["yz_project_id"] = long.Parse(Project), ["yz_admin_type_id"] = 2200000000000100001L });
            using var ordinary = factory.CreateClient();
            var login = await Body(await ordinary.PostAsync("/api/admin/sys/yz_manager/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = password }))); Data(login);
            ordinary.DefaultRequestHeaders.Add("loginStateId", login.GetProperty("map").GetProperty("loginStateId").GetString()); SetCandidateProject(ordinary, Project);
            foreach (var action in new[] { "list_control_tables", "select_page_options", "references", "clear_page_control_detail", "control_metadata" })
                Assert.Equal(500, (await Post(ordinary, CandidateRoute + action, action is "list_control_tables" or "select_page_options" ? new { } : (object)new { id = fixture.ColumnId.ToString() })).GetProperty("code").GetInt32());
        }
        finally
        {
            SetCandidateProject(client, Project);
            if (fixture is not null)
            {
                await store.UpdateAsync("config_column", fixture.ColumnId, new Dictionary<string, object?> { ["delete_no"] = null });
                await store.UpdateAsync("config_table", fixture.TableId, new Dictionary<string, object?> { ["delete_no"] = null, ["yz_project_id"] = long.Parse(Project) });
                await DeleteCandidateTable(client, fixture.TableId);
            }
            foreach (var session in await store.ReadAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = account }))
                await store.UpdateAsync("sys_session", SqlRowStore.Id(session), new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
            await store.UpdateAsync("yz_manager", account, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
            await store.UpdateAsync("yz_role", role, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
        }
    }

    private sealed record CandidateTable(long TableId, string TableName, long PageId, long ColumnId, long LookupId, long FieldId);

    /// <summary>通过现有配置 API 建唯一表和批量字段，测试只改自己拥有的配置行，不创建业务物理表。</summary>
    private static async Task<CandidateTable> CreateCandidateTable(HttpClient client, SqlRowStore store)
    {
        var name = "mt_candidates_" + Guid.NewGuid().ToString("N")[..12];
        var added = Data(await Post(client, CandidateTableRoute + "/add", new { name = "候选接口验收", tableName = name, yzAdminTypeId = "2200000000000100001", isRongyu = 1 }));
        var id = long.Parse(added.GetProperty("id").GetString()!);
        try
        {
            Data(await Post(client, "/admin/custom/index/save_batch_create_columns", new { config_table_id = id.ToString(), content = "目标字段 candidate_value\n关联字段 lookup_value" }));
            var columns = await store.ReadAsync("config_column", new Dictionary<string, object?> { ["config_table_id"] = id });
            var column = columns.Single(row => SqlRowStore.Text(row, "column_name") == "candidate_value");
            var lookup = columns.Single(row => SqlRowStore.Text(row, "column_name") == "lookup_value");
            var page = (await store.ReadAsync("config_page", new Dictionary<string, object?> { ["config_table_id"] = id })).Single();
            var field = (await store.ReadAsync("config_page_column", new Dictionary<string, object?> { ["config_column_id"] = SqlRowStore.Id(column) })).Single();
            return new(id, name, SqlRowStore.Id(page), SqlRowStore.Id(column), SqlRowStore.Id(lookup), SqlRowStore.Id(field));
        }
        catch { await DeleteCandidateTable(client, id); throw; }
    }
    /// <summary>仅移除本测试唯一配置聚合，清理由统一删除观察器执行，不操作物理业务表。</summary>
    private static async Task DeleteCandidateTable(HttpClient client, long id) => Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api" + CandidateTableRoute) { Content = JsonContent.Create(new { id = id.ToString() }) })));
    /// <summary>切换请求上下文只测试真实身份允许的项目选择，不把请求体项目当作权限。</summary>
    private static void SetCandidateProject(HttpClient client, string project)
    { client.DefaultRequestHeaders.Remove("sysParams"); client.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = project }))); }
}
