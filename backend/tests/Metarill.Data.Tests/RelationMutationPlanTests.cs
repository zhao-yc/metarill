using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>传播只接收发布目录的稳定字段ID，提前冻结结构，不能从请求拼SQL或扩大物理写集合。</summary>
public sealed class RelationMutationPlanTests
{
    /// <summary>编译结果与配置顺序无关，修改原目录不污染已发布计划。</summary>
    [Fact]
    public void Plan_is_deterministic_frozen_and_deduplicates_identical_bindings()
    {
        var catalog=Catalog();var definition=Definition();var first=RelationMutationPlan.Compile(catalog,[definition]);catalog.ConfigTables.Reverse();catalog.ConfigColumns.Reverse();Assert.Equal(first.Fingerprint,RelationMutationPlan.Compile(catalog,[definition,definition]).Fingerprint);var sha=first.Fingerprint;catalog.ConfigTables[0].TableName="changed_after_compile";catalog.ConfigColumns[0].ColumnName="changed_after_compile";Assert.Equal(sha,first.Fingerprint);
    }
    /// <summary>普通物理名称缓存和原明确短／完整别名均可证明，不把isJoin当任意写许可。</summary>
    [Theory][InlineData("plain")][InlineData("short")][InlineData("full")]
    public void Physical_cache_accepts_only_same_relation_shape(string kind)
    {
        var catalog=Catalog();var cache=catalog.ConfigColumns.Single(column=>column.Id=="cache");if(kind!="plain"){cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement(kind=="short"?"target.name":"target_ids.name");}Assert.Equal(64,RelationMutationPlan.Compile(catalog,[Definition()]).Fingerprint.Length);
    }
    /// <summary>归属、物理结构和类型不成立时整个计划拒绝，不能部分下发或静默跳过。</summary>
    [Theory][InlineData("sourceRealm")][InlineData("fieldRealm")][InlineData("global")][InlineData("view")][InlineData("idType")][InlineData("projectType")][InlineData("projectJoin")][InlineData("cacheVirtual")][InlineData("cacheType")][InlineData("foreignJoin")][InlineData("foreignType")][InlineData("targetVirtual")][InlineData("targetJson")][InlineData("cacheAlias")][InlineData("cachePath")][InlineData("differentTarget")][InlineData("softDelete")][InlineData("provider")][InlineData("targetArray")][InlineData("cacheTable")][InlineData("cachePage")]
    public void Invalid_definition_is_rejected_before_any_execution(string kind)
    {
        var catalog=Catalog();var source=catalog.ConfigTables[0];var foreign=catalog.ConfigColumns.Single(column=>column.Id=="foreign");var cache=catalog.ConfigColumns.Single(column=>column.Id=="cache");var value=catalog.ConfigColumns.Single(column=>column.Id=="value");
        if(kind=="sourceRealm")source.YzProjectId="other";if(kind=="fieldRealm")cache.YzProjectId="other";if(kind=="global")source.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="view")source.IsView=1;if(kind=="idType")catalog.ConfigColumns.Single(column=>column.Id=="source_id").LogicalType="string";if(kind=="projectType")catalog.ConfigColumns.Single(column=>column.Id=="source_project").LogicalType="bool";if(kind=="projectJoin")catalog.ConfigColumns.Single(column=>column.Id=="source_project").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="cacheVirtual")cache.IsVirtualColumn=1;if(kind=="cacheType")cache.LogicalType="int64";if(kind=="foreignJoin")foreign.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="foreignType")foreign.LogicalType="int64";if(kind=="targetVirtual")value.IsVirtualColumn=1;if(kind=="targetJson")value.LogicalType="json";if(kind=="cacheAlias")cache.ColumnName="unrelated_names";if(kind=="cachePath"){cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("other.name");}if(kind=="differentTarget")foreign.LinkConfigTableId="source";if(kind=="softDelete")catalog.ConfigColumns.Add(new(){Id="bad_delete",ConfigTableId="source",ColumnName="delete_no",LogicalType="int64"});if(kind=="provider")catalog.DatabaseProvider="postgresql";if(kind=="targetArray")value.ControlKind="FileLibrary";if(kind=="cacheTable")cache.Extra["joinTable"]=JsonSerializer.SerializeToElement("other");if(kind=="cachePage")cache.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("other");Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(catalog,[Definition()]));
    }
    /// <summary>凭据即使是物理字符串也不能成为名称缓存；检查独立于生成器。</summary>
    [Fact]
    public void Credential_cannot_be_materialized_as_name()
    {
        var catalog=Catalog();catalog.ConfigTables[1].TableName="yz_manager";catalog.ConfigColumns.Single(column=>column.Id=="value").ColumnName="password";catalog.ConfigColumns.Single(column=>column.Id=="cache").ColumnName="target_passwords";Assert.Throws<InvalidOperationException>(()=>RelationMutationPlan.Compile(catalog,[Definition()]));
    }
    /// <summary>执行器不能悄悄新开连接／事务，调用方必须先建立完整业务事务。</summary>
    [Fact]
    public async Task Propagation_requires_an_existing_transaction()
    {
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Database=offline;User ID=unused;Password=unused"});await Assert.ThrowsAsync<InvalidOperationException>(()=>new RelationMutationPropagator(session).AfterMutationAsync(RelationMutationPlan.Compile(Catalog(),[Definition()]),"target",1,"54",new HashSet<string>{"name"},false));
    }
    /// <summary>同样物理名称的不同发布项目不能共用指纹。</summary>
    [Fact]
    public void Fingerprint_binds_published_project()
    {
        var catalog=Catalog();var first=RelationMutationPlan.Compile(catalog,[Definition()]);catalog.YzProjectId="55";Assert.NotEqual(first.Fingerprint,RelationMutationPlan.Compile(catalog,[Definition()]).Fingerprint);
    }
    /// <summary>固定字段ID，不以表名／列名作为可写请求输入。</summary>
    private static RelationCacheDefinition Definition()=>new("source","foreign","target","value","cache",true);
    /// <summary>最小同项目元数据，仅描述结构，不连接数据库。</summary>
    private static MetadataCatalog Catalog()=>new(){YzProjectId="54",ConfigTables=[new(){Id="source",TableName="source"},new(){Id="target",TableName="target"}],ConfigColumns=[new(){Id="source_id",ConfigTableId="source",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="source_project",ConfigTableId="source",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="target_id",ConfigTableId="target",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="target_project",ConfigTableId="target",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="foreign",ConfigTableId="source",ColumnName="target_ids",LogicalType="string",ColumnLength="max",ControlKind="MultiCheckBoxDatabase",LinkConfigTableId="target"},new(){Id="value",ConfigTableId="target",ColumnName="name",LogicalType="string",ColumnLength="100"},new(){Id="cache",ConfigTableId="source",ColumnName="target_names",LogicalType="string",ColumnLength="max"}]};
}
