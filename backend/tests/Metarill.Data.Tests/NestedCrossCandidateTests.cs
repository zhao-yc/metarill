using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
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


    /// <summary>全局只作用于候选根；普通下级和全局表自己的业务读取按原物理归属约束。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Global_recursive_candidate_omits_only_the_root_project_filter(bool rowProject)
    {
        var catalog=Catalog(!rowProject);
        var table=catalog.ConfigTables.Single(item=>item.Id=="1");
        var page=catalog.ConfigPages.Single(item=>item.Id=="11");
        table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        table.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
        page.YzAdminTypeId="5";
        if(!rowProject)
        {
            var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId==table.Id&&column.ColumnName=="yz_project_id");
            catalog.ConfigColumns.Remove(project);
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId==project.Id);
        }
        var actor=new RuntimeActor(1,"发布项目账号",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L};
        var plan=NonRedundantReads.CompileCrossPageCandidate(catalog,table.Id,page.Id,["id","name"],[]).BindAccount(actor);
        var business=NonRedundantReads.CompileCrossPageMembership(catalog,table,page,["country_name","store_id"],
            new HashSet<string>(StringComparer.Ordinal){"country_name"}).BindAccount(actor);
        Assert.True(plan.MySql.RequiresPublishedProjectIdentity);
        Assert.DoesNotContain("b.`yz_project_id`=",plan.MySql.Scope);
        Assert.Contains(".`yz_project_id`=@project_scope_",plan.MySql.From);
        Assert.Equal(rowProject,business.MySql.Scope.Contains("b.`yz_project_id`=",StringComparison.Ordinal));
        Assert.Contains(actor.StoreId!.Value,plan.MySql.Constants.Values);

        var condition=FixedConditionSqlPlan.Compile(
            FixedConditionPlan.Compile("country_name <> '限制文字' AND store_id=@current_store_id",["country_name","store_id"]),
            new Dictionary<string,string>{{"country_name","string"},{"store_id","int64"}});
        var outer=NonRedundantReads.CompileWithCrossTables(catalog,"90",["id","outer_root_name"],
            [new("9004",[new("11",condition),new("91")])]).BindAccount(actor);
        Assert.True(outer.MySql.RequiresPublishedProjectIdentity);
        Assert.Contains("b.`yz_project_id`=@project_scope_number",outer.MySql.Scope);
        Assert.DoesNotContain("cross1.`yz_project_id`=",outer.MySql.From);
        Assert.Contains("cross2.`yz_project_id`=@project_scope_",outer.MySql.From);
        Assert.Contains("mt_nested_cross1_",outer.MySql.Expression("outer_root_name"));
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",
            ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new NonRedundantReader(session)
            .RowsAsync(plan,[],[],1,projectId:"999"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new NonRedundantReader(session)
            .RowsAsync(outer,[],[],1,projectId:"999"));
        // 全局资格不允许跳过请求上下文或把逻辑键当成账号字段。
        page.WhereSql+=" AND yz_project_id=@current_yz_project_id";
        Assert.ThrowsAny<Exception>(()=>NonRedundantReads.CompileCrossPageCandidate(catalog,table.Id,page.Id,["id"],[]));
    }

    /// <summary>回填使用来源行归属；内层路径变化与直接改名都能找到需要更新的缓存。</summary>
    [Theory]
    [InlineData(false,false,true)]
    [InlineData(true,false,false)]
    [InlineData(false,true,false)]
    [InlineData(true,true,true)]
    public void Recursive_cache_tracks_nested_dependencies_and_binds_row_ownership(bool shared,bool global,bool account)
    {
        var catalog=Catalog(shared);
        var source=catalog.ConfigTables.Single(table=>table.Id=="90");
        var target=catalog.ConfigTables.Single(table=>table.Id=="1");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="11");
        source.IsRongyu=1;
        foreach(var table in catalog.ConfigTables)table.YzProjectId=catalog.YzProjectId;
        if(global)
        {
            target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
            targetPage.YzAdminTypeId="5";
        }
        if(!account)targetPage.WhereSql="country_name <> '限制文字'";
        catalog.ConfigPages.Add(new(){Id="900",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,
            WhereSql=account?"store_id=@current_store_id":""});
        catalog.ConfigColumns.Add(new(){Id="9010",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"});
        var columns=RelationStorageColumns.Expand(source,catalog.ConfigColumns.Where(column=>column.ConfigTableId==source.Id));
        ColumnMetadata Field(string name)=>columns.Single(column=>column.ColumnName==name);
        var definition=new RelationCacheDefinition(source.Id,Field("outer_root_id").Id,target.Id,
            catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="name").Id,
            Field("outer_root_name").Id,false)
        {
            CrossTableColumnId="9004",CrossTableLevelKey="nested_source",CrossTableRecursivePageId=targetPage.Id,
            CrossTablePageId=global?targetPage.Id:null,
            CrossTableGlobalPages=global?new Dictionary<string,string>{{"nested_source",targetPage.Id}}:null
        };
        var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[definition]);
        var read=plan.Plans[source.Id].Read!;
        var mixed=Assert.Single(plan.Mixed.Consumers).Read;
        Assert.Equal(new[]{"outer_root_name"},plan.MixedSourceColumns[source.TableName]);
        Assert.Contains("nested_country",plan.MixedTargets);
        Assert.Contains("nested_place",plan.MixedTargets);
        Assert.Contains("nested_source",plan.MixedTargets);
        Assert.Equal(shared,plan.MixedTargets.Contains("nested_warehouse"));
        Assert.Contains("CASE WHEN",read.Expression("outer_root_name"));
        Assert.Contains("限制文字",read.Constants.Values);
        Assert.Equal(account,read.Expression("outer_root_name").Contains("b.`store_id`",StringComparison.Ordinal));
        Assert.Matches(@"cross\d+\.`yz_project_id`=@project_scope_number",read.From);
        Assert.Equal(!global,read.From.Contains("r0.`yz_project_id`=@project_scope_number",StringComparison.Ordinal));
        Assert.Contains("`offline`.`nested_country`",read.QualifiedMaintenanceFrom("`offline`"));
        var country=catalog.ConfigTables.Single(table=>table.TableName=="nested_country");
        var countryName=catalog.ConfigColumns.Single(column=>column.ConfigTableId==country.Id&&column.ColumnName is "name" or "title").ColumnName;
        Assert.Contains(read.PhysicalReferences,reference=>reference.Table==country.TableName&&reference.Column.Name==countryName);
        Assert.Contains(mixed.Reverse,path=>path.TargetTable==country.TableName&&path.Outputs["outer_root_name"].Contains(countryName));
        if(account)
        {
            catalog.ConfigPages.Add(new(){Id="901",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId});
            Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[definition]));
        }
        Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[definition with{CrossTableRecursivePageId="91"}]));
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
