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

    /// <summary>冗余父范围按实际存储父键关联，常量或账号递归条件都进入查询和当前锁。</summary>
    [Theory]
    [InlineData(false,true)]
    [InlineData(true,false)]
    public void Redundant_parent_uses_the_same_recursive_candidate_for_reads_and_locks(bool global,bool account)
    {
        var catalog=Catalog(global);
        var source=catalog.ConfigTables.Single(table=>table.Id=="90");
        var target=catalog.ConfigTables.Single(table=>table.Id=="1");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="11");
        source.IsRongyu=1;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
        catalog.ConfigPages.Add(new(){Id="900",ConfigTableId=source.Id,
            WhereSql="yz_project_id=@current_yz_project_id AND outer_leaf_id=@parent_id"+(account?" AND store_id=@current_store_id":"")});
        catalog.ConfigColumns.Add(new(){Id="9010",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"});
        foreach(var table in catalog.ConfigTables)table.YzProjectId=catalog.YzProjectId;
        foreach(var page in catalog.ConfigPages)
        {
            page.YzProjectId=catalog.YzProjectId;
            page.YzAdminTypeId="5";
        }
        if(global)target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(!account)targetPage.WhereSql="country_name <> '限制文字'";
        var path=Assert.Single(RedundantSharedParents.Compile(catalog,source.Id,"900")).Value;
        Assert.Equal("outer_leaf_id",path.ColumnName);
        Assert.Equal(2,path.Levels.Count);
        Assert.NotNull(path.Levels[0].RecursiveCandidate);
        Assert.True(path.Levels[0].RequiresCurrentScope);
        Assert.Null(path.Levels[1].RecursiveCandidate);

        var provider=new MySqlDatabaseProvider();
        const long storeId=9007199254741993L;
        var actor=new RuntimeActor(1,"父范围测试",false,catalog.YzProjectId,"5"){StoreId=account?storeId:null};
        FixedConditionSqlPlan.Clause Render(RuntimeActor identity,bool locking)=>RedundantSharedParentScopeSql.Render(
            provider,[path],identity,"test_parent",field=>"a."+provider.QuoteIdentifier(field),locking);
        var read=Render(actor,false);
        var write=Render(actor,true);
        Assert.Contains("BINARY CAST(a.`outer_leaf_id` AS CHAR)",read.Sql);
        Assert.Contains("b.`id`=rp0.`id`",read.Sql);
        Assert.Contains("LEFT JOIN `nested_country`",read.Sql);
        Assert.Contains("@test_parent_0_0_recursive_mt_candidate_",read.Sql);
        Assert.DoesNotContain("限制文字",read.Sql);
        Assert.Contains("限制文字",read.Parameters.Values);
        Assert.Equal(!global,read.Sql.Contains("rp0.`yz_project_id`=",StringComparison.Ordinal));
        Assert.Equal(account,read.Parameters.Values.Contains(storeId));
        Assert.DoesNotContain("FOR UPDATE",read.Sql);
        Assert.Contains(" FOR SHARE)",write.Sql);
        Assert.EndsWith(" FOR UPDATE)",write.Sql);
        Assert.Equal(read.Parameters,write.Parameters);
        if(account)Assert.Throws<UnauthorizedAccessException>(()=>Render(actor with{StoreId=null,IsAdministrator=true},false));
        if(global)Assert.Throws<UnauthorizedAccessException>(()=>Render(actor with{ProjectId="999"},false));
    }

    /// <summary>多值名称读取按当前账号，回填按各来源行归属，并记录内层节点的改名依赖。</summary>
    [Theory]
    [InlineData("MultiSelectPage",false,false,false,null)]
    [InlineData("MultiCheckBoxDatabase",true,false,false,null)]
    [InlineData("MultiSelectPage",false,true,false,null)]
    [InlineData("MultiCheckBoxDatabase",true,true,false,null)]
    [InlineData("MultiSelectPage",false,true,true,null)]
    [InlineData("MultiCheckBoxDatabase",true,true,true,null)]
    [InlineData("MultiSelectPage",false,true,true,true)]
    [InlineData("MultiSelectPage",false,true,false,false)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,false)]
    [InlineData("MultiCheckBoxDatabase",true,true,false,true)]
    [InlineData("MultiSelectPage",false,true,false,null,true)]
    [InlineData("MultiSelectPage",false,true,true,true,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,false,true)]
    [InlineData("MultiCheckBoxDatabase",true,true,false,true,true)]
    [InlineData("MultiSelectPage",false,true,false,null,true,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,true,true,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,false,false,true,true)]
    [InlineData("MultiSelectPage",false,true,true,null,true,true,true)]
    public void Multi_label_cache_keeps_recursive_scope_and_its_reverse_dependencies(
        string kind,bool global,bool rootCondition,bool physical,bool? globalRowProject,bool parent=false,bool logicalParent=false,bool onlyParent=false)
    {
        var catalog=Catalog(global);
        var visible=catalog.ConfigTables.Single(table=>table.Id=="1");
        if(global)
        {
            visible.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            visible.YzProjectId=catalog.YzProjectId;
            catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
            catalog.ConfigPages.Single(page=>page.Id=="11").YzAdminTypeId="5";
        }

        catalog.ConfigPages.Add(new(){Id="900",ConfigTableId="90",YzProjectId=catalog.YzProjectId,
            WhereSql=rootCondition&&!onlyParent?"outer_root_name <> '隐藏名称' AND store_id=@current_store_id":"",
            Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});
        if(rootCondition)catalog.ConfigColumns.Add(new(){Id="9008",ConfigTableId="90",ColumnName="store_id",LogicalType="int64"});
        if(globalRowProject is { } rowProject)
        {
            var target=catalog.ConfigTables.Single(table=>table.Id=="90");
            target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            target.YzProjectId=catalog.YzProjectId;
            if(!catalog.YzAdminTypes.Any(admin=>admin.Id=="5"))
                catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
            catalog.ConfigPages.Single(page=>page.Id=="900").YzAdminTypeId="5";
            if(!rowProject)catalog.ConfigColumns.RemoveAll(column=>column.Id=="9002");
        }
        catalog.ConfigColumns.Add(new(){Id="9007",ConfigTableId="90",ColumnName="outer_root_name",LogicalType="string"});
        catalog.ConfigPageColumns.Add(new(){Id="9009007",ConfigColumnId="9007",ConfigPageId="900",IsShow=1});
        catalog.ConfigTables.Add(new(){Id="98",TableName="nested_multis",IsRongyu=0,YzProjectId=catalog.YzProjectId});
        var label=physical?"name":"outer_root_name";
        var field="picked_"+label+"s";
        if(physical)catalog.ConfigPageColumns.Add(new(){Id="9009003",ConfigColumnId="9003",ConfigPageId="900",IsShow=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="9801",ConfigTableId="98",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="9802",ConfigTableId="98",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="9803",ConfigTableId="98",ColumnName="store_id",LogicalType="int64"},
            new(){Id="9804",ConfigTableId="98",ColumnName="picked_ids",LogicalType="string",ControlKind=kind,
                LinkConfigTableId="90",ControlDetail=JsonSerializer.Serialize(new{tableId="90",pageId="900",nameField=label})},
            new(){Id="9805",ConfigTableId="98",ColumnName=field,LogicalType="string",Extra=new()
                {["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("picked."+label)}}
        ]);
        catalog.ConfigPages.Add(new(){Id="980",ConfigTableId="98",WhereSql="store_id=@current_store_id"});
        var targetParentField=logicalParent?"outer_root_id":"target_scope_id";
        var parentTable=logicalParent?"nested_source":"label_scope_parent";
        if(parent)
        {
            if(!logicalParent)catalog.ConfigTables.Add(new(){Id="97",TableName="label_scope_parent",IsRongyu=1});
            if(!logicalParent)catalog.ConfigColumns.AddRange([
                new(){Id="9701",ConfigTableId="97",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="9702",ConfigTableId="97",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="9703",ConfigTableId="97",ColumnName="delete_no",LogicalType="datetime"},
                new(){Id="9094",ConfigTableId="90",ColumnName="target_scope_id",LogicalType="string",LinkConfigTableId="97"}
            ]);
            catalog.ConfigColumns.Add(new(){Id="9894",ConfigTableId="98",ColumnName="source_scope_id",LogicalType="int64",LinkConfigTableId=logicalParent?"1":"97"});
            var targetPage=catalog.ConfigPages.Single(page=>page.Id=="900");
            targetPage.WhereSql+=(targetPage.WhereSql.Length>0?" AND ":"")+targetParentField+"=@parent_id";
            catalog.ConfigPages.Single(page=>page.Id=="980").WhereSql+=" AND source_scope_id=@parentId";
        }

        var read=MySqlNonRedundantReadCompiler.Compile(catalog,"98",[field]);
        var actor=new RuntimeActor(1,"多值账号",false,catalog.YzProjectId,"5"){StoreId=9007199254741973L};
        Assert.True(read.RequiresAccount);
        Assert.Contains(actor.StoreId!.Value,read.BindAccount(actor).Constants.Values);
        Assert.Equal(global||globalRowProject is not null,read.RequiresPublishedProjectIdentity);
        Assert.Contains("mt_nested_",read.ArrayNamePredicates[field]);
        if(rootCondition)
        {
            var selection=NonRedundantReads.CompileScopedMultiLabel(catalog,"90","900",label,["id",label],[]);
            var bound=selection.BindAccount(actor).MySql;
            if(!onlyParent)Assert.Contains("隐藏名称",bound.Constants.Values);
            if(!onlyParent)Assert.Contains("mt_nested_",bound.Scope);
            Assert.DoesNotContain("( AND ",bound.Scope);
            Assert.Contains("IS NOT NULL",bound.Scope);
            Assert.Equal(parent?targetParentField:null,bound.RequiredMultiParent);
            if(parent)
            {
                Assert.Contains(parentTable,bound.Scope);
                Assert.Contains("b.`"+targetParentField+"`",bound.Scope);
                Assert.Contains("multi_parent.`delete_no` IS NULL",bound.Scope);
                if(logicalParent)
                {
                    Assert.Contains("CASE WHEN",bound.Expression(targetParentField));
                    Assert.Contains("nested_outer_leaf",bound.From);
                }
            }
            if(globalRowProject is not null)
            {
                Assert.DoesNotContain("b.`yz_project_id`=",bound.Scope);
                Assert.Contains(".`yz_project_id`=@project_scope_",bound.From);
                Assert.True(bound.RequiresPublishedProjectIdentity);
            }
            if(!onlyParent)Assert.Contains("隐藏名称",read.BindAccount(actor).Constants.Values);
        }

        catalog.ConfigTables.Single(table=>table.Id=="98").IsRongyu=1;
        var binding=new RelationCacheDefinition("98","9804","90",physical?"9003":"9007","9805",true);
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]);
        var compiled=maintenance.Plans["98"].Read!;
        var projection=compiled.MultiProjections[field];
        Assert.False(compiled.RequiresAccount);
        Assert.Equal(parent?new[]{"source_scope_id","store_id"}:new[]{"store_id"},projection.Owner!.Fields);
        if(parent)
        {
            Assert.Contains(")=BINARY CAST(b.`source_scope_id` AS CHAR)",projection.TargetScope);
            Assert.Contains(logicalParent?"CAST(CASE WHEN":"CAST(multi_value.`target_scope_id` AS CHAR)",projection.TargetScope);
            Assert.Contains(parentTable,maintenance.MixedTargets);
            Assert.Contains(compiled.Reverse,path=>path.TargetTable==parentTable&&path.ForeignExpression=="b.`source_scope_id`"&&path.Outputs[field].Contains("delete_no"));
        }
        Assert.Contains("b.`store_id`",projection.NameExpression!);
        Assert.Contains("限制文字",compiled.Constants.Values);
        if(rootCondition)
        {
            if(!onlyParent)Assert.Contains("隐藏名称",compiled.Constants.Values);
            if(!onlyParent)Assert.Contains("mt_nested_",projection.TargetScope);
            Assert.DoesNotContain("( AND ",projection.TargetScope);
            Assert.Contains("CASE WHEN",projection.NameExpression!);
            if(globalRowProject is not null)
            {
                Assert.DoesNotContain("multi_value.`yz_project_id`=",projection.TargetScope);
                Assert.Contains(".`yz_project_id`=@project_scope_",projection.Joins);
                Assert.True(compiled.RequiresPublishedProjectIdentity);
            }
            Assert.Contains(compiled.Reverse,path=>path.TargetTable=="nested_outer"&&path.Outputs[field].Contains(onlyParent?"outer_leaf_id":"store_id"));
        }
        Assert.Contains("nested_country",maintenance.MixedTargets);
        Assert.Equal(global,maintenance.MixedTargets.Contains("nested_warehouse"));
        Assert.Contains(compiled.Reverse,path=>path.TargetTable=="nested_country"&&
            path.Outputs[field].Any(member=>member is "name" or "title"));
        Assert.Contains(compiled.Reverse,path=>path.TargetTable=="nested_source"&&
            path.Outputs[field].Contains("store_id"));
        var pages=MySqlNonRedundantReadCompiler.PublishedCascadeLabelPages(catalog,out var owners);
        Assert.Contains("11",pages);
        foreach(var role in global?new[]{"country","area","place","warehouse"}:new[]{"country","area","place"})
        {
            var table=catalog.ConfigTables.Single(item=>item.TableName=="nested_"+role);
            Assert.Contains(catalog.ConfigPages.Single(page=>page.ConfigTableId==table.Id).Id,pages);
        }
        Assert.Contains("980",owners);
        Assert.Equal(rootCondition,pages.Contains("900"));

        catalog.ConfigPages.Add(new(){Id="981",ConfigTableId="98"});
        Assert.Throws<InvalidOperationException>(()=>DerivedCrossTableCaches.Validate(catalog,[binding],[]));
        if(rootCondition)
        {
            catalog.ConfigTables.Single(table=>table.Id=="98").IsRongyu=0;
            catalog.ConfigPages.Single(page=>page.Id=="900").YzProjectId="999";
            if(globalRowProject is null)
                Assert.Throws<UnauthorizedAccessException>(()=>MySqlNonRedundantReadCompiler.Compile(catalog,"98",[field]));
            else
                Assert.Throws<NotSupportedException>(()=>MySqlNonRedundantReadCompiler.Compile(catalog,"98",[field]));
        }
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
