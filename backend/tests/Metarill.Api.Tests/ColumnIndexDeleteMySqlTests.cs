using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>只准备真实Server-200原HTTP/生成Service/索引工作台回归；没有配置时明确跳过，不执行DDL。</summary>
public sealed class ColumnIndexDeleteMySqlTests
{
    /// <summary>所有正常写操作经原HTTP元数据生命周期；只在确认完整自有图且测试成功后精确清理。</summary>
    [MySqlApiFact]
    public async Task Original_http_rejects_active_and_disabled_index_references_then_allows_removed_references_and_table_aggregate()
    {
        await using var f=await MaterializedDerivedApiFixture.CreateAsync();
        var table=await f.CreateTable("column_index_delete");var added=await f.AddColumns(table,"索引字段 indexed_value");
        Assert.True(added.Length==1,"本次新增字段数量必须为1。");var column=added[0];
        for(var enabled=1;enabled>=0;enabled--)
        {
            await SaveIndex(f,table,column,enabled);await f.Capture();var before=await f.Snapshot();
            var response=await DeleteResponse(f,table,column);
            Assert.Equal(500,response.GetProperty("code").GetInt32());Assert.Contains("字段已被索引引用",response.GetProperty("msg").GetString());
            Assert.True(before==await f.Snapshot(),"被索引引用的删列失败必须保留自有完整元数据图。");
        }
        var snapshot=MaterializedDerivedApiFixture.Data(await f.Post("/admin/custom/config_table/index_config",new{configTableId=Text(table)}));
        await f.Capture();
        var cleared=MaterializedDerivedApiFixture.Data(await f.Post("/admin/custom/config_table/save_indexes",new{configTableId=Text(table),revision=snapshot.GetProperty("revision").GetString(),indexes=Array.Empty<object>()}));
        Assert.True(cleared.GetProperty("indexes").GetArrayLength()==0,"移除索引回执必须为空。");
        await f.RegisterIndexDelete(table,true);await f.Capture();
        var oldChildren=await f.Store.ReadIncludingDeletedAsync("config_index_column",new Dictionary<string,object?>{{"yz_project_id",f.Project},{"config_column_id",column}});
        Assert.True(oldChildren.Count==1,"自有索引字段数量必须为1。");Assert.NotNull(oldChildren[0]["delete_no"]);
        await f.Delete(MaterializedDerivedApiFixture.ColumnRoute,column,Text(table));
        Assert.NotNull((await f.Row("config_column",column,true))["delete_no"]);
        var pageFields=await f.Store.ReadIncludingDeletedAsync("config_page_column",new Dictionary<string,object?>{{"yz_project_id",f.Project},{"config_column_id",column}});
        Assert.True(pageFields.Count>0,"原字段必须已有自有页面映射用于验证删除联动。");Assert.All(pageFields,field=>Assert.NotNull(field["delete_no"]));
        Assert.NotNull((await f.Row("config_index_column",SqlRowStore.Id(oldChildren[0]),true))["delete_no"]);

        var whole=await f.CreateTable("whole_index_delete");var wholeColumns=await f.AddColumns(whole,"聚合字段 indexed_value");
        Assert.True(wholeColumns.Length==1,"聚合样本新增字段数量必须为1。");await SaveIndex(f,whole,wholeColumns[0],1);await f.Capture();
        var ownIndexes=await f.Store.ReadAsync("config_index",new Dictionary<string,object?>{{"yz_project_id",f.Project},{"config_table_id",whole}});
        Assert.True(ownIndexes.Count==1,"自有聚合索引数量必须为1。");var indexId=SqlRowStore.Id(ownIndexes[0]);
        var ownChildren=await f.Store.ReadAsync("config_index_column",new Dictionary<string,object?>{{"yz_project_id",f.Project},{"config_index_id",indexId}});
        Assert.True(ownChildren.Count==1,"自有聚合索引字段数量必须为1。");var childId=SqlRowStore.Id(ownChildren[0]);
        await f.Capture();await f.Delete(MaterializedDerivedApiFixture.TableRoute,whole);await f.RegisterIndexDelete(whole,false);await f.Capture();
        Assert.NotNull((await f.Row("config_index",indexId,true))["delete_no"]);Assert.NotNull((await f.Row("config_index_column",childId,true))["delete_no"]);
        Assert.NotNull((await f.Row("config_column",wholeColumns[0],true))["delete_no"]);
        Assert.True((await f.Store.ReadAsync("config_column",new Dictionary<string,object?>{{"yz_project_id",f.Project},{"config_table_id",whole}})).Count==0,"整表删除不能残留活动字段。");
        await f.SaveEvidence("column-index-delete-parity",new{activeReferenceRefused=true,disabledReferenceRefused=true,failedDeletePreservedWholeGraph=true,removedReferenceAllowed=true,ownTableAggregateAllowed=true,noDdl=true,onlyOwnedMetadata=true});
        await f.CompleteAsync();
        // 清理额外验证包含墓碑的精确随机项目图；断言只输出计数，不输出元数据行。
        foreach(var project in new[]{f.Project,f.ForeignProject})
        {
            foreach(var name in new[]{"config_index_column","config_index","config_page_column","config_page","config_column","config_table","yz_admin_type"})
                Assert.True((await f.Store.ReadIncludingDeletedAsync(name,new Dictionary<string,object?>{{"yz_project_id",project}})).Count==0,"自有元数据含墓碑必须零残留。");
            Assert.True((await f.Store.ReadIncludingDeletedAsync("yz_project",new Dictionary<string,object?>{{"id",project}})).Count==0,"自有随机项目必须零残留。");
        }
        await f.SaveEvidence("column-index-delete-cleanup",new{ownedRemaining=0,unrelatedPlatformGraphTouched=false});
    }

