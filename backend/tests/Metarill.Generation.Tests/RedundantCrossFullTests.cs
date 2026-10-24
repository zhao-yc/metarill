using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>完整包执行真实原页面／组件、包内Foundation构建及原在线更新动作，不用替代页面。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantCrossFullTests
{
    /// <summary>独立链、来源单层／重复角色、独立名称、双控件及隐藏用途分别构建和更新。</summary>
    [Theory]
    [InlineData("independent",false)][InlineData("independent",true)]
    [InlineData("source0",false)][InlineData("source1",false)][InlineData("source2",false)]
    [InlineData("repeat3",false)][InlineData("repeat5",false)][InlineData("repeat6",false)][InlineData("repeat7",false)]
    [InlineData("repeat3",true)][InlineData("repeat5",true)][InlineData("repeat6",true)][InlineData("repeat7",true)]
    [InlineData("double",false)][InlineData("hidden",false)]
    public async Task Full_package_builds_updates_and_keeps_original_vue(string kind,bool custom)
    {
        var c=Catalog(kind,custom);var before=JsonSerializer.Serialize(c);var root=NonRedundantApplicationFixture.Root();var model=GenerationModel.Create(c,"Example.RedundantCrossFull");Assert.Empty(GenerationCapabilityAudit.Collect(model));var boundary=Path.Combine(Path.GetTempPath(),"metarill-redundant-cross-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var package=new ApplicationPackager().Create(c,output,model.Namespace,root);Assert.True(package.FrontendIncluded);Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);foreach(var path in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/detailLayoutRuntime.js","common/yzp_runtime/originalListOptionsMixin.js"})Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root,"frontend/metarill_uni_admin",path)),await File.ReadAllBytesAsync(Path.Combine(output,"frontend",path)));
            using(var caps=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var controls=caps.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(x=>x.TryGetProperty("sourceMode",out var mode)&&mode.GetString()=="crossTable").ToArray();Assert.Equal(kind=="double"?2:1,controls.Length);Assert.All(controls,x=>{Assert.True(x.GetProperty("fullApplicationAccepted").GetBoolean());Assert.True(x.GetProperty("renamePropagationSupported").GetBoolean());});}
            var hand=Path.Combine(output,"backend/generated/Services/RrFullSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 完整包更新必须保护的手写扩展。\n");var bytes=await File.ReadAllBytesAsync(hand);var next=JsonSerializer.Deserialize<MetadataCatalog>(before)!;next.ConfigColumns.Single(x=>x.Id=="22").Name="来源业务标识";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(next,candidate,model.Namespace,root);var updater=new LocalGenerationUpdater();var update=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(update.CanApply);updater.Apply(update,update.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            if(kind=="hidden"){var page=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/rr_full_source-detail-201.vue"));Assert.DoesNotContain("<yzp-cascade-tree",page);var vo=await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/VOs/RrFullSourcePage201Vo.g.cs"));Assert.DoesNotContain(" PlaceId ",vo);}else{var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"RedundantCrossFullVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(kind);probe.ArgumentList.Add(custom.ToString());await NonRedundantApplicationFixture.RunAsync(probe);}
            Assert.Equal(before,JsonSerializer.Serialize(c));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>坏定义与未证明用途在任何stage创建前整体拒绝，隐藏字段也不能绕过。</summary>
    [Theory][InlineData("unpublished")][InlineData("foreignRealm")][InlineData("global")][InlineData("nonredundant")][InlineData("badParent")][InlineData("pageOverride")][InlineData("idAlias")][InlineData("nameAlias")][InlineData("emptyPrefix")][InlineData("pathImport")][InlineData("pathExport")][InlineData("legacyExport")][InlineData("hiddenBad")][InlineData("virtual")][InlineData("hiddenVirtual")][InlineData("hiddenVirtualR0")][InlineData("badCompanion")][InlineData("sourceParent")]
    public async Task Unproven_use_rejects_full_atomically(string kind)
    {
        var c=Catalog(kind=="badCompanion"?"double":"independent",false);var control=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(control.ControlDetail)!;
        if(kind=="unpublished")foreach(var page in c.ConfigPages)page.YzAdminTypeId="";
        if(kind=="foreignRealm")c.ConfigPages.Single(x=>x.Id=="301").YzProjectId="other";
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="nonredundant")c.ConfigTables.Single(x=>x.Id=="3").IsRongyu=0;
        if(kind=="badParent")json["levels"]![1]!["parentField"]="id";
        if(kind=="idAlias")json["levels"]![0]!["idPropertyName"]="differentId";
        if(kind=="nameAlias")json["levels"]![0]!["namePropertyName"]="differentName";
        if(kind=="emptyPrefix")json["levels"]![1]!["columnPrefix"]="";
        if(kind=="pathImport")c.ConfigPages.Single(x=>x.Id=="201").IsImport=1;
        if(kind=="pathExport")c.ConfigPages.Single(x=>x.Id=="201").IsExport=1;
        if(kind=="legacyExport")c.ConfigPages.Single(x=>x.Id=="201").Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        if(kind=="hiddenBad"){c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId=="25");json["levels"]![1]!["whereSql"]="status=1";}
        if(kind is "virtual" or "hiddenVirtual" or "hiddenVirtualR0")control.IsVirtualColumn=1;if(kind is "hiddenVirtual" or "hiddenVirtualR0")c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId=="25");if(kind=="hiddenVirtualR0")c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=0;
        if(kind=="badCompanion"){var other=c.ConfigColumns.Single(x=>x.Id=="80");var second=JsonNode.Parse(other.ControlDetail)!;second["levels"]![1]!["whereSql"]="status=1";other.ControlDetail=second.ToJsonString();c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId=="80");}
        if(kind=="sourceParent")c.ConfigPages.Single(x=>x.Id=="201").WhereSql+=" AND country_id=@parent_id";
        control.ControlDetail=json.ToJsonString();if(kind=="pageOverride"){json["displayJoiner"]=" / ";c.ConfigPageColumns.Single(x=>x.ConfigColumnId=="25").Extra["controlDetail"]=JsonSerializer.SerializeToElement(json.ToJsonString());}
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-redundant-full-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.RedundantFullReject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));if(Directory.Exists(boundary))Assert.Empty(Directory.EnumerateDirectories(boundary,Path.GetFileName(output)+".staging-*"));}finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>只组合现有真实目录，页面／控制器／菜单明确归属包内项目，不改原组件的属性或交互。</summary>
    internal static MetadataCatalog Catalog(string kind,bool custom)
    {
        const string prefix="rr_full";MetadataCatalog c=kind.StartsWith("repeat",StringComparison.Ordinal)?RedundantRepeatedCrossSourceTests.Catalog(prefix,int.Parse(kind[6..],System.Globalization.CultureInfo.InvariantCulture),"string","int64",custom):kind.StartsWith("source",StringComparison.Ordinal)?CrossTableSourceGraphTests.Catalog(prefix,int.Parse(kind[6..],System.Globalization.CultureInfo.InvariantCulture),"string","int64"):CrossTableNameCacheGenerationTests.Catalog(prefix,"string","int64",custom);
        if(kind=="double"){var source=c.ConfigColumns.Single(x=>x.Id=="25");var second=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(source))!;second.Id="80";second.ColumnName="secondary_place_id";var json=JsonNode.Parse(source.ControlDetail)!;foreach(var level in json["levels"]!.AsArray())level!["columnPrefix"]="secondary_"+level!["columnPrefix"]!.GetValue<string>();second.ControlDetail=json.ToJsonString();c.ConfigColumns.Add(second);c.ConfigPageColumns.Add(new(){Id="20180",ConfigPageId="201",ConfigColumnId="80",IsShow=0,Extra=new(){["isDetailShow"]=JsonSerializer.SerializeToElement(1)}});}
        if(kind=="hidden")c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId=="25");
        foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;if(!c.YzAdminTypes.Any(x=>x.Id=="5"))c.YzAdminTypes.Add(new(){Id="5",YzProjectId=c.YzProjectId});foreach(var page in c.ConfigPages)page.YzAdminTypeId="5";c.ConfigMenus.Add(new(){Id="9001",YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="原跨表页面",Kind="GENERATED_PAGE",ConfigPageId="201"});return c;
    }
}
