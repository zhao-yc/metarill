using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>角色读取证明、具体回执用途与待验收账号服务门禁分别验证；本组不访问数据库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FixedRoleCheckboxCapabilityGateTests
{
    /// <summary>type=2回读统一VO，详情可读但网格隐藏的角色也会输出；完全隐藏则不误加选择用途缺口。</summary>
    [Theory]
    [InlineData("ordinary",false)] [InlineData("selection",true)]
    [InlineData("detail-only-selection",true)] [InlineData("hidden-selection",false)]
    public void Selection_gap_tracks_the_actual_selected_row_output(string kind,bool expected)
    {
        var catalog=FixedRoleCheckboxTests.Catalog();var source=ManagerPage(catalog);
        source.Extra["type"]=JsonSerializer.SerializeToElement(kind=="ordinary"?0:2);
        if(kind=="detail-only-selection")
        {
            var field=catalog.ConfigPageColumns.Single(item=>item.ConfigColumnId==FixedRoleCheckboxCompilation.RoleColumn);
            field.IsShow=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);
        }
        if(kind=="hidden-selection")HideRole(catalog);
        var model=GenerationModel.Create(catalog,"Example.RoleSelectionGap");var table=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable);var page=Assert.Single(table.Pages);
        var gaps=RoleGaps(model,page.Source.Id);
        Assert.Single(gaps,gap=>gap.Usage=="runtime");Assert.Equal(expected,gaps.Any(gap=>gap.Usage=="selection"));
        Assert.Equal(expected,SelectionPageRenderer.Enabled(page)&&page.Readable.Any(column=>column.Source.Id==FixedRoleCheckboxCompilation.RoleColumn));
        Assert.DoesNotContain(gaps,gap=>gap.Usage=="export");Assert.Equal(expected?2:1,gaps.Length);
        var exception=Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        if(expected)Assert.Contains("/yz_role_ids/selection",exception.Message);
    }

    /// <summary>坏声明或被移除字段不能抹掉安全服务身份；实际生成仍可编译普通保存，但完整包在写stage前拒绝。</summary>
    [Theory]
    [InlineData("unknown")] [InlineData("different-control")] [InlineData("hidden-selection")] [InlineData("removed")]
    public async Task Hidden_or_missing_role_proof_keeps_runtime_gate_and_real_generation_compiles(string kind)
    {
        var catalog=FixedRoleCheckboxTests.Catalog();HideRole(catalog);
        var source=catalog.ConfigColumns.Single(item=>item.Id==FixedRoleCheckboxCompilation.RoleColumn);
        if(kind=="unknown")source.ControlDetail="unproved_roles;name;unknown_sql";
        if(kind=="different-control")source.ControlKind="";
        if(kind=="hidden-selection")ManagerPage(catalog).Extra["type"]=JsonSerializer.SerializeToElement(2);
        if(kind=="removed")catalog.ConfigColumns.Remove(source);
        const string rootNamespace="Example.RoleServiceGate";
        var model=GenerationModel.Create(catalog,rootNamespace);var table=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable);var page=Assert.Single(table.Pages);
        Assert.Null(FixedRoleCheckboxCompilation.ForPage(model,table,page));
        if(kind!="hidden-selection")Assert.Null(FixedRoleCheckboxCompilation.ForService(model,table,page));
        var gap=Assert.Single(RoleGaps(model,page.Source.Id));Assert.Equal("runtime",gap.Usage);Assert.Contains("Runtime A",gap.Reason);
        Assert.True(ReadProjectionCompilation.ForPage(model,table,page).Detail.CanCompile);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-role-service-gate");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var full=Path.Combine(directory,"full");
            var denied=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,full,rootNamespace,SourceRoot()));
            Assert.Contains("/yz_role_ids/runtime",denied.Message);Assert.False(Directory.Exists(full));Assert.Empty(Directory.GetDirectories(directory,"full.staging-*"));
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,rootNamespace);
            foreach(var path in new[]{"DTOs/YzManagerPage6031906822946816Requests.g.cs","VOs/YzManagerPage6031906822946816Vo.g.cs"})
            {
                var text=File.ReadAllText(Path.Combine(generated,path));Assert.DoesNotContain("YzRoleIds {",text);Assert.DoesNotContain("YzRoleNames {",text);
            }
            Assert.DoesNotContain("findNameIdYzRole",File.ReadAllText(Path.Combine(generated,"Controllers/YzManagerPage6031906822946816Controller.g.cs")));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var request=assembly.GetType(rootNamespace+".Generated.DTOs.YzManagerPage6031906822946816UpdateRequest",true)!;
            Assert.NotNull(request.GetProperty("Name"));Assert.Null(request.GetProperty("YzRoleIds"));
            var service=assembly.GetType(rootNamespace+".Generated.Services.YzManagerService",true)!;Assert.NotNull(service.GetMethod("Update6031906822946816Async"));
            if(kind=="hidden-selection")Assert.NotNull(service.GetMethod("SelectionSelected6031906822946816Async"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>选择端点当前真实编译仍调用无名称参数的VO转换，因此用途缺口不能随Runtime A一起取消。</summary>
    [Fact]
    public async Task Readable_role_selection_real_generation_retains_its_separate_usage_gap()
    {
        var catalog=FixedRoleCheckboxTests.Catalog();ManagerPage(catalog).Extra["type"]=JsonSerializer.SerializeToElement(2);
        var model=GenerationModel.Create(catalog,"Example.RoleSelectionProof");var page=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable).Pages.Single();
        Assert.Single(RoleGaps(model,page.Source.Id),gap=>gap.Usage=="selection");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-role-selection-gate");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var full=Path.Combine(directory,"full");var denied=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,full,"Example.RoleSelectionProof",SourceRoot()));
            Assert.Contains("/yz_role_ids/selection",denied.Message);Assert.False(Directory.Exists(full));Assert.Empty(Directory.GetDirectories(directory,"full.staging-*"));
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,"Example.RoleSelectionProof");
            Assert.Contains("return selected.Select(id => YzManagerPage6031906822946816Vo.FromEntity(rows[id])).ToArray();",File.ReadAllText(Path.Combine(generated,"Services/YzManagerService.g.cs")));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.RoleSelectionProof.Generated.Services.YzManagerService",true)!.GetMethod("SelectionSelected6031906822946816Async"));
            Assert.NotNull(assembly.GetType("Example.RoleSelectionProof.Generated.VOs.YzManagerPage6031906822946816Vo",true)!.GetProperty("YzRoleNames"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>同一固定安全表的其它可写页面也等待A；真正只读的其它页不借此扩大DTO、候选或用途缺口。</summary>
    [Theory]
    [InlineData("create",true)] [InlineData("update",true)] [InlineData("delete",true)]
    [InlineData("copy",true)] [InlineData("import-flag",true)] [InlineData("import-mode",true)] [InlineData("readonly",false)]
    public void Every_actual_write_entry_on_the_fixed_manager_service_keeps_the_pending_gate(string action,bool expected)
    {
        var catalog=FixedRoleCheckboxTests.Catalog();HideRole(catalog);var source=ManagerPage(catalog);var previous=source.Id;source.Id="2300000000000001099";
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==previous))field.ConfigPageId=source.Id;
        source.IsNew=0;source.IsSave=0;source.IsDelete=0;source.IsCopy=0;source.IsImport=0;source.Extra["isUpdate"]=JsonSerializer.SerializeToElement(0);source.Extra["importMethod"]=JsonSerializer.SerializeToElement("");
        if(action=="create")source.IsNew=1;
        if(action=="update")source.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);
        if(action=="delete")source.IsDelete=1;
        if(action=="copy")source.IsCopy=1;
        if(action=="import-flag")source.IsImport=1;
        if(action=="import-mode")source.Extra["importMethod"]=JsonSerializer.SerializeToElement("OnlyNew");
        var model=GenerationModel.Create(catalog,"Example.ManagerWriteGate");var table=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable);var page=Assert.Single(table.Pages);
        Assert.Equal(expected,FixedRoleCheckboxCompilation.RequiresRuntimeCoordination(table,page));
        var gaps=RoleGaps(model,source.Id);Assert.Equal(expected,gaps.Any(gap=>gap.Usage=="runtime"));
        if(expected)Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));else Assert.Empty(gaps);
        Assert.Null(FixedRoleCheckboxCompilation.ForPage(model,table,page));Assert.Null(FixedRoleCheckboxCompilation.ForService(model,table,page));
    }

    /// <summary>原固定账号页仍代表待验收的安全服务，不因临时关闭全部页面按钮而解除发布门禁。</summary>
    [Fact]
    public void Original_fixed_manager_page_still_waits_for_runtime_when_all_write_switches_are_closed()
    {
        var catalog=FixedRoleCheckboxTests.Catalog();HideRole(catalog);var source=ManagerPage(catalog);
        source.IsNew=0;source.IsSave=0;source.IsDelete=0;source.IsCopy=0;source.IsImport=0;source.Extra["isUpdate"]=JsonSerializer.SerializeToElement(0);
        var model=GenerationModel.Create(catalog,"Example.FixedManagerGate");
        var gap=Assert.Single(RoleGaps(model,source.Id));Assert.Equal("runtime",gap.Usage);
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
    }

    /// <summary>只有固定安全服务ID与表名同时匹配才应用待验收门槛，普通业务表不受影响。</summary>
    [Fact]
    public void Pending_runtime_gate_does_not_apply_to_unrelated_business_tables()
    {
        var model=GenerationModel.Create(GeneratorTests.Catalog(),"Example.OrdinaryTable");
        Assert.All(model.Tables,table=>Assert.All(table.Pages,page=>Assert.False(FixedRoleCheckboxCompilation.RequiresRuntimeCoordination(table,page))));
        Assert.DoesNotContain(GenerationCapabilityAudit.Collect(model),gap=>gap.Usage=="runtime");
    }

    private static PageMetadata ManagerPage(MetadataCatalog catalog)=>catalog.ConfigPages.Single(page=>page.Id==FixedRoleCheckboxCompilation.ManagerPage);
    private static void HideRole(MetadataCatalog catalog)=>catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId==FixedRoleCheckboxCompilation.ManagerPage&&field.ConfigColumnId==FixedRoleCheckboxCompilation.RoleColumn);
    private static GenerationCapabilityGap[] RoleGaps(GenerationModel model,string pageId)=>GenerationCapabilityAudit.Collect(model).Where(gap=>gap.PageId==pageId&&gap.Column=="yz_role_ids").ToArray();
    private static string SourceRoot([CallerFilePath]string source="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../.."));
}
