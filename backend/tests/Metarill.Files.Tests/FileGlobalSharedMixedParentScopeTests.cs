using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Files;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>全局物理父必须与同页完整共享逻辑父证明等值，不能只取消目标项目条件。</summary>
public sealed class FileGlobalSharedMixedParentScopeTests
{
    /// <summary>四键、长短分支及有无目标项目列都冻结独立物理父列，SQL条件次序不影响证明。</summary>
    [Theory][InlineData("string","string",false,false)][InlineData("string","int64",true,true)][InlineData("int64","string",false,true)][InlineData("int64","int64",true,false)]
    public void Mixed_global_parent_is_frozen_as_equality_to_validated_shared_target(string type,string parent,bool shorter,bool noProject)
    {
        var c=Catalog(type,parent,shorter,noProject);var page=c.ConfigPages.Single(page=>page.Id=="105");page.WhereSql="owner_country_id=@parentId AND yz_project_id=@current_yz_project_id AND country_id=@parentId";var context=new FileMetadataContext([c]);var frozen=context.Catalogs.Single();var scope=context.PageScope(frozen,frozen.ConfigPages.Single(page=>page.Id=="105"));Assert.Empty(scope.Parents);Assert.Equal("18",Assert.Single(scope.GlobalParents).Id);Assert.Single(scope.Crosses);c.ConfigColumns.Single(column=>column.Id=="18").ControlDetail="{}";Assert.Same(scope,context.PageScope(frozen,frozen.ConfigPages.Single(page=>page.Id=="105")));
    }
    /// <summary>错页、不同名称、物理父覆盖、缺同页逻辑父、未声明物理List及未发布目标均在查询前拒绝。</summary>
    [Theory][InlineData("page")][InlineData("name")][InlineData("overlay")][InlineData("logicalMissing")][InlineData("notList")][InlineData("unpublished")]
    public void Unproven_physical_global_parent_cannot_borrow_shared_scope(string kind)
    {
        var c=Catalog("string","int64",false,true);var column=c.ConfigColumns.Single(column=>column.Id=="18");if(kind=="page")column.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("301");if(kind=="name"){c.ConfigColumns.Add(new(){Id="299",YzProjectId=c.YzProjectId,ConfigTableId="2",ColumnName="caption",LogicalType="string"});c.ConfigPageColumns.Add(new(){Id="201299",YzProjectId=c.YzProjectId,ConfigPageId="201",ConfigColumnId="299",IsShow=1});column.ControlDetail="{\"tableId\":\"2\",\"nameField\":\"caption\"}";}if(kind=="overlay")c.ConfigPageColumns.Single(field=>field.ConfigPageId=="105"&&field.ConfigColumnId=="18").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");if(kind=="logicalMissing")c.ConfigPages.Single(page=>page.Id=="105").WhereSql="yz_project_id=@current_yz_project_id AND owner_country_id=@parentId";if(kind=="notList")column.ControlKind="";if(kind=="unpublished")c.ConfigTables.Single(table=>table.Id=="2").YzProjectId="";MetadataValidation.Validate(c);Assert.Throws<NotSupportedException>(()=>FileReferenceScope.CompilePage(c,c.ConfigTables.Single(table=>table.Id=="1"),c.ConfigPages.Single(page=>page.Id=="105")));
    }
    /// <summary>新增物理全局等值按原字符串ID逐字校验，不能以浮点、前导零或空白借用真实节点。</summary>
    [Theory][InlineData("01")][InlineData("+1")][InlineData(" 1")][InlineData("1.0")]
    public void Invalid_raw_global_parent_is_not_a_canonical_node(string value)=>Assert.Throws<UnauthorizedAccessException>(()=>FileReferenceScope.CanonicalId(value));
    /// <summary>只在原共享文件模型中为实际物理父增加独立List声明，原普通模型不变。</summary>
    internal static MetadataCatalog Catalog(string type,string parent,bool shorter,bool noProject)
    {
        var c=FileSharedCrossParentScopeTests.Catalog(type,parent,shorter);var table=c.ConfigTables.Single(table=>table.Id=="2");table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");c.ConfigPages.Single(page=>page.ConfigTableId=="2").WhereSql="enabled=1";var column=c.ConfigColumns.Single(column=>column.Id=="18");column.ControlKind="List";column.ControlDetail="{\"tableId\":\"2\",\"nameField\":\"name\"}";column.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");if(noProject){var removed=c.ConfigColumns.Where(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id").Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);c.ConfigColumns.RemoveAll(column=>removed.Contains(column.Id));c.ConfigPageColumns.RemoveAll(field=>removed.Contains(field.ConfigColumnId));}return c;
    }
}
