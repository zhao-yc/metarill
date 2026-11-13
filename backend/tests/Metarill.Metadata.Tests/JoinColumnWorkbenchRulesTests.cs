using System.Text.Json;
using Metarill.Runtime.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>原联接字段协议的纯配置计算，不执行SQL，不代替HTTP/数据库验收。</summary>
public sealed class JoinColumnWorkbenchRulesTests
{
    /// <summary>固定前缀、辅助名称排除、系统列保护、重复路径和目标列原排序同时保持。</summary>
    [Fact]
    public void Original_groups_keep_ids_names_order_and_exclude_auxiliary_system_and_existing_fields()
    {
        var f=new Sample();var snapshot=f.Build();var group=Assert.Single(snapshot.Detail.Groups);Assert.Equal("30",group.SourceColumnId);Assert.Equal("buyer",group.SourcePrefix);Assert.Equal("2",group.TargetTableId);Assert.Equal("客户",group.TargetName);
        Assert.Equal(new[]{"notes","city_id"},group.Fields.Select(field=>field.ColumnName));Assert.Equal("buyer.notes",group.Fields[0].ColumnFullName);Assert.Equal("buyer_notes",group.Fields[0].JoinColumnName);Assert.Equal("city",group.Fields[1].JoinTable);
        var json=JsonSerializer.SerializeToElement(snapshot.Detail,new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.Equal("1",json.GetProperty("configTableId").GetString());Assert.Equal("41",json.GetProperty("groups")[0].GetProperty("fields")[0].GetProperty("targetColumnId").GetString());
        Assert.False(json.GetRawText().Contains("whereSql",StringComparison.Ordinal));
    }
    /// <summary>List、选择页、多选择页、原前缀推导均解析同项目真实目标。</summary>
    [Theory]
    [InlineData("List","{\"tableId\":\"2\",\"table\":\"customer\"}")]
    [InlineData("List","{\"tableId\":\"999\",\"table\":\"customer\"}")]
    [InlineData("SelectPage","{\"pageId\":\"71\",\"nameField\":\"name\"}")]
    [InlineData("MultiSelectPage","{\"pageId\":\"71\",\"nameField\":\"name\"}")]
    [InlineData("","{}")]
    public void Original_target_resolution_uses_published_configuration(string kind,string detail)
    {
        var f=new Sample();f.Relation["join_table"]="";f.Relation["control_kind"]=kind;f.Relation["control_detail"]=detail;if(kind.Length==0)f.Relation["column_name"]="customer_id";
        Assert.Equal("2",Assert.Single(f.Build().Detail.Groups).TargetTableId);
    }
    /// <summary>未找到目标也保留原分组诊断，不读取另一个项目的同名表。</summary>
    [Fact]
    public void Missing_target_returns_empty_fields_and_original_resolve_message()
    {
        var f=new Sample();f.Tables.RemoveAt(1);var result=Assert.Single(f.Build().Detail.Groups);Assert.Empty(result.Fields);Assert.Empty(result.TargetTableId);Assert.StartsWith("未找到关联目标表",result.ResolveMessage);
    }
    /// <summary>坏控件明细不被当作空配置或执行脚本，声明冲突不猜选另一目标。</summary>
    [Theory]
    [InlineData("not-json")][InlineData("[]")][InlineData("{\"tableId\":\"2\",\"tableId\":\"3\"}")][InlineData("{\"tableId\":\"2\",\"table\":\"city\"}")]
    public void Malformed_or_ambiguous_relation_definition_is_rejected(string detail)
    {
        var f=new Sample();f.Relation["join_table"]="";f.Relation["control_detail"]=detail;Assert.ThrowsAny<Exception>(()=>f.Build());
    }
    /// <summary>候选中的任何越界或删除对象均失败关闭，不返回其名字和配置。</summary>
    [Theory]
    [InlineData("table")][InlineData("column")][InlineData("page")][InlineData("deleted")]
    public void Foreign_or_deleted_configuration_never_enters_snapshot(string kind)
    {
        var f=new Sample();var row=kind=="table"?f.Tables[1]:kind=="page"?f.Pages[0]:f.Columns[1];row[kind=="deleted"?"delete_no":"yz_project_id"]=kind=="deleted"?DateTime.UtcNow:20L;Assert.Throws<InvalidOperationException>(()=>f.Build());
    }
    /// <summary>任一无效选择阻断整批；合法子集不能被静默提交。</summary>
    [Theory]
    [InlineData("[]")][InlineData("null")][InlineData("[{}]")]
    [InlineData("[{\"sourceColumnId\":\"30\",\"targetColumnId\":\"41\"},{\"sourceColumnId\":\"30\",\"targetColumnId\":\"999\"}]")]
    [InlineData("[{\"sourceColumnId\":\"30\",\"targetColumnId\":\"41\"},{\"sourceColumnId\":\"30\",\"targetColumnId\":\"41\"}]")]
    [InlineData("[{\"sourceColumnId\":\"30\",\"targetColumnId\":\"41\",\"columnName\":\"injected\"}]")]
    public void Invalid_duplicate_or_stale_selection_rejects_the_whole_batch(string json)
    {
        var f=new Sample();using var document=JsonDocument.Parse(json);Assert.Throws<InvalidOperationException>(()=>JoinColumnWorkbenchRules.Select(document.RootElement,f.Build()));
    }
    /// <summary>客户端选择顺序不改变原排序；ID以字符串或JSON整数提交均保持精度。</summary>
    [Fact]
    public void Selection_uses_canonical_keys_and_original_candidate_order()
    {
        var f=new Sample();var selected=JsonSerializer.SerializeToElement(new[]{new{sourceColumnId="30",targetColumnId="42"},new{sourceColumnId="30",targetColumnId="41"}});Assert.Equal(new[]{"41","42"},JoinColumnWorkbenchRules.Select(selected,f.Build()).Select(plan=>plan.Field.TargetColumnId));
    }
    /// <summary>前缀名称和完整路径以忽略大小写的原规则去重。</summary>
    [Theory]
    [InlineData("BUYER_NOTES","")][InlineData("legacy_notes","BUYER.NOTES")]
    public void Existing_alias_or_full_path_cannot_be_added_again(string name,string full)
    {
        var f=new Sample();f.Columns.Add(Row(90,("config_table_id",1L),("column_name",name),("column_full_name",full),("is_join_column",1)));Assert.DoesNotContain(f.Build().Plans,plan=>plan.Field.TargetColumnId=="41");
    }
    /// <summary>同一关系前缀造成目标列别名冲突时，批量保存必须整体拒绝。</summary>
    [Fact]
    public void Two_selected_paths_cannot_write_the_same_column_alias()
    {
        var f=new Sample();f.Columns.Add(Row(31,("config_table_id",1L),("column_name","buyer_id"),("join_table","customer")));using var document=JsonDocument.Parse("[{\"sourceColumnId\":\"30\",\"targetColumnId\":\"41\"},{\"sourceColumnId\":\"31\",\"targetColumnId\":\"41\"}]");Assert.Throws<InvalidOperationException>(()=>JoinColumnWorkbenchRules.Select(document.RootElement,f.Build()));
    }
    /// <summary>辅助字段遵循原nameField配置；选择ID作显示字段时不再排除普通名称字段。</summary>
    [Theory]
    [InlineData("notes",true)][InlineData("id",false)]
    public void Auxiliary_exclusion_uses_declared_display_field(string display,bool excludeNotes)
    {
        var f=new Sample();f.Relation["control_detail"]=JsonSerializer.Serialize(new{table="customer",nameField=display});var names=Assert.Single(f.Build().Detail.Groups).Fields.Select(field=>field.ColumnName).ToArray();Assert.Contains("name",names);Assert.Equal(excludeNotes,!names.Contains("notes"));
    }
    /// <summary>所有样本只含配置对象，不包含数据库连接、SQL执行器或业务实体。</summary>
    private sealed class Sample
    {
        internal readonly Dictionary<string,object?> Source=Row(1,("name","订单"),("table_name","orders"));
        internal readonly List<Dictionary<string,object?>> Tables=[Row(1,("name","订单"),("table_name","orders")),Row(2,("name","客户"),("table_name","customer")),Row(3,("name","城市"),("table_name","city"))];
        internal readonly Dictionary<string,object?> Relation=Row(30,("config_table_id",1L),("name","买方"),("column_name","buyer_id"),("join_table","customer"),("control_kind","List"),("control_detail","{\"table\":\"customer\",\"nameField\":\"name\",\"whereSql\":\"不能执行的原文\"}"));
        internal readonly List<Dictionary<string,object?>> Columns=[];
        internal readonly List<Dictionary<string,object?>> Pages=[Row(71,("config_table_id",2L))];
        internal Sample(){Columns.Add(Relation);Columns.AddRange([Row(40,("config_table_id",2L),("column_name","name")),Row(41,("config_table_id",2L),("name","备注"),("column_name","notes"),("column_type","nvarchar"),("column_length","255"),("sort_no",10)),Row(42,("config_table_id",2L),("column_name","city_id"),("sort_no",20)),Row(43,("config_table_id",2L),("column_name","id")),Row(44,("config_table_id",2L),("column_name","section"),("control_kind","Title")),Row(45,("config_table_id",2L),("column_name","insert_manager_real_name"))]);}
        internal JoinColumnSnapshot Build()=>JoinColumnWorkbenchRules.Build(Source,Tables,Columns,Pages);
    }
    /// <summary>构造同项目纯配置行，避免依赖真实业务记录。</summary>
    private static Dictionary<string,object?> Row(long id,params(string Name,object? Value)[] fields){var row=new Dictionary<string,object?>{{"id",id},{"yz_project_id",10L}};foreach(var field in fields)row[field.Name]=field.Value;return row;}
}
