using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>跨表物理列共用计划，不回写元数据，也不静默覆盖已有业务列。</summary>
public sealed class CrossTableStorageColumnsTests
{
    /// <summary>五个缺失辅助列稳定派生，ID沿叶子逻辑类型，名称为空且无业务默认值。</summary>
    [Theory]
    [InlineData("string")]
    [InlineData("int64")]
    public void All_levels_expand_once_with_stable_ids_and_original_catalog_untouched(string type)
    {
        var catalog=Catalog(type);var original=JsonSerializer.Serialize(catalog);var table=Assert.Single(catalog.ConfigTables);var first=MetadataStoragePolicy.ForTable(catalog,table);var again=MetadataStoragePolicy.ForTable(catalog,table);Assert.Equal(8,first.PhysicalColumns.Count);Assert.Equal(first.PhysicalColumns.Select(column=>column.Id),again.PhysicalColumns.Select(column=>column.Id));
        foreach(var field in first.PhysicalColumns.Where(column=>column.Id.StartsWith(CrossTableStorageColumns.ReservedIdPrefix))){Assert.Equal(0,field.IsDbNotNull);Assert.Equal("",field.DefaultValue);Assert.True(first.IsPhysical(field));Assert.Equal(field.ColumnName.EndsWith("_id")?type:"string",field.LogicalType);}
        Assert.Equal(original,JsonSerializer.Serialize(catalog));Assert.Equal(5,first.PhysicalColumns.Count(column=>column.Id.StartsWith(CrossTableStorageColumns.ReservedIdPrefix)));Assert.Equal(8,CascadeTreeStorageColumns.Expand(table,first.PhysicalColumns).Count);
    }
    /// <summary>错误类型或虚拟显式列优先保留，生成器负责拒绝，不擅自修复存量列。</summary>
    [Fact]
    public void Explicit_conflicts_nonredundant_and_reserved_ids_are_not_silently_rewritten()
    {
        var catalog=Catalog();catalog.ConfigColumns.Add(new(){Id="4",ConfigTableId="1",ColumnName="country_name",LogicalType="int64",IsVirtualColumn=1});var table=Assert.Single(catalog.ConfigTables);var plan=MetadataStoragePolicy.ForTable(catalog,table);var field=Assert.Single(CascadeTreeStorageColumns.Expand(table,catalog.ConfigColumns),column=>column.ColumnName=="country_name");Assert.Equal("4",field.Id);Assert.Equal("int64",field.LogicalType);Assert.False(plan.IsPhysical(field));
        table.IsRongyu=0;Assert.DoesNotContain(MetadataStoragePolicy.ForTable(catalog,table).PhysicalColumns,column=>column.Id.StartsWith(CrossTableStorageColumns.ReservedIdPrefix));
        var reserved=Catalog();reserved.ConfigColumns[1].Id=CrossTableStorageColumns.ReservedIdPrefix+"forged";Assert.Throws<InvalidOperationException>(()=>MetadataValidation.Validate(reserved));
    }
    /// <summary>系统字段、重复前缀与末级不匹配不能派生额外实体字段。</summary>
    [Fact]
    public void Invalid_shapes_never_guess_physical_columns()
    {
        foreach(var replace in new[]{("\"country\"","\"yz_project\""),("\"place\"","\"country\""),("\"crossTable\"","\"unknown\"")}){var catalog=Catalog();catalog.ConfigColumns[2].ControlDetail=catalog.ConfigColumns[2].ControlDetail.Replace(replace.Item1,replace.Item2);Assert.Equal(3,MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables[0]).PhysicalColumns.Count);}
    }
    /// <summary>存储层只需要来源表及结构定义，目标访问权限留给固定生成计划。</summary>
    private static MetadataCatalog Catalog(string type="string")
    {
        var catalog=new MetadataCatalog{YzProjectId="1"};catalog.ConfigTables.Add(new(){Id="1",TableName="entry",IsRongyu=1});catalog.ConfigColumns.AddRange([new(){Id="1",ConfigTableId="1",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="2",ConfigTableId="1",ColumnName="name",LogicalType="string"},new(){Id="3",ConfigTableId="1",ColumnName="place_id",LogicalType=type,ColumnLength=type=="string"?"64":"",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{new{table="country",columnPrefix="country"},new{table="area",columnPrefix="area",parentField="country_id"},new{table="place",columnPrefix="place",parentField="area_id"}}})}]);return catalog;
    }
}
