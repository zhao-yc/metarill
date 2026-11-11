using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class FreshInstallMySqlTests
{
    /// <summary>用原表配置入口及独立实库读回证明默认值、合法首次选择和保存后冻结。</summary>
    private static async Task VerifyTableStorageModeAsync(HttpClient client, SqlRowStore store, MetadataCatalog platform)
    {
        const string route = "/api/admin/auto/config_table/6191083005411328";
        const string invalidMode = "冗余模式仅支持 0 或 1";
        const string frozenMode = "配置表首次保存后不能修改冗余模式，请新建配置表";
        const string invalidTable = "V8 非冗余模式仅支持实体表，视图和函数表不能选择此模式";
        var prefix = "mt_storage_" + Guid.NewGuid().ToString("N")[..12] + "_";
        Dictionary<string, object?> Create(string suffix) => new()
        {
            ["name"] = "存储模式验收",
            ["tableName"] = prefix + suffix,
            ["yzAdminTypeId"] = platform.YzAdminTypes.Single().Id
        };
        async Task<string> AddAsync(Dictionary<string, object?> payload, int expectedMode)
        {
            var row = (await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/add", payload)).GetProperty("data");
            Assert.Equal(expectedMode, row.GetProperty("isRongyu").GetInt32());
            return row.GetProperty("id").GetString()!;
        }
        async Task RejectAddAsync(string suffix, int mode, string message, string? flag = null)
        {
            var payload = Create(suffix);
            payload["isRongyu"] = mode;
            if (flag is not null) payload[flag] = 1;
            await DeniedAsync(client, HttpMethod.Post, route + "/add", payload, message);
            Assert.Empty(await store.ReadIncludingDeletedAsync("config_table", new Dictionary<string, object?> { ["table_name"] = prefix + suffix }));
        }
        async Task<Dictionary<string, object?>> ReadAsync(string id) => Assert.Single(await store.ReadAsync("config_table",
            new Dictionary<string, object?> { ["id"] = long.Parse(id, CultureInfo.InvariantCulture) }));

        var redundant = await AddAsync(Create("default"), 1);
        var withNull = Create("null");
        withNull["isRongyu"] = null;
        var explicitNull = await AddAsync(withNull, 1);
        var nonRedundant = Create("explicit");
        nonRedundant["isRongyu"] = 0;
        var sparse = await AddAsync(nonRedundant, 0);
        await RejectAddAsync("range", 2, invalidMode);
        await RejectAddAsync("view", 0, invalidTable, "isView");
        await RejectAddAsync("function", 0, invalidTable, "isFunctionTable");

        // 原整实体会回传未变的模式，普通稀疏更新也应继续可用。
        await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/update", new { id = redundant, name = "冗余配置可编辑", isRongyu = 1 });
        await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/update", new { id = sparse, name = "非冗余配置可编辑" });
        var redundantBefore = await ReadAsync(redundant);
        var sparseBefore = await ReadAsync(sparse);
        Assert.Equal("1", SqlRowStore.Text(redundantBefore, "is_rongyu"));
        Assert.Equal("0", SqlRowStore.Text(sparseBefore, "is_rongyu"));
        Assert.Equal("1", SqlRowStore.Text(await ReadAsync(explicitNull), "is_rongyu"));
        Assert.Equal("冗余配置可编辑", SqlRowStore.Text(redundantBefore, "name"));
        Assert.Equal("非冗余配置可编辑", SqlRowStore.Text(sparseBefore, "name"));

        await DeniedAsync(client, HttpMethod.Post, route + "/update", new { id = redundant, isRongyu = 0, name = "不得覆盖" }, frozenMode);
        await DeniedAsync(client, HttpMethod.Post, route + "/update", new { id = sparse, isRongyu = 1, name = "不得覆盖" }, frozenMode);
        await DeniedAsync(client, HttpMethod.Post, route + "/update", new { id = sparse, isRongyu = (int?)null }, invalidMode);
        await DeniedAsync(client, HttpMethod.Post, route + "/update", new { id = sparse, isRongyu = 2 }, invalidMode);
        await DeniedAsync(client, HttpMethod.Post, route + "/update", new { id = sparse, isView = 1 }, invalidTable);
        await DeniedAsync(client, HttpMethod.Post, route + "/update", new { id = sparse, isFunctionTable = 1 }, invalidTable);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(redundantBefore), JsonSerializer.SerializeToElement(await ReadAsync(redundant))));
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(sparseBefore), JsonSerializer.SerializeToElement(await ReadAsync(sparse))));
    }
}
