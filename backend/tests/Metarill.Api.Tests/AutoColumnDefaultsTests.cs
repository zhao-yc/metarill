using System.Net.Http.Json;
using Metarill.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    [MySqlApiFact]
    public async Task Automatically_created_system_columns_have_editable_layout_defaults_and_stable_order()
    {
        var (factory,client)=await Login(); await using var host=factory; using var http=client;
        const string tableRoute="/admin/auto/config_table/6191083005411328";
        string? tableId=null;
        try
        {
            tableId=Data(await Post(client,tableRoute+"/add",new{name="自动字段默认值验收",tableName="mt_default_"+Guid.NewGuid().ToString("N"),yzAdminTypeId="2200000000000100001",isRongyu=1})).GetProperty("id").GetString();
            var store=factory.Services.GetRequiredService<SqlRowStore>();
            var columns=await store.ReadAsync("config_column",new Dictionary<string,object?>{{"config_table_id",long.Parse(tableId!)}});
            Assert.Equal(8,columns.Count);
            Assert.All(columns,column=>{Assert.Equal(1,Convert.ToInt32(column["col_span"]));Assert.Equal(1,Convert.ToInt32(column["mobile_list_page_col_span"]));});
            Assert.Equal(8,columns.Select(column=>Convert.ToInt32(column["sort_no"])).Distinct().Count());
            Assert.All(columns,column=>Assert.True(Convert.ToInt32(column["sort_no"])>0));
            var name=columns.Single(column=>SqlRowStore.Text(column,"column_name")=="name");
            var updated=Data(await Post(client,"/admin/auto/config_column/6217715532627968/update?parentId="+tableId,new{id=SqlRowStore.Text(name,"id"),name="名称字段回写",colSpan=name["col_span"],mobileListPageColSpan=name["mobile_list_page_col_span"]}));
            Assert.Equal("名称字段回写",updated.GetProperty("name").GetString());
        }
        finally
        {
            if(tableId is not null)Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api"+tableRoute){Content=JsonContent.Create(new{id=tableId})})));
        }
    }
}
