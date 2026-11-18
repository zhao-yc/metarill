using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class CascadeMultiLabelReadTests
{
    /// <summary>自树检查完整祖先链及环路，同一名称可用于筛选并记录根及父变更依赖。</summary>
    [Theory]
    [InlineData(false,"string")]
    [InlineData(true,"string")]
    [InlineData(false,"int32")]
    [InlineData(true,"int64")]
    [InlineData(false,"string",true)]
    [InlineData(true,"string",true)]
    public void Self_tree_label_requires_reaching_visible_root(bool account,string type,bool selectedScope=false)
    {
        var catalog=DerivedMultipleCacheTests.Catalog("string","int64");
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="cache");
        var tree=catalog.ConfigTables.Single(table=>table.Id=="tree");
        tree.IsRongyu=1;
        var treeLabel=catalog.ConfigColumns.Single(column=>column.Id=="tree_name");
        treeLabel.Extra.Clear();
        treeLabel.LogicalType=type;
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

        if(account)AddAccountScope(catalog,"consumer","201");
        if(selectedScope)
        {
            catalog.ConfigColumns.AddRange([
                new(){Id="target_store",ConfigTableId="source",ColumnName="store_id",LogicalType="int64"},
                new(){Id="consumer_store",ConfigTableId="consumer",ColumnName="store_id",LogicalType="int64"}
            ]);
            catalog.ConfigPages.Single(item=>item.Id=="101").WhereSql="leaf_name <> '排除' AND store_id=@current_store_id";
            var consumerPage=catalog.ConfigPages.SingleOrDefault(item=>item.Id=="901");
            if(consumerPage is null)catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="consumer",WhereSql="store_id=@current_store_id"});
            else consumerPage.WhereSql+=" AND store_id=@current_store_id";
        }
        var candidate=selectedScope
            ?NonRedundantReads.CompileScopedMultiLabel(catalog,"source","101","leaf_name",["leaf_name"],[]).MySql
            :MySqlNonRedundantReadCompiler.CompileMultiLabel(catalog,"source","leaf_name");
        var sql=candidate.Expression("leaf_name");
        Assert.Contains("WITH RECURSIVE",sql);
        Assert.Contains("depth<64",sql);
        Assert.Contains("NOT JSON_CONTAINS",sql);
        Assert.Contains("parent_key IS NULL",sql);
        Assert.Contains("delete_no",sql);
        Assert.Equal(type!="string",sql.Contains(".`"+treeLabel.ColumnName+"` AS CHAR)",StringComparison.Ordinal));
        var binding=new RelationCacheDefinition("consumer","label_consumer_fk","source","leaf_label","label_consumer_cache",true);
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]);
        var read=maintenance.Plans["consumer"].Read!;
        if(account)
        {
            Assert.True(candidate.RequiresAccount);
            Assert.Contains("b.`record_owner_id`",read.MultiProjections["picked_leaf_names"].NameExpression!);
            Assert.Equal(selectedScope?new[]{"record_owner_id","store_id"}:new[]{"record_owner_id"},
                read.MultiProjections["picked_leaf_names"].Owner!.Fields);
            Assert.False(read.RequiresAccount);
        }
        if(selectedScope)
        {
            Assert.True(candidate.RequiresAccount);
            Assert.Contains(".`store_id`=@",candidate.Scope);
            Assert.Contains("WITH RECURSIVE",candidate.Scope);
            Assert.Contains("store_id",read.SourceDependencies);
            Assert.Equal(account,read.SourceDependencies.Contains("record_owner_id",StringComparer.Ordinal));
            Assert.Contains("b.`store_id`",read.MultiProjections["picked_leaf_names"].NameExpression!);
            Assert.Contains("排除",read.Constants.Values);
            Assert.False(read.RequiresAccount);
        }
        var reverse=read.Reverse.Where(item=>item.TargetTable=="ds_tree").ToArray();
        Assert.Equal(selectedScope?2:1,reverse.Length);
        Assert.All(reverse,item=>Assert.Contains("parent_node_id",item.Outputs["picked_leaf_names"]));
        Assert.All(reverse,item=>Assert.Contains("@target_id",item.TargetPredicate));
    }


    /// <summary>来源行归属和全局节点范围独立编译，选中ID与逻辑名称使用同一已发布树。</summary>
    [Theory]
    [InlineData(true,true)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(false,false)]
    public void Global_self_tree_projects_leaf_in_fixed_page_scope(bool sourceProject,bool targetGlobal)
    {
        var catalog=DerivedMultipleCacheTests.Catalog("string","int64");
        var source=catalog.ConfigTables.Single(table=>table.Id=="source");
        source.IsRongyu=0;
        source.YzProjectId=catalog.YzProjectId;
        source.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(!sourceProject)catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==source.Id&&column.ColumnName=="yz_project_id");
        var tree=catalog.ConfigTables.Single(table=>table.Id=="tree");
        tree.IsRongyu=1;
        tree.YzProjectId=catalog.YzProjectId;
        if(targetGlobal)tree.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        var leaf=catalog.ConfigColumns.Single(column=>column.Id=="foreign");
        leaf.ColumnName="tree_id";
        catalog.ConfigColumns.RemoveAll(column=>column.Id is "choices" or "choice_names");
        catalog.ConfigColumns.Single(column=>column.Id=="tree_name").Extra.Clear();
        var page=new PageMetadata{Id="global_tree_source",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="18"};
        catalog.ConfigPages.Add(page);
        catalog.YzAdminTypes.Add(new(){Id="18",YzProjectId=catalog.YzProjectId});
        var targetPage=catalog.ConfigPages.Single(item=>item.Id=="201");
        targetPage.YzProjectId=catalog.YzProjectId;
        targetPage.YzAdminTypeId="18";
        targetPage.WhereSql=targetGlobal?"id > 8":"yz_project_id=@current_yz_project_id AND id > 8";
        catalog.ConfigPageColumns.Add(new(){Id="201parent",ConfigPageId=targetPage.Id,ConfigColumnId="parent"});
        if(!sourceProject&&!targetGlobal)
        {
            Assert.Throws<NotSupportedException>(()=>NonRedundantReads.Compile(catalog,source.Id,["id","tree_id"],["tree_name"],deferCascadeNames:true));
            return;
        }

        var regular=NonRedundantReads.Compile(catalog,source.Id,["id","tree_id"],["tree_name"],deferCascadeNames:true).MySql;
        Assert.True(regular.RequiresPublishedProjectIdentity);
        Assert.Equal(sourceProject,regular.Scope.Contains("b.`yz_project_id`",StringComparison.Ordinal));
        var name=regular.Expression("tree_name");
        Assert.Contains("WITH RECURSIVE",name);
        Assert.Equal(!targetGlobal,name.Contains(".`yz_project_id`=",StringComparison.Ordinal));
        Assert.Contains("delete_no",name);
        Assert.Contains(regular.Constants.Values,value=>Equals(value,8L));
        Assert.Contains(targetPage.Id,regular.CascadeLabelPages);
        if(sourceProject)
        {
            var candidate=NonRedundantReads.CompileGlobalPage(catalog,source.Id,page.Id,["id","tree_id"],["tree_name"]).MySql;
            Assert.DoesNotContain("b.`yz_project_id`",candidate.Scope);
            Assert.Equal(name,candidate.Expression("tree_name"));
        }
    }

    /// <summary>自树叶名筛选的每个祖先都使用同页逻辑条件，而不是稀疏物理值。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    [InlineData(true,true,true)]
    public void Scoped_self_tree_leaf_name_reuses_published_cross_filter(bool global,bool cache=false,bool globalSource=false)
    {
        var catalog=NonRedundantCrossTableReadTests.Catalog("mt_scoped_self","string","int64");
        var target=catalog.ConfigTables.Single(table=>table.Id=="1");
        if(global)target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
        foreach(var page in catalog.ConfigPages)
        {
            page.YzAdminTypeId="5";
            foreach(var field in catalog.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId&&
                (column.ColumnName=="title"||column.LinkConfigTableId.Length>0)))
                catalog.ConfigPageColumns.Add(new(){Id=page.Id+field.Id,ConfigPageId=page.Id,ConfigColumnId=field.Id});
        }
        catalog.ConfigColumns.AddRange([
            new(){Id="node_parent",ConfigTableId="1",ColumnName="node_parent_id",LogicalType="int64"},
            new(){Id="node_store",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"},
            new(){Id="consumer_id",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="consumer_project",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="consumer_tree",ConfigTableId="9",ColumnName="node_id",LogicalType="string",ControlKind="CascadeTree",LinkConfigTableId="1",
                ControlDetail="{\"sourceMode\":\"selfTree\",\"selfTree\":{\"table\":\"mt_scoped_self_source\",\"parentField\":\"node_parent_id\"}}"},
            new(){Id="consumer_leaf",ConfigTableId="9",ColumnName="node_name",LogicalType="string"}
        ]);
        catalog.ConfigTables.Add(new(){Id="9",YzProjectId=catalog.YzProjectId,TableName="mt_scoped_self_consumer",IsRongyu=0});
        var pageId="11";
        catalog.ConfigPages.Add(new(){Id=pageId,ConfigTableId="1",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",
            WhereSql=(global?"":"yz_project_id=@current_yz_project_id AND ")+"country_name <> '隐藏树节点' AND store_id=@current_store_id"});
        catalog.ConfigPageColumns.AddRange([
            new(){Id="11name",ConfigPageId=pageId,ConfigColumnId="12"},
            new(){Id="11parent",ConfigPageId=pageId,ConfigColumnId="node_parent"}
        ]);
        if(cache)
        {
            var source=catalog.ConfigTables.Single(table=>table.Id=="9");
            source.IsRongyu=1;
            if(globalSource)
            {
                source.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                catalog.ConfigColumns.RemoveAll(field=>field.Id=="consumer_project");
            }
            catalog.ConfigColumns.Add(new(){Id="consumer_store",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"});
            var sourcePage=new PageMetadata{Id="91",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5"};
            catalog.ConfigPages.Add(sourcePage);
            Assert.Throws<InvalidOperationException>(()=>SelfTreeCache.Compile(catalog,source.Id,"consumer_tree",pageId));
            sourcePage.WhereSql=(globalSource?"":"yz_project_id=@current_yz_project_id AND ")+"store_id=@current_store_id";
            var binding=SelfTreeCache.Compile(catalog,source.Id,"consumer_tree",pageId);
            Assert.Throws<InvalidOperationException>(()=>RelationMutationPlan.Compile(catalog,[binding]));
            var compiled=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]);
            var planForCache=compiled.Plans[source.Id].Read!;
            Assert.False(planForCache.RequiresAccount);
            Assert.Contains("b.`store_id`",planForCache.Expression("node_name"));
            Assert.Contains("WHEN BINARY b.`node_id`=BINARY '' THEN ''",planForCache.Expression("node_name"));
            Assert.Contains("store_id",planForCache.SourceDependencies);
            Assert.Contains("node_name",compiled.MixedSourceColumns["mt_scoped_self_consumer"]);
            var reverse=Assert.Single(planForCache.Reverse,path=>path.TargetTable=="mt_scoped_self_source"&&
                path.Outputs["node_name"].Contains("node_parent_id"));
            Assert.DoesNotContain("`store_id`",reverse.TargetPredicate);
            Assert.DoesNotContain("`delete_no`",reverse.TargetPredicate);
            Assert.Equal(!global,reverse.TargetPredicate.Contains("`yz_project_id`",StringComparison.Ordinal));
            Assert.Contains(planForCache.Reverse,path=>path.TargetTable=="mt_scoped_self_country"&&
                path.Outputs["node_name"].Contains("title")&&path.TargetPredicate.Contains(".node_id"));
            return;
        }
        var actor=new RuntimeActor(1,"树范围",false,catalog.YzProjectId,"5"){StoreId=9007199254741991L};
        var plan=NonRedundantReads.Compile(catalog,"9",["id","node_id"],["node_name"],deferCascadeNames:true).BindAccount(actor).MySql;
        var sql=plan.Expression("node_name");
        Assert.Contains("WITH RECURSIVE",sql);
        Assert.Contains("LEFT JOIN `mt_scoped_self_country`",sql);
        Assert.Contains("`store_id`",sql);
        Assert.Contains("ancestor.`id`",sql);
        Assert.Contains("leaf.`id`",sql);
        Assert.Contains("隐藏树节点",plan.Constants.Values);
        Assert.Contains(actor.StoreId!.Value,plan.Constants.Values);
        Assert.Equal(global,plan.RequiresPublishedProjectIdentity);
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
        var catalog=CrossCatalog(kind);
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

    /// <summary>账号读取从可信身份绑定，回填仅依赖来源行归属；任何未证明归属的页面都会阻止缓存发布。</summary>
    [Theory]
    [InlineData("MultiSelectPage")]
    [InlineData("MultiCheckBoxDatabase")]
    public void Actor_read_and_row_owned_refresh_have_separate_bindings(string kind)
    {
        var catalog=CrossCatalog(kind);
        AddAccountScope(catalog,"9","21");
        var candidate=MySqlNonRedundantReadCompiler.Compile(catalog,"9",["selected_country_names"]);
        Assert.True(candidate.RequiresAccount);
        Assert.Throws<InvalidOperationException>(()=>candidate.Constants);
        Assert.Contains(9007199254740993L,candidate.BindAccount(new RuntimeActor(9007199254740993L,"甲",true,catalog.YzProjectId,"1")).Constants.Values);
        Assert.Contains(9007199254740994L,candidate.BindAccount(new RuntimeActor(9007199254740994L,"乙",false,catalog.YzProjectId,"1")).Constants.Values);
        Assert.True(candidate.RequiresAccount);

        catalog.ConfigTables.Single(table=>table.Id=="9").IsRongyu=1;
        var binding=new RelationCacheDefinition("9","93","1","19","94",true);
        var refresh=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]).Plans["9"].Read!;
        Assert.False(refresh.RequiresAccount);
        var projection=refresh.MultiProjections["selected_country_names"];
        Assert.Equal(new[]{"record_owner_id"},projection.Owner!.Fields);
        Assert.Contains("b.`record_owner_id`",projection.NameExpression!);
        Assert.Contains(refresh.PhysicalReferences,item=>item.Table=="b185_consumer"&&item.Column.Name=="record_owner_id");
        Assert.Contains(refresh.Reverse,item=>item.TargetTable=="b185_country"&&item.Outputs["selected_country_names"].Contains("node_owner_id"));
        var published=MySqlNonRedundantReadCompiler.PublishedCascadeLabelPages(catalog,out var owners);
        Assert.Contains("21",published);Assert.Contains("901",owners);

        catalog.ConfigPages.Add(new(){Id="902",ConfigTableId="9"});
        Assert.Throws<InvalidOperationException>(()=>DerivedCrossTableCaches.Validate(catalog,[binding],[]));
    }

    /// <summary>沿现有测试目录建立来源与节点两侧的独立账号物理列。</summary>
    internal static void AddAccountScope(MetadataCatalog catalog,string sourceId,string nodePageId)
    {
        var nodePage=catalog.ConfigPages.Single(page=>page.Id==nodePageId);
        catalog.ConfigColumns.Add(new(){Id="node_owner",ConfigTableId=nodePage.ConfigTableId,ColumnName="node_owner_id",LogicalType="int64"});
        catalog.ConfigColumns.Add(new(){Id="record_owner",ConfigTableId=sourceId,ColumnName="record_owner_id",LogicalType="string",ColumnLength="32"});
        nodePage.WhereSql=string.Join(" AND ",new[]{nodePage.WhereSql,"node_owner_id=@current_yz_manager_id"}.Where(term=>term.Length>0));
        catalog.ConfigPages.Add(new(){Id="901",ConfigTableId=sourceId,WhereSql="record_owner_id=@current_yz_manager_id"});
    }

    /// <summary>普通来源与跨表级联显示目标共用的最小元数据。</summary>
    internal static MetadataCatalog CrossCatalog(string kind)
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

        return catalog;
    }
}
