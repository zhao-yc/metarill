using System.Text.Json;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>索引工作台候选和保存复用与DDL同一物理计划，仍保留原禁止联接/布局索引规则。</summary>
public sealed class IndexNonRedundantStorageTests
{
    /// <summary>非冗余审计名派生，冗余审计名真实存储；联接标志不因冗余开启而变为可索引。</summary>
    [Theory]
    [InlineData(0,false)][InlineData(1,true)]
    public void Indexable_columns_respect_storage_mode_and_original_join_restriction(int redundant,bool auditAllowed)
    {
        var table=new Dictionary<string,object?>{{"id",1L},{"table_name","orders"},{"is_rongyu",redundant}};
        var rows=new[]{Row(11,"id"),Row(12,"name"),Row(13,"update_manager_real_name"),Row(14,"customer_notes",("is_join_column",1)),Row(15,"section",("control_kind","Title"))};var ids=IndexWorkbenchBusiness.CompileIndexableColumns(table,rows).Select(row=>SqlRowStore.Id(row)).ToArray();Assert.Contains(11L,ids);Assert.Contains(12L,ids);Assert.Equal(auditAllowed,ids.Contains(13));Assert.DoesNotContain(14L,ids);Assert.DoesNotContain(15L,ids);
    }
    /// <summary>级联祖先ID也不是本表物理列，不能因它名字以_id结尾就作为索引候选。</summary>
    [Fact]
    public void Cascade_ancestors_are_excluded_from_nonredundant_index_options()
    {
        var table=new Dictionary<string,object?>{{"id",1L},{"table_name","orders"},{"is_rongyu",0}};var rows=new[]{Row(11,"id"),Row(12,"country_id"),Row(13,"city_id",("control_kind","CascadeTree"),("control_detail","{\"levels\":[{\"table\":\"country\"},{\"table\":\"city\"}]}")),Row(14,"country_name"),Row(15,"city_name")};Assert.Equal(new[]{11L,13L},IndexWorkbenchBusiness.CompileIndexableColumns(table,rows).Select(row=>SqlRowStore.Id(row)));
    }
    /// <summary>真实MySQL配置工作台不落非法派生索引；冗余模式的原合法审计索引可保存，均不执行DDL。</summary>
    [MetadataIntegrationFact]
    public Task Nonredundant_index_workbench_rejects_derived_audit_index() => CheckWorkbench(0,false);
    /// <summary>冗余模式沿原审计物理列候选及保存协议，不额外封禁。</summary>
    [MetadataIntegrationFact]
    public Task Redundant_index_workbench_keeps_original_audit_index() => CheckWorkbench(1,true);
    /// <summary>每个场景仅修改fixture自己的配置行，保存仍走真实项目锁和完整索引事务。</summary>
    private static async Task CheckWorkbench(int redundant,bool allowed)
    {
        await using var f=await IndexWorkbenchFixture.CreateAsync();var metadata=f.Metadata;await metadata.Store.UpdateAsync("config_table",metadata.TableId,new Dictionary<string,object?>{{"is_rongyu",redundant}});await metadata.Store.UpdateAsync("config_column",metadata.SecondColumnId,new Dictionary<string,object?>{{"column_name","update_manager_real_name"}});
        var snapshot=await f.Snapshot();Assert.Equal(allowed,snapshot.ColumnOptions.Any(column=>column.ConfigColumnId==metadata.SecondColumnId.ToString()));var request=IndexWorkbenchFixture.Payload(snapshot,f.Index(name:"yzp_idx_audit",column:"update_manager_real_name",columnId:metadata.SecondColumnId.ToString()));
        if(allowed){var saved=await f.Business().SaveAsync(request);Assert.Single(saved.Indexes);}else{await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Business().SaveAsync(request));Assert.Empty(await metadata.Store.ReadAsync("config_index",new Dictionary<string,object?>{{"config_table_id",metadata.TableId}}));}
    }
    /// <summary>输入只包含索引分类所需的自有配置字段，Id保留真实long后由统一元数据解码。</summary>
    private static Dictionary<string,object?> Row(long id,string name,params(string Key,object? Value)[] fields)
    {var row=new Dictionary<string,object?>{{"id",id},{"config_table_id",1L},{"column_name",name}};foreach(var field in fields)row[field.Key]=field.Value;return row;}
}
