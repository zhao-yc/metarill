using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>文件父范围不能将隐式祖先当物理外键，编译和坏存储拒绝均不连接数据库。</summary>
public sealed class FileCrossParentScopeTests
{
    /// <summary>四种来源／父键类型冻结为逻辑范围，原目录变化不污染已发布路径。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public void Published_file_scope_has_frozen_logical_path_instead_of_fake_physical_parent(string type,string parentType)
    {
        var catalog=Catalog("file_cross",type,parentType);var context=new FileMetadataContext([catalog]);var frozen=context.Catalogs.Single();var scope=context.PageScope(frozen,frozen.ConfigPages.Single(page=>page.Id=="101"));Assert.Empty(scope.Parents);var cross=Assert.Single(scope.Crosses);Assert.True(context.HasLogicalFileParents);Assert.Equal(0,cross.Level);Assert.Equal(new[]{"country_id","area_id","place_id"},cross.StorageNames);
        catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="invalid";Assert.Same(scope,context.PageScope(frozen,frozen.ConfigPages.Single(page=>page.Id=="101")));Assert.Throws<InvalidOperationException>(()=>context.PageScope(catalog,catalog.ConfigPages[0]));
    }
    /// <summary>页覆盖、目标动态条件／错误绑定、来源逻辑常量、目标隐藏字段不能被当作无范围。</summary>
    [Theory][InlineData("override")][InlineData("target")][InlineData("ambiguous")][InlineData("hidden")][InlineData("constant")][InlineData("exact")][InlineData("targetType")]
    public void Unproved_file_cross_scope_is_rejected_at_publication(string defect)
    {
        var catalog=Catalog("file_bad","string","string");var page=catalog.ConfigPages.Single(page=>page.Id=="101");
        switch(defect)
        {
            case "override":catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");break;
            case "target":catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="name=@unknown";break;
            case "ambiguous":var copy=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;copy.Id="202";catalog.ConfigPages.Add(copy);foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;clone.Id="extra"+field.Id;clone.ConfigPageId="202";catalog.ConfigPageColumns.Add(clone);}break;
            case "hidden":catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="22").IsShow=0;break;
            case "constant":page.WhereSql+=" AND country_id='1'";break;
            case "exact":page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");break;
            case "targetType":catalog.ConfigColumns.Single(column=>column.Id=="32").LogicalType="int64";break;
        }
        Assert.Throws<NotSupportedException>(()=>FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],page));
    }
    /// <summary>歧义、非规范ID、缺槽和空选择只能拒绝，离线端口证明未查询目标或猜最深值。</summary>
    [Theory][InlineData("ambiguous")][InlineData("missing")][InlineData("noncanonical")][InlineData("empty")]
    public async Task Bad_sparse_storage_fails_without_a_database_query(string kind)
    {
        var catalog=Catalog("file_sparse","string","string");var scope=FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],catalog.ConfigPages[0]);using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});var row=new Dictionary<string,object?>{{"country_id",null},{"area_id",null},{"place_id",null}};
        if(kind=="ambiguous"){row["country_id"]="1";row["place_id"]="3";}if(kind=="missing")row.Remove("area_id");if(kind=="noncanonical")row["place_id"]="03";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>FileCrossParentScope.ValuesAsync(scope.Crosses,new SqlRowStore(session,catalog),row,1,false,default));
    }
    /// <summary>普通R0来源及三个独立R1目标，名称只作为固定目标页定义，不增加文件数据投影。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parentType)
    {
        var result=new MetadataCatalog{YzProjectId="1"};var names=new[]{"source","country","area","place"};
        for(var index=0;index<4;index++)
        {
            var id=(index+1).ToString();result.ConfigTables.Add(new(){Id=id,TableName=prefix+"_"+names[index],IsRongyu=index==0?0:1,YzProjectId="1"});
            Column("1","id","int64",true);Column("2","name","string");Column("3","yz_project_id",index==2?"string":"int64");Column("4","delete_no","datetime");
            if(index==0){Column("7","attachments","string");result.ConfigColumns[^1].ControlKind="FileLibrary";result.ConfigColumns.Add(new(){Id="15",ConfigTableId="1",ColumnName="place_id",LogicalType=type,ColumnLength="64",ControlKind="CascadeTree",LinkConfigTableId="4",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{new{table=prefix+"_country",columnPrefix="country",nameField="name"},new{table=prefix+"_area",columnPrefix="area",parentField="country_id",nameField="name"},new{table=prefix+"_place",columnPrefix="place",parentField="area_id",nameField="name"}}})});}
            else{Column("5","enabled","bool");if(index>1){Column("6",index==2?"country_id":"area_id",parentType);result.ConfigColumns[^1].LinkConfigTableId=index.ToString();}}
            var page=new PageMetadata{Id=id+"01",ConfigTableId=id,YzProjectId="1",WhereSql="yz_project_id=@current_yz_project_id"+(index==0?" AND country_id=@parent_id":" AND enabled=1")};result.ConfigPages.Add(page);foreach(var field in result.ConfigColumns.Where(field=>field.ConfigTableId==id))result.ConfigPageColumns.Add(new(){Id=page.Id+field.Id,ConfigPageId=page.Id,ConfigColumnId=field.Id,IsShow=1,IsEdit=field.ControlKind=="FileLibrary"?1:0});
            void Column(string suffix,string name,string logical,bool required=false)=>result.ConfigColumns.Add(new(){Id=id+suffix,ConfigTableId=id,YzProjectId="1",ColumnName=name,LogicalType=logical,IsDbNotNull=required?1:0,ColumnLength="100"});
        }
        return result;
    }
}
