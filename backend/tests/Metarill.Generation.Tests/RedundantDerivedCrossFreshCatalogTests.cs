using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;
/// <summary>正式派生跨表模型先验证实际Full和原组件，不把离线构建算作真实身份验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantDerivedCrossFreshCatalogTests
{
    /// <summary>四种键与物理／派生／混合／重复目标生成实际完整包；目录保持不变。</summary>
    [Theory][InlineData("physical")][InlineData("derived")][InlineData("mixed")][InlineData("repeated")]
    public async Task Full_catalog_builds_before_lease_is_requested(string profile)
    {
        var c=Catalog(profile);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.RedundantDerivedFresh");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Equal(profile=="physical"?5:6,c.ConfigTables.Count);Assert.Contains(Prefix+"_audit",MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns.Keys);var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-fresh-catalog");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var root=NonRedundantApplicationFixture.Root();
        try{Assert.True(new ApplicationPackager().Create(c,output,model.Namespace,root).FrontendIncluded);foreach(var action in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,action);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));Assert.Equal(before,JsonSerializer.Serialize(c));}finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    internal const string Prefix="rr_derived_fresh";
    /// <summary>矩阵按键类型交叉，不以一个模型代表全部组合。</summary>
    internal static (string Type,string ParentType) Definition(string profile)=>profile switch{"physical"=>("string","string"),"derived"=>("string","int64"),"mixed"=>("int64","string"),"repeated"=>("int64","int64"),_=>throw new ArgumentOutOfRangeException(nameof(profile))};
    /// <summary>沿真实元数据加入独立下游和原FileLibrary；R0派生名称不新增物理列。</summary>
    internal static MetadataCatalog Catalog(string profile)
    {
        var definition=Definition(profile);var c=RedundantCrossDerivedTargetsTests.Catalog(Prefix,definition.Type,definition.ParentType,profile);c.ConfigColumns.RemoveAll(x=>(x.ConfigTableId=="6"||x.ConfigTableId=="5")&&x.ColumnName=="delete_no");var cache=profile=="mixed"?"area_name":"country_name";
        c.ConfigTables.Add(new(){Id="7",TableName=Prefix+"_audit",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="71",ConfigTableId="7",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="72",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="73",ConfigTableId="7",ColumnName="delete_no",LogicalType="datetime"},new(){Id="74",ConfigTableId="7",ColumnName="source_id",LogicalType="int64",LinkConfigTableId="2"},new(){Id="75",ConfigTableId="7",ColumnName="source_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("source."+cache)}}]);c.ConfigPages.Add(new(){Id="701",ConfigTableId="7",WhereSql="yz_project_id=@current_yz_project_id"});foreach(var id in new[]{"74","75"})c.ConfigPageColumns.Add(new(){Id="701"+id,ConfigPageId="701",ConfigColumnId=id});
        c.ConfigColumns.Add(new(){Id="9001",ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});c.ConfigPageColumns.Add(new(){Id="2019001",ConfigPageId="201",ConfigColumnId="9001",IsShow=1,IsEdit=1});
        foreach(var row in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))row.YzProjectId=c.YzProjectId;foreach(var page in c.ConfigPages){page.YzAdminTypeId="5";page.IsSave=1;c.ConfigMenus.Add(new(){Id=(700000+int.Parse(page.Id,System.Globalization.CultureInfo.InvariantCulture)).ToString(System.Globalization.CultureInfo.InvariantCulture),YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="派生跨表验收"+page.Id,Kind="GENERATED_PAGE",ConfigPageId=page.Id});}c.ConfigPages.Single(p=>p.Id=="201").IsCopy=1;return c;
    }
}
