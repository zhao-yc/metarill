using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>自树固定_name叶子缓存支持物理自定义名称；不能借控制类型替任意副本取得传播许可。</summary>
public sealed class SelfTreeCacheTests
{
    /// <summary>四种外键组合及普通R0／R1物理目标、同表书签共享同一稳定字段证明。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public void Physical_leaf_cache_uses_declared_name_and_does_not_mutate_metadata(string type,string parent)
    {
        foreach(var mode in new[]{"r1","r0","same"})
        {
            var c=Catalog(type,parent);if(mode=="r0")c.ConfigTables.Single(t=>t.Id=="target").IsRongyu=0;
            if(mode=="same"){var foreign=c.ConfigColumns.Single(x=>x.Id=="foreign");foreign.LinkConfigTableId="source";foreign.ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table="cache_source",parentField="parent_node_id",nameField="name"}});c.ConfigColumns.Add(new(){Id="source_parent",ConfigTableId="source",ColumnName="parent_node_id",LogicalType=parent,LinkConfigTableId="source"});}
            var saved=JsonSerializer.Serialize(c);var definition=SelfTreeCache.Compile(c,"source","foreign");Assert.False(definition.Multiple);Assert.Null(definition.CrossTableColumnId);var plan=RelationMutationPlan.Compile(c,[definition]);Assert.Equal("cache",definition.CacheColumnId);Assert.Equal(mode=="same"?"source_name":"value",definition.ValueColumnId);Assert.Equal(mode=="same"?"source":"target",definition.TargetTableId);Assert.NotEmpty(plan.Fingerprint);Assert.Equal(saved,JsonSerializer.Serialize(c));
        }
    }
    /// <summary>坏定义、范围、非物理拓扑及凭据不会因为后缀匹配而成为可写缓存。</summary>
    [Theory][InlineData("mode")][InlineData("where")][InlineData("globalSource")][InlineData("globalTarget")][InlineData("sourceR0")][InlineData("virtualForeign")][InlineData("joinValue")][InlineData("virtualValue")][InlineData("wrongLink")][InlineData("badParent")][InlineData("credential")][InlineData("cacheLink")][InlineData("textType")]
    public void Unproven_leaf_definition_never_compiles(string kind)
    {
        var c=Catalog("string","int64");var foreign=c.ConfigColumns.Single(x=>x.Id=="foreign");var value=c.ConfigColumns.Single(x=>x.Id=="value");
        if(kind=="mode")foreign.ControlDetail="{\"sourceMode\":\"crossTable\"}";if(kind=="where")foreign.ControlDetail=foreign.ControlDetail.Replace("\"selfTree\":{","\"whereSql\":\"1=1\",\"selfTree\":{");if(kind=="globalSource")c.ConfigTables[0].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="globalTarget")c.ConfigTables[1].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="sourceR0")c.ConfigTables[0].IsRongyu=0;if(kind=="virtualForeign")foreign.IsVirtualColumn=1;if(kind=="joinValue")value.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="virtualValue")value.IsVirtualColumn=1;if(kind=="wrongLink")foreign.LinkConfigTableId="source";if(kind=="badParent")c.ConfigColumns.Single(x=>x.Id=="parent").LinkConfigTableId="source";if(kind=="credential"){value.ColumnName="password";foreign.ControlDetail=foreign.ControlDetail.Replace("caption","password");}if(kind=="cacheLink")c.ConfigColumns.Single(x=>x.Id=="cache").LinkConfigTableId="target";if(kind=="textType")foreign.ControlDetail=foreign.ControlDetail.Replace("\"caption\"","123");Assert.ThrowsAny<Exception>(()=>SelfTreeCache.Compile(c,"source","foreign"));
    }
    /// <summary>修改目标值或借另一个缓存列不能绕过控制定义，即使普通后缀检查恰好一致。</summary>
    [Fact]public void Forged_definition_cannot_borrow_another_name_or_cache()
    {
        var c=Catalog("int64","string");var d=SelfTreeCache.Compile(c,"source","foreign");c.ConfigColumns.Add(new(){Id="alias",ConfigTableId="target",ColumnName="alias",LogicalType="string"});c.ConfigColumns.Add(new(){Id="other_cache",ConfigTableId="source",ColumnName="tree_alias",LogicalType="string"});Assert.Throws<NotSupportedException>(()=>RelationMutationPlan.Compile(c,[d with{ValueColumnId="alias",CacheColumnId="other_cache"}]));
    }
    /// <summary>旧受管名称只有join标记时由固定selfTree补足；显式错路径仍拒绝。</summary>
    [Fact]public void Legacy_managed_marker_needs_no_invented_join_path()
    {
        var c=Catalog("string","int64");var cache=c.ConfigColumns.Single(x=>x.Id=="cache");cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);var d=SelfTreeCache.Compile(c,"source","foreign");Assert.NotEmpty(RelationMutationPlan.Compile(c,[d]).Fingerprint);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("tree.unrelated");Assert.Throws<NotSupportedException>(()=>SelfTreeCache.Compile(c,"source","foreign"));
    }
    /// <summary>执行计划只保留冻结字段与类型，配置重排或随后编辑不能在线改写计划。</summary>
    [Fact]public void Published_plan_remains_frozen()
    {
        var c=Catalog("string","string");var d=SelfTreeCache.Compile(c,"source","foreign");var plan=RelationMutationPlan.Compile(c,[d]);var sha=plan.Fingerprint;c.ConfigColumns.Reverse();Assert.Equal(sha,RelationMutationPlan.Compile(c,[d]).Fingerprint);c.ConfigTables[1].TableName="edited_node";c.ConfigColumns.Single(x=>x.Id=="value").ColumnName="edited_name";Assert.Equal("target",d.TargetTableId);Assert.Equal("value",d.ValueColumnId);Assert.Equal(sha,plan.Fingerprint);
    }
    /// <summary>纯目录模型不需要数据库，固定实际父键和自定义物理名称。</summary>
    private static MetadataCatalog Catalog(string type,string parent)
    {
        var c=new MetadataCatalog{YzProjectId="1"};c.ConfigTables.AddRange([new(){Id="source",TableName="cache_source",IsRongyu=1},new(){Id="target",TableName="cache_node",IsRongyu=1}]);
        c.ConfigColumns.AddRange([new(){Id="source_id",ConfigTableId="source",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="source_name",ConfigTableId="source",ColumnName="name",LogicalType="string"},new(){Id="source_project",ConfigTableId="source",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="foreign",ConfigTableId="source",ColumnName="tree_id",LogicalType=type,ControlKind="CascadeTree",LinkConfigTableId="target",ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table="cache_node",parentField="parent_node_id",nameField="caption"}})},new(){Id="cache",ConfigTableId="source",ColumnName="tree_name",LogicalType="string"},new(){Id="target_id",ConfigTableId="target",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="value",ConfigTableId="target",ColumnName="caption",LogicalType="string"},new(){Id="target_project",ConfigTableId="target",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="parent",ConfigTableId="target",ColumnName="parent_node_id",LogicalType=parent,LinkConfigTableId="target"}]);return c;
    }
}
