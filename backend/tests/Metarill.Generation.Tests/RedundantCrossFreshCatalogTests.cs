using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1正式模型先离线证明完整包，再申请专属空库；不修改真实平台配置。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantCrossFreshCatalogTests
{
    /// <summary>四角色图交叉四键；角色独立物理名称与文件由同一完整包发布。</summary>
    [Theory][InlineData("cross3")][InlineData("cross5")][InlineData("cross6")][InlineData("cross7")]
    public async Task Published_redundant_models_package_without_mutating_metadata(string profile)
    {
        var c=Catalog(profile);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.RedundantCrossFresh");Assert.Empty(GenerationCapabilityAudit.Collect(model));var boundary=Path.Combine(Path.GetTempPath(),"metarill-redundant-cross-fresh-catalog");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{Assert.True(new ApplicationPackager().Create(c,output,model.Namespace,NonRedundantApplicationFixture.Root()).FrontendIncluded);Assert.Equal(before,JsonSerializer.Serialize(c));}
        finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>类型、角色及物理名称固定于发布目录，请求不能选择查询图。</summary>
    internal static (string Type,string ParentType,int Mask,bool Custom) Definition(string profile)=>profile switch
    {
        "cross3"=>("string","string",3,false),"cross5"=>("string","int64",5,true),"cross6"=>("int64","string",6,false),"cross7"=>("int64","int64",7,true),_=>throw new ArgumentOutOfRangeException(nameof(profile))
    };
    /// <summary>沿已证明R1完整包增补保护文件；父键保持真实物理字段，不复用受管缓存槽。</summary>
    internal static MetadataCatalog Catalog(string profile)
    {
        var d=Definition(profile);var c=RedundantCrossFullTests.Catalog("repeat"+d.Mask,d.Custom);var control=c.ConfigColumns.Single(x=>x.Id=="25");control.LogicalType=d.Type;if(d.Type=="string")control.ColumnLength="64";
        using var json=JsonDocument.Parse(control.ControlDetail);foreach(var level in json.RootElement.GetProperty("levels").EnumerateArray())if(level.TryGetProperty("parentField",out var parent)&&!string.IsNullOrEmpty(parent.GetString()))
        {
            var table=c.ConfigTables.Single(x=>x.TableName==level.GetProperty("table").GetString());var column=c.ConfigColumns.Single(x=>x.ConfigTableId==table.Id&&x.ColumnName==parent.GetString());column.LogicalType=d.ParentType;if(d.ParentType=="string")column.ColumnLength="64";
        }
        c.ConfigMenus.Clear();c.ConfigColumns.Add(new(){Id="9001",YzProjectId=c.YzProjectId,ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});c.ConfigPageColumns.Add(new(){Id="2019001",YzProjectId=c.YzProjectId,ConfigPageId="201",ConfigColumnId="9001",IsShow=1,IsEdit=1,SortNo=10000});
        var status=c.ConfigColumns.Single(x=>x.ConfigTableId=="2"&&x.ColumnName=="status");c.ConfigPages.Add(new(){Id="211",YzProjectId=c.YzProjectId,YzAdminTypeId="5",ConfigTableId="2",IsSave=1,WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="211status",YzProjectId=c.YzProjectId,ConfigPageId="211",ConfigColumnId=status.Id});foreach(var page in c.ConfigPages){page.IsSave=1;c.ConfigMenus.Add(new(){Id=(700000+int.Parse(page.Id,CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture),YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="R1正式验收"+page.Id,Kind="GENERATED_PAGE",ConfigPageId=page.Id});}return c;
    }
}
