using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>来源表可在固定链重复出现，各角色使用独立的真实父键和稀疏选择位。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RepeatedCrossSourceTests
{
    /// <summary>根中／根末／中末／全三角色，四种键及物理／派生名称均生成全部消费者。</summary>
    [Theory]
    [InlineData(3,"string","string",false)]
    [InlineData(3,"string","string",true)]
    [InlineData(3,"string","int64",false)]
    [InlineData(3,"string","int64",true)]
    [InlineData(3,"int64","string",false)]
    [InlineData(3,"int64","string",true)]
    [InlineData(3,"int64","int64",false)]
    [InlineData(3,"int64","int64",true)]
    [InlineData(5,"string","string",false)]
    [InlineData(5,"string","string",true)]
    [InlineData(5,"string","int64",false)]
    [InlineData(5,"string","int64",true)]
    [InlineData(5,"int64","string",false)]
    [InlineData(5,"int64","string",true)]
    [InlineData(5,"int64","int64",false)]
    [InlineData(5,"int64","int64",true)]
    [InlineData(6,"string","string",false)]
    [InlineData(6,"string","string",true)]
    [InlineData(6,"string","int64",false)]
    [InlineData(6,"string","int64",true)]
    [InlineData(6,"int64","string",false)]
    [InlineData(6,"int64","string",true)]
    [InlineData(6,"int64","int64",false)]
    [InlineData(6,"int64","int64",true)]
    [InlineData(7,"string","string",false)]
    [InlineData(7,"string","string",true)]
    [InlineData(7,"string","int64",false)]
    [InlineData(7,"string","int64",true)]
    [InlineData(7,"int64","string",false)]
    [InlineData(7,"int64","string",true)]
    [InlineData(7,"int64","int64",false)]
    [InlineData(7,"int64","int64",true)]
    public async Task Repeated_roles_compile_without_merging_logical_and_stored_ids(int mask,string type,string parentType,bool derived)
    {
        var c=Catalog("nr_repeat",mask,type,parentType,derived);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.RepeatedSource");var table=model.Tables.Single(x=>x.Source.Id=="2");var page=Assert.Single(table.Pages);var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));Assert.Equal(Roles(mask).Length,plan.Levels.Count(x=>x.Read.Target.Source.Id=="2"));Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-repeat-source");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>同一表的首个来源角色读业务标识，其余角色读固定派生名称，不能把不同角色别名合并。</summary>
    [Theory]
    [InlineData(3,"string","string")]
    [InlineData(3,"string","int64")]
    [InlineData(3,"int64","string")]
    [InlineData(3,"int64","int64")]
    [InlineData(5,"string","string")]
    [InlineData(5,"string","int64")]
    [InlineData(5,"int64","string")]
    [InlineData(5,"int64","int64")]
    [InlineData(6,"string","string")]
    [InlineData(6,"string","int64")]
    [InlineData(6,"int64","string")]
    [InlineData(6,"int64","int64")]
    [InlineData(7,"string","string")]
    [InlineData(7,"string","int64")]
    [InlineData(7,"int64","string")]
    [InlineData(7,"int64","int64")]
    public async Task Mixed_role_names_compile_without_alias_or_dependency_collision(int mask,string type,string parentType)
    {
        var c=Catalog("nr_repeat",mask,type,parentType,"mixed");var model=GenerationModel.Create(c,"Example.RepeatedMixedSource");var table=model.Tables.Single(x=>x.Source.Id=="2");var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,table,Assert.Single(table.Pages)));Assert.False(plan.Levels[Roles(mask)[0]].Read.DerivedName);Assert.All(Roles(mask).Skip(1),index=>Assert.True(plan.Levels[index].Read.DerivedName));var boundary=Path.Combine(Path.GetTempPath(),"metarill-repeat-mixed-source");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Empty(GenerationCapabilityAudit.Collect(model));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>四种角色图及三种名称模式的Full使用原组件、实际Foundation构建及原更新动作。</summary>
    [Theory]
    [InlineData(3,"physical")]
    [InlineData(3,"derived")]
    [InlineData(3,"mixed")]
    [InlineData(5,"physical")]
    [InlineData(5,"derived")]
    [InlineData(5,"mixed")]
    [InlineData(6,"physical")]
    [InlineData(6,"derived")]
    [InlineData(6,"mixed")]
    [InlineData(7,"physical")]
    [InlineData(7,"derived")]
    [InlineData(7,"mixed")]
    public async Task Full_repeated_roles_preserve_original_component_and_extensions(int mask,string mode)
    {
        var c=Catalog("rr_source_full",mask,"string","int64",mode);var boundary=Path.Combine(Path.GetTempPath(),"metarill-repeat-source-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var root=NonRedundantApplicationFixture.Root();
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.RepeatSourceFull",root).FrontendIncluded);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));
            using(var json=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var cap=Assert.Single(json.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),x=>x.TryGetProperty("sourceMode",out var value)&&value.GetString()=="crossTable");Assert.True(cap.GetProperty("sameSourceTableSupported").GetBoolean());Assert.True(cap.GetProperty("repeatedSourceRolesSupported").GetBoolean());Assert.Equal(mode!="physical",cap.GetProperty("derivedNameSupported").GetBoolean());}
            var hand=Path.Combine(output,"backend/generated/Services/RrSourceFullSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 重复来源角色的手写扩展保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var next=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(c))!;next.ConfigColumns.Single(x=>x.Id=="22").Name="来源业务标识";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(next,candidate,"Example.RepeatSourceFull",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"RepeatedCrossSourceVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(mode);probe.ArgumentList.Add(mask.ToString(System.Globalization.CultureInfo.InvariantCulture));await NonRedundantApplicationFixture.RunAsync(probe);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>R1物理名称重复角色须独立证明并登记缓存，不能误用R0稀疏存储。</summary>
    [Fact]
    public void R1_repeated_source_uses_explicit_physical_role_proof()
    {
        var c=Catalog("repeat_r1",7,"string","int64",false);c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;var model=GenerationModel.Create(c,"Example.RepeatR1Gap");var table=model.Tables.Single(x=>x.Source.Id=="2");var result=CrossTableWriteCompilation.Resolve(model,table,Assert.Single(table.Pages),table.Columns.Single(x=>x.Source.Id=="25"));Assert.NotNull(result.Definition);Assert.Equal(3,RelationPropagationCompilation.Bindings(model).Count);
    }
    /// <summary>重复角色不能打开共享槽位、逻辑父、全局、凭据及未确认路径Excel。</summary>
    [Theory][InlineData("sparseParent")][InlineData("sharedSlots")][InlineData("derivedParent")][InlineData("global")][InlineData("credential")][InlineData("sourceCondition")][InlineData("extra")][InlineData("pathImport")]
    public void Invalid_repeated_role_uses_reject_full_before_staging(string kind)
    {
        var c=Catalog("repeat_bad",7,"string","int64","derived");var control=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(control.ControlDetail)!;
        if(kind=="sparseParent")json["levels"]![1]!["parentField"]="country_id";
        if(kind=="derivedParent"){var parent=c.ConfigColumns.Single(x=>x.ConfigTableId=="2"&&x.ColumnName=="repeat_bad_country_id");parent.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);parent.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="62").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="92").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="sourceCondition")c.ConfigPages.Single(x=>x.Id=="201").WhereSql+=" AND country_id='9007199254745001'";
        if(kind=="extra")json["levels"]![1]!["whereSql"]="status=1";
        if(kind=="pathImport")c.ConfigPages.Single(x=>x.Id=="201").IsImport=1;
        control.ControlDetail=json.ToJsonString();
        if(kind=="sharedSlots"){var second=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(control))!;second.Id="199";second.ColumnName="secondary_place_id";var shared=JsonNode.Parse(second.ControlDetail)!;shared["levels"]![2]!["columnPrefix"]="secondary_place";second.ControlDetail=shared.ToJsonString();c.ConfigColumns.Add(second);c.ConfigPageColumns.Add(new(){Id="201199",YzProjectId=c.YzProjectId,ConfigPageId="201",ConfigColumnId="199"});}
        var output=Path.Combine(Path.GetTempPath(),"metarill-repeat-source-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.RepeatSourceReject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>混合名称仅改变对应角色的nameField，元数据中的独立业务标识与联接值保持。</summary>
    internal static MetadataCatalog Catalog(string prefix,int mask,string type,string parentType,string mode)
    {
        if(mode is not("physical" or "derived" or "mixed"))throw new ArgumentOutOfRangeException(nameof(mode));var c=Catalog(prefix,mask,type,parentType,mode!="physical");if(mode=="mixed"){var control=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(control.ControlDetail)!;json["levels"]![Roles(mask)[0]]!["nameField"]="name";control.ControlDetail=json.ToJsonString();}return c;
    }
    /// <summary>掩码只描述固定三层角色，不接受请求传入动态层目录。</summary>
    internal static int[] Roles(int mask)=>mask is 3 or 5 or 6 or 7?Enumerable.Range(0,3).Where(index=>(mask&(1<<index))!=0).ToArray():throw new ArgumentOutOfRangeException(nameof(mask));
    /// <summary>沿已发布单来源模型增添来源角色，各真实父字段与country/area/place_id始终分离。</summary>
    internal static MetadataCatalog Catalog(string prefix,int mask,string type,string parentType,bool derived)
    {
        var roles=Roles(mask);var c=NonRedundantCrossSourceTests.Catalog(prefix,roles[0],type,parentType,derived);var control=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(control.ControlDetail)!;
        foreach(var index in roles)
        {
            json["levels"]![index]!["table"]=prefix+"_source";if(derived)json["levels"]![index]!["nameField"]="display_label";
            if(index>0)
            {
                var name=prefix+"_"+new[]{"country","area"}[index-1]+"_id";var field=c.ConfigColumns.SingleOrDefault(x=>x.ConfigTableId=="2"&&x.ColumnName==name);
                if(field is null){var id=(188+index).ToString(System.Globalization.CultureInfo.InvariantCulture);field=new(){Id=id,YzProjectId=c.YzProjectId,ConfigTableId="2",ColumnName=name,LogicalType=parentType};c.ConfigColumns.Add(field);c.ConfigPageColumns.Add(new(){Id="201"+id,YzProjectId=c.YzProjectId,ConfigPageId="201",ConfigColumnId=id});}
                field.LinkConfigTableId=roles.Contains(index-1)?"2":(index+2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if(index==roles[0])continue;var removed=(index+3).ToString(System.Globalization.CultureInfo.InvariantCulture);foreach(var field in c.ConfigColumns.Where(x=>x.LinkConfigTableId==removed))field.LinkConfigTableId="2";var pages=c.ConfigPages.Where(x=>x.ConfigTableId==removed).Select(x=>x.Id).ToHashSet(StringComparer.Ordinal);c.ConfigTables.RemoveAll(x=>x.Id==removed);c.ConfigColumns.RemoveAll(x=>x.ConfigTableId==removed);c.ConfigPages.RemoveAll(x=>pages.Contains(x.Id));c.ConfigPageColumns.RemoveAll(x=>pages.Contains(x.ConfigPageId));
        }
        control.ControlDetail=json.ToJsonString();control.LinkConfigTableId=roles.Contains(2)?"2":"5";return c;
    }
}