    /// <summary>创建与禁用都走原索引聚合保存，不直接插写索引或伪造其引用关系。</summary>
    private static async Task SaveIndex(MaterializedDerivedApiFixture f,long table,long column,int enabled)
    {
        var snapshot=MaterializedDerivedApiFixture.Data(await f.Post("/admin/custom/config_table/index_config",new{configTableId=Text(table)}));
        await f.Capture();
        var existing=snapshot.GetProperty("indexes").EnumerateArray().ToArray();Assert.True(existing.Length<=1,"自有样本不应混入其它索引。");
        var id=existing.Length==0?"":existing[0].GetProperty("id").GetString();
        var fields=existing.Length==0?[]:existing[0].GetProperty("columns").EnumerateArray().ToArray();Assert.True(fields.Length<=1,"自有样本索引不应混入其它字段。");
        var child=fields.Length==0?"":fields[0].GetProperty("id").GetString();
        var saved=MaterializedDerivedApiFixture.Data(await f.Post("/admin/custom/config_table/save_indexes",new
        {
            configTableId=Text(table),revision=snapshot.GetProperty("revision").GetString(),
            indexes=new[]{new{id,indexName="yzp_idx_delete_guard",indexType="NORMAL",isEnabled=enabled,
                columns=new[]{new{id=child,configColumnId=Text(column),columnName="indexed_value",sortNo=0,sortDirection="ASC"}}}}
        }));
        await f.RegisterIndexSave(saved,table,column,"yzp_idx_delete_guard",enabled);
    }

    /// <summary>原DELETE body与parentId保持不变，读取明确的失败响应而不将其转换为测试成功。</summary>
    private static async Task<JsonElement> DeleteResponse(MaterializedDerivedApiFixture f,long parent,long id)
    {
        using var response=await f.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api"+MaterializedDerivedApiFixture.ColumnRoute+"?parentId="+Text(parent)){Content=JsonContent.Create(new{id=Text(id)})});
        return await MaterializedDerivedApiFixture.Body(response);
    }
    /// <summary>所有元数据长ID按原字符串协议写入HTTP载荷。</summary>
    private static string Text(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
