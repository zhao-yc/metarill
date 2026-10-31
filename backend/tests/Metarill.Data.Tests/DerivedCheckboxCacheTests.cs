using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>数据库复选与原弹层多选有不同目标协议，但派生缓存共享同一完整维护权威。</summary>
public sealed class DerivedCheckboxCacheTests
{
    /// <summary>旧四段、明确表／页及原回退规则不借用type=2或目标名称列的页面读取权限。</summary>
    [Theory][InlineData("four")][InlineData("empty")][InlineData("json")][InlineData("page")][InlineData("extraPage")][InlineData("join")][InlineData("hint")][InlineData("prefix")][InlineData("boundHint")][InlineData("pageBoundHint")]
    public void Original_target_definitions_compile_immutable_cache_closure(string kind)
    {
        var c=Catalog();var foreign=c.ConfigColumns.Single(x=>x.Id=="choices");
        if(kind=="empty")foreign.ControlDetail="";
        if(kind=="json")foreign.ControlDetail="{\"tableId\":\"tree\",\"nameField\":\"name\",\"whereSql\":\"status=1\"}";
        if(kind=="page"){foreign.LinkConfigTableId="";foreign.ControlDetail="{\"pageId\":\"201\"}";}
        if(kind=="extraPage"){foreign.LinkConfigTableId="";foreign.ControlDetail="";foreign.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");}
        if(kind=="join"){foreign.LinkConfigTableId="";foreign.ControlDetail="";foreign.Extra["joinTable"]=JsonSerializer.SerializeToElement("ds_tree");}
        if(kind is "hint" or "prefix"){foreign.LinkConfigTableId="";foreign.ColumnName="ds_tree_ids";c.ConfigColumns.Single(x=>x.Id=="choice_names").ColumnName="ds_tree_names";foreign.ControlDetail=kind=="hint"?"ds_tree":"";}
        if(kind=="boundHint")foreign.ControlDetail="ignored_legacy_hint";
        if(kind=="pageBoundHint"){foreign.LinkConfigTableId="";foreign.ControlDetail="ignored_legacy_hint";foreign.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");}
        c.ConfigPages.Single(x=>x.Id=="201").Extra.Clear();c.ConfigPageColumns.Clear();var before=JsonSerializer.Serialize(c);var d=Definition();var self=SelfTreeCache.Compile(c,"source","foreign");DerivedCrossTableCaches.Validate(c,[self,d],[]);var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[self,d]);Assert.Contains(c.ConfigColumns.Single(x=>x.Id=="choice_names").ColumnName,plan.MixedSourceColumns["ds_source"]);Assert.Contains("ds_company",plan.MixedTargets);Assert.Equal(before,JsonSerializer.Serialize(c));Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[d]));
    }
    /// <summary>相同无声明回退也适用于既有R0读取，名称仍为逻辑数组，不变成物理缓存读取。</summary>
    [Fact]public void Original_nonredundant_empty_definition_uses_exact_prefix()
    {
        var c=Catalog();c.ConfigTables.Single(x=>x.Id=="source").IsRongyu=0;var f=c.ConfigColumns.Single(x=>x.Id=="choices");f.ColumnName="ds_tree_ids";f.LinkConfigTableId="";f.ControlDetail="";var cache=c.ConfigColumns.Single(x=>x.Id=="choice_names");cache.ColumnName="ds_tree_names";cache.Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("ds_tree.name")};var before=JsonSerializer.Serialize(c);var read=Metarill.Data.Queries.NonRedundantReads.Compile(c,"source",["id","ds_tree_names"]);Assert.Equal("string",read.OutputTypes["ds_tree_names"]);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>冲突、未知查询、假页、假缓存和凭据在计划发布前拒绝，不能只保留其中有效一条。</summary>
    [Theory][InlineData("table")][InlineData("tableName")][InlineData("page")][InlineData("pageConflict")][InlineData("joined")][InlineData("hint")][InlineData("duplicate")][InlineData("unknown")][InlineData("name")][InlineData("segments")][InlineData("cache")][InlineData("virtual")][InlineData("credential")][InlineData("global")]
    public void Unproven_checkbox_definition_never_publishes_partial_cache(string kind)
    {
        var c=Catalog();var f=c.ConfigColumns.Single(x=>x.Id=="choices");
        if(kind=="table")f.ControlDetail="{\"tableId\":\"company\"}";
        if(kind=="tableName")f.ControlDetail="{\"table\":\"ds_company\"}";
        if(kind=="page")f.ControlDetail="{\"pageId\":\"missing\"}";
        if(kind=="pageConflict"){f.ControlDetail="{\"pageId\":\"201\"}";f.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("missing");}
        if(kind=="joined")f.Extra["joinTable"]=JsonSerializer.SerializeToElement("ds_company");
        if(kind=="hint"){f.LinkConfigTableId="";f.ControlDetail="unrelated";}
        if(kind=="duplicate")f.ControlDetail="{\"tableId\":\"tree\",\"tableId\":\"tree\"}";
        if(kind=="unknown")f.ControlDetail="{\"sql\":\"SELECT * FROM unknown\"}";
        if(kind=="name")f.ControlDetail="{\"nameField\":\"caption\"}";
        if(kind=="segments")f.ControlDetail="a;b;c";
        if(kind=="cache")c.ConfigColumns.Single(x=>x.Id=="choice_names").ColumnName="wrong_names";
        if(kind=="virtual")c.ConfigColumns.Single(x=>x.Id=="choice_names").IsVirtualColumn=1;
        if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="company_name").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="tree_name").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="tree").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[Definition()]));
    }
    /// <summary>基于同目标双消费者目录，只换原复选配置，不绕过物理ID和项目证明。</summary>
    private static MetadataCatalog Catalog(){var c=DerivedMultipleCacheTests.Catalog("string","int64");var f=c.ConfigColumns.Single(x=>x.Id=="choices");f.ControlKind="MultiCheckBoxDatabase";f.ControlDetail="旧头;旧表;parent_id=@parentId AND status=1;name desc";return c;}
    /// <summary>固定ID描述不携带SQL或请求路径。</summary>
    private static RelationCacheDefinition Definition()=>new("source","choices","tree","tree_name","choice_names",true);
}
