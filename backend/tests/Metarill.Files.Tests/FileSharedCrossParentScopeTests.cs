using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Queries;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>共享文件父范围必须按全部真实末端一致恢复，不能逐控件误判稀疏空祖先。</summary>
public sealed class FileSharedCrossParentScopeTests
{
    /// <summary>四键、短主叶及派生目标都在发布时冻结成同一个组身份；物理父外键保持独立。</summary>
    [Theory][InlineData("string","string",false,false)][InlineData("string","int64",false,false)][InlineData("int64","string",false,false)][InlineData("int64","int64",false,false)][InlineData("string","int64",true,false)][InlineData("int64","string",true,false)][InlineData("string","int64",false,true)]
    public void Published_file_scopes_share_one_group_and_keep_physical_owner(string type,string parent,bool shorter,bool derived)
    {
        var c=Catalog(type,parent,shorter,derived);var context=new FileMetadataContext([c]);var frozen=context.Catalogs.Single();Assert.True(context.HasLogicalFileParents);
        foreach(var id in new[]{"101","102","103","104","105"}){var scope=context.PageScope(frozen,frozen.ConfigPages.Single(p=>p.Id==id));var cross=Assert.Single(scope.Crosses);Assert.Equal("15",cross.SourceId);Assert.Equal(shorter?3:4,cross.StorageNames.Count);if(id=="105")Assert.Single(scope.Parents);else Assert.Empty(scope.Parents);}
        c.ConfigColumns.Single(x=>x.Id=="16").ControlDetail="{}";Assert.Equal(shorter?3:4,Assert.Single(context.PageScope(frozen,frozen.ConfigPages.Single(p=>p.Id=="101")).Crosses).StorageNames.Count);
    }
    /// <summary>共享同伴的坏名称／范围／覆盖、歧义目标页与非法父身份都不能按单个好分支发布。</summary>
    [Theory][InlineData("peer")][InlineData("overlay")][InlineData("target")][InlineData("ambiguous")][InlineData("hidden")][InlineData("constant")][InlineData("exact")][InlineData("global")][InlineData("view")][InlineData("parentName")]
    public void Broken_shared_peer_or_identity_keeps_entire_page_closed(string kind)
    {
        var c=Catalog("string","int64");if(kind=="peer"){var col=c.ConfigColumns.Single(x=>x.Id=="16");var json=JsonNode.Parse(col.ControlDetail)!;json["levels"]![2]!["nameField"]="missing";col.ControlDetail=json.ToJsonString();}if(kind=="overlay")c.ConfigPageColumns.Single(x=>x.ConfigPageId=="101"&&x.ConfigColumnId=="16").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");if(kind=="target")c.ConfigPages.Single(x=>x.Id=="501").WhereSql="name=@unknown";if(kind=="ambiguous"){var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(c.ConfigPages.Single(x=>x.Id=="501")))!;page.Id="502";c.ConfigPages.Add(page);foreach(var field in c.ConfigPageColumns.Where(x=>x.ConfigPageId=="501").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="502_"+copy.Id;copy.ConfigPageId="502";c.ConfigPageColumns.Add(copy);}}if(kind=="hidden"){var field=c.ConfigPageColumns.Single(x=>x.ConfigPageId=="501"&&x.ConfigColumnId=="52");field.IsShow=0;field.IsEdit=0;}if(kind=="constant")c.ConfigPages.Single(x=>x.Id=="101").WhereSql+=" AND country_id='1'";if(kind=="exact")c.ConfigPages.Single(x=>x.Id=="101").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");if(kind=="global")c.ConfigTables.Single(x=>x.Id=="5").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="view")c.ConfigTables.Single(x=>x.Id=="5").IsView=1;if(kind=="parentName")c.ConfigPages.Single(x=>x.Id=="101").WhereSql="country_name=@parentId";var context=new FileMetadataContext([c]);var f=context.Catalogs.Single();Assert.Throws<NotSupportedException>(()=>context.PageScope(f,f.ConfigPages.Single(x=>x.Id=="101")));
    }
    /// <summary>已发布全局共同祖先与独立分支同时编译文件范围，目标可不带项目列。</summary>
    [Theory][InlineData("2",false,false)][InlineData("2",true,true)][InlineData("5",false,true)][InlineData("5",true,false)]
    public void Published_global_shared_file_scope_keeps_frozen_group(string targetId,bool shorter,bool noProject)
    {
        var c=Catalog("string","int64",shorter);var target=c.ConfigTables.Single(table=>table.Id==targetId);target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");c.ConfigPages.Single(page=>page.ConfigTableId==targetId).WhereSql="enabled=1";if(noProject){var removed=c.ConfigColumns.Where(column=>column.ConfigTableId==targetId&&column.ColumnName=="yz_project_id").Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);c.ConfigColumns.RemoveAll(column=>removed.Contains(column.Id));c.ConfigPageColumns.RemoveAll(field=>removed.Contains(field.ConfigColumnId));}
        var table=c.ConfigTables[0];var sourcePage=c.ConfigPages.Single(page=>page.Id=="101");var group=SharedCrossTableSparseSelection.Compile(table,"15",c.ConfigColumns.Where(column=>column.ConfigTableId==table.Id));var scopes=group.Branches.Select(branch=>FileCrossParentScope.CompileBranch(c,table,sourcePage,c.ConfigColumns.Single(column=>column.Id==branch.SourceColumnId),group.BranchSelection(branch.SourceColumnId),0).Binding).ToArray();_=NonRedundantReads.CompileWithCrossTables(c,table.Id,["id"],scopes,group.Slots.Select(slot=>slot.ColumnName).ToArray());
        var context=new FileMetadataContext([c]);var frozen=context.Catalogs.Single();Assert.Equal(shorter?3:4,Assert.Single(context.PageScope(frozen,frozen.ConfigPages.Single(page=>page.Id=="101")).Crosses).StorageNames.Count);
    }
    /// <summary>空组、祖先／后代并存、缺位和错误类型在访问目标前拒绝；离线端口不提供任何假成功。</summary>
    [Theory][InlineData("empty")][InlineData("ancestor")][InlineData("missing")][InlineData("format")][InlineData("type")]
    public async Task Invalid_shared_raw_storage_is_denied_before_query(string kind)
    {
        var c=Catalog("string","int64");var scope=FileReferenceScope.CompilePage(c,c.ConfigTables[0],c.ConfigPages[0]);var row=new Dictionary<string,object?>{{"country_id",null},{"area_id",null},{"place_id",null},{"warehouse_id",null}};if(kind=="ancestor"){row["country_id"]="1";row["warehouse_id"]="4";}if(kind=="missing")row.Remove("area_id");if(kind=="format")row["warehouse_id"]="04";if(kind=="type")row["warehouse_id"]=4L;using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>FileCrossParentScope.ValuesAsync(scope.Crosses,new SqlRowStore(session,c),row,1,false,default));
    }
    /// <summary>在原固定文件模型中只增加共享仓库和物理国家归属；全部范围来自元数据。</summary>
    internal static MetadataCatalog Catalog(string type,string parent,bool shorter=false,bool derived=false)
    {
        const string prefix="file_shared";var c=FileCrossParentScopeTests.Catalog(prefix,type,parent);var first=c.ConfigColumns.Single(x=>x.Id=="15");var warehouse=JsonNode.Parse(first.ControlDetail)!;warehouse["levels"]![2]!["table"]=prefix+"_warehouse";warehouse["levels"]![2]!["columnPrefix"]="warehouse";c.ConfigTables.Add(new(){Id="5",TableName=prefix+"_warehouse",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="51",ConfigTableId="5",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="52",ConfigTableId="5",ColumnName="name",LogicalType="string"},new(){Id="53",ConfigTableId="5",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="54",ConfigTableId="5",ColumnName="delete_no",LogicalType="datetime"},new(){Id="55",ConfigTableId="5",ColumnName="enabled",LogicalType="bool"},new(){Id="56",ConfigTableId="5",ColumnName="area_id",LogicalType=parent,LinkConfigTableId="3"},new(){Id="16",ConfigTableId="1",ColumnName="warehouse_id",LogicalType=type,ControlKind="CascadeTree",ControlDetail=warehouse.ToJsonString(),LinkConfigTableId="5"},new(){Id="18",ConfigTableId="1",ColumnName="owner_country_id",LogicalType=type,LinkConfigTableId="2"}]);c.ConfigPages.Add(new(){Id="501",ConfigTableId="5",WhereSql="yz_project_id=@current_yz_project_id AND enabled=1"});foreach(var id in new[]{"52","56"})c.ConfigPageColumns.Add(new(){Id="501"+id,ConfigPageId="501",ConfigColumnId=id,IsShow=1});foreach(var id in new[]{"16","18"})c.ConfigPageColumns.Add(new(){Id="101"+id,ConfigPageId="101",ConfigColumnId=id,IsShow=1,IsEdit=1});
        if(shorter){first.ColumnName="area_id";first.LinkConfigTableId="3";var json=JsonNode.Parse(first.ControlDetail)!;json["levels"]!.AsArray().RemoveAt(2);first.ControlDetail=json.ToJsonString();}
        var original=c.ConfigPages.Single(p=>p.Id=="101");foreach(var (id,condition) in new[]{("102","area_id=@parentId"),("103",(shorter?"area":"place")+"_id=@parentId"),("104","warehouse_id=@parentId"),("105","country_id=@parentId AND owner_country_id=@parentId")}){var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;page.Id=id;page.WhereSql="yz_project_id=@current_yz_project_id AND "+condition;c.ConfigPages.Add(page);foreach(var field in c.ConfigPageColumns.Where(f=>f.ConfigPageId=="101").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id=id+"_"+field.Id;copy.ConfigPageId=id;c.ConfigPageColumns.Add(copy);}}
        if(derived){c.ConfigTables.Add(new(){Id="6",TableName=prefix+"_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="62",ConfigTableId="6",ColumnName="name",LogicalType="string"},new(){Id="63",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"}]);foreach(var t in c.ConfigTables.Where(x=>x.Id is "2" or "3" or "4")){t.IsRongyu=0;var name=c.ConfigColumns.Single(x=>x.ConfigTableId==t.Id&&x.ColumnName=="name");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");c.ConfigColumns.Add(new(){Id=t.Id+"9",ConfigTableId=t.Id,ColumnName="company_id",LogicalType="int64",LinkConfigTableId="6"});}}
        foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;return c;
    }
}
