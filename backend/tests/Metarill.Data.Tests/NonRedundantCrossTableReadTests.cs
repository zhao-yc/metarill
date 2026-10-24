using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>跨表逻辑投影与物理稀疏位分离，目标范围只由固定页面绑定提供，不扩张公开输出。</summary>
public sealed class NonRedundantCrossTableReadTests
{
    internal const string BlockedTitle="不应进入SQL'条件";
    /// <summary>一个三层控件只联接三次，目标常量只进参数，源元数据及绑定数组后改不污染计划。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public void Fixed_scopes_compile_linear_paths_and_preserve_logical_output_types(string sourceType,string parentType)
    {
        var catalog=Catalog("nr_cross",sourceType,parentType);var before=JsonSerializer.Serialize(catalog);var levels=Targets(catalog);var scope=new NonRedundantCrossTableScope("15",levels);levels[0]=new("unknown");var plan=NonRedundantReads.CompileWithCrossTables(catalog,"1",["id","country_id","country_name","area_id","area_name","place_id","place_name"],crossTables:[scope]);
        Assert.Equal(7,plan.OutputTypes.Count);Assert.Equal(sourceType,plan.OutputTypes["country_id"]);Assert.Equal("string",plan.OutputTypes["country_name"]);Assert.Equal(before,JsonSerializer.Serialize(catalog));
        var mysql=MySqlNonRedundantReadCompiler.CompileWithCrossTables(catalog,"1",["country_id","country_name","area_name","place_name"],crossTables:[scope]);var sql=(string)typeof(MySqlNonRedundantReadPlan).GetProperty("From",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mysql)!;Assert.Equal(3,sql.Split(" LEFT JOIN ").Length-1);Assert.DoesNotContain(BlockedTitle,sql);Assert.Contains("mt_cross_scope_",sql);
        catalog.ConfigColumns.Single(column=>column.Id=="22").ColumnName="changed_after_compile";catalog.ConfigPages.Single(page=>page.Id=="21").WhereSql="not valid";Assert.Equal(sql,typeof(MySqlNonRedundantReadPlan).GetProperty("From",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mysql));Assert.Throws<NotSupportedException>(()=>((IList<NonRedundantCrossTableTargetScope>)scope.Levels).Add(new("extra")));
    }
    /// <summary>两个独立控件各有一条线性链，不能把整张来源表误限为一个所选ID。</summary>
    [Fact]
    public void Independent_cross_controls_have_separate_logical_paths_and_required_bindings()
    {
        var catalog=Catalog("nr_two_cross","string","int64",twoTrees:true);var scopes=Scopes(catalog);Assert.Equal(2,scopes.Length);var plan=MySqlNonRedundantReadCompiler.CompileWithCrossTables(catalog,"1",["country_name","secondary_country_name"],scopes);var sql=(string)typeof(MySqlNonRedundantReadPlan).GetProperty("From",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plan)!;Assert.Equal(6,sql.Split(" LEFT JOIN ").Length-1);
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileWithCrossTables(catalog,"1",["secondary_country_name"],[scopes.Single(scope=>scope.SourceColumnId=="15")]));
    }
    /// <summary>与原生成模型共用布尔／整数开关语义，文字true不能被悄悄解释成联接标志。</summary>
    [Theory][InlineData("true",true)][InlineData("1",true)][InlineData("\"true\"",false)][InlineData("false",false)][InlineData("0",false)]
    public void Physical_target_join_flags_keep_original_metadata_semantics(string json,bool joined)
    {
        var catalog=Catalog("nr_cross_flags","string","int64");catalog.ConfigColumns.Single(column=>column.Id=="22").Extra["isJoinColumn"]=JsonSerializer.Deserialize<JsonElement>(json);
        if(joined)Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileWithCrossTables(catalog,"1",["country_name"],Scopes(catalog)));else Assert.Equal("string",NonRedundantReads.CompileWithCrossTables(catalog,"1",["country_name"],Scopes(catalog)).OutputTypes["country_name"]);
    }
    /// <summary>缺绑定、错表／项目／父键、隐藏动态范围及错误常量类型不能降级为空条件。</summary>
    [Theory][InlineData("missing")][InlineData("wrongPage")][InlineData("fewLevels")][InlineData("foreign")][InlineData("global")][InlineData("rootGlobal")][InlineData("nameJoin")][InlineData("nameType")][InlineData("parentTarget")][InlineData("conditionMissing")][InlineData("conditionChanged")][InlineData("conditionType")][InlineData("parentContext")][InlineData("extra")][InlineData("sourceTarget")][InlineData("unknownBinding")]
    public void Incomplete_or_contradictory_path_proofs_fail_before_database(string kind)
    {
        var catalog=Catalog("nr_reject","string","string");IReadOnlyList<NonRedundantCrossTableScope>? scopes=Scopes(catalog);var levels=Targets(catalog);
        switch(kind)
        {
            case "missing":scopes=null;break;case "wrongPage":levels[0]=new("31",levels[0].Condition);break;case "fewLevels":levels=levels[..2];break;case "foreign":catalog.ConfigTables.Single(table=>table.Id=="2").YzProjectId="999";break;case "global":catalog.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");break;case "rootGlobal":catalog.ConfigTables.Single(table=>table.Id=="1").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");break;case "nameJoin":catalog.ConfigColumns.Single(column=>column.Id=="22").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);break;case "nameType":catalog.ConfigColumns.Single(column=>column.Id=="22").LogicalType="int64";break;case "parentTarget":catalog.ConfigColumns.Single(column=>column.Id=="36").LinkConfigTableId="4";break;case "conditionMissing":levels[0]=new("21");break;case "conditionChanged":levels[0]=new("21",Condition("is_enabled=0"));break;case "conditionType":levels[0]=new("21",FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("is_enabled='1'",["is_enabled"]),new Dictionary<string,string>{{"is_enabled","string"}}));break;case "parentContext":catalog.ConfigPages.Single(page=>page.Id=="31").WhereSql="country_id=@parentId";break;case "extra":catalog.ConfigColumns.Single(column=>column.Id=="15").ControlDetail=catalog.ConfigColumns.Single(column=>column.Id=="15").ControlDetail.Replace("\"sourceMode\":\"crossTable\"","\"whereSql\":\"1=1\",\"sourceMode\":\"crossTable\"");break;case "sourceTarget":catalog.ConfigColumns.Single(column=>column.Id=="15").LinkConfigTableId="2";break;case "unknownBinding":scopes=[new("unknown",levels)];break;
        }
        if(kind is not("missing" or "unknownBinding"))scopes=[new("15",levels)];var before=JsonSerializer.Serialize(catalog);Assert.ThrowsAny<Exception>(()=>kind=="missing"?NonRedundantReads.Compile(catalog,"1",["id","country_name"]):NonRedundantReads.CompileWithCrossTables(catalog,"1",["id","country_name"],crossTables:scopes!));Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }
    /// <summary>固定目标页与常量来自同一发布模型，生成层仍负责动作授权；此处不包含请求上下文。</summary>
    internal static NonRedundantCrossTableScope[] Scopes(MetadataCatalog catalog)=>catalog.ConfigColumns.Where(column=>column.ConfigTableId=="1"&&column.ControlKind=="CascadeTree").Select(column=>new NonRedundantCrossTableScope(column.Id,Targets(catalog))).ToArray();
    /// <summary>全部层保留目标原常量，项目身份由真正宿主绑定。</summary>
    internal static NonRedundantCrossTableTargetScope[] Targets(MetadataCatalog catalog)=>new[]{"21","31","41"}.Select(id=>new NonRedundantCrossTableTargetScope(id,Condition("is_enabled=1 AND title <> '"+BlockedTitle.Replace("'","''",StringComparison.Ordinal)+"'"))).ToArray();
    /// <summary>只有明确的物理标量参与固定条件，文本从不拼入执行SQL。</summary>
    internal static FixedConditionSqlPlan Condition(string definition)=>FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(definition,["is_enabled","title"]),new Dictionary<string,string>{{"is_enabled","bool"},{"title","string"}});
    /// <summary>普通R0来源与三个独立R1目标；各表允许相同主键，并混用项目／父键存储类型。</summary>
    internal static MetadataCatalog Catalog(string prefix,string sourceType,string parentType,bool twoTrees=false)
    {
        var result=new MetadataCatalog{YzProjectId="901000000000000001"};var names=new[]{"source","country","area","place"};
        for(var index=0;index<4;index++)
        {
            var id=(index+1).ToString();result.ConfigTables.Add(new(){Id=id,YzProjectId=result.YzProjectId,TableName=prefix+"_"+names[index],IsRongyu=index==0?0:1});
            Column("1","id","int64",true);Column("2",index==0?"name":"title","string");Column("3","yz_project_id",index==2?"string":"int64");Column("4","delete_no","datetime");
            if(index==0){Column("7","amount","decimal");result.ConfigColumns.Add(new(){Id="15",ConfigTableId=id,YzProjectId=result.YzProjectId,ColumnName="place_id",LogicalType=sourceType,ColumnLength="64",ControlKind="CascadeTree",LinkConfigTableId="4",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{new{table=prefix+"_country",columnPrefix="country",nameField="title"},new{table=prefix+"_area",columnPrefix="area",parentField="country_id",nameField="title"},new{table=prefix+"_place",columnPrefix="place",parentField="area_id",nameField="title"}}})});result.ConfigColumns.Add(new(){Id="18",ConfigTableId=id,YzProjectId=result.YzProjectId,ColumnName="owner_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="2",ControlDetail="{\"tableId\":\"2\",\"nameField\":\"title\"}"});}
            else{Column("5","is_enabled","bool");if(index>1){Column("6",index==2?"country_id":"area_id",parentType);result.ConfigColumns[^1].LinkConfigTableId=index.ToString();}result.ConfigPages.Add(new(){Id=id+"1",ConfigTableId=id,YzProjectId=result.YzProjectId,WhereSql="yz_project_id=@current_yz_project_id AND is_enabled=1 AND title <> '"+BlockedTitle.Replace("'","''",StringComparison.Ordinal)+"'"});}
            void Column(string suffix,string name,string type,bool required=false)=>result.ConfigColumns.Add(new(){Id=id+suffix,ConfigTableId=id,YzProjectId=result.YzProjectId,ColumnName=name,LogicalType=type,IsDbNotNull=required?1:0,ColumnLength="100"});
        }
        if(twoTrees)
        {
            var original=result.ConfigColumns.Single(column=>column.Id=="15");var detail=System.Text.Json.Nodes.JsonNode.Parse(original.ControlDetail)!;
            foreach(var level in detail["levels"]!.AsArray())level!["columnPrefix"]="secondary_"+level["columnPrefix"]!.GetValue<string>();
            result.ConfigColumns.Add(new(){Id="19",ConfigTableId="1",YzProjectId=result.YzProjectId,ColumnName="secondary_place_id",LogicalType=sourceType,ColumnLength="64",ControlKind="CascadeTree",LinkConfigTableId="4",ControlDetail=detail.ToJsonString()});
        }
        return result;
    }
}
