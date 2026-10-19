using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定名称筛选只编译可证明的存储列；实际Query/JSON方言接线另行验收。</summary>
public sealed class SelectionNameFilterCompilationTests
{
    /// <summary>单值名称case真实加入临时C#项目编译，原Vue仍提交相同辅助键。</summary>
    [Fact]
    public async Task Scalar_name_binding_matches_original_vue_key_and_compiles_real_typed_case()
    {
        var catalog=ScalarCatalog();var model=GenerationModel.Create(catalog,"Example.NameFilters");var table=model.Tables.Single(table=>table.Source.Id=="1");var page=table.Pages.Single();var plan=SelectionNameFilterCompilation.ForPage(model,table,page);var binding=Assert.Single(plan.Bindings);Assert.Empty(plan.Gaps);Assert.Equal("groupName",binding.FilterKey);Assert.Equal("group_name",binding.StorageColumn.Source.ColumnName);Assert.Equal(SelectionNameFilterKind.ScalarLike,binding.Kind);Assert.True(plan.ConsumedKeys.SetEquals(["groupName"]));Assert.DoesNotContain("groupId",plan.ConsumedKeys);
        var cases=SelectionNameFilterCompilation.RenderCases(plan);Assert.Contains("entity.GroupName.Contains(value)",cases);Assert.Contains("JsonValueKind.String",cases);Assert.DoesNotContain("JsonSerializer.Deserialize<long",cases);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-name-filter-compiler");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"generated");
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.NameFilters");var detail=File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/customer-list-101.vue"));Assert.Contains("v-model='filters.groupName'",detail);
            await File.WriteAllTextAsync(Path.Combine(output,"Services/SelectionNameFilterCaseProbe.cs"),"using FreeSql; using System; using System.Text.Json; using System.Collections.Generic; using Example.NameFilters.Generated.Entities; namespace Example.NameFilters.Generated; public static class SelectionNameFilterCaseProbe { public static ISelect<Customer> Apply(ISelect<Customer> query,string key,JsonElement raw) { var filter=new KeyValuePair<string,JsonElement>(key,raw); switch(filter.Key){"+cases+"default:throw new ArgumentException();} return query; }}");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.NameFilters.Generated.SelectionNameFilterCaseProbe"));await RunProbe(output,"scalar");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>多选按已证明Names列给出JSON数组绑定，没有方言不得退成序列化文本LIKE。</summary>
    [Fact]
    public async Task Redundant_multi_names_use_shared_original_key_and_require_real_json_dialect()
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog();catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);var model=GenerationModel.Create(catalog,"Example.MultiNameFilters");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();var plan=SelectionNameFilterCompilation.ForPage(model,table,page);var binding=Assert.Single(plan.Bindings);Assert.Empty(plan.Gaps);Assert.Equal("targetNames",binding.FilterKey);Assert.Equal("27",binding.StorageColumn.Source.Id);Assert.Equal(SelectionNameFilterKind.JsonArrayAnyLike,binding.Kind);Assert.Throws<InvalidOperationException>(()=>SelectionNameFilterCompilation.RenderCases(plan));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-name-filter-compiler");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,output,"Example.MultiNameFilters");Assert.Contains("v-model='filters.targetNames'",File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/relation_source-list-201.vue")));await RunProbe(output,"multi");}
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>源字段显式允许筛选也不能替代存储/投影/目标或非冗余读计划的证明。</summary>
    [Theory]
    [InlineData("missing")][InlineData("hidden")][InlineData("join")][InlineData("type")][InlineData("nonredundant")][InlineData("targetHidden")][InlineData("override")][InlineData("scope")][InlineData("idFallback")][InlineData("targetOwnership")]
    public void Unsupported_metadata_produces_explicit_gap_without_guessing_name_columns(string scenario)
    {
        var catalog=ScalarCatalog();var storage=catalog.ConfigColumns.Single(column=>column.Id=="22");var field=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="21");
        if(scenario=="missing"){catalog.ConfigColumns.Remove(storage);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="22");}
        if(scenario=="hidden"){var settings=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="22");settings.IsShow=0;settings.IsEdit=0;}
        if(scenario=="join")storage.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(scenario=="type")storage.LogicalType="int64";
        if(scenario=="nonredundant")catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=0;
        if(scenario=="targetHidden")catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="201");
        if(scenario=="override")field.Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"alias\"}");
        if(scenario=="scope")field.Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"whereSql\":\"name like @input\"}");
        if(scenario=="targetOwnership"){catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id");catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="";}
        if(scenario=="idFallback")catalog.ConfigColumns.Single(column=>column.Id=="21").ControlDetail="{\"pageId\":\"201\",\"nameField\":\"id\"}";
        var model=GenerationModel.Create(catalog,"Example.NameGap");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single());Assert.Empty(plan.Bindings);Assert.Single(plan.Gaps);Assert.Contains("名称筛选尚未支持",plan.Gaps[0].Reason);Assert.Empty(plan.ConsumedKeys);Assert.Throws<InvalidOperationException>(()=>SelectionNameFilterCompilation.RenderCases(plan));
    }
    /// <summary>两个来源控件争用同一辅助列时全部拒绝，不依顺序选中其中一个。</summary>
    [Fact]
    public void Ambiguous_alias_has_no_generated_case()
    {
        var catalog=ScalarCatalog();catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="21").Extra.Remove("isSearch");
        catalog.ConfigColumns.Add(new(){Id="205",ConfigTableId="2",ColumnName="full_name",LogicalType="string"});catalog.ConfigPageColumns.Add(new(){Id="1205",ConfigPageId="201",ConfigColumnId="205",IsShow=1});
        foreach(var tuple in new[]{("31","a_id","full_name"),("32","a_full_id","name")}){catalog.ConfigColumns.Add(new(){Id=tuple.Item1,ConfigTableId="1",ColumnName=tuple.Item2,LogicalType="int64",ControlKind="SelectPage",ControlDetail=JsonSerializer.Serialize(new{pageId="201",nameField=tuple.Item3}),LinkConfigTableId="2"});catalog.ConfigPageColumns.Add(new(){Id="f"+tuple.Item1,ConfigPageId="101",ConfigColumnId=tuple.Item1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});}
        catalog.ConfigColumns.Add(new(){Id="33",ConfigTableId="1",ColumnName="a_full_name",LogicalType="string"});catalog.ConfigPageColumns.Add(new(){Id="f33",ConfigPageId="101",ConfigColumnId="33",IsShow=1,IsEdit=0});var model=GenerationModel.Create(catalog,"Example.AliasGap");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single());Assert.Empty(plan.Bindings);Assert.Equal(2,plan.Gaps.Count);Assert.All(plan.Gaps,gap=>Assert.Contains("多个来源",gap.Reason));
    }
    /// <summary>辅助名来自原字段定义，合法页buttonText覆盖不改输入键或稳定名称。</summary>
    [Fact]
    public void Button_only_overlay_preserves_stable_name_binding()
    {
        var catalog=ScalarCatalog();catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="21").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"buttonText\":\"选择分组\"}");var model=GenerationModel.Create(catalog,"Example.NameOverlay");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single());Assert.Empty(plan.Gaps);Assert.Equal(SelectionDisplayFieldCompilation.Resolve(table,table.Columns.Single(column=>column.Source.Id=="21")).JsonName,Assert.Single(plan.Bindings).FilterKey);
    }
    /// <summary>使用真正元数据模型构造单选可读名称物理列，不手写业务实体。</summary>
    private static MetadataCatalog ScalarCatalog()
    {
        var catalog=GeneratorTests.Catalog();catalog.ConfigColumns.AddRange([new(){Id="21",ConfigTableId="1",ColumnName="group_id",LogicalType="int64",ControlKind="SelectPage",ControlDetail="{\"pageId\":\"201\",\"nameField\":\"name\"}",LinkConfigTableId="2"},new(){Id="22",ConfigTableId="1",ColumnName="group_name",LogicalType="string"}]);catalog.ConfigPageColumns.AddRange([new(){Id="121",ConfigPageId="101",ConfigColumnId="21",IsShow=1,IsEdit=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}},new(){Id="122",ConfigPageId="101",ConfigColumnId="22",IsShow=1,IsEdit=0}]);catalog.ConfigTables.Add(new(){Id="2",TableName="group_item"});catalog.ConfigColumns.AddRange([new(){Id="201",ConfigTableId="2",ColumnName="id",LogicalType="int64"},new(){Id="202",ConfigTableId="2",ColumnName="name",LogicalType="string"},new(){Id="203",ConfigTableId="2",ColumnName="yz_project_id",LogicalType="int64"}]);catalog.ConfigPages.Add(new(){Id="201",ConfigTableId="2",WhereSql="yz_project_id=@current_yz_project_id",Extra=new(){["type"]=JsonSerializer.SerializeToElement(1)}});catalog.ConfigPageColumns.Add(new(){Id="1202",ConfigPageId="201",ConfigColumnId="202",IsShow=1});return catalog;
    }
    /// <summary>运行实际生成原列表，HTTP只记录公开请求合同，不冒称后端过滤已经接线。</summary>
    private static async Task RunProbe(string output,string scenario,[CallerFilePath]string file="")
    {
        var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!,"SelectionNameFilterProbe.mjs"));start.ArgumentList.Add(output);start.ArgumentList.Add(scenario);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
