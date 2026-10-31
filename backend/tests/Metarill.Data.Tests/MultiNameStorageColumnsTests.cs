using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>自动缓存必须在结构、实体和仓储之前就有一致且不可由输入伪造的物理计划。</summary>
public sealed class MultiNameStorageColumnsTests
{
    /// <summary>两类根声明生成稳定物理文本数组缓存，输入和原页面配置保持原样。</summary>
    [Theory][InlineData("MultiSelectPage","name","target_names")][InlineData("MultiSelectPage","status","target_status")][InlineData("MultiCheckBoxDatabase","name","target_names")]
    public void Redundant_cache_is_deterministic_physical_nullable_text_without_metadata_writeback(string kind,string label,string expected)
    {
        var catalog=Catalog(kind,label);var saved=JsonSerializer.Serialize(catalog);var table=catalog.ConfigTables[0];var expanded=RelationStorageColumns.Expand(table,catalog.ConfigColumns);var cache=Assert.Single(expanded,column=>column.ColumnName==expected);Assert.StartsWith(MultiNameStorageColumns.ReservedIdPrefix,cache.Id);Assert.Equal("max",cache.ColumnLength);Assert.Equal("string",cache.LogicalType);Assert.Equal(0,cache.IsDbNotNull);Assert.Equal("target."+label,cache.Extra["columnFullName"].GetString());
        Assert.Equal(cache.Id,RelationStorageColumns.Expand(table,catalog.ConfigColumns).Single(column=>column.ColumnName==expected).Id);Assert.Equal(cache.Id,RelationStorageColumns.Expand(table,expanded).Single(column=>column.ColumnName==expected).Id);var plan=MetadataStoragePolicy.ForTable(catalog,table);Assert.True(plan.IsPhysical(cache));var schema=MySqlSchemaPlanner.Compile(catalog,"test");Assert.Contains(schema.Tables[0].Columns,column=>column.Name==expected&&column.Nullable);Assert.Equal(saved,JsonSerializer.Serialize(catalog));Assert.Empty(catalog.ConfigPageColumns);
    }
    /// <summary>显式名称列包括虚拟/错误类型/大小写冲突都保留，不借自动规则绕过配置缺口。</summary>
    [Theory][InlineData("target_names","int64",0)][InlineData("target_names","string",1)][InlineData("TARGET_NAMES","string",0)]
    public void Explicit_names_always_take_precedence(string name,string type,int virtualColumn)
    {
        var catalog=Catalog();catalog.ConfigColumns.Add(new(){Id="explicit",ConfigTableId="1",ColumnName=name,LogicalType=type,IsVirtualColumn=virtualColumn});var expanded=RelationStorageColumns.Expand(catalog.ConfigTables[0],catalog.ConfigColumns);Assert.Equal(3,expanded.Count);Assert.DoesNotContain(expanded,column=>column.Id.StartsWith(MultiNameStorageColumns.ReservedIdPrefix,StringComparison.Ordinal));
    }
    /// <summary>非冗余、派生来源、全局/安全表和ID展示不能被擅自改为物理缓存。</summary>
    [Theory][InlineData("nonredundant")][InlineData("view")][InlineData("function")][InlineData("global")][InlineData("virtual")][InlineData("join")][InlineData("integer")][InlineData("identifier")][InlineData("idDisplay")][InlineData("badJson")][InlineData("duplicate")][InlineData("caseKey")][InlineData("objectLabel")][InlineData("manager")][InlineData("roleId")]
    public void Unsupported_or_different_storage_is_not_guessed(string kind)
    {
        var catalog=Catalog();var table=catalog.ConfigTables[0];var source=catalog.ConfigColumns[1];
        if(kind=="nonredundant")table.IsRongyu=0;if(kind=="view")table.IsView=1;if(kind=="function")table.IsFunctionTable=1;if(kind=="global")table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="virtual")source.IsVirtualColumn=1;if(kind=="join")source.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="integer")source.LogicalType="int64";if(kind=="identifier")source.ColumnName="target";if(kind=="idDisplay")source.ControlDetail="{\"nameField\":\"id\"}";if(kind=="badJson")source.ControlDetail="[";if(kind=="duplicate")source.ControlDetail="{\"nameField\":\"name\",\"nameField\":\"name\"}";if(kind=="caseKey")source.ControlDetail="{\"NameField\":\"name\"}";if(kind=="objectLabel")source.ControlDetail="{\"nameField\":{}}";if(kind=="manager")table.TableName="yz_manager";if(kind=="roleId"){table.Id="1692532858499969026";foreach(var column in catalog.ConfigColumns)column.ConfigTableId=table.Id;}
        Assert.Equal(2,RelationStorageColumns.Expand(table,catalog.ConfigColumns).Count);
    }
    /// <summary>根配置不能伪造自动列稳定ID；入口在DDL或输出前拒绝。</summary>
    [Fact]
    public void Input_cannot_forge_reserved_identity()
    {
        var catalog=Catalog();catalog.ConfigColumns[1].Id=MultiNameStorageColumns.ReservedIdPrefix+"forged";Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Compile(catalog,"test"));
    }
    /// <summary>只含一张普通源表的结构模型，目标用途另由生成与真实HTTP验证。</summary>
    private static MetadataCatalog Catalog(string kind="MultiSelectPage",string label="name")=>new(){ConfigTables=[new(){Id="1",TableName="cache_source"}],ConfigColumns=[new(){Id="id",ConfigTableId="1",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="source",ConfigTableId="1",ColumnName="target_ids",LogicalType="string",ColumnLength="max",ControlKind=kind,ControlDetail=JsonSerializer.Serialize(new{nameField=label})}]};
}
