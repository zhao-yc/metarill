using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>混合依赖图不依赖请求或存量缓存值，字段循环和未证明路径不能进入运行时队列。</summary>
public sealed class MixedRelationMutationPlanTests
{
    /// <summary>R0中间名称没有物理副本，但其中间外键和最终目标均成为真实变更来源。</summary>
    [Fact]
    public void Nonredundant_intermediate_registers_each_real_target_and_freezes_inputs()
    {
        var model=Chain();var catalog=RelationMaintenanceCatalog.Compile(model);Assert.Equal(new[]{"maintenance_company","maintenance_target"},catalog.MixedTargets);Assert.Equal(new[]{"target_name"},catalog.MixedSourceColumns["maintenance_source"]);var sha=catalog.Fingerprint;model.ConfigTables[1].TableName="changed_after_compile";model.ConfigColumns.Clear();Assert.Equal(new[]{"maintenance_company","maintenance_target"},catalog.MixedTargets);Assert.Equal(sha,catalog.Fingerprint);Assert.Throws<NotSupportedException>(()=>((IList<string>)catalog.MixedSourceColumns["maintenance_source"])[0]="cannot_change");
    }
    /// <summary>多角色到同一目标不能合并外键路径，输出快照必须保留两个独立缓存。</summary>
    [Fact]
    public void Two_roles_to_same_target_keep_both_source_columns()
    {
        var model=RelationMaintenanceCatalogTests.Catalog();model.ConfigColumns.Add(new(){Id="alternate",ConfigTableId="source",ColumnName="alternate_id",LogicalType="int64",LinkConfigTableId="target"});model.ConfigColumns.Add(new(){Id="alternate_name",ConfigTableId="source",ColumnName="alternate_name",LogicalType="string",ColumnLength="100",Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement("alternate.name")}}});var catalog=RelationMaintenanceCatalog.Compile(model);Assert.Single(catalog.MixedTargets);Assert.Equal(new[]{"alternate_name","target_name"},catalog.MixedSourceColumns["maintenance_source"]);
    }
    /// <summary>R1副本之间的字段循环不能靠运行时重复重算收敛，发布阶段必须拒绝。</summary>
    [Fact]
    public void Physical_cache_cycle_is_rejected_before_any_connection()
    {
        var model=RelationMaintenanceCatalogTests.Catalog();model.ConfigTables[1].IsRongyu=1;model.ConfigColumns.Add(new(){Id="back",ConfigTableId="target",ColumnName="source_id",LogicalType="int64",LinkConfigTableId="source"});var name=model.ConfigColumns.Single(column=>column.Id=="target_name");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("source.target_name");Assert.Throws<InvalidOperationException>(()=>RelationMaintenanceCatalog.Compile(model));
    }
    /// <summary>平台专用协调消费者按稳定ID排除，维护读取计划仍可存在，不能重复挂通用写入。</summary>
    [Fact]
    public void Exclusion_removes_automatic_consumer_without_removing_maintenance_option()
    {
        var model=Chain();var normal=RelationMaintenanceCatalog.Compile(model);var excluded=RelationMaintenanceCatalog.Compile(model,excludedMixedConsumers:["source"]);Assert.NotEqual(normal.Fingerprint,excluded.Fingerprint);Assert.Empty(excluded.MixedTargets);Assert.Empty(excluded.MixedSourceColumns);Assert.Contains(excluded.Tables,item=>item.Id=="source");
    }
    /// <summary>不能因一条路径可编译而忽略同表损坏派生列；自动传播与维护采用相同的整表门禁。</summary>
    [Fact]
    public void Invalid_peer_prevents_partial_automatic_plan()
    {
        var model=Chain();model.ConfigColumns.Add(new(){Id="invalid",ConfigTableId="source",ColumnName="invalid_name",LogicalType="string",Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement("missing.name")}}});var catalog=RelationMaintenanceCatalog.Compile(model);Assert.Empty(catalog.MixedTargets);Assert.Empty(catalog.MixedSourceColumns);
    }
    /// <summary>原事务缺失必须在数据库连接前拒绝，自动传播不能自行建立或提交事务。</summary>
    [Fact]
    public async Task Coordinator_requires_callers_transaction()
    {
        using var database=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused"});await Assert.ThrowsAsync<InvalidOperationException>(()=>new ProjectRelationMutationPropagator(database).AfterMutationAsync(RelationMaintenanceCatalog.Compile(Chain()),"maintenance_company",1,"54",new HashSet<string>{"name"},false,false));
    }
    /// <summary>中间表不落name列；来源仍保留目标ID与唯一物理缓存，最终目标为普通实体。</summary>
    internal static MetadataCatalog Chain()
    {
        var model=RelationMaintenanceCatalogTests.Catalog();var middleName=model.ConfigColumns.Single(column=>column.Id=="target_name");middleName.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);middleName.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");model.ConfigColumns.Add(new(){Id="company_ref",ConfigTableId="target",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="company"});model.ConfigTables.Add(new(){Id="company",TableName="maintenance_company",IsRongyu=1});model.ConfigColumns.AddRange([new(){Id="company_id",ConfigTableId="company",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="company_project",ConfigTableId="company",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="company_deleted",ConfigTableId="company",ColumnName="delete_no",LogicalType="datetime"},new(){Id="company_name",ConfigTableId="company",ColumnName="name",LogicalType="string",ColumnLength="100"}]);return model;
    }
}
