using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>验证真实结构编译/草案的时间精度与审计默认；不连接数据库或执行DDL。</summary>
public sealed class SchemaTemporalDefinitionTests
{
    /// <summary>只建立显式物理类型的元数据，不创建连接。</summary>
    private static ColumnMetadata Column(string kind,int precision,string name="value",string value="")=>new(){Id="4",ConfigTableId="2",ColumnName=name,ColumnType=kind+"("+precision+")",LogicalType=kind,DefaultValue=value};
    /// <summary>最小完整结构保留真实主键，后续调用实际规划器。</summary>
    private static MetadataCatalog Catalog(ColumnMetadata value)=>new(){YzProjectId="1",ConfigTables=[new(){Id="2",TableName="temporal_record",YzProjectId="1"}],ConfigColumns=[new(){Id="3",ConfigTableId="2",ColumnName="id",LogicalType="int64",ColumnType="bigint",IsDbNotNull=1},value]};
    /// <summary>零精度不能升级成六位；每种时间精度经过真正DDL生成及反向草案双向零差异。</summary>
    [Theory][InlineData("datetime")][InlineData("time")]
    public void Explicit_precision_roundtrips_through_real_compiler_and_draft(string kind)
    {
        for(var precision=0;precision<=6;precision++)
        {
            var catalog=Catalog(Column(kind,precision));var before=JsonSerializer.Serialize(catalog);
            var schema=MySqlSchemaPlanner.Compile(catalog,"temporal_test");var type=precision==0?kind:kind+"("+precision+")";
            Assert.Equal(type,schema.Tables[0].Columns.Single(column=>column.Name=="value").StoreType);
            Assert.Contains("`value` "+type+" NULL",Assert.Single(MySqlSchemaPlanner.Compare(new("temporal_test",[]),schema)).Sql);
            var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"read-only"),[]),"1");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues.Select(issue=>issue.Message)));
            var restored=MySqlSchemaPlanner.Compile(draft.LogicalMetadata,"temporal_test");Assert.Empty(MySqlSchemaPlanner.Compare(schema,restored));Assert.Empty(MySqlSchemaPlanner.Compare(restored,schema));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
    }
    /// <summary>旧长度50/255从未表示时间精度，裸类型继续六位；更窄类型不自动迁移已有数据。</summary>
    [Theory][InlineData("")][InlineData("50")][InlineData("255")]
    public void Legacy_lengths_keep_six_digits_and_precision_changes_are_not_automatic(string length)
    {
        var column=Column("datetime",6);column.ColumnType="datetime";column.ColumnLength=length;
        Assert.Equal("datetime(6)",MySqlSchemaPlanner.CompileColumn(column).StoreType);
        var old=MySqlSchemaPlanner.Compile(Catalog(column),"temporal_test");var desired=MySqlSchemaPlanner.Compile(Catalog(Column("datetime",0)),"temporal_test");
        Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Compare(old,desired));Assert.NotEqual(MySqlSchemaPlanner.Fingerprint(old),MySqlSchemaPlanner.Fingerprint(desired));
    }
    /// <summary>注入尾句、超范围、不同时间种类均拒绝，不能退回默认精度。</summary>
    [Theory][InlineData("datetime(7)")][InlineData("datetime(-1)")][InlineData("datetime(03)")][InlineData("datetime(3);DROP TABLE x")][InlineData("time(3)")][InlineData("datetime(3)\n")]
    public void Malformed_or_conflicting_precision_is_rejected(string declaration)
    {
        var column=Column("datetime",0);column.ColumnType=declaration;Assert.Throws<ArgumentException>(()=>MySqlSchemaPlanner.CompileColumn(column));
    }
    /// <summary>只有原两个审计字段能声明固定当前时间，函数精度必须匹配列，实际草案往返无损。</summary>
    [Theory][InlineData("insert_time")][InlineData("update_time")]
    public void Audit_current_timestamp_aliases_roundtrip_without_raw_expression_support(string name)
    {
        for(var precision=0;precision<=6;precision++)
        {
            var value=precision==0?"now()":"NOW("+precision+")";var catalog=Catalog(Column("datetime",precision,name,value));
            var schema=MySqlSchemaPlanner.Compile(catalog,"temporal_test");var canonical=precision==0?"CURRENT_TIMESTAMP":"CURRENT_TIMESTAMP("+precision+")";
            Assert.Equal(canonical,schema.Tables[0].Columns.Single(column=>column.Name==name).DefaultSql);
            var alias=schema with{Tables=[schema.Tables[0] with{Columns=schema.Tables[0].Columns.Select(column=>column.Name==name?column with{DefaultSql=value}:column).ToArray()}]};
            Assert.Equal(MySqlSchemaPlanner.Fingerprint(schema),MySqlSchemaPlanner.Fingerprint(alias));Assert.Empty(MySqlSchemaPlanner.Compare(alias,schema));
            var draft=SchemaImportDraftCompiler.Compile(new(new("mysql",schema,"read-only"),[]),"1");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues.Select(issue=>issue.Message)));Assert.Empty(MySqlSchemaPlanner.Compare(schema,MySqlSchemaPlanner.Compile(draft.LogicalMetadata,"temporal_test")));
        }
    }
    /// <summary>动态默认不扩大到普通业务字段、软删列、其它函数、不同精度或生成列。</summary>
    [Theory][InlineData("value","now()")][InlineData("delete_no","now()")][InlineData("insert_time","now(6)")][InlineData("insert_time","CURRENT_TIMESTAMP + INTERVAL 1 DAY")][InlineData("insert_time","now();DROP TABLE x")]
    public void Unproven_dynamic_defaults_are_rejected(string name,string value)=>Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.CompileColumn(Column("datetime",0,name,value)));
    /// <summary>基础设施同名字段若时间精度不同不能被物理合并去重隐藏。</summary>
    [Fact]
    public void Physical_composition_checks_effective_temporal_precision()
    {
        var original=Catalog(Column("datetime",0));var different=Catalog(Column("datetime",3));
        Assert.Throws<InvalidOperationException>(()=>MetadataCatalogComposition.MergePhysical(original,different));
        var equivalent=Catalog(Column("datetime",6));equivalent.ConfigColumns[1].ColumnType="datetime";
        Assert.NotNull(MetadataCatalogComposition.MergePhysical(equivalent,Catalog(Column("datetime",6))));
    }
}
