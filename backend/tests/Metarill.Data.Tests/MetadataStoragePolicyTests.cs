using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>结构与仓储共享的原非冗余字段归属，不将派生项反建为物理列。</summary>
public sealed class MetadataStoragePolicyTests
{
    /// <summary>冗余模式按原虚拟标志保存，不将原联接/审计/布局字段一并删除。</summary>
    [Fact]
    public void Redundant_mode_keeps_existing_storage_and_only_excludes_explicit_virtual()
    {
        var columns=Columns();var table=Table(1);var plan=MetadataStoragePolicy.Compile(table,columns);
        Assert.Equal(columns.Where(column=>column.IsVirtualColumn==0).Select(column=>column.Id),plan.PhysicalColumns.Select(column=>column.Id));Assert.Equal(MetadataFieldStorage.Virtual,plan.Kind(columns.Single(column=>column.ColumnName=="virtual_value")));
    }
    /// <summary>发布计划固定物理字段快照，源对象及同名扩展键不能改写已选列名或类型。</summary>
    [Fact]
    public void Physical_columns_are_independent_snapshots_without_extension_key_override()
    {
        var column=Column("name");column.Extra["columnName"]=JsonSerializer.SerializeToElement("another_column");var plan=MetadataStoragePolicy.Compile(Table(0),new[]{Column("id","int64"),column});column.ColumnName="changed_after_compile";column.LogicalType="json";column.Extra.Clear();var frozen=plan.PhysicalColumns.Single(item=>item.Id=="name");Assert.Equal("name",frozen.ColumnName);Assert.Equal("string",frozen.LogicalType);Assert.Equal("another_column",frozen.Extra["columnName"].GetString());
    }
    /// <summary>非冗余分别识别联接、审计展示、纯布局和虚拟项，普通外键仍保留物理存储。</summary>
    [Fact]
    public void Nonredundant_mode_compiles_the_original_field_kinds_without_mutating_metadata()
    {
        var columns=Columns();var before=JsonSerializer.Serialize(columns);var plan=MetadataStoragePolicy.Compile(Table(0),columns);
        Assert.Equal(new[]{"id","name","customer_id"},plan.PhysicalColumns.Select(column=>column.ColumnName));Assert.Equal(MetadataFieldStorage.Join,plan.Kind(columns.Single(column=>column.ColumnName=="customer_name")));Assert.Equal(MetadataFieldStorage.RelationDisplay,plan.Kind(columns.Single(column=>column.ColumnName=="insert_manager_real_name")));Assert.Equal(MetadataFieldStorage.Layout,plan.Kind(columns.Single(column=>column.ColumnName=="section")));Assert.Equal(before,JsonSerializer.Serialize(columns));
    }
    /// <summary>未声明关系的业务ID和相似名称不是派生字段，不能由尾缀猜测后删列。</summary>
    [Fact]
    public void Plain_business_id_does_not_imply_a_relation_or_derived_name()
    {
        var columns=new[]{Column("id","int64"),Column("external_id","int64"),Column("external_name")};var plan=MetadataStoragePolicy.Compile(Table(0),columns);Assert.Equal(3,plan.PhysicalColumns.Count);
    }
    /// <summary>自动辅助别名与普通业务字段同名必须明确报错，不能静默吞掉业务列。</summary>
    [Theory]
    [InlineData("List","customer_id","customer_name")][InlineData("MultiSelectPage","customer_ids","customer_names")][InlineData("MultiCheckBoxDatabase","customer_ids","customer_names")]
    public void Relationship_auxiliary_collision_requires_explicit_join_or_virtual(string kind,string relationName,string name)
    {
        var relation=Column(relationName,"int64");relation.ControlKind=kind;var alias=Column(name);var columns=new[]{Column("id","int64"),relation,alias};Assert.Throws<InvalidOperationException>(()=>MetadataStoragePolicy.Compile(Table(0),columns));alias.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);Assert.Equal(MetadataFieldStorage.Join,MetadataStoragePolicy.Compile(Table(0),columns).Kind(alias));
    }
    /// <summary>选择ID作为显示值时不创建同名辅助列，也不误删原名称业务列。</summary>
    [Fact]
    public void Id_display_needs_no_auxiliary_name()
    {
        var relation=Column("customer_id","int64");relation.ControlKind="List";relation.ControlDetail="{\"nameField\":\"id\"}";var name=Column("customer_name");Assert.True(MetadataStoragePolicy.Compile(Table(0),new[]{Column("id","int64"),relation,name}).IsPhysical(name));
    }
    /// <summary>旧App审计姓名必须有明确用户关系；固定管理员审计名按原展示字段识别。</summary>
    [Fact]
    public void Application_audit_name_requires_declared_user_relationship()
    {
        var name=Column("insert_user_name");var columns=new List<ColumnMetadata>{Column("id","int64"),name};Assert.Throws<InvalidOperationException>(()=>MetadataStoragePolicy.Compile(Table(0),columns));var relation=Column("insert_user_id","int64");relation.ControlKind="List";columns.Add(relation);Assert.Equal(MetadataFieldStorage.RelationDisplay,MetadataStoragePolicy.Compile(Table(0),columns).Kind(name));
    }
    /// <summary>跨表级联只持久化所选末级ID，祖先ID与层级名称保持派生。</summary>
    [Fact]
    public void Cross_table_cascade_keeps_leaf_id_and_derives_ancestors_and_names()
    {
        var leaf=Column("city_id","int64");leaf.ControlKind="CascadeTree";leaf.ControlDetail="{\"levels\":[{\"table\":\"country\"},{\"table\":\"city\"}]}";var columns=new[]{Column("id","int64"),Column("country_id","int64"),Column("country_name"),leaf,Column("city_name")};var plan=MetadataStoragePolicy.Compile(Table(0),columns);Assert.Equal(new[]{"id","city_id"},plan.PhysicalColumns.Select(column=>column.ColumnName));
    }
    /// <summary>自树只声明本列ID及名称，不能把普通相似业务列一起归为祖先。</summary>
    [Fact]
    public void Self_tree_keeps_own_id_and_does_not_guess_parent_columns()
    {
        var leaf=Column("region_id","int64");leaf.ControlKind="CascadeTree";leaf.ControlDetail="{\"sourceMode\":\"selfTree\",\"selfTree\":{\"table\":\"region\"}}";var columns=new[]{Column("id","int64"),leaf,Column("region_name"),Column("region_parent_id","int64")};var plan=MetadataStoragePolicy.Compile(Table(0),columns);Assert.Equal(new[]{"id","region_id","region_parent_id"},plan.PhysicalColumns.Select(column=>column.ColumnName));
    }
    /// <summary>损坏级联不回退为普通物理字段，避免错误创建祖先列。</summary>
    [Theory]
    [InlineData("[]")][InlineData("{\"levels\":[]}")][InlineData("{\"sourceMode\":\"unknown\"}")][InlineData("{\"levels\":[{\"table\":\"region\"}]}")][InlineData("{\"levels\":[{\"table\":\"city\"},{\"table\":\"city\"}]}")]
    public void Invalid_cascade_definition_fails_before_storage_plan(string json)
    {var leaf=Column("city_id","int64");leaf.ControlKind="CascadeTree";leaf.ControlDetail=json;Assert.Throws<InvalidOperationException>(()=>MetadataStoragePolicy.Compile(Table(0),new[]{Column("id","int64"),leaf}));}
    /// <summary>计划不能接另一张表或重复ID，未知字段不能默认取得物理资格。</summary>
    [Fact]
    public void Storage_plan_rejects_foreign_duplicate_and_unknown_fields()
    {
        var column=Column("id","int64");var other=Column("name");other.ConfigTableId="other";Assert.Throws<InvalidOperationException>(()=>MetadataStoragePolicy.Compile(Table(0),new[]{column,other}));Assert.Throws<InvalidOperationException>(()=>MetadataStoragePolicy.Compile(Table(0),new[]{column,column}));Assert.Throws<InvalidOperationException>(()=>MetadataStoragePolicy.Compile(Table(0),new[]{column}).IsPhysical(Column("other")));
    }
    /// <summary>MySQL结构首次只创建真实列，重复预览零差异；已有历史多余列不自动删除。</summary>
    [Fact]
    public void Schema_plan_uses_the_same_nonredundant_storage_and_never_drops_historical_columns()
    {
        var catalog=new MetadataCatalog{ConfigTables=[Table(0)],ConfigColumns=Columns().ToList()};var desired=MySqlSchemaPlanner.Compile(catalog,"test_db");Assert.Equal(new[]{"customer_id","id","name"},desired.Tables.Single().Columns.Select(column=>column.Name));var empty=new DatabaseSchema("test_db",[]);var changes=MySqlSchemaPlanner.Compare(empty,desired);var create=Assert.Single(changes);Assert.DoesNotContain("customer_name",create.Sql);Assert.DoesNotContain("insert_manager_real_name",create.Sql);Assert.Empty(MySqlSchemaPlanner.Compare(desired,desired));
        var historical=desired with{Tables=[desired.Tables.Single() with{Columns=[..desired.Tables.Single().Columns,new ColumnSchema("customer_name","varchar(255)",true,false,null)]}]};Assert.Empty(MySqlSchemaPlanner.Compare(historical,desired));
    }
    /// <summary>启用索引只能引用同一物理计划内的列，不能给派生路径创建无效索引。</summary>
    [Fact]
    public void Index_on_derived_column_is_rejected_before_sql()
    {
        var catalog=new MetadataCatalog{ConfigTables=[Table(0)],ConfigColumns=Columns().ToList(),ConfigIndexes=[JsonSerializer.SerializeToElement(new{id="index1",configTableId="table1",indexName="ix_customer_name"})],ConfigIndexColumns=[JsonSerializer.SerializeToElement(new{id="indexcol1",configIndexId="index1",configColumnId="customer_name",sortNo=10})]};Assert.Throws<InvalidOperationException>(()=>MySqlSchemaPlanner.Compile(catalog,"test_db"));
    }
    /// <summary>固定基础设施不能通过切换冗余模式改变物理含义。</summary>
    [Fact]
    public void Reserved_infrastructure_storage_mode_cannot_be_redeclared_by_application()
    {
        var infrastructure=new MetadataCatalog{ConfigTables=[Table(1)],ConfigColumns=[Column("id","int64")]};var application=new MetadataCatalog{ConfigTables=[Table(0)],ConfigColumns=[Column("id","int64")]};Assert.Throws<InvalidOperationException>(()=>MetadataCatalogComposition.MergePhysical(application,infrastructure));
    }
    /// <summary>纯元数据样本复用原关系列和审计列命名，不依赖私有配置。</summary>
    internal static ColumnMetadata[] Columns()
    {
        var relation=Column("customer_id","int64");relation.ControlKind="List";var joined=Column("customer_name");joined.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);var audit=Column("insert_manager_real_name");var layout=Column("section");layout.ControlKind="Title";var virtualValue=Column("virtual_value");virtualValue.IsVirtualColumn=1;
        return[Column("id","int64"),Column("name"),relation,joined,audit,layout,virtualValue];
    }
    /// <summary>构造明确冗余模式的独立元数据表。</summary>
    internal static TableMetadata Table(int redundant)=>new(){Id="table1",TableName="test_record",IsRongyu=redundant};
    /// <summary>构造满足物理类型要求的测试字段，主键显式非空。</summary>
    internal static ColumnMetadata Column(string name,string type="string")=>new(){Id=name,ConfigTableId="table1",ColumnName=name,LogicalType=type,IsDbNotNull=name=="id"?1:0,ColumnLength=type=="string"?"255":""};
}
