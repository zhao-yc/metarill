using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>显式共享读取计划必须冻结全部同伴范围，不能把空的祖先物理位当作完整逻辑路径。</summary>
public sealed class SharedCrossTableReadTests
{
    /// <summary>四键与短控件、派生目标均编译完整组；源表仅保留物理ID位，计数与无COUNT逐行都带组校验。</summary>
    [Theory][InlineData("string","string",false,false)][InlineData("string","int64",false,false)][InlineData("int64","string",false,false)][InlineData("int64","int64",false,false)][InlineData("string","int64",true,false)][InlineData("int64","string",true,false)][InlineData("string","int64",false,true)]
    public void Group_reads_compile_with_all_peers_and_canonical_storage(string type,string parent,bool shorter,bool derived)
    {
        var c=Catalog("shared",type,parent,shorter,derived);var before=JsonSerializer.Serialize(c);var names=Outputs(shorter);var plan=NonRedundantReads.CompileWithCrossTables(c,"1",names,Scopes(c));Assert.Equal(type,plan.OutputTypes["country_id"]);Assert.Equal(type,plan.OutputTypes["area_id"]);Assert.Equal(names.Order(StringComparer.Ordinal),plan.OutputTypes.Keys.Order(StringComparer.Ordinal));Assert.DoesNotContain(MetadataStoragePolicy.ForTable(c,c.ConfigTables[0]).PhysicalColumns,x=>x.ColumnName.EndsWith("_name",StringComparison.Ordinal));Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>缺同伴、不同读取页、不同条件及来源参与不能降级成取第一个控件。</summary>
    [Theory][InlineData("missing")][InlineData("page")][InlineData("condition")][InlineData("self")][InlineData("global")]
    public void Unproven_shared_scope_rejects(string kind)
    {
        var c=Catalog("shared","string","int64");var scopes=Scopes(c).ToArray();if(kind=="missing")scopes=scopes.Take(1).ToArray();if(kind is "page" or "condition"){var entries=scopes[1].Levels.ToArray();entries[0]=kind=="page"?entries[0] with{PageId="another"}:entries[0] with{Condition=FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("status=2",new[]{"status"}),new Dictionary<string,string>{{"status","int32"}})};scopes[1]=new(scopes[1].SourceColumnId,entries);}if(kind=="self")foreach(var control in c.ConfigColumns.Where(x=>x.Id is "31" or "41")){var json=JsonNode.Parse(control.ControlDetail)!;json["levels"]![0]!["table"]="shared_source";control.ControlDetail=json.ToJsonString();}if(kind=="global")c.ConfigTables.Single(t=>t.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind is "missing" or "self" or "global")Assert.Throws<NotSupportedException>(()=>MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"1",Outputs(false),scopes));else Assert.Throws<InvalidOperationException>(()=>MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"1",Outputs(false),scopes));
    }
    /// <summary>源表仍使用原字段；候选表、条件和页绑定由同一完整目录产生。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter=false,bool derived=false)
    {
        var c=SharedCrossTableSparseSelectionTests.Catalog(type,shorter);c.YzProjectId="9007199254740993";c.ConfigTables[0].TableName=prefix+"_source";foreach(var control in c.ConfigColumns.Where(x=>x.ControlKind=="CascadeTree"))control.ControlDetail=control.ControlDetail.Replace("shared_",prefix+"_",StringComparison.Ordinal);c.ConfigColumns.AddRange([new(){Id="source_name",ConfigTableId="1",ColumnName="name",LogicalType="string"},new(){Id="source_amount",ConfigTableId="1",ColumnName="amount",LogicalType="decimal",Precision=18,Scale=2},new(){Id="source_project",ConfigTableId="1",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="source_delete",ConfigTableId="1",ColumnName="delete_no",LogicalType="datetime"}]);var roles=new[]{"country","area","place","warehouse"};
        for(var index=0;index<roles.Length;index++)
        {
            var id=(index+2).ToString(CultureInfo.InvariantCulture);c.ConfigTables.Add(new(){Id=id,TableName=prefix+"_"+roles[index],IsRongyu=derived&&index<3?0:1});var cols=new[]{new ColumnMetadata{Id="target_"+id+"_id",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="target_"+id+"_name",ConfigTableId=id,ColumnName="name",LogicalType="string"},new(){Id="target_"+id+"_project",ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},new(){Id="target_"+id+"_delete",ConfigTableId=id,ColumnName="delete_no",LogicalType="datetime"},new(){Id="target_"+id+"_status",ConfigTableId=id,ColumnName="status",LogicalType="int32"}};c.ConfigColumns.AddRange(cols);if(index>0)c.ConfigColumns.Add(new(){Id="target_"+id+"_parent",ConfigTableId=id,ColumnName=index==1?"country_id":"area_id",LogicalType=parent,LinkConfigTableId=index==1?"2":"3"});c.ConfigPages.Add(new(){Id=id+"01",ConfigTableId=id,WhereSql="yz_project_id=@current_yz_project_id AND status=1"});
            if(derived&&index<3){cols[1].Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cols[1].Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");c.ConfigColumns.Add(new(){Id="target_"+id+"_company",ConfigTableId=id,ColumnName="company_id",LogicalType="int64",LinkConfigTableId="6"});}
        }
        if(derived){c.ConfigTables.Add(new(){Id="6",TableName=prefix+"_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="company_id",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="company_name",ConfigTableId="6",ColumnName="name",LogicalType="string"},new(){Id="company_project",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="company_delete",ConfigTableId="6",ColumnName="delete_no",LogicalType="datetime"}]);}foreach(var item in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages))item.YzProjectId=c.YzProjectId;return c;
    }
    /// <summary>每个源控件绑定完整目标页，目标常量与页面元数据保持一致。</summary>
    internal static IReadOnlyList<NonRedundantCrossTableScope> Scopes(MetadataCatalog c)=>c.ConfigColumns.Where(x=>x.ConfigTableId=="1"&&x.ControlKind=="CascadeTree").OrderBy(x=>x.Id,StringComparer.Ordinal).Select(control=>
    {
        using var json=JsonDocument.Parse(control.ControlDetail);return new NonRedundantCrossTableScope(control.Id,json.RootElement.GetProperty("levels").EnumerateArray().Select(level=>{var table=c.ConfigTables.Single(t=>t.TableName==level.GetProperty("table").GetString());var page=c.ConfigPages.Single(p=>p.ConfigTableId==table.Id);return new NonRedundantCrossTableTargetScope(page.Id,FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("status=1",new[]{"status"}),new Dictionary<string,string>{{"status","int32"}}));}).ToArray());
    }).ToArray();
    /// <summary>原字段及实时名称，没有隐藏的节点集合或额外HTTP标记。</summary>
    internal static string[] Outputs(bool shorter)=>new[]{"id","name","amount","country_id","country_name","area_id","area_name","warehouse_id","warehouse_name"}.Concat(shorter?Array.Empty<string>():new[]{"place_id","place_name"}).ToArray();
}
