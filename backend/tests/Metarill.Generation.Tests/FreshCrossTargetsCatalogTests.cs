using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>正式跨表四模型先离线证明目录、存储和Full能力，避免先建库才发现测试配置矛盾。</summary>
public sealed class FreshCrossTargetsCatalogTests
{
    /// <summary>四种键配四种目标模式，真实宿主另独立验证，离线不能计作正式通过。</summary>
    [Theory]
    [InlineData("string","string","physical",13)]
    [InlineData("string","int64","derived",14)]
    [InlineData("int64","string","mixed",14)]
    [InlineData("int64","int64","repeated",14)]
    public async Task Catalog_keeps_unique_realm_and_proven_storage(string type,string parentType,string profile,int expectedTables)
    {
        var c=Catalog(type,parentType,profile);Assert.Equal("5",Assert.Single(c.YzAdminTypes).Id);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.CrossFresh");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Equal(before,JsonSerializer.Serialize(c));Assert.Equal(expectedTables-9,c.ConfigTables.Count);
        foreach(var t in c.ConfigTables.Where(t=>t.Id is "3" or "4" or "5"))Assert.Equal(!Derived(profile,int.Parse(t.Id)-3),MetadataStoragePolicy.ForTable(c,t).PhysicalColumns.Any(column=>column.ColumnName=="name"));
        if(profile=="repeated")foreach(var id in new[]{"25","80"})Assert.Equal("3",c.ConfigColumns.Single(x=>x.Id==id).LinkConfigTableId);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-fresh-catalog");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{Assert.True(new ApplicationPackager().Create(c,output,"Example.CrossFresh",NonRedundantApplicationFixture.Root()).FrontendIncluded);Assert.Equal(before,JsonSerializer.Serialize(c));}finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>不重复Published；为目标维护登记原字段，仍沿原uni-app和生成CRUD。</summary>
    internal static MetadataCatalog Catalog(string type,string parentType,string profile)
    {
        var c=NonRedundantCrossFullTests.Catalog(type,parentType,true);NonRedundantCrossTargetsTests.Apply(c,"nr_cross_full",profile=="repeated"?"derived":profile);
        if(profile=="repeated")
        {
            NonRedundantCrossTargetsTests.RepeatTarget(c,"nr_cross_full",parentType);var source=c.ConfigColumns.Single(x=>x.Id=="25");var second=c.ConfigColumns.Single(x=>x.Id=="80");second.LinkConfigTableId="3";second.ControlDetail=source.ControlDetail.Replace("\"columnPrefix\":\"","\"columnPrefix\":\"secondary_",StringComparison.Ordinal);
        }
        foreach(var table in c.ConfigTables.Where(t=>t.Id is "3" or "4" or "5"))
        {
            var status=c.ConfigColumns.Single(x=>x.ConfigTableId==table.Id&&x.ColumnName=="status");c.ConfigPageColumns.Add(new(){Id=table.Id+"01status",YzProjectId=c.YzProjectId,ConfigPageId=table.Id+"01",ConfigColumnId=status.Id,IsShow=1,IsEdit=1});
        }
        // 状态维护使用独立无状态过滤页，使停用后的同一记录仍可按原规则恢复。
        var rootStatus=c.ConfigColumns.Single(x=>x.ConfigTableId=="3"&&x.ColumnName=="status");c.ConfigPages.Add(new(){Id="311",YzProjectId=c.YzProjectId,YzAdminTypeId="5",ConfigTableId="3",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="311status",YzProjectId=c.YzProjectId,ConfigPageId="311",ConfigColumnId=rootStatus.Id,IsShow=1,IsEdit=1});
        foreach(var page in c.ConfigPages.Where(page=>page.ConfigTableId!="2"))page.IsSave=1;return c;
    }
    /// <summary>重复角色共享同一普通目标；混合模式仅中层派生名称。</summary>
    internal static bool Derived(string profile,int level)=>profile is "derived" or "repeated"||profile=="mixed"&&level==1;
}
