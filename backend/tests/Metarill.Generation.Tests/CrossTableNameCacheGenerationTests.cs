using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原R1跨表名称在同一生成计划中登记传播和维护，不靠生成后补丁。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableNameCacheGenerationTests
{
    /// <summary>默认／自定义物理名称和四种键都编译实际控制器、服务及静态标量缓存目录。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public async Task Generated_cross_cache_plan_compiles_without_rewriting_metadata(string type,string parentType,bool custom)
    {
        var c=Catalog("cross_cache",type,parentType,custom);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.CrossNameCache");var bindings=RelationPropagationCompilation.Bindings(model);Assert.Equal(3,bindings.Count);Assert.All(bindings,b=>{Assert.False(b.Multiple);Assert.Equal("25",b.CrossTableColumnId);Assert.NotNull(b.CrossTableLevelKey);});var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-name-cache");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var registration=await File.ReadAllTextAsync(Path.Combine(directory,"generated/GeneratedRegistration.g.cs"));Assert.Contains("crossTableColumnId",registration);Assert.Contains("RelationMaintenanceCatalog.Compile",registration);using var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"generated/generation-capabilities.json")));var cap=Assert.Single(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),x=>x.GetProperty("controlKind").GetString()=="CascadeTree");Assert.True(cap.GetProperty("renamePropagationSupported").GetBoolean());Assert.False(cap.GetProperty("fullApplicationAccepted").GetBoolean());Assert.Equal(before,JsonSerializer.Serialize(c));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>R0实时投影不物化；新证明的R1重复角色按独立槽位登记。</summary>
    [Fact]
    public void Nonredundant_has_no_cache_and_proven_repeated_roles_use_distinct_bindings()
    {
        var c=NonRedundantCrossSourceTests.Catalog("cross_r0",0,"string","int64",false);Assert.Empty(RelationPropagationCompilation.Bindings(GenerationModel.Create(c,"Example.R0NoCache")));var repeated=RepeatedCrossSourceTests.Catalog("cross_repeat",7,"string","int64",false);repeated.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;var bindings=RelationPropagationCompilation.Bindings(GenerationModel.Create(repeated,"Example.R1RepeatRoles"));Assert.Equal(3,bindings.Count);Assert.Equal(3,bindings.Select(x=>x.CrossTableLevelKey).Distinct().Count());
    }
    /// <summary>全局目标仍有独立缺口，不因普通R1计划被偷偷赋予缓存传播能力。</summary>
    [Fact]
    public void Global_target_keeps_read_proof_but_does_not_publish_partial_name_propagation()
    {
        var c=Catalog("global_cross","string","int64",false);c.ConfigTables.Single(x=>x.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");var m=GenerationModel.Create(c,"Example.GlobalCrossGap");var t=m.Tables.Single(x=>x.Source.Id=="2");Assert.NotNull(CrossTableWriteCompilation.Resolve(m,t,Assert.Single(t.Pages),t.Columns.Single(x=>x.Source.Id=="25")).Definition);Assert.Empty(RelationPropagationCompilation.Bindings(m));
    }
    /// <summary>默认列名保持原协议，只改变显式目标nameField，不把caption写成新前端字段。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parentType,bool custom)
    {
        var c=CrossTableReadTests.Catalog(prefix,parentType,parentType);c.ConfigColumns.Single(x=>x.Id=="25").LogicalType=type;c.ConfigPages.Single(x=>x.Id=="201").IsCopy=1;foreach(var page in c.ConfigPages)page.IsSave=1;foreach(var column in c.ConfigColumns.Where(x=>x.ConfigTableId!="2"&&x.ColumnName=="name"))column.ColumnLength="100";if(custom){var source=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(source.ControlDetail)!;foreach(var level in json["levels"]!.AsArray()){level!["nameField"]="caption";var table=c.ConfigTables.Single(t=>t.TableName==level["table"]!.GetValue<string>());c.ConfigColumns.Single(x=>x.ConfigTableId==table.Id&&x.ColumnName=="name").ColumnName="caption";}source.ControlDetail=json.ToJsonString();}return c;
    }
}
