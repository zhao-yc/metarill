using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Files;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>逻辑文件父范围允许已证明R0名称，仍以真实物理父键和固定范围判定。</summary>
public sealed class FileCrossDerivedTargetScopeTests
{
    /// <summary>名称无需成为物理列；四种键及物理／派生名称共用同一完整跨表证明。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public void Published_file_parent_accepts_proven_nonredundant_name(string type,string parentType,bool derived)
    {
        var c=Catalog(type,parentType,derived);var context=new FileMetadataContext([c]);var frozen=context.Catalogs.Single();var scope=context.PageScope(frozen,frozen.ConfigPages.Single(x=>x.Id=="101"));Assert.True(context.HasLogicalFileParents);Assert.Empty(scope.Parents);Assert.Equal(0,Assert.Single(scope.Crosses).Level);
    }
    /// <summary>接受逻辑名称不接受凭据、多值、非物理父／范围、隐藏或不确定目标。</summary>
    [Theory][InlineData("credential")][InlineData("array")][InlineData("parent")][InlineData("scope")][InlineData("hidden")][InlineData("global")][InlineData("view")][InlineData("ambiguous")]
    public void Unproven_names_or_ranges_keep_whole_page_closed(string kind)
    {
        var c=Catalog("string","int64",true);
        if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="52").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="22").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(x=>x.Id=="52").ControlKind="FileLibrary";
        if(kind=="parent"){var parent=c.ConfigColumns.Single(x=>x.Id=="36");parent.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);parent.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(kind=="scope")c.ConfigPages.Single(x=>x.Id=="201").WhereSql+=" AND name='未知范围'";
        if(kind=="hidden")c.ConfigPageColumns.Single(x=>x.ConfigPageId=="201"&&x.ConfigColumnId=="22").IsShow=0;
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="view")c.ConfigTables.Single(x=>x.Id=="2").IsView=1;
        if(kind=="ambiguous"){var copy=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(c.ConfigPages.Single(x=>x.Id=="201")))!;copy.Id="202";c.ConfigPages.Add(copy);c.ConfigPageColumns.Add(new(){Id="20222",ConfigPageId="202",ConfigColumnId="22",IsShow=1,IsEdit=0});}
        var context=new FileMetadataContext([c]);var frozen=context.Catalogs.Single();Assert.Throws<NotSupportedException>(()=>context.PageScope(frozen,frozen.ConfigPages.Single(x=>x.Id=="101")));Assert.False(context.HasLogicalFileParents);
    }
    /// <summary>原来源与目标范围保持，仅把各目标名称转换为固定公司联接。</summary>
    private static MetadataCatalog Catalog(string type,string parentType,bool derived)
    {
        var c=FileCrossParentScopeTests.Catalog("file_cross_derived",type,parentType);foreach(var table in c.ConfigTables.Where(x=>x.Id!="1"))table.IsRongyu=0;
        if(derived)
        {
            c.ConfigTables.Add(new(){Id="5",TableName="file_cross_derived_company",YzProjectId=c.YzProjectId,IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="51",ConfigTableId="5",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="52",ConfigTableId="5",ColumnName="name",LogicalType="string"},new(){Id="53",ConfigTableId="5",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="54",ConfigTableId="5",ColumnName="delete_no",LogicalType="datetime"}]);
            foreach(var target in c.ConfigTables.Where(x=>x.Id is "2" or "3" or "4")){var name=c.ConfigColumns.Single(x=>x.ConfigTableId==target.Id&&x.ColumnName=="name");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");c.ConfigColumns.Add(new(){Id=target.Id+"9",ConfigTableId=target.Id,ColumnName="company_id",LogicalType="int64",LinkConfigTableId="5"});}
        }
        foreach(var field in c.ConfigColumns)field.YzProjectId=c.YzProjectId;return c;
    }
}
