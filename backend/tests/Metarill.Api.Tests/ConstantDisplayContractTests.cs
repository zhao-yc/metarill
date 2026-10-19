using System.Net.Http.Json;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    [MySqlApiFact]
    public async Task Generated_constant_labels_are_readonly_and_keep_unknown_values_visible()
    {
        var (factory, client) = await Login(); await using var disposable = factory; using var http = client;
        const string route = "/admin/auto/config_table/6191083005411328";
        string? tableId = null;
        try
        {
            var table = Data(await Post(client, route + "/add", new { name = "常量展示验收", tableName = "display_" + Guid.NewGuid().ToString("N")[..16], yzAdminTypeId = "2200000000000100001", isRongyu = 1 }));
            tableId = table.GetProperty("id").GetString();
            Data(await Post(client, "/admin/custom/index/save_batch_create_columns", new { config_table_id = tableId, content = "显示备注 display_notes" }));
            var page = Data(await Post(client, "/admin/auto/config_column/6217715532627968/page", new { parentId = tableId, pageSize = 100, filters = new { } }));
            var records = page.GetProperty("records").EnumerateArray().ToArray();
            var known = records.Single(record => record.GetProperty("columnName").GetString() == "display_notes");
            Assert.Equal("nvarchar", known.GetProperty("columnType").GetString());
            Assert.Equal("字符", known.GetProperty("columnTypeShow").GetString());
            // 逻辑类型不在旧常量清单时仍显示原值，避免空白误导配置人员。
            var unknown = records.Single(record => record.GetProperty("columnName").GetString() == "id");
            Assert.Equal("int64", unknown.GetProperty("columnTypeShow").GetString());
            Assert.Equal(500, (await Post(client, "/admin/auto/config_column/6217715532627968/update", new { id = known.GetProperty("id").GetString(), columnTypeShow = "篡改展示" })).GetProperty("code").GetInt32());
        }
        finally
        {
            // 验收只新建元数据；删除仅清理本用例唯一表及其从属记录，不执行 DDL。
            if (tableId is not null) Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api" + route) { Content = JsonContent.Create(new { id = tableId }) })));
        }
    }
}
