using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>无项目列来源必须逐项证明发布边界；仅编译元数据，不建立数据库连接。</summary>
public sealed class GlobalCachedSourceProofTests
{
    /// <summary>原全局物理List同时支持原字符串键和int64键，输入目录不被修改。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Published_global_cached_single_requires_exact_owner_and_target(string type)
    {
        var c=Catalog(type);var before=JsonSerializer.Serialize(c);var proof=GlobalSingleRelationTarget.RequireCached(c,c.ConfigTables[0],c.ConfigColumns.Single(column=>column.Id=="13"),c.ConfigTables[1]);
        Assert.Equal("20",proof.Page.Id);Assert.Equal("22",proof.Name.Id);Assert.Equal(before,JsonSerializer.Serialize(c));
        Assert.Throws<NotSupportedException>(()=>GlobalSingleRelationTarget.Require(c,c.ConfigTables[0],c.ConfigColumns.Single(column=>column.Id=="13"),c.ConfigTables[1]));
    }
    /// <summary>不能靠移除项目列、变造对象、切换后台或不完整发布取得全局资格。</summary>
    [Theory][InlineData("implicit")][InlineData("projectColumn")][InlineData("ownerProject")][InlineData("foreignProject")][InlineData("pageProject")][InlineData("adminProject")][InlineData("noPage")][InlineData("keyNullable")][InlineData("keyJoin")][InlineData("deletedNonNullable")][InlineData("exactScope")][InlineData("r0")][InlineData("selectPage")][InlineData("virtualForeign")][InlineData("nonCanonicalProject")]
    public void Unproven_global_source_is_rejected_before_any_execution(string kind)
    {
        var c=Catalog("string");var owner=c.ConfigTables[0];var foreign=c.ConfigColumns.Single(column=>column.Id=="13");
        switch(kind)
        {
            case "implicit":owner.Extra.Clear();break;
            case "projectColumn":c.ConfigColumns.Add(Col("15","1","yz_project_id","int64"));break;
            case "ownerProject":owner.YzProjectId="200";break;
            case "foreignProject":foreign.YzProjectId="200";break;
            case "pageProject":c.ConfigPages[0].YzProjectId="200";break;
            case "adminProject":c.YzAdminTypes[0].YzProjectId="200";break;
            case "noPage":c.ConfigPages.RemoveAt(0);break;
            case "keyNullable":c.ConfigColumns[0].IsDbNotNull=0;break;
            case "keyJoin":c.ConfigColumns[0].Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);break;
            case "deletedNonNullable":c.ConfigColumns[1].IsDbNotNull=1;break;
            case "exactScope":c.ConfigPages[0].Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");break;
            case "r0":owner.IsRongyu=0;break;
            case "selectPage":foreign.ControlKind="SelectPage";break;
            case "virtualForeign":foreign.IsVirtualColumn=1;break;
            case "nonCanonicalProject":c.YzProjectId="0100";break;
        }
        Assert.ThrowsAny<Exception>(()=>GlobalSingleRelationTarget.RequireCached(c,owner,foreign,c.ConfigTables[1]));
    }
    /// <summary>同ID的目录外副本不能取代实际发布对象。</summary>
    [Fact]
    public void Detached_owner_or_foreign_cannot_supply_publication_proof()
    {
        var c=Catalog("string");var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var clone=JsonSerializer.Deserialize<TableMetadata>(JsonSerializer.Serialize(c.ConfigTables[0],options),options)!;
        Assert.Throws<NotSupportedException>(()=>GlobalCachedRelationSource.Require(c,clone));
        var foreign=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(c.ConfigColumns[2],options),options)!;
        Assert.Throws<NotSupportedException>(()=>GlobalCachedRelationSource.RequireSingle(c,c.ConfigTables[0],foreign));
    }
    /// <summary>名称副本只允许原List的一层真实名称，不能将任意联接值升级成全局写目标。</summary>
    [Theory][InlineData("valid")][InlineData("foreignPath")][InlineData("unknownPath")][InlineData("wrongName")][InlineData("noJoin")][InlineData("virtual")][InlineData("wrongType")][InlineData("wrongCacheName")][InlineData("foreignLink")][InlineData("detached")]
    public void Physical_cache_requires_exact_single_binding(string kind)
    {
        var c=Catalog("string");var source=c.ConfigTables[0];var cache=c.ConfigColumns.Single(column=>column.Id=="14");
        cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.name");
        switch(kind)
        {
            case "foreignPath":cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target_id.name");break;
            case "unknownPath":cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("unpublished.name");break;
            case "wrongName":cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.password");break;
            case "noJoin":cache.Extra.Remove("isJoinColumn");break;
            case "virtual":cache.IsVirtualColumn=1;break;
            case "wrongType":cache.LogicalType="int64";break;
            case "wrongCacheName":cache.ColumnName="other_name";break;
            case "foreignLink":cache.LinkConfigTableId="2";break;
            case "detached":cache=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(cache))!;break;
        }
        if(kind is "valid" or "foreignPath")
        {
            var binding=GlobalCachedRelationSource.RequireCache(c,source,cache);Assert.Equal("13",binding.Foreign.Id);Assert.Equal("2",binding.Target.Id);Assert.Equal("22",binding.Name.Id);Assert.Same(cache,binding.Cache);
        }
        else Assert.ThrowsAny<Exception>(()=>GlobalCachedRelationSource.RequireCache(c,source,cache));
    }
    /// <summary>最小目录含真实后台/页面和物理根键，目标仍由原全局发布证明约束。</summary>
    internal static MetadataCatalog Catalog(string type)
    {
        var c=new MetadataCatalog{YzProjectId="100",YzAdminTypes=[new(){Id="9",YzProjectId="100"}]};
        foreach(var (id,name) in new[]{("1","global_source"),("2","global_target")})c.ConfigTables.Add(new(){Id=id,TableName=name,YzProjectId="100",IsRongyu=1,Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
        c.ConfigColumns=[Col("11","1","id","int64",true),Col("12","1","delete_no","datetime"),new(){Id="13",ConfigTableId="1",ColumnName="global_target_id",YzProjectId="100",LogicalType=type,ColumnLength="50",ControlKind="List",ControlDetail="{\"table\":\"global_target\",\"tableId\":\"2\",\"pageId\":\"20\",\"nameField\":\"name\"}",LinkConfigTableId="2"},Col("14","1","global_target_name","string"),Col("21","2","id","int64",true),Col("22","2","name","string"),Col("23","2","delete_no","datetime")];
        c.ConfigPages=[new(){Id="10",ConfigTableId="1",YzProjectId="100",YzAdminTypeId="9"},new(){Id="20",ConfigTableId="2",YzProjectId="100",YzAdminTypeId="9"}];return c;
    }
    /// <summary>所有测试列均明确归属，不依赖可选项目推断。</summary>
    private static ColumnMetadata Col(string id,string table,string name,string type,bool key=false)=>new(){Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,ColumnLength="100",YzProjectId="100",IsDbNotNull=key?1:0};
}
