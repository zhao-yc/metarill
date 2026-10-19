using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>真实候选按显式类型分离，并验证父表/项目/软删和严格输入，不放宽标准父范围。</summary>
    [MySqlApiFact]
    public async Task Multi_selection_page_candidates_keep_type_and_real_parent_ownership_without_opening_standard_page_scope()
    {
        var (factory, client) = await Login(); await using var disposable = factory; using var http = client;
        var store = factory.Services.GetRequiredService<SqlRowStore>(); CandidateTable? fixture = null;
        try
        {
            fixture = await CreateCandidateTable(client, store);
            await store.UpdateAsync("config_page", fixture.PageId, new Dictionary<string, object?> { ["type"] = 2 });
            var selectedId = fixture.PageId.ToString();
            // 无参数旧合同仍仅单选；明确多选只返回真实type=2，且不能泄漏控件/SQL等额外字段。
            Assert.DoesNotContain(Data(await Post(client, CandidateRoute + "select_page_options", new { })).EnumerateArray(), option => option.GetProperty("id").GetString() == selectedId);
            Assert.DoesNotContain(Data(await Post(client, CandidateRoute + "select_page_options", new { controlKind = "SelectPage" })).EnumerateArray(), option => option.GetProperty("id").GetString() == selectedId);
            var options = Data(await Post(client, CandidateRoute + "select_page_options", new { controlKind = "MultiSelectPage" })).EnumerateArray().ToArray();
            Assert.Contains(options, option => option.GetProperty("id").GetString() == selectedId);
            Assert.All(options, option => Assert.Equal(new[] { "id", "name", "text", "value" }, option.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray()));
            using var form = await client.PostAsync("/api" + CandidateRoute + "select_page_options", new FormUrlEncodedContent(new Dictionary<string, string> { ["controlKind"] = "MultiSelectPage" }));
            Assert.Contains(Data(await Body(form)).EnumerateArray(), option => option.GetProperty("id").GetString() == selectedId);
            foreach (var invalid in new object?[] { null, 2, "2", "", "MultiSelectPage ", "MultiCheckBoxDatabase", new[] { "MultiSelectPage" } })
                Assert.Equal(500, (await Post(client, CandidateRoute + "select_page_options", new { controlKind = invalid })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, CandidateRoute + "select_page_options", new { controlKind = "MultiSelectPage", projectId = Project })).GetProperty("code").GetInt32());
            using var duplicate = await client.PostAsync("/api" + CandidateRoute + "select_page_options", new StringContent("{\"controlKind\":\"SelectPage\",\"controlKind\":\"MultiSelectPage\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(500, (await Body(duplicate)).GetProperty("code").GetInt32());
            // 页和真实父表必须同时有效且属于认证项目，不因多选候选扩成无父范围的标准读取。
            foreach (var owner in new[] { "config_page", "config_table" })
            {
                var id = owner == "config_page" ? fixture.PageId : fixture.TableId;
                await store.UpdateAsync(owner, id, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
                Assert.DoesNotContain(Data(await Post(client, CandidateRoute + "select_page_options", new { controlKind = "MultiSelectPage" })).EnumerateArray(), option => option.GetProperty("id").GetString() == selectedId);
                await store.UpdateAsync(owner, id, new Dictionary<string, object?> { ["delete_no"] = null });
            }
            var foreign = factory.Services.GetRequiredService<MetadataRegistry>().Platform.YzProjectId;
            await store.UpdateAsync("config_table", fixture.TableId, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(foreign) });
            Assert.DoesNotContain(Data(await Post(client, CandidateRoute + "select_page_options", new { controlKind = "MultiSelectPage" })).EnumerateArray(), option => option.GetProperty("id").GetString() == selectedId);
            await store.UpdateAsync("config_table", fixture.TableId, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(Project) });
            SetCandidateProject(client, foreign);
            Assert.DoesNotContain(Data(await Post(client, CandidateRoute + "select_page_options", new { controlKind = "MultiSelectPage" })).EnumerateArray(), option => option.GetProperty("id").GetString() == selectedId);
            using var anonymous = factory.CreateClient();
            Assert.Equal(500, (await Post(anonymous, CandidateRoute + "select_page_options", new { controlKind = "MultiSelectPage" })).GetProperty("code").GetInt32());
        }
        finally
        {
            SetCandidateProject(client, Project);
            if (fixture is not null)
            {
                await store.UpdateAsync("config_page", fixture.PageId, new Dictionary<string, object?> { ["delete_no"] = null });
                await store.UpdateAsync("config_table", fixture.TableId, new Dictionary<string, object?> { ["delete_no"] = null, ["yz_project_id"] = long.Parse(Project) });
                await DeleteCandidateTable(client, fixture.TableId);
            }
        }
    }
}
