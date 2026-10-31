using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享R0／R1的正式组合先离线构建；真实账号及数据库由独占租约另验。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SharedCrossFreshCatalogTests
{
    internal const string Prefix="shared_fresh";
    /// <summary>四种所选键／父键分别组合普通、短控件、派生和短派生，不以一种模型冒充四种。</summary>
    internal static (string Type,string ParentType,bool Shorter,bool Derived) Definition(string profile)=>profile switch
    {
        "plain"=>("string","string",false,false),"short"=>("string","int64",true,false),
        "derived"=>("int64","string",false,true),"short-derived"=>("int64","int64",true,true),
        _=>throw new ArgumentOutOfRangeException(nameof(profile))
    };
    /// <summary>同一份原元数据建立两张来源；后端、原页面及权限完全交给正式生成链路。</summary>
    internal static MetadataCatalog Catalog(string profile)
    {
        var d=Definition(profile);var c=SharedCrossTableGenerationTests.Catalog(Prefix,d.Type,d.ParentType,d.Shorter,d.Derived);c.ConfigTables.Single(t=>t.Id=="2").IsRongyu=0;
        c.ConfigColumns.Add(new(){Id="9001",ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});c.ConfigPageColumns.Add(new(){Id="2019001",ConfigPageId="201",ConfigColumnId="9001",IsShow=1,IsEdit=1});
        c.ConfigTables.Add(new(){Id="10",TableName=Prefix+"_item",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="101",ConfigTableId="10",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="102",ConfigTableId="10",ColumnName="name",LogicalType="string"},new(){Id="103",ConfigTableId="10",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="104",ConfigTableId="10",ColumnName="delete_no",LogicalType="datetime"},new(){Id="106",ConfigTableId="10",ColumnName="country_id",LogicalType=d.Type},new(){Id="105",ConfigTableId="2",ColumnName="a_item_id",LogicalType="int64",LinkConfigTableId="10",ControlKind="List",ControlDetail="{\"tableId\":\"10\",\"nameField\":\"name\",\"whereSql\":\"country_id=@formParameters.countryId\"}",Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("1001")}}]);c.ConfigPages.Add(new(){Id="1001",ConfigTableId="10",WhereSql="yz_project_id=@current_yz_project_id",IsSave=1});foreach(var id in new[]{"102","106"})c.ConfigPageColumns.Add(new(){Id="1001"+id,ConfigPageId="1001",ConfigColumnId=id});c.ConfigPageColumns.Add(new(){Id="201105",ConfigPageId="201",ConfigColumnId="105"});
        // 分页验收会发送共同国家的级联筛选，必须先在原页面元数据明确启用。
        c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var original=c.ConfigTables.Single(t=>t.Id=="2");var cached=Clone(original);cached.Id="12";cached.TableName=Prefix+"_cached";cached.IsRongyu=1;c.ConfigTables.Add(cached);
        var map=new Dictionary<string,string>(StringComparer.Ordinal);foreach(var column in c.ConfigColumns.Where(x=>x.ConfigTableId=="2").ToArray()){var copy=Clone(column);copy.Id=Add(column.Id);copy.ConfigTableId="12";map.Add(column.Id,copy.Id);c.ConfigColumns.Add(copy);}var page=Clone(c.ConfigPages.Single(x=>x.Id=="201"));page.Id="1201";page.ConfigTableId="12";c.ConfigPages.Add(page);foreach(var field in c.ConfigPageColumns.Where(x=>x.ConfigPageId=="201").ToArray()){var copy=Clone(field);copy.Id="1201_"+field.Id;copy.ConfigPageId="1201";copy.ConfigColumnId=map[field.ConfigColumnId];c.ConfigPageColumns.Add(copy);}
        foreach(var row in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))row.YzProjectId=c.YzProjectId;foreach(var entry in c.ConfigPages){entry.YzAdminTypeId="5";entry.IsSave=1;if(!c.ConfigMenus.Any(m=>m.ConfigPageId==entry.Id))c.ConfigMenus.Add(new(){Id=(700000+int.Parse(entry.Id,CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture),YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="共享正式验收"+entry.Id,Kind="GENERATED_PAGE",ConfigPageId=entry.Id});}return c;
    }
    /// <summary>离线实际Full构建、更新及原组件保护通过后，才允许申请精确新库租约。</summary>
    [Theory][InlineData("plain")][InlineData("short")][InlineData("derived")][InlineData("short-derived")]
    public async Task Full_catalog_builds_before_lease_is_requested(string profile)
    {
        var c=Catalog(profile);var before=JsonSerializer.Serialize(c);var m=GenerationModel.Create(c,"Example.SharedFresh");Assert.Empty(GenerationCapabilityAudit.Collect(m));foreach(var id in new[]{"2","12"}){var t=m.Tables.Single(t=>t.Source.Id==id);var page=Assert.Single(t.Pages);Assert.Contains(page.Searchable,column=>column.JsonName==(Definition(profile).Shorter?"areaId":"placeId")&&column.IsFixedCrossTableFilter);var group=Assert.Single(SharedCrossTableCompilation.ForPage(m,t,page).Groups);Assert.Equal(2,group.Plans.Count);}var direct=RelationPropagationCompilation.Bindings(m).Where(b=>b.Source.Source.Id=="12").ToArray();var derived=RelationPropagationCompilation.DerivedBindings(m).Where(b=>b.Source.Source.Id=="12").ToArray();Assert.Equal(Definition(profile).Shorter?3:4,direct.Length+derived.Length);Assert.Equal(Definition(profile).Derived?1:Definition(profile).Shorter?3:4,direct.Length);Assert.Equal(Definition(profile).Derived?Definition(profile).Shorter?2:3:0,derived.Length);
        var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-fresh-catalog");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,m.Namespace,root).FrontendIncluded);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,m.Namespace,root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);Assert.Equal("unchanged",updater.Apply(plan,plan.PlanSha256).Status);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));Assert.Equal(before,JsonSerializer.Serialize(c));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>序列化复制隔离元数据字典，原来源不与缓存来源共享可变实例。</summary>
    private static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    /// <summary>来源元数据ID显式隔离，目标表／页面引用保持不变。</summary>
    private static string Add(string value)=>(10000+int.Parse(value,CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
}
