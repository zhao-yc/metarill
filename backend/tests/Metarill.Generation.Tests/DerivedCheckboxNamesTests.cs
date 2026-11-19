using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>数据库复选目标name来自R0单值联接，保留原控件和名称数组。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedCheckboxNamesTests
{
    /// <summary>显式与自动名称均从真实元数据生成编译，候选及Excel不访问IsIgnore属性。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Derived_checkbox_names_generate_and_compile(bool automatic)
    {
        var c=Catalog("relation",automatic);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-checkbox");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.DerivedCheckbox");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(c,"Example.DerivedCheckbox")));
            Assert.DoesNotContain("name",MetadataStoragePolicy.ForTable(c,c.ConfigTables.Single(table=>table.Id=="3")).PhysicalColumns.Select(column=>column.ColumnName));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>同名反查只使用同时符合控件条件与实际目标页条件的记录。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkbox_excel_applies_the_bound_target_page(bool derived)
    {
        const string ns="Example.CheckboxExcelScope";
        var catalog=Catalog("checkbox_excel_scope",false);
        if(!derived)
        {
            var name=catalog.ConfigColumns.Single(column=>column.Id=="32");
            name.Extra.Remove("isJoinColumn");
            name.Extra.Remove("columnFullName");
        }
        var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        source.ControlDetail="{\"tableId\":\"3\",\"pageId\":\"301\",\"nameField\":\"name\",\"whereSql\":\"segment=@formParameters.segment AND status=1\"}";
        var sourceField=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==source.Id);
        sourceField.IsShow=sourceField.IsEdit=1;
        catalog.ConfigColumns.Add(new(){Id="3005",ConfigTableId="3",ColumnName="sheet_status",LogicalType="int32"});
        catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql+=" AND sheet_status=41";
        RedundantMultiNameCacheTests.Published(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-excel-page");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var generated=Assert.Single(Directory.GetFiles(Path.Combine(directory,"generated"),"CheckboxExcelScopeSourceService.g.cs",SearchOption.AllDirectories));
            var code=await File.ReadAllTextAsync(generated);
            var read=derived?"DerivedCheckboxRows":"CheckboxScope";
            var candidate=Assert.Single(code.Split('\n'),line=>line.Contains(read+"201_25Async(request.ParentId",StringComparison.Ordinal));
            Assert.DoesNotContain("excel:true",candidate);
            Assert.Contains("        if(excel)",code);
            Assert.Contains("        if(excel)",code[..code.IndexOf("CheckboxOptions201_25Async",StringComparison.Ordinal)]);
            Assert.Contains(derived?"if(excel)filters.Add(new(\"parent_id\"":"query=query.Where(row=>row.ParentId==",code);
            Assert.Contains(code.Split('\n'),line=>line.Contains(read+"201_25Async(parentId,actor,",StringComparison.Ordinal)&&
                line.Contains("excel:true",StringComparison.Ordinal));
            Assert.Contains(code.Split('\n'),line=>line.Contains(read+"201_25Async(entity.ParentId",StringComparison.Ordinal)&&
                line.Contains("excel:true",StringComparison.Ordinal));

            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.CheckboxExcelScopeSourceService",true)!;
            const System.Reflection.BindingFlags privateStatic=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            var plan=Assert.IsType<Metarill.Data.Queries.FixedConditionSqlPlan>(
                service.GetField("CheckboxExcelCondition201_25",privateStatic)!.GetValue(null));
            var clause=plan.GetType().GetMethod("RenderConstants",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!
                .Invoke(plan,[new Metarill.Data.MySqlDatabaseProvider(),"scope",new Func<string,string>(field=>field)])!;
            Assert.Contains("sheet_status",Assert.IsType<string>(clause.GetType().GetProperty("Sql")!.GetValue(clause)));
            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(clause.GetType().GetProperty("Parameters")!.GetValue(clause));
            Assert.Equal(41,Assert.Single(parameters.Values));
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>非默认派生名称的候选、原导入导出和两种源存储使用同一固定字段。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task Explicit_checkbox_label_uses_one_derived_field_in_generation_and_storage(int mode, bool automatic)
    {
        var catalog = Catalog("custom_checkbox", automatic);
        var owner = catalog.ConfigTables.Single(table => table.Id == "2");
        owner.IsRongyu = mode;
        var field = catalog.ConfigColumns.Single(column => column.Id == "25");
        field.ControlDetail = JsonSerializer.Serialize(new
        {
            tableId = "3", nameField = "caption",
            whereSql = "parent_id=@parentId AND segment=@formParameters.segment AND status=1", orderBy = "caption desc"
        });
        catalog.ConfigColumns.Single(column => column.Id == "32").ColumnName = "caption";
        if (!automatic)
        {
            var explicitNames = catalog.ConfigColumns.Single(column => column.Id == "27");
            explicitNames.ColumnName = "target_captions";
            explicitNames.Extra["columnFullName"] = JsonSerializer.SerializeToElement("target.caption");
        }
        catalog.ConfigColumns.Add(new() { Id = "ordinary_names", ConfigTableId = owner.Id, ColumnName = "target_names", LogicalType = "string" });
        catalog.ConfigPageColumns.Add(new()
        {
            Id="20225",ConfigPageId="202",ConfigColumnId=field.Id,YzProjectId=catalog.YzProjectId,
            Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement("{\"orderBy\":\"id desc\"}")}
        });
        var model = GenerationModel.Create(catalog, "Example.CustomCheckbox");
        var table = model.Tables.Single(candidate => candidate.Source.Id == owner.Id);
        var page = table.Pages.Single(candidate => candidate.Source.Id == "201");
        var checkbox = Assert.Single(DatabaseCheckboxCompilation.ForPage(model, table, page));
        Assert.Equal("caption", checkbox.Name.Source.ColumnName);
        Assert.Equal("target_captions", checkbox.Names.Source.ColumnName);
        Assert.True(checkbox.DerivedName);
        var alternate=table.Pages.Single(candidate=>candidate.Source.Id=="202");
        var overrideCheckbox=Assert.Single(DatabaseCheckboxCompilation.ForService(model,table,alternate));
        Assert.Equal("caption",overrideCheckbox.Name.Source.ColumnName);
        Assert.Equal(checkbox.Conditions!.Definition,overrideCheckbox.Conditions!.Definition);
        Assert.Equal("segment",Assert.Single(overrideCheckbox.Conditions.Forms).Column.Source.ColumnName);
        var order=Assert.Single(overrideCheckbox.Order);
        Assert.True(order.Column.IsId);
        Assert.True(order.Descending);
        var physical = MetadataStoragePolicy.ForTable(catalog, owner).PhysicalColumns.Select(column => column.ColumnName);
        Assert.Contains("target_names", physical);
        Assert.Equal(mode == 1, physical.Contains("target_captions"));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if (mode == 1)
            Assert.Contains("target_captions", MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns[owner.TableName]);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-custom-checkbox-label");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), model.Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(model.Namespace + ".Generated.Services.CustomCheckboxSourceService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            catalog.ConfigPageColumns.Single(item => item.ConfigPageId == "201" && item.ConfigColumnId == field.Id)
                .Extra["controlDetail"] = JsonSerializer.SerializeToElement(
                    field.ControlDetail.Replace("\"caption\"", "\"name\"", StringComparison.Ordinal));
            Assert.NotEmpty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog, model.Namespace)));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>完整包从包内源码构建，并挂载原复选组件；重复更新保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_derived_checkbox_keeps_original_component_and_handwritten_code(bool automatic)
    {
        var root=NonRedundantApplicationFixture.Root();var c=Catalog("relation",automatic);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-checkbox-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.DerivedCheckboxApplication",root).FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            const string component="uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue";Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",component)),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(Path.Combine(root,"backend/tests/Metarill.Generation.Tests/DatabaseCheckboxVueProbe.mjs"));start.ArgumentList.Add(output);using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
            using(var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var subset=Assert.Single(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),item=>item.GetProperty("controlKind").GetString()=="MultiCheckBoxDatabase");Assert.True(subset.GetProperty("derivedNameSupported").GetBoolean());Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());}
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 派生复选手写保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.DerivedCheckboxApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明的名称、存储或条件在stage创建前拒绝，隐藏字段不能绕过。</summary>
    [Theory][InlineData("global")][InlineData("targetR1")][InlineData("sourceR1BadCache")][InlineData("credential")][InlineData("array")][InlineData("broken")][InlineData("decimalKey")][InlineData("derivedScope")][InlineData("overlay")][InlineData("hiddenBad")]
    public void Invalid_derived_checkbox_is_rejected_before_staging(string kind)
    {
        var c=Catalog("relation",false);var name=c.ConfigColumns.Single(column=>column.Id=="32");
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="targetR1")c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=1;
        if(kind=="sourceR1BadCache"){c.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;c.ConfigColumns.Single(x=>x.Id=="27").IsVirtualColumn=1;}
        if(kind=="credential"){c.ConfigColumns.Single(column=>column.Id=="42").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(column=>column.Id=="42").ControlKind="FileLibrary";
        if(kind is "broken" or "hiddenBad")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="decimalKey")c.ConfigColumns.Single(column=>column.Id=="37").LogicalType="decimal";
        if(kind=="derivedScope")c.ConfigColumns.Single(column=>column.Id=="25").ControlDetail="头;头;name='X';name asc";
        if(kind=="overlay")c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25").Extra["controlKind"]=JsonSerializer.SerializeToElement("List");
        if(kind=="hiddenBad")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");
        var output=Path.Combine(Path.GetTempPath(),"metarill-derived-checkbox-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidDerivedCheckbox",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>沿派生多值元数据，仅换回原数据库复选协议；whereSql按来源真实父及表单执行。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool automatic)
    {
        var c=DerivedMultiNamesTests.Catalog(prefix,automatic);var source=c.ConfigColumns.Single(column=>column.Id=="25");source.ControlKind="MultiCheckBoxDatabase";source.ControlDetail="旧第一段;旧第二段;parent_id=@parentId AND segment=@formParameters.segment AND status=1;name desc";
        c.ConfigColumns.AddRange([new(){Id="28",ConfigTableId="2",ColumnName="segment",LogicalType="int32",DefaultValue="1"},new(){Id="38",ConfigTableId="3",ColumnName="segment",LogicalType="int32",DefaultValue="1"},new(){Id="39",ConfigTableId="3",ColumnName="status",LogicalType="int32",DefaultValue="1"}]);
        foreach(var page in c.ConfigPages.Where(page=>page.ConfigTableId=="2"))c.ConfigPageColumns.Add(new(){Id=page.Id+"28",ConfigPageId=page.Id,ConfigColumnId="28"});
        c.ConfigPageColumns.AddRange([new(){Id="30138",ConfigPageId="301",ConfigColumnId="38"},new(){Id="30139",ConfigPageId="301",ConfigColumnId="39"}]);
        foreach(var record in c.ConfigColumns.Cast<MetadataRecord>().Concat(c.ConfigPageColumns))if(string.IsNullOrWhiteSpace(record.YzProjectId))record.YzProjectId=c.YzProjectId;
        if(!automatic)c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="27").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        return c;
    }
}
