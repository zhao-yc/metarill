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

    /// <summary>全局根使用固定发布页和附加逻辑范围，项目内层位与真实账号过滤仍进入同一 SQL。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Global_list_condition_preserves_published_scope_and_project_identity(bool tree)
    {
        var catalog=Catalog(false);
        var table=catalog.ConfigTables.Single(item=>item.Id=="1");
        var page=catalog.ConfigPages.Single(item=>item.Id=="11");
        foreach(var item in catalog.ConfigTables)item.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
        page.YzAdminTypeId="5";
        page.WhereSql="name <> '发布页排除' AND store_id=@current_store_id";
        table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(tree)AddVisibilityLeafScope(catalog,catalog.ConfigTables.Single(item=>item.Id=="90"),table,page,true);
        var field=tree?"visibility_name":"country_name";
        if(tree)
            page.WhereSql="name <> '发布页排除' AND store_id=@current_store_id";
        var actor=new RuntimeActor(1,"发布项目账号",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L};
        var plan=NonRedundantReads.CompileGlobalListCandidate(catalog,table.Id,page.Id,[field],["id","name"],[]);
        Assert.True(plan.MySql.RequiresPublishedProjectIdentity);
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));
        var bound=plan.BindAccount(actor);
        Assert.DoesNotContain("b.`yz_project_id`=",bound.MySql.Scope);
        Assert.Contains("发布页排除",bound.MySql.Constants.Values);
        Assert.Contains(actor.StoreId!.Value,bound.MySql.Constants.Values);
        Assert.DoesNotContain("发布页排除",bound.MySql.Scope);
        Assert.Contains(tree?"nested_visibility":"nested_country",bound.MySql.From+" "+bound.MySql.Expression(field));
        if(!tree)Assert.Contains(".`yz_project_id`=@project_scope_",bound.MySql.From);
        Assert.Equal(new[]{"id","name"},bound.OutputTypes.Keys);

        var condition=FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(field+" <> '控件排除'",[field]),
            new Dictionary<string,string>{{field,"string"}});
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",
            ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});
        Assert.Throws<UnauthorizedAccessException>(()=>NonRedundantCrossTableScopeSql.ApplyCondition(
            session.Select<ScopeRow>(),session,bound,condition,"options","999"));
        var sql=NonRedundantCrossTableScopeSql.ApplyCondition(session.Select<ScopeRow>(),session,bound,condition,
            "options",actor.ProjectId).ToSql();
        Assert.Contains("@options_condition_",sql);
        Assert.Contains("@options_constant_",sql);
        Assert.DoesNotContain("控件排除",sql);
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileGlobalListCandidate(
            catalog,table.Id,page.Id,["store_id"],["id"],[]));
    }

    /// <summary>生成实体查询只需固定主键关联，测试直接检查 MySQL SQL。</summary>
    private sealed class ScopeRow
    {
        [FreeSql.DataAnnotations.Column(Name="id",IsPrimary=true)]
        public long Id { get; set; }
    }

    /// <summary>原单值名称联接执行同一全局目标页，来源行继续绑定本项目和真实账号。</summary>
    [Theory]
    [InlineData("List",false,"string",false)]
    [InlineData("SelectPage",true,"int64",false)]
    [InlineData("List",false,"string",true)]
    [InlineData("SelectPage",true,"int64",true)]
    [InlineData("List",false,"string",false,true)]
    [InlineData("SelectPage",true,"int64",false,true)]
    public async Task Global_single_relation_joins_only_targets_visible_in_the_fixed_tree_page(string kind,bool rowProject,string key,bool cross,
        bool physicalAccount=false)
    {
        var catalog=Catalog(false);
        var source=catalog.ConfigTables.Single(table=>table.Id=="90");
        var target=catalog.ConfigTables.Single(table=>table.Id=="1");
        var page=catalog.ConfigPages.Single(item=>item.Id=="11");
        foreach(var table in catalog.ConfigTables)table.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
        page.YzAdminTypeId="5";
        page.WhereSql="store_id=@current_store_id";
        target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(!rowProject)
        {
            var field=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="yz_project_id");
            catalog.ConfigColumns.Remove(field);
            catalog.ConfigPageColumns.RemoveAll(item=>item.ConfigColumnId==field.Id);
        }
        if(physicalAccount)page.WhereSql+=" AND name <> '限制叶子'";
        else if(cross)page.WhereSql+=" AND country_name <> '限制叶子'";
        else AddVisibilityLeafScope(catalog,source,target,page,true);
        catalog.ConfigColumns.Add(new(){Id="9014",ConfigTableId=source.Id,ColumnName="shared_id",LogicalType=key,ControlKind=kind,LinkConfigTableId=target.Id,
            ControlDetail=JsonSerializer.Serialize(new{tableId=target.Id,pageId=page.Id,nameField="name"})});
        catalog.ConfigColumns.Add(new(){Id="9015",ConfigTableId=source.Id,ColumnName="shared_name",LogicalType="string",Extra=new()
        {
            ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
            ["columnFullName"]=JsonSerializer.SerializeToElement("shared.name")
        }});
        var actor=new RuntimeActor(1,"发布项目账号",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L};
        var plan=NonRedundantReads.Compile(catalog,source.Id,["id","shared_name"]);
        var candidate=physicalAccount?NonRedundantReads.CompileGlobalCandidate(catalog,target.Id,page.Id,["id","name"]):
            NonRedundantReads.CompileCrossPageCandidate(catalog,target.Id,page.Id,["id","name"],[]);
        Assert.True(plan.MySql.RequiresPublishedProjectIdentity);
        Assert.True(plan.MySql.RequiresAccount);
        Assert.Contains("b.`yz_project_id`=@project_scope_number",plan.MySql.Scope);
        Assert.DoesNotContain("r0.`yz_project_id`=",plan.MySql.From);
        Assert.DoesNotContain("b.`yz_project_id`=",candidate.MySql.Scope);
        if(physicalAccount)
        {
            Assert.Contains("r0.`store_id`=",plan.MySql.From);
            Assert.Contains("b.`store_id`=",candidate.MySql.Scope);
            Assert.Empty(candidate.MySql.Crosses);
        }
        else if(cross)
        {
            var correlated=plan.MySql.From.IndexOf("EXISTS (SELECT 1 FROM `nested_source`",StringComparison.Ordinal);
            Assert.True(correlated>plan.MySql.From.IndexOf("LEFT JOIN `nested_source` r0 ON",StringComparison.Ordinal));
            Assert.Contains("nested_country",plan.MySql.From[correlated..]);
            Assert.Contains("nested_place",candidate.MySql.From);
        }
        else
        {
            Assert.Contains("nested_visibility",plan.MySql.From);
            Assert.Contains("nested_visibility",candidate.MySql.Scope);
        }
        Assert.DoesNotContain("限制叶子",plan.MySql.From);
        Assert.Contains("限制叶子",plan.BindAccount(actor).MySql.Constants.Values);
        Assert.Contains(actor.StoreId!.Value,plan.BindAccount(actor).MySql.Constants.Values);
        Assert.Contains(actor.StoreId.Value,candidate.BindAccount(actor).MySql.Constants.Values);
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",
            ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new NonRedundantReader(session)
            .RowsAsync(plan.BindAccount(actor),[],[],1,projectId:"999"));
        source.IsRongyu=1;
        var sourcePage=new PageMetadata{Id="900",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",
            WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id"};
        catalog.ConfigPages.Add(sourcePage);
        catalog.ConfigColumns.Add(new(){Id="9016",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"});
        var cacheBinding=new RelationCacheDefinition(source.Id,"9014",target.Id,
            catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="name").Id,"9015",false)
            {ScopedSinglePageId=page.Id};
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[cacheBinding]);
        Assert.Empty(maintenance.Propagation.Bindings);
        Assert.Throws<InvalidOperationException>(()=>RelationMutationPlan.Compile(catalog,[cacheBinding with{ScopedSinglePageId=null}]));
        var consumer=Assert.Single(maintenance.Mixed.Consumers,item=>item.Table.Option.Id==source.Id);
        Assert.Contains("store_id",consumer.Inputs);
        Assert.Contains("b.`store_id`",consumer.Read.Expression("shared_name"));
        Assert.Contains("CASE WHEN r0.`id` IS NOT NULL",consumer.Read.Expression("shared_name"));
        Assert.False(consumer.Read.RequiresAccount);
        if(!physicalAccount)
            Assert.Single(consumer.Read.Reverse,item=>item.TargetTable==(cross?"nested_country":"nested_visibility"));
        if(cross)Assert.Single(consumer.Read.Reverse,item=>item.TargetTable=="nested_place");
        var direct=Assert.Single(consumer.Read.Reverse,item=>item.TargetTable==target.TableName);
        Assert.Contains(physicalAccount?"name":cross?"country_id":"visibility_id",direct.Outputs["shared_name"]);
        Assert.Contains("store_id",direct.Outputs["shared_name"]);
        sourcePage.WhereSql="yz_project_id=@current_yz_project_id";
        Assert.Throws<InvalidOperationException>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[cacheBinding]));
        sourcePage.WhereSql+=" AND store_id=@current_store_id";
        Assert.Throws<InvalidOperationException>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[cacheBinding with{ScopedSinglePageId="950"}]));
        source.IsRongyu=0;

        var definition=page.WhereSql;
        catalog.ConfigColumns.Add(new(){Id="9511",ConfigTableId=target.Id,ColumnName="password",LogicalType="string"});
        page.WhereSql+=" AND password <> '测试占位'";
        Assert.Throws<NotSupportedException>(()=>GlobalSingleRelationTarget.Require(catalog,source,
            catalog.ConfigColumns.Single(column=>column.Id=="9014"),target));
        page.WhereSql=definition;
        if(!cross&&!physicalAccount)
        {
            catalog.ConfigTables.Single(table=>table.Id=="95").Extra.Remove("scopeKind");
            Assert.Throws<NotSupportedException>(()=>NonRedundantReads.Compile(catalog,source.Id,["shared_name"]));
        }
    }

    /// <summary>项目内副本只读同项目、同物理父和同来源行归属的实际目标页。</summary>
    [Theory]
    [InlineData("List","string")]
    [InlineData("SelectPage","int64")]
    [InlineData("List","string",true,false)]
    [InlineData("SelectPage","int64",true,false)]
    [InlineData("List","string",true,true)]
    [InlineData("SelectPage","int64",true,true)]
    public void Project_single_cache_uses_fixed_page_and_physical_parent(string kind,string key,bool physicalAccount=false,bool withParent=true)
    {
        var catalog=Catalog(false);
        var source=catalog.ConfigTables.Single(table=>table.Id=="90");
        var target=catalog.ConfigTables.Single(table=>table.Id=="1");
        var page=catalog.ConfigPages.Single(item=>item.Id=="11");
        source.IsRongyu=1;
        foreach(var table in catalog.ConfigTables)table.YzProjectId=catalog.YzProjectId;
        page.YzAdminTypeId="5";
        if(physicalAccount)
            page.WhereSql="yz_project_id=@current_yz_project_id AND name <> '目标页隐藏' AND store_id=@current_store_id";
        if(withParent)page.WhereSql+=" AND group_id=@parent_id";
        var sourcePage=new PageMetadata{Id="900",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",
            WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id"+
                (withParent?" AND group_id=@parent_id":"")};
        catalog.ConfigPages.Add(sourcePage);
        catalog.ConfigColumns.AddRange([
            new(){Id="9014",ConfigTableId=source.Id,ColumnName="shared_id",LogicalType=key,ControlKind=kind,LinkConfigTableId=target.Id,
                ControlDetail=JsonSerializer.Serialize(new{tableId=target.Id,pageId=page.Id}),
                Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement(page.Id)}},
            new(){Id="9015",ConfigTableId=source.Id,ColumnName="shared_name",LogicalType="string",Extra=new()
            {
                ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                ["columnFullName"]=JsonSerializer.SerializeToElement("shared.name")
            }},
            new(){Id="9016",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"},
            new(){Id="9017",ConfigTableId=source.Id,ColumnName="group_id",LogicalType=key,LinkConfigTableId="91"},
            new(){Id="1111",ConfigTableId=target.Id,ColumnName="group_id",LogicalType="int64",LinkConfigTableId="91"}
        ]);
        var value=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="name");
        var binding=new RelationCacheDefinition(source.Id,"9014",target.Id,value.Id,"9015",false){ScopedSinglePageId=page.Id};
        source.IsRongyu=0;
        var actor=new RuntimeActor(1,"页面账号",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L};
        var readBinding=new NonRedundantSingleTargetScope("9014",page.Id,withParent?"9017":null);
        var read=NonRedundantReads.CompileProjectPage(catalog,source.Id,["id","shared_name"],["shared_name"],[],[readBinding]);
        var live=read.BindAccount(actor).MySql;
        Assert.Throws<UnauthorizedAccessException>(()=>read.BindAccount(actor with{StoreId=null}));
        Assert.Contains(actor.StoreId!.Value,live.Constants.Values);
        Assert.Contains("r0.`yz_project_id`=@project_scope_number",live.From);
        Assert.Contains("EXISTS (SELECT 1",live.From);
        Assert.Equal("r0.`name`",live.Expression("shared_name"));
        Assert.DoesNotContain("store_id",live.Scope);
        if(withParent)
        {
            Assert.Contains("b.`group_id`",live.From);
            Assert.Contains("nested_outer_leaf",live.From);
            Assert.Throws<InvalidOperationException>(()=>NonRedundantReads.CompileProjectPage(catalog,source.Id,["shared_name"],[],[],
                [readBinding with{SourceParentId="9014"}]));
        }
        source.IsRongyu=1;
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]);
        var consumer=Assert.Single(maintenance.Mixed.Consumers,item=>item.Table.Option.Id==source.Id);
        var label=consumer.Read.Expression("shared_name");
        Assert.Contains("b.`store_id`",label);
        Assert.Contains("r0.`yz_project_id`=@project_scope_number",consumer.Read.From);
        if(withParent)
        {
            Assert.Contains("b.`group_id`",label);
            Assert.Contains("r0.`group_id`",label);
            Assert.Contains("`yz_project_id`=@project_scope_number",label);
            Assert.Contains("group_id",consumer.Inputs);
            var reverse=Assert.Single(consumer.Read.Reverse,item=>item.TargetTable=="nested_outer_leaf");
            if(key=="int64")Assert.Equal("b.`group_id`=@target_id",reverse.TargetPredicate);
            else Assert.Contains("BINARY b.`group_id`=BINARY @target_text",reverse.TargetPredicate);
        }
        else Assert.DoesNotContain("group_id",label);
        if(physicalAccount)
        {
            Assert.False(consumer.Read.RequiresAccount);
            Assert.Contains("目标页隐藏",consumer.Read.Constants.Values);
            Assert.Contains("store_id",consumer.Inputs);
            Assert.Contains(consumer.Read.Reverse,item=>item.TargetTable==target.TableName&&
                item.Outputs["shared_name"].Contains("store_id"));
            var definition=sourcePage.WhereSql;
            sourcePage.WhereSql="yz_project_id=@current_yz_project_id";
            Assert.Throws<InvalidOperationException>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]));
            sourcePage.WhereSql=definition;
        }
        Assert.Throws<InvalidOperationException>(()=>RelationMutationPlan.Compile(catalog,[binding with{ScopedSinglePageId=null}]));

        if(kind=="SelectPage")
        {
            catalog.ConfigPages.Add(new(){Id="12",ConfigTableId=target.Id});
            catalog.ConfigPageColumns.Add(new(){Id="90014",ConfigPageId="900",ConfigColumnId="9014",
                Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement("{\"pageId\":\"12\"}")}});
            Assert.Throws<InvalidOperationException>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]));
        }
        else
        {
            catalog.ConfigPageColumns.Add(new(){Id="90014",ConfigPageId="900",ConfigColumnId="9014",
                Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"id\"}")}});
            Assert.Throws<InvalidOperationException>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]));
        }
    }

    /// <summary>回填使用来源行归属；内层路径变化与直接改名都能找到需要更新的缓存。</summary>
    [Theory]
    [InlineData(false,false,true)]
    [InlineData(true,false,false)]
    [InlineData(false,true,false)]
    [InlineData(true,true,true)]
    [InlineData(false,false,true,true)]
    [InlineData(true,true,true,true)]
    public void Recursive_cache_tracks_nested_dependencies_and_binds_row_ownership(bool shared,bool global,bool account,bool selfTreeCondition=false)
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
        if(selfTreeCondition)AddVisibilityLeafScope(catalog,source,target,targetPage,global);
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
        if(selfTreeCondition)
        {
            Assert.Contains("nested_visibility",plan.MixedTargets);
            Assert.Contains("WITH RECURSIVE",read.Expression("outer_root_name"));
            Assert.Contains("store_id",read.SourceDependencies);
            Assert.Contains(mixed.Reverse,path=>path.TargetTable=="nested_source"&&path.Outputs["outer_root_name"].Contains("visibility_id"));
            var node=Assert.Single(mixed.Reverse,path=>path.TargetTable=="nested_visibility");
            foreach(var field in new[]{"id","name","parent_id","status","delete_no"})Assert.Contains(field,node.Outputs["outer_root_name"]);
            Assert.Contains("@target_text",node.TargetPredicate);
            if(!global)
            {
                Assert.Contains("b.`merchant_id`",read.Expression("outer_root_name"));
                Assert.Contains("merchant_id",read.SourceDependencies);
                var page=catalog.ConfigPages.Single(item=>item.Id=="900");
                var original=page.WhereSql;
                page.WhereSql="store_id=@current_store_id";
                var error=Assert.Throws<InvalidOperationException>(()=>DerivedCrossTableCaches.Validate(catalog,[definition],[]));
                Assert.Contains("物理账号归属",error.Message);
                page.WhereSql=original;
            }
        }
        if(account)
        {
            catalog.ConfigPages.Add(new(){Id="901",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId});
            Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[definition]));
        }
        Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[definition with{CrossTableRecursivePageId="91"}]));
    }

    /// <summary>在既有跨表范围上叠加自树条件，覆盖同时依赖两种级联的缓存。</summary>
    private static void AddVisibilityLeafScope(MetadataCatalog catalog,TableMetadata source,TableMetadata target,PageMetadata page,bool global)
    {
        var visibility=new TableMetadata{Id="95",TableName="nested_visibility",IsRongyu=1,YzProjectId=catalog.YzProjectId};
        if(global)visibility.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigTables.Add(visibility);
        catalog.ConfigColumns.AddRange([
            new(){Id="9501",ConfigTableId="95",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="9502",ConfigTableId="95",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="9503",ConfigTableId="95",ColumnName="name",LogicalType="string"},
            new(){Id="9504",ConfigTableId="95",ColumnName="parent_id",LogicalType="int64",LinkConfigTableId="95"},
            new(){Id="9505",ConfigTableId="95",ColumnName="status",LogicalType="int32"},
            new(){Id="9506",ConfigTableId="95",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="9507",ConfigTableId="95",ColumnName="merchant_id",LogicalType="int64"},
            new(){Id="9510",ConfigTableId=target.Id,ColumnName="visibility_id",LogicalType="int64",ControlKind="CascadeTree",
                LinkConfigTableId="95",ControlDetail="""{"sourceMode":"selfTree","selfTree":{"table":"nested_visibility","parentField":"parent_id"}}"""}
        ]);
        var filter="status=1";
        if(!global)
        {
            filter+=" AND merchant_id=@current_merchant_id";
            catalog.ConfigColumns.Add(new(){Id="9011",ConfigTableId=source.Id,ColumnName="merchant_id",LogicalType="int64"});
            catalog.ConfigPages.Single(item=>item.Id=="900").WhereSql+=" AND merchant_id=@current_merchant_id";
        }
        catalog.ConfigPages.Add(new(){Id="950",ConfigTableId="95",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",WhereSql=filter});
        foreach(var id in new[]{"9503","9504"})catalog.ConfigPageColumns.Add(new(){Id="950_"+id,ConfigPageId="950",ConfigColumnId=id});
        page.WhereSql+=" AND visibility_name <> '限制叶子'";
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
    [InlineData("MultiSelectPage",false,true,false,null,true,false,false,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,true,true,true,false,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,false,true,true,true,true)]
    [InlineData("MultiSelectPage",false,true,true,null,true,true,false,true,true)]
    [InlineData("MultiSelectPage",false,true,false,null,true,true,false,false,false,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,true,true,true,false,false,false,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,false,true,true,true,true,false,true)]
    [InlineData("MultiSelectPage",false,true,false,null,true,false,false,false,false,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,false,true,false,false,false,false,true)]
    [InlineData("MultiCheckBoxDatabase",false,true,true,true,true,false,false,true,true,true)]
    public async Task Multi_label_cache_keeps_recursive_scope_and_its_reverse_dependencies(
        string kind,bool global,bool rootCondition,bool physical,bool? globalRowProject,bool parent=false,bool logicalParent=false,bool onlyParent=false,bool logicalSource=false,bool sharedSource=false,bool globalParent=false)
    {
        var catalog=Catalog(global);
        var visible=catalog.ConfigTables.Single(table=>table.Id=="1");
        if(global||globalParent)
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
        var parentTable=logicalParent||logicalSource||globalParent?"nested_source":"label_scope_parent";
        if(parent)
        {
            if(!logicalParent&&!logicalSource&&!globalParent)catalog.ConfigTables.Add(new(){Id="97",TableName="label_scope_parent",IsRongyu=1});
            if(!logicalParent&&!logicalSource&&!globalParent)catalog.ConfigColumns.AddRange([
                new(){Id="9701",ConfigTableId="97",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="9702",ConfigTableId="97",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="9703",ConfigTableId="97",ColumnName="delete_no",LogicalType="datetime"},
            ]);
            if(!logicalParent)catalog.ConfigColumns.Add(new(){Id="9094",ConfigTableId="90",ColumnName="target_scope_id",LogicalType="string",LinkConfigTableId=logicalSource||globalParent?"1":"97"});
            catalog.ConfigColumns.Add(new(){Id="9894",ConfigTableId="98",ColumnName="source_scope_id",LogicalType="int64",LinkConfigTableId=logicalParent||logicalSource||globalParent?"1":"97"});
            if(logicalSource)catalog.ConfigColumns.Add(new(){Id="9895",ConfigTableId="98",ColumnName="source_leaf_id",LogicalType="int64",LinkConfigTableId="91",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new
            {
                sourceMode="crossTable",levels=new object[]{new{table="nested_source",columnPrefix="source_scope"},
                    new{table="nested_outer_leaf",columnPrefix="source_leaf",parentField="root_id"}}
            })});
            if(sharedSource)
            {
                var original=catalog.ConfigColumns.Single(column=>column.Id=="9895");
                catalog.ConfigColumns.Add(new(){Id="9896",ConfigTableId="98",ColumnName="source_alt_id",LogicalType="int64",
                    LinkConfigTableId="91",ControlKind="CascadeTree",ControlDetail=original.ControlDetail.Replace("source_leaf","source_alt",StringComparison.Ordinal)});
            }
            var targetPage=catalog.ConfigPages.Single(page=>page.Id=="900");
            targetPage.WhereSql+=(targetPage.WhereSql.Length>0?" AND ":"")+targetParentField+"=@parent_id";
            catalog.ConfigPages.Single(page=>page.Id=="980").WhereSql+=" AND source_scope_id=@parentId";
        }

        var read=MySqlNonRedundantReadCompiler.Compile(catalog,"98",[field]);
        var actor=new RuntimeActor(1,"多值账号",false,catalog.YzProjectId,"5"){StoreId=9007199254741973L};
        Assert.True(read.RequiresAccount);
        Assert.Contains(actor.StoreId!.Value,read.BindAccount(actor).Constants.Values);
        Assert.Equal(global||globalParent||globalRowProject is not null,read.RequiresPublishedProjectIdentity);
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
                Assert.Contains("b.`"+targetParentField+"`",bound.Scope);
                if(globalParent)
                {
                    Assert.DoesNotContain("multi_parent.`yz_project_id`",bound.Scope);
                    if(!logicalParent)
                    {
                        Assert.Contains("global_parent.`delete_no` IS NULL",bound.Scope);
                        Assert.Contains("global_parent.`store_id`",bound.Scope);
                        Assert.DoesNotContain("global_parent.`yz_project_id`",bound.Scope);
                        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
                        var missing=await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session)
                            .ReadStatisticsAsync(bound,[],[],projectId:actor.ProjectId));
                        Assert.Contains("必须绑定实际父ID",missing.Message);
                    }
                    var parentPlan=NonRedundantReads.CompileMultiGlobalParent(catalog,"90","900").BindAccount(actor).MySql;
                    Assert.Contains("nested_source",parentPlan.From);
                    Assert.DoesNotContain("b.`yz_project_id`=",parentPlan.Scope);
                    Assert.Contains("限制文字",parentPlan.Constants.Values);
                    Assert.Contains(actor.StoreId!.Value,parentPlan.Constants.Values);
                }
                else
                {
                    Assert.Contains(parentTable,bound.Scope);
                    Assert.Contains("multi_parent.`delete_no` IS NULL",bound.Scope);
                }
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
        if(logicalSource)
        {
            var readProjection=read.MultiProjections[field];
            Assert.Contains("CAST(mt_parent_input.`source_scope_id` AS CHAR)",readProjection.TargetScope);
            Assert.Contains("CASE WHEN",readProjection.Owner!.SourceExpressions!["source_scope_id"]);
            Assert.Contains("LEFT JOIN `nested_outer_leaf` cross",read.From);
            Assert.Contains("source_leaf_id",read.ArrayNamePredicates[field]);
            if(sharedSource)
            {
                Assert.Contains("source_alt_id",read.ArrayNamePredicates[field]);
                Assert.Contains(read.Crosses,path=>path.RequireRowValidity&&path.SharedGroupKey is not null);
            }
            Assert.DoesNotContain("mt_parent_input.",read.ArrayNamePredicates[field]);
            if(logicalParent)
            {
                Assert.Contains("cross0 ON ",readProjection.Joins);
                Assert.DoesNotContain("` cross0 ON ",readProjection.Joins);
            }
            Assert.Contains("980",MySqlNonRedundantReadCompiler.PublishedCascadeLabelPages(catalog));
            return;
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

    /// <summary>多值候选和实时名称绑定账号，回填按来源行的账号及父键重算。</summary>
    [Theory]
    [InlineData("MultiSelectPage",true)]
    [InlineData("MultiCheckBoxDatabase",false)]
    [InlineData("MultiSelectPage",true,false,false)]
    [InlineData("MultiCheckBoxDatabase",true,false,false)]
    [InlineData("MultiSelectPage",true,false,true)]
    [InlineData("MultiCheckBoxDatabase",true,false,true)]
    [InlineData("MultiSelectPage",false,true,true)]
    [InlineData("MultiCheckBoxDatabase",true,true,true)]
    public void Multi_account_scope_uses_source_row_for_maintenance(string kind,bool rowProject,
        bool global=true,bool parent=false)
    {
        var catalog=Catalog(false);
        var target=catalog.ConfigTables.Single(table=>table.Id=="1");
        var source=catalog.ConfigTables.Single(table=>table.Id=="90");
        var page=catalog.ConfigPages.Single(item=>item.Id=="11");
        var name=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="name");
        foreach(var table in catalog.ConfigTables)table.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
        page.YzAdminTypeId="5";
        page.Extra["type"]=JsonSerializer.SerializeToElement(2);
        page.WhereSql=(global?"":"yz_project_id=@current_yz_project_id AND ")+
            "name <> '范围外名称' AND store_id=@current_store_id"+(parent?" AND group_id=@parent_id":"");
        if(global)target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(!rowProject)
        {
            var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="yz_project_id");
            catalog.ConfigColumns.Remove(project);
            catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId==project.Id);
        }
        if(!catalog.ConfigPageColumns.Any(column=>column.ConfigPageId==page.Id&&column.ConfigColumnId==name.Id))
            catalog.ConfigPageColumns.Add(new(){Id="publish_multi_name",ConfigPageId=page.Id,ConfigColumnId=name.Id});
        catalog.ConfigPages.Add(new(){Id="900",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",
            WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id"+
                (parent?" AND group_id=@parent_id":"")});
        if(parent)
        {
            catalog.ConfigColumns.Add(new(){Id="9017",ConfigTableId=source.Id,ColumnName="group_id",LogicalType="int64",LinkConfigTableId="91"});
            catalog.ConfigColumns.Add(new(){Id="1111",ConfigTableId=target.Id,ColumnName="group_id",LogicalType="string",LinkConfigTableId="91"});
        }
        catalog.ConfigColumns.AddRange([
            new(){Id="9014",ConfigTableId=source.Id,ColumnName="picked_ids",LogicalType="string",ControlKind=kind,
                LinkConfigTableId=target.Id,ControlDetail=JsonSerializer.Serialize(new{tableId=target.Id,pageId=page.Id})},
            new(){Id="9015",ConfigTableId=source.Id,ColumnName="picked_names",LogicalType="string",Extra=new()
            {
                ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                ["columnFullName"]=JsonSerializer.SerializeToElement("picked.name")
            }},
            new(){Id="9016",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"}
        ]);
        var actor=new RuntimeActor(1,"多值账号",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L};
        var plan=NonRedundantReads.CompileScopedMultiLabel(catalog,target.Id,page.Id,"name",["id","name"],[]);
        var bound=plan.BindAccount(actor).MySql;
        Assert.Equal(global,bound.RequiresPublishedProjectIdentity);
        Assert.Contains(actor.StoreId!.Value,bound.Constants.Values);
        Assert.Contains("范围外名称",bound.Constants.Values);
        Assert.Equal(!global,bound.Scope.Contains("b.`yz_project_id`=",StringComparison.Ordinal));
        Assert.Equal(parent?"group_id":null,bound.RequiredMultiParent);
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));
        if(global&&parent)
        {
            var pagePlan=NonRedundantReads.CompileGlobalAccountMultiPage(catalog,target.Id,page.Id,["id","name"],[],[]).BindAccount(actor).MySql;
            Assert.Equal("group_id",pagePlan.RequiredMultiParent);
            Assert.Contains("nested_outer_leaf",pagePlan.Scope);
            Assert.DoesNotContain("b.`yz_project_id`=",pagePlan.Scope);
            Assert.Contains(actor.StoreId.Value,pagePlan.Constants.Values);
            Assert.Throws<NotSupportedException>(()=>GlobalCrossTableTarget.RequireAccountCandidate(catalog,target,page));
        }

        var live=NonRedundantReads.Compile(catalog,source.Id,["picked_names"]).BindAccount(actor).MySql;
        Assert.Contains("b.`yz_project_id`=@project_scope_number",live.Scope);
        Assert.Contains("multi_value.`store_id`",live.ArrayNamePredicates["picked_names"]);
        Assert.Contains(actor.StoreId.Value,live.Constants.Values);

        source.IsRongyu=1;
        var binding=new RelationCacheDefinition(source.Id,"9014",target.Id,name.Id,"9015",true);
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]);
        var refresh=maintenance.Plans[source.Id].Read!;
        var projection=refresh.MultiProjections["picked_names"];
        Assert.False(refresh.RequiresAccount);
        Assert.Contains("b.`store_id`",projection.TargetScope);
        Assert.Equal(!global,projection.TargetScope.Contains("multi_value.`yz_project_id`=",StringComparison.Ordinal));
        if(parent)
        {
            Assert.Contains("b.`group_id`",projection.TargetScope);
            Assert.Contains("nested_outer_leaf",projection.TargetScope);
            Assert.Contains("nested_outer_leaf",maintenance.MixedTargets);
        }
        Assert.Contains("范围外名称",refresh.Constants.Values);
        Assert.Contains(refresh.Reverse,entry=>entry.TargetTable==target.TableName&&entry.Outputs["picked_names"].Contains("store_id"));
        var published=MySqlNonRedundantReadCompiler.PublishedCascadeLabelPages(catalog,out var owners);
        Assert.Contains(page.Id,published);
        Assert.Contains("900",owners);
        catalog.ConfigPages.Single(item=>item.Id=="900").WhereSql="yz_project_id=@current_yz_project_id";
        Assert.Throws<InvalidOperationException>(()=>DerivedCrossTableCaches.Validate(catalog,[binding],[]));
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
