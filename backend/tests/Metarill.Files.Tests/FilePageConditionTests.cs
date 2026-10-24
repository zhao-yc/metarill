using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Files;
using MySqlConnector;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>文件范围只消费发布物理列，固定常量与真实父范围同时成立，不解释请求SQL。</summary>
public sealed class FilePageConditionTests
{
    /// <summary>规范AST保留字符串标点与精确整数，常量只产生参数，父定义仍独立验证。</summary>
    [Fact]
    public void Published_scope_compiles_parent_and_typed_constants_without_opening_database()
    {
        var catalog=Catalog("parent_id=@parentId AND status IN (1,2,NULL) AND name='A AND B,O''Neil' AND id>=9007199254740993");
        var scope=FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],catalog.ConfigPages[0]);Assert.Single(scope.Parents);Assert.Equal(new[]{"status","name","id"},scope.ConditionFields);
        using var session=Offline();using var command=new MySqlCommand();var sql=scope.Condition!.BindConstants(command,session,"files");Assert.DoesNotContain("O'Neil",sql);Assert.DoesNotContain("9007199254740993",sql);Assert.Contains("NULL",sql);
        Assert.Contains(command.Parameters.Cast<System.Data.Common.DbParameter>(),p=>Equals(p.Value,9007199254740993L));
    }
    /// <summary>未接线的语法或值整页拒绝，不能只执行其中合法的父条件。</summary>
    [Theory]
    [InlineData("status=@current_yz_manager_id")][InlineData("status=@formParameters.status")]
    [InlineData("status IN(@parentId,1)")][InlineData("status=1 OR id=2")][InlineData("status=1;DELETE FROM child")]
    [InlineData("missing=1")][InlineData("status=2147483648")][InlineData("status=1.5")][InlineData("name=1")]
    [InlineData("attachments IS NOT NULL")][InlineData("extra_json IS NULL")]
    public void Unproved_conditions_reject_the_whole_published_file_source(string condition)
    {var catalog=Catalog("parent_id=@parentId AND "+condition);Assert.Throws<NotSupportedException>(()=>FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],catalog.ConfigPages[0]));}
    /// <summary>输入及无效来源不能改变已缓存计划，另一个合法来源仍保持可用。</summary>
    [Fact]
    public void Context_freezes_plans_and_rejects_bad_or_foreign_sources()
    {
        var catalog=Catalog("status=2");catalog.ConfigPages.Add(new(){Id="4",ConfigTableId="1",WhereSql="status=@unknown"});var context=new FileMetadataContext([catalog]);var frozen=context.Catalogs.Single();
        var scope=context.PageScope(frozen,frozen.ConfigPages[0]);Assert.Same(scope,context.PageScope(frozen,frozen.ConfigPages[0]));catalog.ConfigPages[0].WhereSql="status=3";
        Assert.Throws<InvalidOperationException>(()=>context.PageScope(catalog,catalog.ConfigPages[0]));Assert.Throws<NotSupportedException>(()=>context.PageScope(frozen,frozen.ConfigPages[1]));
        using var session=Offline();using var command=new MySqlCommand();scope.Condition!.BindConstants(command,session,"page");Assert.Equal(2,Assert.Single(command.Parameters.Cast<System.Data.Common.DbParameter>()).Value);
    }
    /// <summary>非冗余派生条件及多选名称JSON不能借字符串类型进入标量范围。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Derived_or_multi_names_remain_rejected(bool derived)
    {
        var catalog=Catalog(derived?"derived_name IS NOT NULL":"target_names IS NOT NULL");
        if(derived){catalog.ConfigTables[0].IsRongyu=0;catalog.ConfigColumns.Add(new(){Id="41",ConfigTableId="1",ColumnName="derived_name",Extra=new(){{"isJoinColumn",System.Text.Json.JsonSerializer.SerializeToElement(1)},{"columnFullName",System.Text.Json.JsonSerializer.SerializeToElement("parent.name")}}});}
        else{catalog.ConfigColumns.Add(new(){Id="41",ConfigTableId="1",ColumnName="target_ids",ControlKind="MultiSelectPage"});catalog.ConfigColumns.Add(new(){Id="42",ConfigTableId="1",ColumnName="target_names"});}
        Assert.Throws<NotSupportedException>(()=>FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],catalog.ConfigPages[0]));
    }
    /// <summary>只有ORM构建和参数编译，端口不可连接，不能作为实库证据。</summary>
    private static DatabaseSession Offline()=>new(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
    /// <summary>所有条件列与父关系先来自发布模型，不手写实体或Mapper。</summary>
    private static MetadataCatalog Catalog(string definition)=>new()
    {
        YzProjectId="1",ConfigTables=[new(){Id="1",TableName="child"},new(){Id="2",TableName="parent"}],ConfigPages=[new(){Id="3",ConfigTableId="1",WhereSql=definition}],
        ConfigColumns=[new(){Id="11",ConfigTableId="1",ColumnName="id",LogicalType="int64"},new(){Id="12",ConfigTableId="1",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="13",ConfigTableId="1",ColumnName="parent_id",LogicalType="int64",LinkConfigTableId="2"},new(){Id="14",ConfigTableId="1",ColumnName="status",LogicalType="int32"},new(){Id="15",ConfigTableId="1",ColumnName="name",LogicalType="string"},new(){Id="16",ConfigTableId="1",ColumnName="attachments",LogicalType="string",ControlKind="FileLibrary"},new(){Id="17",ConfigTableId="1",ColumnName="extra_json",LogicalType="json"},new(){Id="21",ConfigTableId="2",ColumnName="id",LogicalType="int64"},new(){Id="22",ConfigTableId="2",ColumnName="yz_project_id",LogicalType="int64"}]
    };
}
