using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>固定跨表完整源码包及原Vue组件共同验证，包内正式认证与实际登录验收分开记录。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantCrossFullTests
{
    /// <summary>四种键类型包含双控件、逻辑父范围及文件，实际构建完整包并保护手写partial。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",false)]
    public async Task Full_cross_package_builds_updates_and_mounts_original_vue(string type,string parentType,bool files)
    {
        var catalog=Catalog(type,parentType,files);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CrossFull");Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var root=NonRedundantApplicationFixture.Root();
        try
        {
            var package=new ApplicationPackager().Create(catalog,output,model.Namespace,root);Assert.True(package.FrontendIncluded);Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
            Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));
            using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,".metarill-package.json")));Assert.Equal("web-database-v1",marker.RootElement.GetProperty("authenticationProfile").GetString());
            using var profile=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"backend/generated/generation-capabilities.json")));var paths=profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(item=>item.TryGetProperty("sourceMode",out var mode)&&mode.GetString()=="crossTable").ToArray();Assert.Equal(10,paths.Length);Assert.Equal(8,paths.Count(path=>path.GetProperty("sourceParentSupported").GetBoolean()));Assert.All(paths,path=>{Assert.True(path.GetProperty("partialSelectionSupported").GetBoolean());Assert.False(path.GetProperty("pathCellImportSupported").GetBoolean());Assert.False(path.GetProperty("pathCellExportSupported").GetBoolean());Assert.False(path.GetProperty("customPropertyAliasesSupported").GetBoolean());});
            var hand=Path.Combine(output,"backend/generated/Services/NrCrossFullSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 完整跨表应用的手写业务保护标记。\n");var protectedBytes=await File.ReadAllBytesAsync(hand);
            var next=Catalog(type,parentType,files);next.ConfigColumns.Single(column=>column.Id=="22").Name="更新记录标签";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(next,candidate,model.Namespace,root);
            var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(protectedBytes,await File.ReadAllBytesAsync(hand));var repeated=updater.Preview(output,candidate,actions);Assert.True(repeated.CanApply);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var evidence=Path.Combine(root,".local/nonredundant-cross-table-full");Directory.CreateDirectory(evidence);
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))foreach(var kind in new[]{"list","detail"}){var path="pages/yzp/nr_cross_full_source-"+kind+"-"+page.Id+".vue";Assert.Equal(File.ReadAllBytes(Path.Combine(output,"frontend",path)),File.ReadAllBytes(Path.Combine(output,"backend/generated/frontend",path)));}
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"NonRedundantCrossFullVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));await NonRedundantApplicationFixture.RunAsync(probe);Assert.Equal(before,JsonSerializer.Serialize(catalog));await File.WriteAllTextAsync(Path.Combine(evidence,"package-"+type+"-"+parentType+"-"+files+".json"),JsonSerializer.Serialize(new{type,parentType,files,fullApplication=true,formalAuthenticationProfile="web-database-v1",realPackageBuild=true,originalComponentByteIdentical=true,originalPageCopiesIdentical=true,originalVueMounted=true,handwrittenProtected=true,repeatUpdateUnchanged=true,canonicalPropertiesAndNullFallback=parentType=="string",formalLoginExecuted=false,browserExecuted=false})+"\n");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>实际路径Excel用途、隐藏坏链和未发布目标都必须在创建stage前拒绝。</summary>
    [Theory][InlineData("import")][InlineData("export")][InlineData("targetRealm")][InlineData("hiddenBad")][InlineData("targetGlobal")][InlineData("unknownContext")][InlineData("hiddenTargetRealm")][InlineData("targetTableRealm")][InlineData("idPropertyName")][InlineData("namePropertyName")][InlineData("propertyCase")][InlineData("propertyType")][InlineData("prefixEmpty")][InlineData("prefixUpper")][InlineData("keyWhitespace")][InlineData("joinerEmpty")][InlineData("joinerWhitespace")]
    public void Full_cross_keeps_actual_unproven_uses_and_target_realms_gated(string kind)
    {
        var catalog=Catalog("string","int64",false);var page=catalog.ConfigPages.Single(page=>page.Id=="201");
        if(kind.StartsWith("prefix",StringComparison.Ordinal)||kind is "keyWhitespace" or "joinerEmpty" or "joinerWhitespace")
        {
            var column=catalog.ConfigColumns.Single(column=>column.Id=="25");var json=JsonNode.Parse(column.ControlDetail)!;
            if(kind.StartsWith("prefix",StringComparison.Ordinal))
            {
                json["levels"]![0]!["columnPrefix"]=kind=="prefixEmpty"?"":"COUNTRY";var removed=catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"&&page.Id!="201").Select(page=>page.Id).ToHashSet();catalog.ConfigPages.RemoveAll(page=>removed.Contains(page.Id));catalog.ConfigPageColumns.RemoveAll(field=>removed.Contains(field.ConfigPageId));
            }
            else if(kind=="keyWhitespace")json["levels"]![0]!["key"]=" ";else json["displayJoiner"]=kind=="joinerEmpty"?"":" ";column.ControlDetail=json.ToJsonString();
        }
        if(kind is "idPropertyName" or "namePropertyName" or "propertyCase" or "propertyType")
        {
            var column=catalog.ConfigColumns.Single(column=>column.Id=="25");var json=JsonNode.Parse(column.ControlDetail)!;var entry=json["levels"]![0]!;
            if(kind=="propertyType")entry["idPropertyName"]=123;else entry[kind=="propertyCase"?"idPropertyName":kind]=kind=="propertyCase"?"CountryId":"unprovedAlias";column.ControlDetail=json.ToJsonString();
        }
        if(kind=="import"){page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");}
        if(kind=="export")catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25").IsShow=1;
        if(kind=="targetRealm")catalog.ConfigPages.Single(page=>page.Id=="301").YzProjectId="";
        if(kind=="hiddenBad"){catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="80");catalog.ConfigColumns.Single(column=>column.Id=="80").ControlDetail="{\"sourceMode\":\"crossTable\",\"levels\":[]}";}
        if(kind=="hiddenTargetRealm"){catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "25" or "80");catalog.ConfigPages.Single(page=>page.Id=="301").YzProjectId="";}
        if(kind=="targetTableRealm")catalog.ConfigTables.Single(table=>table.Id=="3").YzProjectId="";
        if(kind=="targetGlobal")catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="unknownContext")catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql+=" AND status=@unknown";
        var output=Path.Combine(Path.GetTempPath(),"metarill-cross-full-gate",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,"Example.CrossFullGate",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));if(Directory.Exists(Path.GetDirectoryName(output)))Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(output)!,Path.GetFileName(output)+".staging-*"));
    }
    /// <summary>只读跨表不在原Excel可写表头，已有标量导入仍能随完整包交付。</summary>
    [Fact]
    public async Task Readonly_cross_keeps_scalar_import_in_full_package_without_opening_path_cells()
    {
        var catalog=Catalog("string","int64",false);catalog.ConfigPages.Single(page=>page.Id=="201").IsImport=1;
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201"&&field.ConfigColumnId is "25" or "80"))field.Extra["isReadOnly"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,"Example.ReadonlyCrossFull");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single(page=>page.Source.Id=="201");Assert.DoesNotContain(ImportRenderer.Columns(page),column=>column.Source.ControlKind=="CascadeTree");Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-readonly-cross-full");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{Assert.True(new ApplicationPackager().Create(catalog,output,model.Namespace,NonRedundantApplicationFixture.Root()).FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);}
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>验收模型未配置路径导入，保留原标量导出；不改平台元数据或现有项目按钮。</summary>
    internal static MetadataCatalog Catalog(string type,string parentType,bool files)
    {
        var catalog=NonRedundantCrossParentTests.Catalog("nr_cross_full",type,parentType);foreach(var page in catalog.ConfigPages){page.IsImport=0;page.Extra.Remove("importMethod");}RedundantMultiNameCacheTests.Published(catalog);
        // 两组显式同名属性与null回退也挂载原组件，不能通过禁止所有属性声明来掩盖不匹配。
        if(parentType=="string")foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId=="2"&&column.ControlKind=="CascadeTree"))
        {
            var json=JsonNode.Parse(column.ControlDetail)!;var levels=json["levels"]!.AsArray();for(var index=0;index<levels.Count;index++){var entry=levels[index]!;var prefix=entry["columnPrefix"]!.GetValue<string>();entry["idPropertyName"]=index==levels.Count-1?null:JsonValue.Create(GenerationModel.Camel(prefix+"_id"));entry["namePropertyName"]=index==levels.Count-1?null:JsonValue.Create(GenerationModel.Camel(prefix+"_name"));}column.ControlDetail=json.ToJsonString();
        }
        if(files){catalog.ConfigColumns.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",SortNo=10000,ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))catalog.ConfigPageColumns.Add(new(){Id=page.Id+"9001",YzProjectId=catalog.YzProjectId,ConfigPageId=page.Id,ConfigColumnId="9001",IsShow=1,IsEdit=1,SortNo=40});}return catalog;
    }
}
