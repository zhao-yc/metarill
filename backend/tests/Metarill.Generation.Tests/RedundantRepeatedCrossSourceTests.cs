using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1重复来源只由固定层角色、独立真实父键和已证明物理缓存接通。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantRepeatedCrossSourceTests
{
    /// <summary>四种角色图和四种键实际编译；额外名称模型证明同一实体的不同名称字段不合并。</summary>
    [Theory]
    [InlineData(3,"string","string",false)]
    [InlineData(3,"string","int64",false)]
    [InlineData(3,"int64","string",false)]
    [InlineData(3,"int64","int64",false)]
    [InlineData(5,"string","string",false)]
    [InlineData(5,"string","int64",false)]
    [InlineData(5,"int64","string",false)]
    [InlineData(5,"int64","int64",false)]
    [InlineData(6,"string","string",false)]
    [InlineData(6,"string","int64",false)]
    [InlineData(6,"int64","string",false)]
    [InlineData(6,"int64","int64",false)]
    [InlineData(7,"string","string",false)]
    [InlineData(7,"string","int64",false)]
    [InlineData(7,"int64","string",false)]
    [InlineData(7,"int64","int64",false)]
    [InlineData(3,"string","int64",true)]
    [InlineData(5,"string","int64",true)]
    [InlineData(6,"string","int64",true)]
    [InlineData(7,"string","int64",true)]
    public async Task Physical_roles_compile_with_distinct_caches_and_stored_parent_verification(int mask,string type,string parentType,bool custom)
    {
        var c=Catalog("rr_physical",mask,type,parentType,custom);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.RedundantRepeat");var table=model.Tables.Single(x=>x.Source.Id=="2");var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,table,Assert.Single(table.Pages)));Assert.Equal(RepeatedCrossSourceTests.Roles(mask).Length,plan.Levels.Count(x=>x.Read.Target.Source.Id=="2"));var bindings=RelationPropagationCompilation.Bindings(model);Assert.Equal(3,bindings.Count);Assert.All(bindings,x=>{Assert.False(x.Multiple);Assert.Equal("25",x.CrossTableColumnId);});Assert.Equal(3,bindings.Select(x=>x.CrossTableLevelKey).Distinct().Count());
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-redundant-repeat");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/RrPhysicalSourceService.g.cs"));Assert.Contains("跨表来源结构外键在存储后",code);using var caps=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"generated/generation-capabilities.json")));var cap=Assert.Single(caps.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),x=>x.TryGetProperty("sourceMode",out var mode)&&mode.GetString()=="crossTable");Assert.True(cap.GetProperty("repeatedSourceRolesSupported").GetBoolean());Assert.True(cap.GetProperty("renamePropagationSupported").GetBoolean());Assert.False(cap.GetProperty("fullApplicationAccepted").GetBoolean());Assert.Equal(before,JsonSerializer.Serialize(c));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>不把真实父字段、名称、页级角色或来源条件与受管槽位混用。</summary>
    [Theory][InlineData("managedParent")][InlineData("managedName")][InlineData("global")][InlineData("nonredundantTarget")][InlineData("pageOverride")][InlineData("sourceCondition")]
    public void Unproven_role_or_cache_overlap_keeps_the_write_gap(string kind)
    {
        var c=Catalog("rr_reject",kind=="nonredundantTarget"?3:7,"string","int64",false);var control=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(control.ControlDetail)!;
        if(kind=="managedParent")json["levels"]![1]!["parentField"]="country_id";
        if(kind=="managedName")json["levels"]![0]!["nameField"]="country_name";
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="nonredundantTarget")c.ConfigTables.Single(x=>x.Id=="5").IsRongyu=0;
        if(kind=="sourceCondition")c.ConfigPages.Single(x=>x.Id=="201").WhereSql+=" AND country_id='9007199254745001'";
        control.ControlDetail=json.ToJsonString();if(kind=="pageOverride"){json["displayJoiner"]=" / ";c.ConfigPageColumns.Single(x=>x.ConfigColumnId=="25").Extra["controlDetail"]=JsonSerializer.SerializeToElement(json.ToJsonString());}
        var model=GenerationModel.Create(c,"Example.RepeatedReject");var table=model.Tables.Single(x=>x.Source.Id=="2");Assert.Null(CrossTableWriteCompilation.Resolve(model,table,Assert.Single(table.Pages),table.Columns.Single(x=>x.Source.Id=="25")).Definition);
    }
    /// <summary>沿原R0角色目录只改存储模式；可为每个来源角色声明独立物理名称，原业务name保留。</summary>
    internal static MetadataCatalog Catalog(string prefix,int mask,string type,string parentType,bool custom=false)
    {
        var c=RepeatedCrossSourceTests.Catalog(prefix,mask,type,parentType,false);c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;
        if(custom){var control=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(control.ControlDetail)!;foreach(var role in RepeatedCrossSourceTests.Roles(mask)){var id=(280+role).ToString(System.Globalization.CultureInfo.InvariantCulture);var name=new[]{"root_label","middle_label","leaf_label"}[role];c.ConfigColumns.Add(new(){Id=id,ConfigTableId="2",ColumnName=name,LogicalType="string",ColumnLength="100"});c.ConfigPageColumns.Add(new(){Id="201"+id,ConfigPageId="201",ConfigColumnId=id});json["levels"]![role]!["nameField"]=name;}control.ControlDetail=json.ToJsonString();}
        return c;
    }
}
