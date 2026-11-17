using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class CascadeMultiLabelReadTests
{
    /// <summary>稀疏祖先名称共用读取与逆向依赖，末级改父和祖先改名均会刷新。</summary>
    [Theory]
    [InlineData("MultiSelectPage")]
    [InlineData("MultiCheckBoxDatabase")]
    public void Sparse_ancestor_label_is_used_for_array_filter_and_refresh(string kind)
    {
        var catalog=NonRedundantCrossTableReadTests.Catalog("b185","string","int64");
        foreach(var page in catalog.ConfigPages)
            foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId&&
                column.ColumnName is "title" or "country_id" or "area_id"))
                catalog.ConfigPageColumns.Add(new(){Id=page.Id+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id});
        catalog.ConfigPages.Add(new(){Id="11",ConfigTableId="1",Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});
        catalog.ConfigColumns.Add(new(){Id="19",ConfigTableId="1",ColumnName="country_name",LogicalType="string"});
        catalog.ConfigPageColumns.Add(new(){Id="1119",ConfigPageId="11",ConfigColumnId="19"});
        catalog.ConfigTables.Add(new(){Id="9",TableName="b185_consumer",IsRongyu=0});
        catalog.ConfigColumns.AddRange([
            new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="92",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="93",ConfigTableId="9",ColumnName="selected_ids",LogicalType="string",ControlKind=kind,LinkConfigTableId="1",
                ControlDetail="{\"tableId\":\"1\",\"pageId\":\"11\",\"nameField\":\"country_name\"}"},
            new(){Id="94",ConfigTableId="9",ColumnName="selected_country_names",LogicalType="string",Extra=new()
                {["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("selected.country_name")}}
        ]);

        var candidate=MySqlNonRedundantReadCompiler.CompileMultiLabel(catalog,"1","country_name");
        Assert.Contains("CASE WHEN",candidate.Expression("country_name"));
        Assert.Contains("LEFT JOIN `b185_country`",candidate.From);
        Assert.Contains(candidate.Constants.Values,value=>Equals(value,NonRedundantCrossTableReadTests.BlockedTitle));
        var array=MySqlNonRedundantReadCompiler.Compile(catalog,"9",["selected_country_names"]);
        var name=array.MultiProjections["selected_country_names"].NameExpression!;
        Assert.Contains(name+" LIKE {parameter}",array.ArrayNamePredicates["selected_country_names"]);

        catalog.ConfigTables.Single(table=>table.Id=="9").IsRongyu=1;
        var binding=new RelationCacheDefinition("9","93","1","19","94",true);
        DerivedCrossTableCaches.Validate(catalog,[binding],[]);
        var refresh=MySqlNonRedundantReadCompiler.CompileRefresh(catalog,"9",["selected_country_names"],[binding]);
        Assert.Contains(refresh.Reverse,item=>item.TargetTable=="b185_country"&&item.Outputs["selected_country_names"].Contains("title"));
        Assert.Contains(refresh.Reverse,item=>item.TargetTable=="b185_place"&&item.Outputs["selected_country_names"].Contains("area_id"));
        Assert.Contains(refresh.Reverse,item=>item.TargetTable=="b185_source"&&item.Outputs["selected_country_names"].Contains("place_id"));
    }
}
