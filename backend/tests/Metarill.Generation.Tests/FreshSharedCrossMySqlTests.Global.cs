using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局共享正式矩阵复用原完整Web验收，只有精确新库租约到位才连接数据库。</summary>
public sealed partial class FreshSharedCrossMySqlTests
{
    /// <summary>四键覆盖全局共同祖先、短中层、独立仓库及全链，普通源权限与文件仍按原协议。</summary>
    [FreshCrossMySqlTheory]
    [InlineData("global-root","CrossStringStringDatabase","cross_ss",1000)]
    [InlineData("global-middle","CrossStringLongDatabase","cross_sl",1001)]
    [InlineData("global-warehouse","CrossLongStringDatabase","cross_ls",1002)]
    [InlineData("global-all","CrossLongLongDatabase","cross_ll",1003)]
    public Task Global_shared_cross_uses_real_identity_and_fresh_database(string profile,string property,string suffix,int worker)=>Exercise(profile,property,suffix,worker,SeedShared,VerifyShared);

    /// <summary>租约前离线核对正式夹具的四键、短链、显式全局和项目列形状，防止错误夹具消耗新库。</summary>
    [Theory]
    [InlineData("global-root","string","string",false,1,false)]
    [InlineData("global-middle","string","int64",true,2,true)]
    [InlineData("global-warehouse","int64","string",false,8,true)]
    [InlineData("global-all","int64","int64",false,15,true)]
    public void Global_formal_catalog_is_verified_offline_before_lease(string profile,string type,string parent,bool shorter,int mask,bool noProject)
    {
        var scenario=Scenario(profile);Assert.Equal((type,parent,shorter,false),scenario);var catalog=ScenarioCatalog(profile);Assert.Equal(7,catalog.ConfigTables.Count);var model=GenerationModel.Create(catalog,"Example.GlobalSharedLease");Assert.Empty(GenerationCapabilityAudit.Collect(model));foreach(var id in new[]{"2","12"}){var table=model.Tables.Single(table=>table.Source.Id==id);Assert.Equal(type,table.Columns.Single(column=>column.Source.Id==(id=="2"?"25":"10025")).Source.LogicalType);Assert.Equal(2,Assert.Single(SharedCrossTableCompilation.ForPage(model,table,Assert.Single(table.Pages)).Groups).Plans.Count);Assert.Contains(catalog.ConfigColumns,column=>column.ConfigTableId==id&&column.ColumnName=="yz_project_id");}
        var ids=new[]{"3","4","5","8"};for(var role=0;role<4;role++){var table=catalog.ConfigTables.Single(table=>table.Id==ids[role]);var global=(mask&(1<<role))!=0;Assert.Equal(global,GlobalCrossTableTarget.Declared(table));Assert.Equal(!global||!noProject,catalog.ConfigColumns.Any(column=>column.ConfigTableId==table.Id&&column.ColumnName=="yz_project_id"));if(role>0)Assert.Equal(parent,catalog.ConfigColumns.Single(column=>column.ConfigTableId==table.Id&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&column.ColumnName!="yz_project_id"&&column.ColumnName!="id").LogicalType);}
    }

    /// <summary>新模型只改变明确的全局位置；原正式模型的类型及派生规则保持原定义。</summary>
    private static (string Type,string ParentType,bool Shorter,bool Derived) Scenario(string profile)=>profile switch
    {
        "global-root"=>("string","string",false,false),"global-middle"=>("string","int64",true,false),
        "global-warehouse"=>("int64","string",false,false),"global-all"=>("int64","int64",false,false),
        _=>SharedCrossFreshCatalogTests.Definition(profile)
    };
    /// <summary>使用已经实际Full验证的同一目录工厂，不在生成后修补服务或页面。</summary>
    private static MetadataCatalog ScenarioCatalog(string profile)=>profile switch
    {
        "global-root"=>GlobalSharedCrossGenerationTests.Catalog("string","string",false,1,false),
        "global-middle"=>GlobalSharedCrossGenerationTests.Catalog("string","int64",true,2,true),
        "global-warehouse"=>GlobalSharedCrossGenerationTests.Catalog("int64","string",false,8,true),
        "global-all"=>GlobalSharedCrossGenerationTests.Catalog("int64","int64",false,15,true),
        _=>SharedCrossFreshCatalogTests.Catalog(profile)
    };
    private sealed partial class Probe
    {
        /// <summary>按已发布目标元数据识别全局，不能根据是否有项目列猜范围。</summary>
        internal bool Global(int role)=>GlobalCrossTableTarget.Declared(Catalog.ConfigTables.Single(table=>table.TableName==Prefix+"_"+new[]{"country","area","place","warehouse"}[role]));
    }
}
