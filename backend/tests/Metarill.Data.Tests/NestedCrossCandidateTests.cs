using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>候选页依赖其它逻辑级联时，访问范围直接进入当前 MySQL 查询。</summary>
public sealed class NestedCrossCandidateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Outer_path_uses_a_nested_fixed_page_without_exposing_inner_slots(bool shared)
    {
        var catalog=Catalog(shared);
        var before=JsonSerializer.Serialize(catalog);
        var condition=FixedConditionSqlPlan.Compile(
            FixedConditionPlan.Compile("country_name <> '限制文字' AND store_id=@current_store_id",["country_name","store_id"]),
            new Dictionary<string,string>{{"country_name","string"},{"store_id","int64"}});
        var binding=new NonRedundantCrossTableScope("9004",[new("11",condition),new("91")]);
        var plan=NonRedundantReads.CompileWithCrossTables(catalog,"90",["id","outer_root_id","outer_leaf_name"],[binding]);
        var mysql=plan.MySql;
        Assert.Single(mysql.Crosses);
        Assert.Contains("mt_nested_",mysql.Expression("outer_root_id"));
        Assert.Contains("mt_nested_",mysql.Expression("outer_leaf_name"));
        Assert.Contains("nested_country",mysql.From);
        Assert.Contains("nested_place",mysql.From);
        Assert.Equal(shared,mysql.From.Contains("nested_warehouse",StringComparison.Ordinal));
        Assert.Equal(shared?8:5,mysql.From.Split(" LEFT JOIN ").Length-1);
        Assert.Contains("FOR SHARE OF cross0,cross1",mysql.ImportMatchLock);
        Assert.DoesNotContain("限制文字",mysql.From);
        Assert.Throws<InvalidOperationException>(()=>mysql.Constants);
        var actor=new RuntimeActor(1,"候选账号",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L};
        var bound=plan.BindAccount(actor).MySql;
        Assert.Contains(actor.StoreId!.Value,bound.Constants.Values);
        Assert.Contains("限制文字",bound.Constants.Values);
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));

        var direct=NonRedundantReads.CompileCrossPageCandidate(catalog,"1","11",["id","name"],[]);
        Assert.Contains("mt_candidate_",direct.MySql.Scope);
        Assert.Contains("CASE WHEN",direct.MySql.Scope);
        Assert.Contains("IS NOT NULL",direct.MySql.Scope);
        Assert.Contains("nested_country",direct.MySql.From);
        Assert.Equal(new[]{"id","name"},direct.OutputTypes.Keys);
        Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }

    [Fact]
    public void A_recursive_condition_cycle_is_rejected_instead_of_returning_wide_scope()
    {
        var catalog=Catalog(false);
        var source=catalog.ConfigColumns.Single(column=>column.Id=="15");
        var detail=JsonNode.Parse(source.ControlDetail)!;
        detail["levels"]![0]!["table"]="nested_source";
        detail["levels"]![0]!["nameField"]="name";
        source.ControlDetail=detail.ToJsonString();
        catalog.ConfigColumns.Single(column=>column.Id=="36").LinkConfigTableId="1";
        var failure=Assert.Throws<InvalidOperationException>(()=>
            NonRedundantReads.CompileCrossPageCandidate(catalog,"1","11",["id","name"],[]));
        Assert.Contains("循环依赖",failure.Message);
    }

    /// <summary>外层叶子的父级引用带固定条件的 R0 页面，内层复用独立或共享稀疏目录。</summary>
    private static MetadataCatalog Catalog(bool shared)
    {
        var c=shared?SharedCrossTableReadTests.Catalog("nested","string","int64"):
            NonRedundantCrossTableReadTests.Catalog("nested","string","int64");
        c.ConfigColumns.Add(new(){Id="1100",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
        c.ConfigPages.Add(new(){Id="11",ConfigTableId="1",WhereSql="country_name <> '限制文字' AND store_id=@current_store_id"});
        c.ConfigTables.AddRange([
            new(){Id="90",TableName="nested_outer",IsRongyu=0},
            new(){Id="91",TableName="nested_outer_leaf",IsRongyu=1}
        ]);
        foreach(var id in new[]{"90","91"})
        {
            c.ConfigColumns.AddRange([
                new(){Id=id+"01",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id=id+"02",ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id=id+"03",ConfigTableId=id,ColumnName="name",LogicalType="string"}
            ]);
        }
        c.ConfigColumns.Add(new(){Id="9104",ConfigTableId="91",ColumnName="root_id",LogicalType="int64",LinkConfigTableId="1"});
        c.ConfigColumns.Add(new(){Id="9004",ConfigTableId="90",ColumnName="outer_leaf_id",LogicalType="int64",
            LinkConfigTableId="91",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new
            {
                sourceMode="crossTable",
                levels=new object[]{
                    new{table="nested_source",columnPrefix="outer_root",nameField="name"},
                    new{table="nested_outer_leaf",columnPrefix="outer_leaf",nameField="name",parentField="root_id"}
                }
            })});
        c.ConfigPages.Add(new(){Id="91",ConfigTableId="91"});
        foreach(var page in c.ConfigPages)
        {
            page.YzProjectId=c.YzProjectId;
            foreach(var column in c.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId))
                c.ConfigPageColumns.Add(new(){Id=page.Id+"_"+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,IsShow=1});
        }
        return c;
    }
}
