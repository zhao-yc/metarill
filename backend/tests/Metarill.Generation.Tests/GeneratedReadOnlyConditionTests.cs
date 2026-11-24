using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际生成/挂载原Vue详情，复合只读条件只增加必要读取依赖，不改变页面结构与权限门禁。</summary>
public sealed class GeneratedReadOnlyConditionTests
{
    /// <summary>字段物理名及entity前缀均绑定真实JSON属性，仅详情可读条件列不能顺便进入列表。</summary>
    [Fact]
    public async Task Composite_condition_mounts_original_page_and_tracks_only_its_detail_dependencies()
    {
        var catalog=Catalog();catalog.ConfigPages[0].Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("locked || (this.entity.approval_state === '已审' && entity.name != '允许')");
        var model=GenerationModel.Create(catalog,"Example.Readonly");var table=model.Tables.Single();var page=table.Pages.Single();var compiled=PageReadOnlyCompilation.Compile(page)!;
        Assert.Contains("this.entity.approvalState",compiled.JavaScript);Assert.DoesNotContain("approval_state",compiled.JavaScript);Assert.Equal(new[]{"locked","approvalState","name"},compiled.Dependencies.Select(column=>column.JsonName));
        var projection=ReadProjectionCompilation.ForPage(model,table,page);Assert.True(projection.Detail.CanCompile);Assert.True(projection.List.CanCompile);
        Assert.Equal(new[]{"12","16","17"},projection.Detail.Dependencies.Where(dependency=>dependency.Reasons.Any(reason=>reason.Contains("主子只读",StringComparison.Ordinal))).Select(dependency=>dependency.ColumnId).Order());
        Assert.DoesNotContain(projection.List.OutputFields,field=>field.JsonName is "locked" or "approvalState");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-generated-readonly");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,model.Namespace);var path=Path.Combine(output,"frontend/pages/yzp/customer-detail-101.vue");var first=await File.ReadAllTextAsync(path);
            new CSharpGenerator().Generate(catalog,output,model.Namespace);Assert.Equal(first,await File.ReadAllTextAsync(path));
            await Probe(output);
        }
        finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>坏表达式或未知字段仍在生成阶段拒绝，不能把脚本直接插入computed。</summary>
    [Theory][InlineData("locked || sideEffect()")][InlineData("locked = true")][InlineData("missing")][InlineData("this.entity.constructor")][InlineData("name.value")][InlineData("name.length")][InlineData("locked; globalThis.injected=true")]
    public void Unsafe_or_unbound_expression_remains_a_generation_error(string condition)
    {
        var catalog=Catalog();catalog.ConfigPages[0].Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement(condition);
        var model=GenerationModel.Create(catalog,"Example.Readonly");Assert.Throws<InvalidOperationException>(()=>PageReadOnlyCompilation.Compile(model.Tables.Single().Pages.Single()));
        Assert.False(ReadProjectionCompilation.ForPage(model,model.Tables.Single(),model.Tables.Single().Pages.Single()).Detail.CanCompile);
    }
    /// <summary>附件数组长度在原详情中参加只读筛选，同页大整数阈值沿既有精确比较。</summary>
    [Fact]
    public async Task Attachment_array_length_controls_generated_detail_readonly()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",ColumnName="gallery_images",LogicalType="text",ControlKind="MultiImage"});
        catalog.ConfigColumns.Add(new(){Id="19",ConfigTableId="1",ColumnName="limit_count",LogicalType="int64"});
        foreach (var id in new[]{"18","19"})
            catalog.ConfigPageColumns.Add(new(){Id="1"+id,ConfigPageId="101",ConfigColumnId=id,IsEdit=1});
        catalog.ConfigPages.Single().Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement("gallery_images.length > limitCount || locked");
        var boundary = Path.Combine(Path.GetTempPath(),"metarill-array-length-readonly");
        var output = Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.ArrayLengthReadonly");
            await Probe(output,"array-length");
        }
        finally
        {
            if (Directory.Exists(output)) await TestDirectoryCleanup.DeleteAsync(output,boundary);
        }
    }

    /// <summary>已有单字段生成文本保持不变，空条件不额外插入表达式；JSON路径只投影其根列。</summary>
    [Fact]
    public void Simple_field_and_declared_json_path_have_fixed_narrow_bindings()
    {
        var catalog=Catalog();var page=catalog.ConfigPages[0];Assert.Null(Compile());
        page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("locked");Assert.Equal("this.entity.locked",Compile()!.JavaScript);
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",ColumnName="payload",LogicalType="json"});catalog.ConfigPageColumns.Add(new(){Id="118",ConfigPageId="101",ConfigColumnId="18"});
        page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("payload.locked && payload.flags.active");var condition=Compile()!;Assert.Single(condition.Dependencies);Assert.Equal("payload",condition.Dependencies[0].JsonName);Assert.True(condition.UsesObjectPaths);Assert.Contains("this.originalReadOnlyPath(\"payload\",[\"flags\",\"active\"])",condition.JavaScript);
        PageReadOnlyCompilation? Compile()=>PageReadOnlyCompilation.Compile(GenerationModel.Create(catalog,"Example.Readonly").Tables.Single().Pages.Single());
    }
    /// <summary>原详情读取对象及历史JSON文本，坏对象不会让页面崩溃或开放写入；回显不会触发第二次自动保存。</summary>
    [Fact]
    public async Task Json_readonly_and_form_autosave_respect_actual_edits()
    {
        var catalog=Catalog();
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",ColumnName="payload",LogicalType="json"});
        catalog.ConfigPageColumns.Add(new(){Id="118",ConfigPageId="101",ConfigColumnId="18"});
        catalog.ConfigPages[0].Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("payload.flags.locked === true");
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="12").Extra["isAutoSaveAfterChange"]=JsonSerializer.SerializeToElement(1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-json-readonly-form");
        var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.ReadonlyForm");
            await Probe(output,"json-form");
        }
        finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }

    /// <summary>真实生成详情在原自动保存的两笔请求之间保留本地编辑，回显与撤权仍走原规则。</summary>
    [Fact]
    public async Task Autosave_keeps_edits_made_while_the_previous_save_is_in_flight()
    {
        var catalog = Catalog();
        catalog.ConfigPages.Single().Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement("locked");
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="12").Extra["isAutoSaveAfterChange"] = JsonSerializer.SerializeToElement(1);
        var boundary = Path.Combine(Path.GetTempPath(),"metarill-detail-autosave");
        var output = Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.DetailAutosave");
            await Probe(output,"pending-autosave");
        }
        finally
        {
            if (Directory.Exists(output)) await TestDirectoryCleanup.DeleteAsync(output,boundary);
        }
    }

    /// <summary>实际列表行内失焦保存支持连续编辑，未再次失焦时只保留表单草稿。</summary>
    [Fact]
    public async Task Inline_blur_preserves_pending_edits_and_saves_only_after_another_blur()
    {
        var catalog = Catalog();
        catalog.ConfigPages.Single().Extra["isListEdit"] = JsonSerializer.SerializeToElement(1);
        var boundary = Path.Combine(Path.GetTempPath(),"metarill-detail-inline-blur");
        var output = Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.DetailInlineBlur");
            await Probe(output,"inline-blur");
        }
        finally
        {
            if (Directory.Exists(output)) await TestDirectoryCleanup.DeleteAsync(output,boundary);
        }
    }

    /// <summary>浏览器需区别相邻的雪花ID，并让整数比较与同一 AST 的服务端结果一致。</summary>
    [Fact]
    public async Task Integer_readonly_conditions_preserve_adjacent_ids_and_decimal_thresholds()
    {
        var catalog=Catalog();
        foreach(var (id,name) in new[]{("18","owner_id"),("19","other_id")})
        {
            catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId="1",ColumnName=name,LogicalType="int64"});
            catalog.ConfigPageColumns.Add(new(){Id="1"+id,ConfigPageId="101",ConfigColumnId=id,IsEdit=1});
        }
        const string rule="ownerId === 9007199254740993 || (ownerId > otherId && ownerId < 0.5)";
        catalog.ConfigPages[0].Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement(rule);
        var parsed=Metarill.Core.Metadata.ReadOnlyConditionRule.Compile(rule);
        Assert.True(parsed.IsReadOnly(new Dictionary<string,object?>{{"ownerId",9007199254740993L},{"otherId",9L}}));
        Assert.False(parsed.IsReadOnly(new Dictionary<string,object?>{{"ownerId",9007199254740992L},{"otherId",9L}}));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-readonly-int64");
        var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.ReadonlyInteger");
            await Probe(output,"int64");
        }
        finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }

    /// <summary>原布尔控件回显 0／1，详情只读规则仍按其声明的布尔类型求值。</summary>
    [Theory]
    [InlineData("CheckBox")]
    [InlineData("ConstList")]
    [InlineData("ConstDownList")]
    public async Task Boolean_option_conditions_use_the_declared_type_in_original_detail(string control)
    {
        var catalog = Catalog();
        var field = catalog.ConfigColumns.Single(column => column.ColumnName == "locked");
        field.ControlKind = control;
        if (control != "CheckBox") field.ControlDetail = "已锁定|1,未锁定|0";
        const string expression = "locked === true || (approvalState === '停用' && locked === null)";
        catalog.ConfigPages.Single().Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement(expression);
        var serverRule = Metarill.Core.Metadata.ReadOnlyConditionRule.Compile(expression);
        var values = new Dictionary<string, object?> { ["locked"] = true, ["approvalState"] = "待审" };
        Assert.True(serverRule.IsReadOnly(values));
        values["locked"] = false;
        Assert.False(serverRule.IsReadOnly(values));
        values["locked"] = null;
        values["approvalState"] = "停用";
        Assert.True(serverRule.IsReadOnly(values));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-boolean-readonly");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, output, "Example.BooleanReadonly");
            await Probe(output, "boolean-choice");
        }
        finally
        {
            if (Directory.Exists(output)) await TestDirectoryCleanup.DeleteAsync(output, boundary);
        }
    }

    /// <summary>两个条件字段只在详情可读，列表没有该两列；不扩大现有页面读取上限。</summary>
    private static Metarill.Core.Metadata.MetadataCatalog Catalog()
    {
        var catalog=GeneratorTests.Catalog();
        foreach(var(id,name,type)in new[]{("16","locked","bool"),("17","approval_state","string")})
        {catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId="1",ColumnName=name,LogicalType=type});catalog.ConfigPageColumns.Add(new(){Id="1"+id,ConfigPageId="101",ConfigColumnId=id,IsShow=0,IsEdit=1,Extra=new(){["isInGrid"]=JsonSerializer.SerializeToElement(0),["isInEdit"]=JsonSerializer.SerializeToElement(1)}});}
        return catalog;
    }
    /// <summary>执行实际Vue编译器和原详情方法；网络/权限仅为明确夹具，不宣称浏览器或Production身份验收。</summary>
    private static async Task Probe(string output,string scenario="standard",[CallerFilePath]string source="")
    {
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!,"GeneratedReadOnlyConditionProbe.mjs"));start.ArgumentList.Add(output);start.ArgumentList.Add(scenario);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
