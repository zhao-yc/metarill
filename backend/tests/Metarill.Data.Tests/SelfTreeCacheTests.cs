using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>自树固定_name叶子缓存支持物理自定义名称；不能借控制类型替任意副本取得传播许可。</summary>
public sealed class SelfTreeCacheTests
{
    /// <summary>四种外键组合及普通R0／R1物理目标、同表书签共享同一稳定字段证明。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public void Physical_leaf_cache_uses_declared_name_and_does_not_mutate_metadata(string type,string parent)
    {
        foreach(var mode in new[]{"r1","r0","same"})
        {
            var c=Catalog(type,parent);if(mode=="r0")c.ConfigTables.Single(t=>t.Id=="target").IsRongyu=0;
            if(mode=="same"){var foreign=c.ConfigColumns.Single(x=>x.Id=="foreign");foreign.LinkConfigTableId="source";foreign.ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table="cache_source",parentField="parent_node_id",nameField="name"}});c.ConfigColumns.Add(new(){Id="source_parent",ConfigTableId="source",ColumnName="parent_node_id",LogicalType=parent,LinkConfigTableId="source"});}
            var saved=JsonSerializer.Serialize(c);var definition=SelfTreeCache.Compile(c,"source","foreign");Assert.False(definition.Multiple);Assert.Null(definition.CrossTableColumnId);var plan=RelationMutationPlan.Compile(c,[definition]);Assert.Equal("cache",definition.CacheColumnId);Assert.Equal(mode=="same"?"source_name":"value",definition.ValueColumnId);Assert.Equal(mode=="same"?"source":"target",definition.TargetTableId);Assert.NotEmpty(plan.Fingerprint);Assert.Equal(saved,JsonSerializer.Serialize(c));
        }
    }
    /// <summary>坏定义、范围、非物理拓扑及凭据不会因为后缀匹配而成为可写缓存。</summary>
    [Theory][InlineData("mode")][InlineData("where")][InlineData("globalSource")][InlineData("globalTarget")][InlineData("sourceR0")][InlineData("virtualForeign")][InlineData("joinValue")][InlineData("virtualValue")][InlineData("wrongLink")][InlineData("badParent")][InlineData("credential")][InlineData("cacheLink")][InlineData("textType")]
    public void Unproven_leaf_definition_never_compiles(string kind)
    {
        var c=Catalog("string","int64");var foreign=c.ConfigColumns.Single(x=>x.Id=="foreign");var value=c.ConfigColumns.Single(x=>x.Id=="value");
        if(kind=="mode")foreign.ControlDetail="{\"sourceMode\":\"crossTable\"}";if(kind=="where")foreign.ControlDetail=foreign.ControlDetail.Replace("\"selfTree\":{","\"whereSql\":\"1=1\",\"selfTree\":{");if(kind=="globalSource")c.ConfigTables[0].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="globalTarget")c.ConfigTables[1].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="sourceR0")c.ConfigTables[0].IsRongyu=0;if(kind=="virtualForeign")foreign.IsVirtualColumn=1;if(kind=="joinValue")value.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="virtualValue")value.IsVirtualColumn=1;if(kind=="wrongLink")foreign.LinkConfigTableId="source";if(kind=="badParent")c.ConfigColumns.Single(x=>x.Id=="parent").LinkConfigTableId="source";if(kind=="credential"){value.ColumnName="password";foreign.ControlDetail=foreign.ControlDetail.Replace("caption","password");}if(kind=="cacheLink")c.ConfigColumns.Single(x=>x.Id=="cache").LinkConfigTableId="target";if(kind=="textType")foreign.ControlDetail=foreign.ControlDetail.Replace("\"caption\"","123");Assert.ThrowsAny<Exception>(()=>SelfTreeCache.Compile(c,"source","foreign"));
    }
    /// <summary>修改目标值或借另一个缓存列不能绕过控制定义，即使普通后缀检查恰好一致。</summary>
    [Fact]public void Forged_definition_cannot_borrow_another_name_or_cache()
    {
        var c=Catalog("int64","string");var d=SelfTreeCache.Compile(c,"source","foreign");c.ConfigColumns.Add(new(){Id="alias",ConfigTableId="target",ColumnName="alias",LogicalType="string"});c.ConfigColumns.Add(new(){Id="other_cache",ConfigTableId="source",ColumnName="tree_alias",LogicalType="string"});Assert.Throws<NotSupportedException>(()=>RelationMutationPlan.Compile(c,[d with{ValueColumnId="alias",CacheColumnId="other_cache"}]));
    }
    /// <summary>旧受管名称只有join标记时由固定selfTree补足；显式错路径仍拒绝。</summary>
    [Fact]public void Legacy_managed_marker_needs_no_invented_join_path()
    {
        var c=Catalog("string","int64");var cache=c.ConfigColumns.Single(x=>x.Id=="cache");cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);var d=SelfTreeCache.Compile(c,"source","foreign");Assert.NotEmpty(RelationMutationPlan.Compile(c,[d]).Fingerprint);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("tree.unrelated");Assert.Throws<NotSupportedException>(()=>SelfTreeCache.Compile(c,"source","foreign"));
    }
    /// <summary>执行计划只保留冻结字段与类型，配置重排或随后编辑不能在线改写计划。</summary>
    [Fact]public void Published_plan_remains_frozen()
    {
        var c=Catalog("string","string");var d=SelfTreeCache.Compile(c,"source","foreign");var plan=RelationMutationPlan.Compile(c,[d]);var sha=plan.Fingerprint;c.ConfigColumns.Reverse();Assert.Equal(sha,RelationMutationPlan.Compile(c,[d]).Fingerprint);c.ConfigTables[1].TableName="edited_node";c.ConfigColumns.Single(x=>x.Id=="value").ColumnName="edited_name";Assert.Equal("target",d.TargetTableId);Assert.Equal("value",d.ValueColumnId);Assert.Equal(sha,plan.Fingerprint);
    }
    /// <summary>实际传播与回填使用同一固定全局条件，消费者仍按项目读取和写入。</summary>
    [Theory]
    [InlineData("string")]
    [InlineData("int32")]
    [InlineData("int64")]
    [InlineData("bool")]
    [InlineData("decimal")]
    [InlineData("double")]
    [InlineData("date")]
    [InlineData("datetime")]
    [InlineData("time")]
    [InlineData("bool",false)]
    [InlineData("decimal",false)]
    [InlineData("double",false)]
    [InlineData("date",false)]
    [InlineData("datetime",false)]
    [InlineData("time",false)]
    public void Global_leaf_uses_the_same_target_condition_for_rename_and_backfill(string type,bool global=true)
    {
        var catalog=Catalog("string","int64");
        catalog.ConfigColumns.Single(column=>column.Id=="value").LogicalType=type;
        foreach(var table in catalog.ConfigTables)table.YzProjectId=catalog.YzProjectId;
        if(global)catalog.ConfigTables[1].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.Add(new(){Id="status",ConfigTableId="target",ColumnName="status",LogicalType="int32"});
        catalog.YzAdminTypes.Add(new(){Id="admin",YzProjectId=catalog.YzProjectId});
        catalog.ConfigPages.Add(new(){Id="tree_page",ConfigTableId="target",YzAdminTypeId="admin",YzProjectId=catalog.YzProjectId,WhereSql="status=1"});
        var definition=SelfTreeCache.Compile(catalog,"source","foreign",global?"tree_page":null);
        var maintenance=RelationMaintenanceCatalog.Compile(catalog,[definition]);
        var binding=Assert.Single(maintenance.Propagation.Bindings);
        Assert.Equal("int64",binding.Source.ProjectType);
        Assert.Equal(global?"":"int64",binding.Target.ProjectType);
        if(global)Assert.Equal("status",Assert.Single(binding.PublishedCondition!.Fields));
        var read=Assert.IsType<Metarill.Data.Queries.MySqlNonRedundantReadPlan>(maintenance.Plans["source"].Read);
        Assert.Equal(type!="string",binding.TextScalar);
        Assert.Equal("string",read.Types["tree_name"]);
        if(type=="bool")Assert.Contains("THEN 'true' ELSE 'false'",read.Expression("tree_name"));
        else if(type!="string")Assert.Contains(" AS CHAR)",read.Expression("tree_name"));
        Assert.Contains("b.`yz_project_id`",read.Scope);
        Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(catalog,[definition with{SelfTreePageId="missing"}]));
        if(global)
        {
            Assert.Contains("r0.`status`",read.From);
            Assert.DoesNotContain("r0.`yz_project_id`",read.From);
            Assert.Contains(1,read.Constants.Values);
            catalog.ConfigPages[0].WhereSql="yz_project_id=@current_yz_project_id";
            Assert.Throws<NotSupportedException>(()=>RelationMutationPlan.Compile(catalog,[definition]));
        }
        else Assert.Contains("r0.`yz_project_id`",read.From);
    }
    /// <summary>整链刷新和目标反查使用同一来源范围，目标项目列不改写全局节点资格。</summary>
    [Theory]
    [InlineData("string",null)]
    [InlineData("int64",null)]
    [InlineData("int64","string")]
    [InlineData("string","int64")]
    [InlineData("string","int64",true)]
    [InlineData("int64","string",true,true)]
    public void Global_shared_source_uses_complete_published_tree(string type,string? projectType,bool projectTarget=false,bool account=false)
    {
        var catalog=Catalog(type,"int64");
        var source=catalog.ConfigTables[0];
        var target=catalog.ConfigTables[1];
        foreach(var table in catalog.ConfigTables)
        {
            table.YzProjectId=catalog.YzProjectId;
            table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        }
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
        catalog.ConfigPages.AddRange([
            new(){Id="5",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5"},
            new(){Id="6",ConfigTableId=target.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",WhereSql="id>8"}
        ]);
        if(projectTarget)target.Extra.Remove("scopeKind");
        if(account)
        {
            catalog.ConfigColumns.AddRange([
                new(){Id="source_store",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"},
                new(){Id="target_store",ConfigTableId=target.Id,ColumnName="store_id",LogicalType="int64"}
            ]);
            catalog.ConfigPages[0].WhereSql="store_id=@current_store_id";
            catalog.ConfigPages[1].WhereSql+=" AND store_id=@current_store_id";
        }
        catalog.ConfigPageColumns.AddRange([
            new(){Id="61",ConfigPageId="6",ConfigColumnId="value"},
            new(){Id="62",ConfigPageId="6",ConfigColumnId="parent"}
        ]);
        if(projectType is null)catalog.ConfigColumns.RemoveAll(field=>field.Id=="source_project");
        else catalog.ConfigColumns.Single(field=>field.Id=="source_project").LogicalType=projectType;
        var binding=SelfTreeCache.Compile(catalog,source.Id,"foreign","6");
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]);
        var sourcePlan=maintenance.Plans[source.Id];
        var plan=sourcePlan.Read!;
        Assert.True(plan.RequiresPublishedProjectIdentity);
        Assert.Equal(projectType??"",sourcePlan.ProjectType);
        // 夹具未声明逻辑删除，来源范围只使用物理项目列。
        var expectedScope=projectType switch
        {
            "string"=>"b.`yz_project_id`=@project_scope_text",
            "int64"=>"b.`yz_project_id`=@project_scope_number",
            _=>"1=1"
        };
        Assert.Equal(expectedScope,plan.Scope);
        Assert.Equal(projectType is not null,plan.PhysicalReferences.Any(reference=>reference.Table=="cache_source"&&reference.Column.Name=="yz_project_id"));
        if(projectType is not null)
        {
            Assert.Throws<NotSupportedException>(()=>GlobalCachedRelationSource.Require(catalog,source));
            catalog.ConfigColumns.Single(field=>field.Id=="source_project").IsVirtualColumn=1;
            Assert.Throws<NotSupportedException>(()=>SelfTreeCache.Compile(catalog,source.Id,"foreign","6"));
            catalog.ConfigColumns.Single(field=>field.Id=="source_project").IsVirtualColumn=0;
        }
        Assert.Contains("WITH RECURSIVE",plan.Expression("tree_name"));
        Assert.Contains(8L,plan.Constants.Values);
        Assert.Contains("cache_node",maintenance.MixedTargets);
        var reverse=Assert.Single(plan.Reverse,path=>path.TargetTable=="cache_node");
        Assert.Contains("parent_node_id",reverse.Outputs["tree_name"]);
        if(projectTarget)
        {
            Assert.Contains("yz_project_id",reverse.TargetPredicate);
            Assert.Contains("@project_scope_number",plan.Expression("tree_name"));
        }
        else Assert.DoesNotContain("yz_project_id",reverse.TargetPredicate);
        if(account)
        {
            Assert.Contains("b.`store_id`",plan.Expression("tree_name"));
            Assert.Contains("store_id",reverse.Outputs["tree_name"]);
            catalog.ConfigPages[0].WhereSql="";
            var rejected=Assert.Throws<InvalidOperationException>(()=>SelfTreeCache.Compile(catalog,source.Id,"foreign","6"));
            Assert.Contains("物理账号归属",rejected.Message);
        }
        target.Extra.Remove("scopeKind");
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="source_project");
        Assert.Throws<NotSupportedException>(()=>SelfTreeCache.Compile(catalog,source.Id,"foreign","6"));
    }

    /// <summary>带行项目列的来源同时维护父名和叶名；父页固定条件影响反查，来源更新仍按项目隔离。</summary>
    [Theory]
    [InlineData("string", "int64")]
    [InlineData("int64", "string")]
    [InlineData("string", "int64", true)]
    [InlineData("int64", "string", true)]
    public void Published_global_parent_and_project_source_share_the_maintenance_scope(string parentType,string projectType,bool projectParent=false)
    {
        var catalog=Catalog(parentType,"int64");
        var source=catalog.ConfigTables[0];
        var tree=catalog.ConfigTables[1];
        var family=new TableMetadata{Id="family",TableName="cache_family",IsRongyu=1};
        catalog.ConfigTables.Add(family);
        foreach(var table in catalog.ConfigTables)
        {
            table.YzProjectId=catalog.YzProjectId;
            table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        }
        catalog.ConfigColumns.Single(field=>field.Id=="source_project").LogicalType=projectType;
        var key=new ColumnMetadata{Id="family_key",ConfigTableId=source.Id,ColumnName="family_id",LogicalType=parentType,
            ControlKind="List",LinkConfigTableId=family.Id,ControlDetail=JsonSerializer.Serialize(new{tableId=family.Id,pageId="family_page"})};
        var cache=new ColumnMetadata{Id="family_cache",ConfigTableId=source.Id,ColumnName="family_name",LogicalType="string",
            Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("family.name")}};
        catalog.ConfigColumns.AddRange([key,cache,
            new(){Id="family_id",ConfigTableId=family.Id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="family_name",ConfigTableId=family.Id,ColumnName="name",LogicalType="string"},
            new(){Id="family_status",ConfigTableId=family.Id,ColumnName="status",LogicalType="int32"}
        ]);
        if(projectParent)
        {
            family.Extra.Remove("scopeKind");
            catalog.ConfigColumns.Add(new(){Id="family_project",ConfigTableId=family.Id,ColumnName="yz_project_id",LogicalType=parentType});
        }
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});
        var sourcePage=new PageMetadata{Id="source_page",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",WhereSql="family_id=@parentId"};
        catalog.ConfigPages.AddRange([sourcePage,
            new(){Id="tree_page",ConfigTableId=tree.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",WhereSql="id>8"},
            new(){Id="family_page",ConfigTableId=family.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",WhereSql=projectParent?"yz_project_id=@current_yz_project_id AND status=1":"status=1"}
        ]);
        catalog.ConfigPageColumns.AddRange([
            new(){Id="61",ConfigPageId="tree_page",ConfigColumnId="value"},
            new(){Id="62",ConfigPageId="tree_page",ConfigColumnId="parent"}
        ]);

        Assert.Equal(sourcePage,Assert.Single(GlobalCachedRelationSource.RowProjectSelfTreeParentPages(catalog,source,key)));
        var definition=SelfTreeCache.Compile(catalog,source.Id,"foreign","tree_page");
        var maintenance=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[definition]);
        var plan=maintenance.Plans[source.Id].Read!;
        Assert.Contains(projectType=="string"?"@project_scope_text":"@project_scope_number",plan.Scope);
        Assert.Contains("b.`yz_project_id`",plan.Scope);
        Assert.Contains("r0.`status`",plan.From);
        var parent=Assert.Single(plan.Reverse,reverse=>reverse.TargetTable==family.TableName);
        Assert.Contains("status",parent.Outputs["family_name"]);
        Assert.Contains("name",parent.Outputs["family_name"]);
        if(projectParent)
        {
            Assert.Contains("r0.`yz_project_id`="+(parentType=="string"?"@project_scope_text":"@project_scope_number"),plan.From);
            Assert.Contains("yz_project_id",parent.Outputs["family_name"]);
            var familyProject=catalog.ConfigColumns.Single(column=>column.Id=="family_project");
            familyProject.IsVirtualColumn=1;
            Assert.Throws<NotSupportedException>(()=>GlobalCachedRelationSource.RequireCache(catalog,source,cache));
            familyProject.IsVirtualColumn=0;
        }
        Assert.Contains("tree_name",maintenance.MixedSourceColumns[source.TableName]);
        Assert.Contains("family_name",maintenance.MixedSourceColumns[source.TableName]);

        sourcePage.WhereSql="family_id=@parent_id AND tree_id=@parent_id";
        Assert.Throws<NotSupportedException>(()=>GlobalCachedRelationSource.RequireCache(catalog,source,cache));
        sourcePage.WhereSql="";
        Assert.Throws<NotSupportedException>(()=>GlobalCachedRelationSource.RequireCache(catalog,source,cache));
    }

    /// <summary>纯目录模型不需要数据库，固定实际父键和自定义物理名称。</summary>
    private static MetadataCatalog Catalog(string type,string parent)
    {
        var c=new MetadataCatalog{YzProjectId="1"};c.ConfigTables.AddRange([new(){Id="source",TableName="cache_source",IsRongyu=1},new(){Id="target",TableName="cache_node",IsRongyu=1}]);
        c.ConfigColumns.AddRange([new(){Id="source_id",ConfigTableId="source",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="source_name",ConfigTableId="source",ColumnName="name",LogicalType="string"},new(){Id="source_project",ConfigTableId="source",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="foreign",ConfigTableId="source",ColumnName="tree_id",LogicalType=type,ControlKind="CascadeTree",LinkConfigTableId="target",ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table="cache_node",parentField="parent_node_id",nameField="caption"}})},new(){Id="cache",ConfigTableId="source",ColumnName="tree_name",LogicalType="string"},new(){Id="target_id",ConfigTableId="target",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="value",ConfigTableId="target",ColumnName="caption",LogicalType="string"},new(){Id="target_project",ConfigTableId="target",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="parent",ConfigTableId="target",ColumnName="parent_node_id",LogicalType=parent,LinkConfigTableId="target"}]);return c;
    }
}
