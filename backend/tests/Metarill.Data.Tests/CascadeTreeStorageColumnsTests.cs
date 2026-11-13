using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>自动名称定义与显式结构计划共用，保留显式字段、不修改元数据或增加页面配置。</summary>
public sealed class CascadeTreeStorageColumnsTests
{
    /// <summary>原字符串长度继承，整数ID扩展使用50；名字可空无默认值且稳定标识不随配置顺序变化。</summary>
    [Theory]
    [InlineData("string","120","120")]
    [InlineData("STRING","120","120")]
    [InlineData("string","","50")]
    [InlineData("int64","20","50")]
    public void Managed_name_is_deterministic_and_shared_by_physical_schema(string type,string length,string expected)
    {
        var catalog=Catalog(type,length);var saved=JsonSerializer.Serialize(catalog);var table=catalog.ConfigTables[0];var first=MetadataStoragePolicy.ForTable(catalog,table).PhysicalColumns;var name=Assert.Single(first,column=>column.ColumnName=="tree_name");Assert.StartsWith(CascadeTreeStorageColumns.ReservedIdPrefix,name.Id);Assert.Equal("string",name.LogicalType);Assert.Equal(expected,name.ColumnLength);Assert.Equal(0,name.IsDbNotNull);Assert.Equal("",name.DefaultValue);
        Assert.Equal(name.Id,Assert.Single(CascadeTreeStorageColumns.Expand(table,catalog.ConfigColumns.AsEnumerable().Reverse()),column=>column.ColumnName=="tree_name").Id);
        Assert.Equal(first.Count,CascadeTreeStorageColumns.Expand(table,first).Count);
        var schema=MySqlSchemaPlanner.Compile(catalog,"test");Assert.Equal("varchar("+expected+")",Assert.Single(schema.Tables[0].Columns,column=>column.Name=="tree_name").StoreType);Assert.Equal(saved,JsonSerializer.Serialize(catalog));
    }
    /// <summary>显式名称的长度、类型及虚拟归属不被派生覆盖，错误配置留给正式编译拒绝。</summary>
    [Fact]
    public void Explicit_columns_win_and_nonphysical_or_other_modes_are_not_expanded()
    {
        foreach(var type in new[]{"string","int64"})
        {
            var catalog=Catalog();catalog.ConfigColumns.Add(new(){Id="name",ConfigTableId="1",ColumnName="tree_name",LogicalType=type,ColumnLength="15"});var expanded=CascadeTreeStorageColumns.Expand(catalog.ConfigTables[0],catalog.ConfigColumns);Assert.Equal(catalog.ConfigColumns.Count,expanded.Count);Assert.Equal(type,expanded.Single(column=>column.ColumnName=="tree_name").LogicalType);
        }
        foreach(var mutate in new Action<MetadataCatalog>[]
        {
            c=>c.ConfigTables[0].IsFunctionTable=1,c=>c.ConfigTables[0].IsView=1,c=>c.ConfigColumns[1].IsVirtualColumn=1,c=>c.ConfigColumns[1].Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(true),c=>c.ConfigColumns[1].ControlDetail="{}",c=>c.ConfigColumns[1].ControlDetail="{invalid",c=>c.ConfigColumns[1].ControlDetail="{\"sourceMode\":\"selfTree\",\"sourceMode\":\"crossTable\",\"selfTree\":{}}"
        }){var catalog=Catalog();mutate(catalog);Assert.DoesNotContain(CascadeTreeStorageColumns.Expand(catalog.ConfigTables[0],catalog.ConfigColumns),column=>column.ColumnName=="tree_name");}
    }
    /// <summary>非冗余展开只读逻辑名称，物理结构、输入配置和现有ID不增加名称列。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Nonredundant_name_is_logical_only_and_does_not_change_schema_or_metadata(string type)
    {
        var catalog=Catalog(type);catalog.ConfigTables[0].IsRongyu=0;var saved=JsonSerializer.Serialize(catalog);var expanded=CascadeTreeStorageColumns.Expand(catalog.ConfigTables[0],catalog.ConfigColumns);var name=Assert.Single(expanded,column=>column.ColumnName=="tree_name");
        Assert.Equal(MetadataFieldStorage.RelationDisplay,MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables[0]).Kind(name));Assert.DoesNotContain(MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables[0]).PhysicalColumns,column=>column.ColumnName=="tree_name");Assert.DoesNotContain(MySqlSchemaPlanner.Compile(catalog,"test").Tables[0].Columns,column=>column.Name=="tree_name");Assert.Equal(saved,JsonSerializer.Serialize(catalog));
    }
    /// <summary>用户不能伪造派生字段稳定ID，配置校验在任何结构或生成写入之前拒绝。</summary>
    [Fact]
    public void Reserved_derived_identifier_cannot_be_declared_by_input_metadata()
    {
        var catalog=Catalog();catalog.ConfigColumns.Add(new(){Id=CascadeTreeStorageColumns.ReservedIdPrefix+"forged",ConfigTableId="1",ColumnName="business_value",LogicalType="string"});Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Compile(catalog,"test"));
    }
    /// <summary>仅一张受管实体表与明确selfTree声明，无连接、账号或请求参数。</summary>
    private static MetadataCatalog Catalog(string type="string",string length="120")=>new(){YzProjectId="p",ConfigTables=[new(){Id="1",TableName="cascade_source"}],ConfigColumns=[new(){Id="id",ConfigTableId="1",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="leaf",ConfigTableId="1",ColumnName="tree_id",LogicalType=type,ColumnLength=length,ControlKind="CascadeTree",ControlDetail="{\"sourceMode\":\"selfTree\",\"selfTree\":{\"table\":\"target\",\"parentField\":\"parent_id\"}}"}]};
}
