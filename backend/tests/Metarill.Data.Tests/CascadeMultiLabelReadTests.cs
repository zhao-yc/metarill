using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class CascadeMultiLabelReadTests
{
    /// <summary>自树检查完整祖先链及环路，同一名称可用于筛选并记录根及父变更依赖。</summary>
    [Fact]
    public void Self_tree_label_requires_reaching_visible_root()
    {
        var catalog=DerivedMultipleCacheTests.Catalog("string","int64");
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="cache");
        var tree=catalog.ConfigTables.Single(table=>table.Id=="tree");
        tree.IsRongyu=1;
        var treeLabel=catalog.ConfigColumns.Single(column=>column.Id=="tree_name");
        treeLabel.Extra.Clear();
        var leaf=catalog.ConfigColumns.Single(column=>column.Id=="foreign");
        leaf.ColumnName="leaf_id";
        catalog.ConfigTables.Add(new(){Id="consumer",TableName="ds_label_consumer",IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="leaf_label",ConfigTableId="source",ColumnName="leaf_name",LogicalType="string"},
            new(){Id="label_consumer_id",ConfigTableId="consumer",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="label_consumer_project",ConfigTableId="consumer",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="label_consumer_fk",ConfigTableId="consumer",ColumnName="picked_ids",LogicalType="string",ControlKind="MultiSelectPage",
                LinkConfigTableId="source",ControlDetail="{\"pageId\":\"101\",\"nameField\":\"leaf_name\"}"},
            new(){Id="label_consumer_cache",ConfigTableId="consumer",ColumnName="picked_leaf_names",LogicalType="string",Extra=new()
                {["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("picked.leaf_name")}}
        ]);
        catalog.ConfigColumns.RemoveAll(column=>column.Id is "choices" or "choice_names");
        catalog.ConfigTables.Single(table=>table.Id=="source").IsRongyu=0;
        catalog.ConfigPages.Add(new(){Id="101",ConfigTableId="source",Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});
        catalog.ConfigPageColumns.Add(new(){Id="101leaf",ConfigPageId="101",ConfigColumnId="leaf_label"});
        catalog.ConfigPageColumns.Add(new(){Id="201parent",ConfigPageId="201",ConfigColumnId="parent"});

        var candidate=MySqlNonRedundantReadCompiler.CompileMultiLabel(catalog,"source","leaf_name");
        var sql=candidate.Expression("leaf_name");
        Assert.Contains("WITH RECURSIVE",sql);
        Assert.Contains("depth<64",sql);
        Assert.Contains("NOT JSON_CONTAINS",sql);
        Assert.Contains("parent_key IS NULL",sql);
        Assert.Contains("delete_no",sql);
        var binding=new RelationCacheDefinition("consumer","label_consumer_fk","source","leaf_label","label_consumer_cache",true);
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]);
        var reverse=maintenance.Plans["consumer"].Read!.Reverse.Single(item=>item.TargetTable=="ds_tree");
        Assert.Contains("parent_node_id",reverse.Outputs["picked_leaf_names"]);
        Assert.Contains("@target_id",reverse.TargetPredicate);
    }


    /// <summary>共享祖先名称必须同时核对两个来源分支，冲突不能作为有效名称。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shared_ancestor_label_uses_both_candidate_branches(bool shortBranch)
    {
        var catalog=SharedCrossTableReadTests.Catalog("b187","string","int64",shortBranch);
        foreach(var page in catalog.ConfigPages)
            foreach(var member in catalog.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId&&
                column.ColumnName is "name" or "country_id" or "area_id"))
                catalog.ConfigPageColumns.Add(new(){Id=page.Id+member.Id,ConfigPageId=page.Id,ConfigColumnId=member.Id});
        var plan=MySqlNonRedundantReadCompiler.CompileMultiLabel(catalog,"1","country_name");
        Assert.Equal(2,plan.Crosses.Count);
        Assert.Single(plan.Crosses,branch=>branch.RequireRowValidity);
        Assert.Contains("COALESCE",plan.Expression("country_name"));
        Assert.Equal(shortBranch?3:4,plan.CascadeLabelPages.Count);
    }

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
