using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>明确全局R0名称须沿已发布关系实时读取；不能依赖不存在或已过期的物理名称副本。</summary>
public sealed class GlobalDerivedTargetTests
{
    /// <summary>沿原三表模型只改变中间表存储模式，真实计划必须联查当前终点并保留普通来源项目范围。</summary>
    [Theory]
    [InlineData("int64","int64")][InlineData("string","int64")]
    [InlineData("int64","string")][InlineData("string","string")]
    public void Published_global_r0_name_reads_current_terminal(string sourceKey,string innerKey)
    {
        var c=Catalog(0,sourceKey,innerKey);var before=JsonSerializer.Serialize(c);
        var source=c.ConfigTables.Single(t=>t.Id=="1");var name=c.ConfigColumns.Single(f=>f.Id=="14");
        Assert.False(MetadataStoragePolicy.ForTable(c,source).IsPhysical(name));
        var plan=MySqlNonRedundantReadCompiler.Compile(c,"3",["id","global_source_global_target_name"]);
        var from=(string)Read(plan,"From");var expressions=(IReadOnlyDictionary<string,string>)Read(plan,"Expressions");
        Assert.Contains("JOIN `global_source`",from);Assert.Contains("JOIN `global_target`",from);
        Assert.DoesNotContain(".`global_target_name`",expressions["global_source_global_target_name"]);
        Assert.EndsWith(".`name`",expressions["global_source_global_target_name"]);
        Assert.Contains("b.`yz_project_id`=@project_scope_number",(string)Read(plan,"Scope"));
        if(sourceKey=="string")Assert.Contains("BINARY CAST(r0.`id` AS CHAR)=BINARY b.`global_source_id`",from);
        Assert.Equal("string",NonRedundantReads.Compile(c,"3",["global_source_global_target_name"]).OutputTypes["global_source_global_target_name"]);
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }

