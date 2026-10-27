using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Queries;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>R1物理父不会被稀疏重算，公共证明、投影及真实类型在发出查询前固定。</summary>
public sealed class FileRedundantSharedParentScopeTests
{
    /// <summary>四键、长短分支、普通及全局位置和派生目标都逐页冻结实际物理字段。</summary>
    [Theory][InlineData("string","string",false,false)][InlineData("string","int64",false,false)][InlineData("int64","string",false,false)][InlineData("int64","int64",false,false)][InlineData("string","int64",true,false)][InlineData("int64","string",true,false)][InlineData("string","int64",false,true)]
    public void Parent_scopes_keep_actual_physical_fields(string type,string parent,bool shorter,bool derived)
    {
        var catalog=Catalog(type,parent,shorter,derived);foreach(var global in derived?new[]{""}:new[]{"","2","3","4","5"})
        {
            if(shorter&&global=="4")continue;var c=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(catalog))!;if(global.Length>0)ApplyGlobal(c,global,true);var registry=new FileMetadataContext([c]);var frozen=registry.Catalogs.Single();Assert.True(registry.HasLogicalFileParents);
            foreach(var id in new[]{"101","102","103","104","105"}){var scope=registry.PageScope(frozen,frozen.ConfigPages.Single(p=>p.Id==id));var expected=id=="102"?"area_id":id=="103"?(shorter?"area_id":"place_id"):id=="104"?"warehouse_id":"country_id";Assert.Contains(scope.Crosses,cross=>Assert.Single(cross.StorageNames)==expected);Assert.All(scope.Crosses,cross=>Assert.StartsWith("r1:",cross.SourceId));if(id=="105"&&global=="2"){Assert.Empty(scope.Parents);Assert.Equal(2,scope.Crosses.Count);}else if(id=="105")Assert.Single(scope.Parents);}
            c.ConfigColumns.Single(column=>column.Id=="16").ControlDetail="{}";Assert.Single(registry.PageScope(frozen,frozen.ConfigPages.Single(p=>p.Id=="101")).Crosses);
        }
    }
    /// <summary>坏同伴、覆盖、范围、身份和缓存名称不能由另一个有效分支补足。</summary>
    [Theory][InlineData("peer")][InlineData("overlay")][InlineData("exact")][InlineData("globalSource")][InlineData("unpublished")][InlineData("parentName")][InlineData("variable")][InlineData("physicalPage")][InlineData("hidden")][InlineData("nameLink")]
    public void Unproven_parent_page_is_closed(string kind)
    {
        var c=Catalog("string","int64");if(kind=="peer")c.ConfigColumns.Single(x=>x.Id=="16").ControlDetail="{}";if(kind=="overlay")c.ConfigPageColumns.Single(x=>x.ConfigPageId=="101"&&x.ConfigColumnId=="16").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");if(kind=="exact")c.ConfigPages.Single(p=>p.Id=="101").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");if(kind=="globalSource")c.ConfigTables.Single(t=>t.Id=="1").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="unpublished")c.ConfigTables.Single(t=>t.Id=="1").YzProjectId="";if(kind=="parentName")c.ConfigPages.Single(p=>p.Id=="101").WhereSql="country_name=@parentId";if(kind=="hidden"){var field=c.ConfigPageColumns.Single(f=>f.ConfigPageId=="501"&&f.ConfigColumnId=="52");field.IsShow=0;field.IsEdit=0;}if(kind=="nameLink"){c.ConfigColumns.Add(new(){Id="1099",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="country_name",LogicalType="string",LinkConfigTableId="2"});c.ConfigPages.Single(p=>p.Id=="101").WhereSql="country_name=@parentId";}if(kind is "variable" or "physicalPage"){ApplyGlobal(c,"2",false);if(kind=="variable")c.ConfigPages.Single(p=>p.Id=="201").WhereSql="yz_project_id=@current_yz_project_id";else c.ConfigColumns.Single(x=>x.Id=="18").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("301");}
        var page=kind=="physicalPage"?"105":"101";var registry=new FileMetadataContext([c]);var frozen=registry.Catalogs.Single();Assert.Throws<NotSupportedException>(()=>registry.PageScope(frozen,frozen.ConfigPages.Single(p=>p.Id==page)));
    }
    /// <summary>实际字段类型和规范格式错误时不连接数据库，不能用隐式数值转换借用节点。</summary>
    [Theory][InlineData("string","01")][InlineData("string","+1")][InlineData("string","1.0")][InlineData("string",null)][InlineData("int64","1")]
    public async Task Invalid_actual_parent_is_rejected_before_query(string type,string? value)
    {
        var c=Catalog(type,"int64");var scope=FileReferenceScope.CompilePage(c,c.ConfigTables.Single(t=>t.Id=="1"),c.ConfigPages.Single(p=>p.Id=="101"));using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>FileCrossParentScope.ValuesAsync(scope.Crosses,new SqlRowStore(session,c),new Dictionary<string,object?>{{"country_id",value}},1,false,default));
    }
    /// <summary>沿原文件目录，仅改变来源存储及明确发布后台，原页面和字段保持。</summary>
    internal static MetadataCatalog Catalog(string type,string parent,bool shorter=false,bool derived=false)
    {
        var c=FileSharedCrossParentScopeTests.Catalog(type,parent,shorter,derived);c.ConfigTables.Single(t=>t.Id=="1").IsRongyu=1;c.YzAdminTypes.Add(new(){Id="9",YzProjectId=c.YzProjectId});foreach(var page in c.ConfigPages)page.YzAdminTypeId="9";var owner=c.ConfigColumns.Single(column=>column.Id=="18");owner.ControlKind="List";owner.ControlDetail="{\"tableId\":\"2\",\"nameField\":\"name\"}";owner.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");return c;
    }
    /// <summary>全局目标的无项目列不影响来源项目列，固定条件继续保留。</summary>
    private static void ApplyGlobal(MetadataCatalog c,string id,bool noProject)
    {
        c.ConfigTables.Single(t=>t.Id==id).Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");c.ConfigPages.Single(p=>p.ConfigTableId==id).WhereSql="enabled=1";if(noProject){var removed=c.ConfigColumns.Where(column=>column.ConfigTableId==id&&column.ColumnName=="yz_project_id").Select(column=>column.Id).ToHashSet();c.ConfigColumns.RemoveAll(column=>removed.Contains(column.Id));c.ConfigPageColumns.RemoveAll(field=>removed.Contains(field.ConfigColumnId));}
    }
}
