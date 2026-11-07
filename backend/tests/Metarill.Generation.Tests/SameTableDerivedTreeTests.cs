using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>同表的直接父节点和独立书签使用同一真实树，名称可来自固定公司联接。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SameTableDerivedTreeTests
{
    /// <summary>直接父键两种类型，书签四种叶子／父键组合，分别验证物理和派生名称。</summary>
    [Theory]
    [InlineData("string","string",true,false)][InlineData("int64","int64",true,false)]
    [InlineData("string","string",true,true)][InlineData("int64","int64",true,true)]
    [InlineData("string","string",false,false)][InlineData("string","int64",false,false)][InlineData("int64","string",false,false)][InlineData("int64","int64",false,false)]
    [InlineData("string","string",false,true)][InlineData("string","int64",false,true)][InlineData("int64","string",false,true)][InlineData("int64","int64",false,true)]
    public async Task Same_table_plans_generate_and_compile_every_consumer(string type,string parentType,bool direct,bool derived)
    {
        var c=Catalog("same_derived",type,parentType,direct,derived);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.SameDerived");var table=model.Tables.Single(x=>x.Source.Id=="1");var page=table.Pages.Single(x=>x.Source.Id=="202");var plan=Assert.Single(CascadeTreeWriteCompilation.ForService(model,table,page));Assert.Equal(derived,plan.Read.DerivedName);Assert.Equal(table.Source.Id,plan.Read.Target.Source.Id);Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-same-derived-tree");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.SameDerived");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>四种完整包保持原组件、真实构建与手写partial的更新保护。</summary>
    [Theory][InlineData(true,false)][InlineData(true,true)][InlineData(false,false)][InlineData(false,true)]
    public async Task Full_same_table_package_keeps_original_component_and_extensions(bool direct,bool derived)
    {
        var c=FullCatalog(direct,derived);var boundary=Path.Combine(Path.GetTempPath(),"metarill-same-tree-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var root=NonRedundantApplicationFixture.Root();
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.SameFull",root).FrontendIncluded);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));
            using(var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var controls=capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(x=>x.GetProperty("controlKind").GetString()=="CascadeTree").ToArray();Assert.Equal(direct?1:3,controls.Length);Assert.All(controls,x=>{Assert.True(x.GetProperty("sameTableSupported").GetBoolean());Assert.Equal(derived,x.GetProperty("derivedNameSupported").GetBoolean());});}
            var hand=Path.Combine(output,"backend/generated/Services/SameFullNodeService.cs");await File.AppendAllTextAsync(hand,"\n// 同表树手写扩展保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var next=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(c))!;next.ConfigColumns.Single(x=>x.Id=="4").Name="节点业务标识";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(next,candidate,"Example.SameFull",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SameTableDerivedTreeVueProbe.mjs"));probe.ArgumentList.Add(output);probe.ArgumentList.Add(direct?"true":"false");await NonRedundantApplicationFixture.RunAsync(probe);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>Full物理合成要求表／字段跨类别ID唯一，基础单测旧目录的1／2不能与字段复用。</summary>
    private static MetadataCatalog FullCatalog(bool direct,bool derived)
    {
        var c=Catalog("same_full","string",direct?"string":"int64",direct,derived);var map=new Dictionary<string,string>{{"1","90001"},{"2","90002"}};foreach(var table in c.ConfigTables)table.Id=map[table.Id];foreach(var column in c.ConfigColumns){column.ConfigTableId=map[column.ConfigTableId];if(map.TryGetValue(column.LinkConfigTableId,out var target))column.LinkConfigTableId=target;if(column.ControlKind=="List"){var json=JsonNode.Parse(column.ControlDetail)!;json["tableId"]=column.LinkConfigTableId;column.ControlDetail=json.ToJsonString();}}foreach(var page in c.ConfigPages)page.ConfigTableId=map[page.ConfigTableId];return c;
    }
    /// <summary>同表不代表放开全局、凭据、多值、隐藏目标或派生结构／范围。</summary>
    [Theory][InlineData("sourceR1")][InlineData("global")][InlineData("credential")][InlineData("array")][InlineData("parent")][InlineData("condition")][InlineData("hidden")][InlineData("extra")]
    public void Unproven_same_table_uses_still_reject_full_before_staging(string kind)
    {
        var c=Catalog("same_bad","string","int64",false,true);
        if(kind=="sourceR1")c.ConfigTables.Single(x=>x.Id=="1").IsRongyu=1;
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="1").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="22").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="10").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(x=>x.Id=="22").ControlKind="FileLibrary";
        if(kind=="parent"){var parent=c.ConfigColumns.Single(x=>x.Id=="5");parent.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);parent.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(kind=="condition")c.ConfigPages.Single(x=>x.Id=="202").WhereSql+=" AND display_label='未证明范围'";
        if(kind=="hidden"){var field=c.ConfigPageColumns.Single(x=>x.ConfigPageId=="202"&&x.ConfigColumnId=="10");field.IsShow=0;field.IsEdit=0;}
        if(kind=="extra"){var field=c.ConfigColumns.Single(x=>x.Id=="6");var json=JsonNode.Parse(field.ControlDetail)!;json["selfTree"]!["whereSql"]="status=1";field.ControlDetail=json.ToJsonString();}
        var output=Path.Combine(Path.GetTempPath(),"metarill-same-tree-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.SameReject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>保留物理业务标识name，树专用display_label独立联接，能验证更新两者不会错误串名。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parentType,bool direct,bool derived)
    {
        var c=CascadeTreeSameRowTests.Catalog(prefix,type,parentType);c.ConfigTables.Single().IsRongyu=0;
        c.ConfigTables.Add(new(){Id="2",TableName=prefix+"_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="21",ConfigTableId="2",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="22",ConfigTableId="2",ColumnName="name",LogicalType="string"},new(){Id="23",ConfigTableId="2",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="24",ConfigTableId="2",ColumnName="delete_no",LogicalType="datetime"},new(){Id="11",ConfigTableId="1",ColumnName="company_id",LogicalType="int64",SortNo=100,LinkConfigTableId="2",ControlKind="List",ControlDetail="{\"tableId\":\"2\",\"nameField\":\"name\"}"}]);
        c.ConfigPages.Add(new(){Id="301",ConfigTableId="2",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="30122",ConfigPageId="301",ConfigColumnId="22"});
        foreach(var page in c.ConfigPages.Where(x=>x.ConfigTableId=="1"))c.ConfigPageColumns.Add(new(){Id=page.Id+"11",ConfigPageId=page.Id,ConfigColumnId="11",SortNo=100});
        var control=c.ConfigColumns.Single(x=>x.Id=="6");if(direct){c.ConfigColumns.Remove(control);c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId=="6");control=c.ConfigColumns.Single(x=>x.Id=="5");control.ControlKind="CascadeTree";control.ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table=prefix+"_node",parentField="parent_id",nameField="name"},displayJoiner="/"});c.ConfigPages.RemoveAll(x=>x.ConfigTableId=="1"&&x.Id!="202");c.ConfigPages.Single(x=>x.Id=="202").IsImport=1;c.ConfigPageColumns.RemoveAll(x=>x.ConfigPageId is "201" or "203");}
        if(derived){c.ConfigColumns.Add(new(){Id="10",ConfigTableId="1",ColumnName="display_label",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}});c.ConfigPageColumns.Add(new(){Id="20210",ConfigPageId="202",ConfigColumnId="10"});var json=JsonNode.Parse(control.ControlDetail)!;json["selfTree"]!["nameField"]="display_label";control.ControlDetail=json.ToJsonString();}
        return RedundantMultiNameCacheTests.Published(c);
    }
}