    /// <summary>R1普通消费者沿现有混合传播和原维护编译链登记真实上游，不把R0逻辑名塞入物理传播计划。</summary>
    [Theory]
    [InlineData("int64","int64")][InlineData("string","int64")]
    [InlineData("int64","string")][InlineData("string","string")]
    public void Published_global_r0_name_enters_mixed_propagation_and_maintenance(string sourceKey,string innerKey)
    {
        var c=Catalog(1,sourceKey,innerKey);var before=JsonSerializer.Serialize(c);
        var plan=RelationMaintenanceCatalog.Compile(c);
        Assert.Contains("global_source",plan.MixedTargets);Assert.Contains("global_target",plan.MixedTargets);
        Assert.Equal(new[]{"global_source_global_target_name"},plan.MixedSourceColumns["cached_consumer"]);
        Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[new("3","34","1","14","35",false)]));
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }

    /// <summary>无行项目的全局R0事件必须先匹配已发布目录身份，错误身份在连接/事务之前拒绝。</summary>
    [Fact]
    public async Task Global_r0_events_require_published_project_identity_before_database()
    {
        var plan=RelationMaintenanceCatalog.Compile(Catalog(1,"int64","int64"));
        using var database=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused"});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new ProjectRelationMutationPropagator(database).AfterMutationAsync(plan,"global_target",1,"999",new HashSet<string>{"name"},false,false));
    }

    /// <summary>R0本身也可有物理名称，不能为通过新证明额外联查并未用于显示的字典。</summary>
    [Theory][InlineData("int64")][InlineData("string")]
    public void Published_global_r0_physical_name_keeps_direct_read(string key)
    {
        var c=Catalog(0,key,"int64");var label=c.ConfigColumns.Single(f=>f.Id=="14");label.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(0);label.Extra.Remove("columnFullName");label.ColumnName="label";
        var foreign=c.ConfigColumns.Single(f=>f.Id=="34");foreign.ControlDetail=foreign.ControlDetail.Replace("global_target_name","label",StringComparison.Ordinal);var output=c.ConfigColumns.Single(f=>f.Id=="35");output.ColumnName="global_source_label";output.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_source.label");
        var plan=MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_label"]);
        Assert.Contains("JOIN `global_source`",(string)Read(plan,"From"));Assert.DoesNotContain("JOIN `global_target`",(string)Read(plan,"From"));
        Assert.EndsWith(".`label`",((IReadOnlyDictionary<string,string>)Read(plan,"Expressions"))["global_source_label"]);
    }

    /// <summary>R0实时路径沿原单选两种控件读取，旧R1物理List缓存入口的限制保持。</summary>
    [Theory][InlineData("List")][InlineData("SelectPage")]
    public void Published_global_r0_uses_original_single_choice_control(string control)
    {
        var c=Catalog(0,"string","string");c.ConfigColumns.Single(f=>f.Id=="13").ControlKind=control;
        var plan=MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_global_target_name"]);
        Assert.Contains("BINARY CAST(r1.`id` AS CHAR)=BINARY r0.`global_target_id`",(string)Read(plan,"From"));
    }

    /// <summary>同表另一个带父上下文的页面不替换已明确指定的固定目标页。</summary>
    [Fact]
    public void Unused_scoped_page_does_not_replace_fixed_target_page()
    {
        var c=Catalog(0,"int64","int64");c.ConfigPages.Add(new(){Id="11",ConfigTableId="1",YzProjectId=c.YzProjectId,YzAdminTypeId="9",WhereSql="id=@parent_id"});
        var proof=GlobalSingleRelationTarget.Require(c,c.ConfigTables.Single(t=>t.Id=="3"),c.ConfigColumns.Single(f=>f.Id=="34"),c.ConfigTables.Single(t=>t.Id=="1"));Assert.Equal("10",proof.Page.Id);
        Assert.Contains("JOIN `global_target`",(string)Read(MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_global_target_name"]),"From"));
    }

    /// <summary>多跳关系仍逐步验证全局目标与物理键；输出必须读取最后一级当前值。</summary>
    [Fact]
    public void Multi_hop_global_r0_name_reads_the_terminal_instead_of_intermediate_label()
    {
        var c=Catalog(0,"int64","int64");c.ConfigTables.Add(new(){Id="4",YzProjectId=c.YzProjectId,TableName="global_owner",IsRongyu=1,Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
        c.ConfigColumns.AddRange([Field("41","4","id","int64",true),Field("42","4","name","string"),Field("43","4","delete_no","datetime"),new(){Id="29",ConfigTableId="2",YzProjectId=c.YzProjectId,ColumnName="owner_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="4",ControlDetail="{\"tableId\":\"4\",\"table\":\"global_owner\",\"pageId\":\"40\",\"nameField\":\"name\"}"}]);
        c.ConfigPages.Add(new(){Id="40",ConfigTableId="4",YzProjectId=c.YzProjectId,YzAdminTypeId="9"});c.ConfigColumns.Single(f=>f.Id=="14").Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.owner.name");
        var plan=MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_global_target_name"]);Assert.Contains("JOIN `global_owner`",(string)Read(plan,"From"));Assert.EndsWith("r2.`name`",((IReadOnlyDictionary<string,string>)Read(plan,"Expressions"))["global_source_global_target_name"]);
        ColumnMetadata Field(string id,string table,string name,string type,bool key=false)=>new(){Id=id,ConfigTableId=table,YzProjectId=c.YzProjectId,ColumnName=name,LogicalType=type,ColumnLength="100",IsDbNotNull=key?1:0};
    }

    /// <summary>两个消费者存储模式共用严格名称资格，非法目录/路径/类型及动态目标在编译阶段拒绝。</summary>
    [Theory]
    [InlineData("privateProducer")][InlineData("unpublishedProducer")][InlineData("wrongAdmin")][InlineData("selectedVariable")]
    [InlineData("sourceKey")][InlineData("sourceProjectType")][InlineData("sourceProjectMissing")][InlineData("nullableKey")]
    [InlineData("deletedType")][InlineData("virtualName")][InlineData("nameType")][InlineData("nameControl")][InlineData("missingPath")]
    [InlineData("multiForeign")][InlineData("jsonForeign")][InlineData("virtualForeign")][InlineData("joinedForeign")]
    [InlineData("credential")][InlineData("privateLeaf")][InlineData("leafPage")][InlineData("duplicate")][InlineData("wrongTarget")]
    [InlineData("cycle")][InlineData("innerVariable")][InlineData("credentialCondition")]
    public void Unproven_global_r0_name_never_becomes_published(string kind)
    {
        foreach(var mode in new[]{0,1})
        {
            var c=Catalog(mode,"int64","int64");var producer=c.ConfigTables.Single(t=>t.Id=="1");var foreign=c.ConfigColumns.Single(f=>f.Id=="13");var value=c.ConfigColumns.Single(f=>f.Id=="14");
            switch(kind)
            {
                case "privateProducer":producer.Extra.Remove("scopeKind");break;
                case "unpublishedProducer":producer.YzProjectId="999";break;
                case "wrongAdmin":c.ConfigPages.Single(p=>p.Id=="10").YzAdminTypeId="missing";break;
                case "selectedVariable":c.ConfigPages.Single(p=>p.Id=="10").WhereSql="id=@current_yz_project_id";break;
                case "sourceKey":c.ConfigColumns.Single(f=>f.Id=="34").LogicalType="json";break;
                case "sourceProjectType":c.ConfigColumns.Single(f=>f.Id=="32").LogicalType="decimal";break;
                case "sourceProjectMissing":c.ConfigColumns.Remove(c.ConfigColumns.Single(f=>f.Id=="32"));break;
                case "nullableKey":c.ConfigColumns.Single(f=>f.Id=="11").IsDbNotNull=0;break;
                case "deletedType":c.ConfigColumns.Single(f=>f.Id=="12").LogicalType="string";break;
                case "virtualName":value.IsVirtualColumn=1;break;
                case "nameType":value.LogicalType="int64";break;
                case "nameControl":value.ControlKind="FileLibrary";break;
                case "missingPath":value.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");break;
                case "multiForeign":foreign.ControlKind="MultiSelectPage";break;
                case "jsonForeign":foreign.LogicalType="json";break;
                case "virtualForeign":foreign.IsVirtualColumn=1;break;
                case "joinedForeign":foreign.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);foreign.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.id");break;
                case "credential":c.ConfigColumns.Single(f=>f.Id=="22").ColumnName="password";foreign.ControlDetail=foreign.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"password\"",StringComparison.Ordinal);value.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.password");break;
                case "privateLeaf":c.ConfigTables.Single(t=>t.Id=="2").Extra.Remove("scopeKind");break;
                case "leafPage":c.ConfigPages.Single(p=>p.Id=="20").YzProjectId="999";break;
                case "duplicate":foreign.ControlDetail=foreign.ControlDetail.Replace("{","{\"pageId\":\"20\",",StringComparison.Ordinal);break;
                case "wrongTarget":foreign.LinkConfigTableId="3";break;
                case "cycle":foreign.LinkConfigTableId="1";foreign.ControlDetail="{\"tableId\":\"1\",\"table\":\"global_source\",\"pageId\":\"10\",\"nameField\":\"global_target_name\"}";value.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.global_target_name");break;
                case "innerVariable":c.ConfigPages.Single(p=>p.Id=="20").WhereSql="id=@parent_id";break;
                case "credentialCondition":c.ConfigColumns.Add(new(){Id="99",ConfigTableId="2",YzProjectId=c.YzProjectId,ColumnName="password",LogicalType="string",ColumnLength="100"});c.ConfigPages.Single(p=>p.Id=="20").WhereSql="password='x'";break;
            }
            var before=JsonSerializer.Serialize(c);var owner=c.ConfigTables.Single(t=>t.Id=="3");var link=c.ConfigColumns.Single(f=>f.Id=="34");
            Assert.ThrowsAny<Exception>(()=>mode==0?GlobalSingleRelationTarget.Require(c,owner,link,producer):GlobalSingleRelationTarget.RequireCached(c,owner,link,producer));Assert.Equal(before,JsonSerializer.Serialize(c));
        }
    }

    /// <summary>R0与R1使用同一个16层活动字段栈，不能通过切换存储模式重置边界。</summary>
    [Theory][InlineData(14,true)][InlineData(15,true)][InlineData(16,false)]
    public void Global_r0_recursive_name_keeps_existing_depth_bound(int count,bool accepted)
    {
        var c=Catalog(0,"int64","int64");var targetId="1";var targetName="global_source";var page="10";var name="global_target_name";
        for(var index=0;index<count;index++)
        {
            var id=(1000+index).ToString(System.Globalization.CultureInfo.InvariantCulture);var table="global_r0_level_"+index;var pageId=(10000+index).ToString(System.Globalization.CultureInfo.InvariantCulture);var first=50000+index*4;
            c.ConfigTables.Add(new(){Id=id,YzProjectId=c.YzProjectId,TableName=table,IsRongyu=0,Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
            c.ConfigColumns.AddRange([new(){Id=first.ToString(),ConfigTableId=id,YzProjectId=c.YzProjectId,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id=(first+1).ToString(),ConfigTableId=id,YzProjectId=c.YzProjectId,ColumnName="delete_no",LogicalType="datetime"},new(){Id=(first+2).ToString(),ConfigTableId=id,YzProjectId=c.YzProjectId,ColumnName="next_id",LogicalType="int64",ControlKind="List",LinkConfigTableId=targetId,ControlDetail=JsonSerializer.Serialize(new{tableId=targetId,table=targetName,pageId=page,nameField=name})},new(){Id=(first+3).ToString(),ConfigTableId=id,YzProjectId=c.YzProjectId,ColumnName="next_label",LogicalType="string",ColumnLength="100",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("next."+name)}}]);
            c.ConfigPages.Add(new(){Id=pageId,ConfigTableId=id,YzProjectId=c.YzProjectId,YzAdminTypeId="9"});targetId=id;targetName=table;page=pageId;name="next_label";
        }
        var foreign=c.ConfigColumns.Single(f=>f.Id=="34");foreign.LinkConfigTableId=targetId;foreign.ControlDetail=JsonSerializer.Serialize(new{tableId=targetId,table=targetName,pageId=page,nameField=name});var owner=c.ConfigTables.Single(t=>t.Id=="3");var target=c.ConfigTables.Single(t=>t.Id==targetId);var before=JsonSerializer.Serialize(c);
        if(accepted)Assert.Equal(name,GlobalSingleRelationTarget.Require(c,owner,foreign,target).Name.ColumnName);else Assert.Throws<NotSupportedException>(()=>GlobalSingleRelationTarget.Require(c,owner,foreign,target));Assert.Equal(before,JsonSerializer.Serialize(c));
    }

    /// <summary>两种项目列都保持根记录隔离，候选和派生名称只按固定发布页读取。</summary>
    [Theory]
    [InlineData("int64","number")]
    [InlineData("string","text")]
    public void Global_r0_target_role_does_not_remove_its_own_source_project_scope(string type,string scopeKind)
    {
        var catalog=Catalog(0,"int64","int64");
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId=catalog.YzProjectId,
            ColumnName="yz_project_id",LogicalType=type,ColumnLength=type=="string"?"20":""});
        catalog.ConfigColumns.Add(new(){Id="19",ConfigTableId="1",YzProjectId=catalog.YzProjectId,ColumnName="status",LogicalType="int32"});
        catalog.ConfigPages.Single(page=>page.Id=="10").WhereSql="status=1";
        var consumer=MySqlNonRedundantReadCompiler.Compile(catalog,"3",["global_source_global_target_name"]);
        var own=MySqlNonRedundantReadCompiler.Compile(catalog,"1",["global_target_name"]);
        var candidate=MySqlNonRedundantReadCompiler.CompileGlobalCandidate(catalog,"1","10",["global_target_name"]);

        Assert.DoesNotContain("r0.`yz_project_id`",(string)Read(consumer,"From"));
        Assert.Contains("r0.`status`",(string)Read(consumer,"From"));
        Assert.Contains("b.`yz_project_id`=@project_scope_"+scopeKind,(string)Read(own,"Scope"));
        Assert.DoesNotContain("project_scope",(string)Read(candidate,"Scope"));
        Assert.Contains("b.`status`",(string)Read(candidate,"Scope"));
        Assert.Contains("b.`delete_no` IS NULL",(string)Read(candidate,"Scope"));
        Assert.True((bool)Read(candidate,"RequiresPublishedProjectIdentity"));
    }

    /// <summary>新实时名称保留精确管理的同类型int64约束，不借全局声明放开字符串键匹配。</summary>
    [Fact]
    public void Global_r0_exact_match_keeps_original_foreign_key_type_boundary()
    {
        Assert.Equal("string",NonRedundantReads.CompileMatch(Catalog(0,"int64","int64"),"3","global_source_global_target_name").OutputTypes["global_source_global_target_name"]);
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileMatch(Catalog(0,"string","int64"),"3","global_source_global_target_name"));
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileMatch(Catalog(0,"int64","string"),"3","global_source_global_target_name"));
    }

    /// <summary>在R0实时与R1物理缓存之间往返不能绕过同一活动字段环检测。</summary>
    [Fact]
    public void Global_name_cycle_cannot_reset_by_switching_r0_and_r1()
    {
        var c=Catalog(0,"int64","int64");var cache=c.ConfigColumns.Single(f=>f.Id=="22");cache.ColumnName="source_global_target_name";cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("source.global_target_name");
        c.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",YzProjectId=c.YzProjectId,ColumnName="source_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="1",ControlDetail="{\"tableId\":\"1\",\"table\":\"global_source\",\"pageId\":\"10\",\"nameField\":\"global_target_name\"}"});
        var inner=c.ConfigColumns.Single(f=>f.Id=="13");inner.ControlDetail=inner.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"source_global_target_name\"",StringComparison.Ordinal);c.ConfigColumns.Single(f=>f.Id=="14").Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.source_global_target_name");
        Assert.Throws<NotSupportedException>(()=>GlobalSingleRelationTarget.Require(c,c.ConfigTables.Single(t=>t.Id=="3"),c.ConfigColumns.Single(f=>f.Id=="34"),c.ConfigTables.Single(t=>t.Id=="1")));
    }

    /// <summary>有限自关联路径允许真实父链，但单个表达式的物理跳数也有16层界限。</summary>
    [Theory][InlineData(16,true)][InlineData(17,false)]
    public void Global_r0_single_expression_has_a_fixed_hop_bound(int count,bool accepted)
    {
        var c=Catalog(0,"int64","int64");c.ConfigColumns.Add(new(){Id="15",ConfigTableId="1",YzProjectId=c.YzProjectId,ColumnName="literal_name",LogicalType="string",ColumnLength="100"});
        var inner=c.ConfigColumns.Single(f=>f.Id=="13");inner.LinkConfigTableId="1";inner.ControlDetail="{\"tableId\":\"1\",\"table\":\"global_source\",\"pageId\":\"10\",\"nameField\":\"literal_name\"}";c.ConfigColumns.Single(f=>f.Id=="14").Extra["columnFullName"]=JsonSerializer.SerializeToElement(string.Join('.',Enumerable.Repeat("global_target",count).Append("literal_name")));
        var owner=c.ConfigTables.Single(t=>t.Id=="3");var foreign=c.ConfigColumns.Single(f=>f.Id=="34");var target=c.ConfigTables.Single(t=>t.Id=="1");
        if(accepted)Assert.Equal("14",GlobalSingleRelationTarget.Require(c,owner,foreign,target).Name.Id);else Assert.Throws<NotSupportedException>(()=>GlobalSingleRelationTarget.Require(c,owner,foreign,target));
    }

    /// <summary>作为候选根的全局R0仍绑定目录身份，不能因无行项目而在错误项目下建立连接。</summary>
    [Theory][InlineData("int64")][InlineData("string")]
    public async Task Global_r0_root_read_keeps_published_identity_and_current_name(string key)
    {
        var c=Catalog(0,"int64",key);var plan=MySqlNonRedundantReadCompiler.Compile(c,"1",["id","global_target_name"]);
        Assert.True((bool)Read(plan,"RequiresPublishedProjectIdentity"));Assert.DoesNotContain("project_scope",(string)Read(plan,"Scope"));
        Assert.EndsWith(".`name`",((IReadOnlyDictionary<string,string>)Read(plan,"Expressions"))["global_target_name"]);
        using var database=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new MySqlNonRedundantReader(database).ReadPageAsync(plan,[],[],1,20,projectId:"999"));Assert.Null(database.CurrentTransaction);
    }
    /// <summary>单值R0证明不能顺带开放旧多值、级联或文件父范围入口。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public void Global_r0_does_not_relax_other_relation_families(int mode)
    {
        var c=Catalog(mode,"int64","int64");var target=c.ConfigTables.Single(t=>t.Id=="1");var page=c.ConfigPages.Single(p=>p.Id=="10");var name=c.ConfigColumns.Single(f=>f.Id=="14");
        GlobalCrossTableTarget.RequireSingleName(c,target,page,name);
        Assert.Throws<NotSupportedException>(()=>GlobalCrossTableTarget.Require(c,target,page));Assert.Throws<NotSupportedException>(()=>GlobalCrossTableTarget.RequireName(c,target,page,name));
        var foreign=c.ConfigColumns.Single(f=>f.Id=="34");foreign.ControlKind="MultiSelectPage";foreign.ColumnName="global_source_ids";foreign.LogicalType="string";foreign.ColumnLength="max";
        Assert.Throws<NotSupportedException>(()=>mode==0?GlobalMultipleRelationTarget.Require(c,c.ConfigTables.Single(t=>t.Id=="3"),foreign,target):GlobalMultipleRelationTarget.RequireCached(c,c.ConfigTables.Single(t=>t.Id=="3"),foreign,target));
    }
    /// <summary>复用已有固定目录；不删项目字段、页面、控制定义或已发布名称，仅让中间名称采用R0存储。</summary>
    internal static MetadataCatalog Catalog(int mode,string sourceKey,string innerKey)
    {var c=GlobalCachedTargetTests.Catalog(mode,sourceKey,innerKey);c.ConfigTables.Single(t=>t.Id=="1").IsRongyu=0;return c;}
    /// <summary>仅读取实际不可变SQL计划或维护公开投影，不重写编译算法。</summary>
    private static object Read(object value,string name)=>value.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(value)!;
}
