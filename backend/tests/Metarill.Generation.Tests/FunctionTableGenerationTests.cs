using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FunctionTableGenerationTests : IDisposable
{
    private readonly string output = Path.Combine(Path.GetTempPath(), "metarill-function-form", Guid.NewGuid().ToString("N"));

    /// <summary>从真实 Full 包验证原表单、候选权限、功能菜单与无持久化边界。</summary>
    [Fact]
    public async Task Function_form_packages_its_original_detail_and_authorized_options_without_a_mapper()
    {
        var catalog = WithCascades(Catalog());
        new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot());

        var frontend = Path.Combine(output, "frontend");
        var detailPath = Path.Combine(frontend, "pages/yzp/summary_form-detail-102.vue");
        var detail = File.ReadAllText(detailPath);
        Assert.Contains("page_is_function_table:true", detail);
        Assert.Contains("page_is_delete:false", detail);
        Assert.Contains("page_is_update:true", detail);
        Assert.Contains("title", detail);
        Assert.Contains("findNameIdCustomer24", detail);
        Assert.Contains("createMultiSelectFieldMixin", detail);
        Assert.Contains("customer-list-101.vue", detail);
        Assert.Contains(":selectedIds=\"entity.customerIds\"", detail);
        Assert.Contains(":selectedNames=\"entity.customerNames\"", detail);
        Assert.Contains("receiveMultiSelection", detail);
        Assert.Contains("findNameIdApproved26", detail);
        Assert.Contains("v-model='entity.approvedIds'", detail);
        Assert.Contains("readonlyDisplayText(entity.approvedNames)", detail);
        Assert.Contains("cascadeTreePath27", detail);
        Assert.Contains("cascadeTreeOptions28", detail);
        Assert.Contains("cascadeTreeOptions29", detail);
        Assert.Contains("leaf-id-property='treeNodeId'", detail);
        Assert.Contains("leaf-id-property='placeId'", detail);
        Assert.Contains("<yzp-file-library v-model='entity.attachments' config-column-id='37' page-id='102' table-id='2'",detail);
        Assert.Contains(":record-id='id'",detail);
        Assert.Contains("/admin/sys/config_button/execute", detail);
        Assert.Contains("saveEntity", detail);
        Assert.False(File.Exists(Path.Combine(frontend, "pages/yzp/summary_form-list-102.vue")));
        var pages = File.ReadAllText(Path.Combine(frontend, "pages.json"));
        Assert.Contains("summary_form-detail-102", pages);
        Assert.DoesNotContain("summary_form-list-102", pages);
        Assert.DoesNotContain("\"102\"", File.ReadAllText(Path.Combine(frontend,"common/yzp_runtime/page-write-schema.generated.json")));

        var backend = Path.Combine(output, "backend/generated");
        Assert.False(File.Exists(Path.Combine(backend, "Entities/SummaryForm.g.cs")));
        Assert.False(File.Exists(Path.Combine(backend, "Mappers/SummaryFormMapper.g.cs")));
        var controller = File.ReadAllText(Path.Combine(backend, "Controllers/SummaryFormPage102Controller.g.cs"));
        Assert.Contains("findNameIdCustomer24/page", controller);
        Assert.Contains("authorization.RequirePagePermission(\"102\", operation)", controller);
        Assert.Contains("authorization.RequirePagePermission(\"101\", \"list\")", controller);
        Assert.DoesNotContain("[HttpPost(\"page\")]", controller);
        Assert.DoesNotContain("[HttpGet(\"detail/{id}\")]", controller);
        Assert.DoesNotContain("[HttpDelete]", controller);
        Assert.Contains("RelationOptionAuthorization.Require(authorization,\"102\",false,true)",controller);
        Assert.Contains("CheckboxOptions102_26Async(request,actor,cancellationToken)",controller);
        Assert.Contains("CascadePath102_27Async(request,Actor(\"list\")",controller);
        Assert.Contains("CrossPath102_28Async(request,Actor(\"list\")",controller);
        Assert.Contains("CrossOptions102_29Async(request,Actor(\"list\")",controller);
        var targetController = File.ReadAllText(Path.Combine(backend,"Controllers/CustomerPage101Controller.g.cs"));
        Assert.Contains("[HttpPost(\"selection/selected\")]",targetController);
        Assert.Contains("SelectionSelected101Async(request, Actor(\"list\"), cancellationToken)",targetController);
        var service = File.ReadAllText(Path.Combine(backend, "Services/SummaryFormService.g.cs"));
        Assert.Contains("Select<global::Example.FunctionForm.Generated.Entities.Customer>", service);
        Assert.Contains("entity.YzProjectId == long.Parse(actor.ProjectId", service);
        Assert.DoesNotContain("InsertAsync", service);
        Assert.DoesNotContain("MultiName",service);
        Assert.Contains("ReadCheckboxForm102_26",service);
        Assert.Contains("CheckboxTargetCondition102_26",service);
        Assert.DoesNotContain("ValidateCheckbox",service);
        Assert.Contains("CrossTargetPlan102_29_1",service);
        Assert.Contains("CascadeScope102_27(RuntimeActor",service);
        Assert.Contains("relationAuthorization.RequirePagePermission(\"201\",\"list\")",service);
        Assert.Contains("relationAuthorization.RequirePagePermission(\"401\",\"list\")",service);
        Assert.DoesNotContain("ValidateCascade",service);
        var nav = File.ReadAllText(Path.Combine(output, "backend/Application.Host/DevelopmentNavigation.cs"));
        Assert.Contains("table.IsFunctionTable == 1 ? \"-detail-\" : \"-list-\"", nav);
        Assert.Contains("countEndpoint=table.IsFunctionTable==1?null", nav);
        var physical=MySqlSchemaPlanner.Compile(catalog,"form_test").Tables.Select(table=>table.Name).Order(StringComparer.Ordinal);
        Assert.Equal(new[]{"customer","function_area","function_country","function_place","function_tree_node"},physical);

        await VerifyVueAsync(output,"GeneratedFunctionFormProbe.mjs");
        await VerifyVueAsync(output,"GeneratedFunctionFormCascadeProbe.mjs");
        await VerifyVueAsync(output,"GeneratedFunctionFormFileProbe.mjs");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = output, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { "build", "backend/Application.Host/Application.Host.csproj", "--nologo",
            "--disable-build-servers", "--verbosity", "quiet", "-warnaserror", "-p:NuGetAudit=false" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }

    /// <summary>装载完整包内的原表单，验证原确认事件到业务按钮的实际请求参数。</summary>
    private static async Task VerifyVueAsync(string directory, string probe, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, probe));
        start.ArgumentList.Add(directory);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }

    /// <summary>功能表不能使用超出目标页已证明合同的附加条件。</summary>
    [Fact]
    public void Function_form_rejects_an_unbound_multiselect_filter()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column => column.Id == "25").ControlDetail =
            """{"pageId":"101","nameField":"name","whereSql":"name = '限定客户'"}""";
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot()));
        Assert.Contains("额外控件范围", error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>旧复选可直接声明真实候选表，沿用来源页授权。</summary>
    [Fact]
    public void Function_form_checkbox_accepts_legacy_table_only_options()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column => column.Id == "26").ControlDetail = "customer";
        new CSharpGenerator().Generate(catalog, output, "Example.FunctionForm");
        var service = File.ReadAllText(Path.Combine(output, "Services/SummaryFormService.g.cs"));
        Assert.Contains("CheckboxOptions102_26Async", service);
        Assert.Contains("RelationOptionAuthorization.Require(relationAuthorization,\"102\",false,true)", service);
        Assert.DoesNotContain("CheckboxTargetCondition102_26", service);
    }

    /// <summary>虚拟表单值只能绑定当前来源页开放的确切字段。</summary>
    [Fact]
    public void Function_form_checkbox_rejects_an_unpublished_form_slot()
    {
        var catalog = Catalog();
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == "23");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot()));
        Assert.Contains("当前页开放的非凭据标量", error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>功能表不能作为实体候选或选择列表，拒绝生成悬空导航。</summary>
    [Fact]
    public void Function_form_cannot_be_used_as_its_own_database_options_target()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column=>column.Id=="24").ControlDetail =
            """{"tableId":"2","table":"summary_form","nameField":"title"}""";
        var error = Assert.Throws<InvalidOperationException>(() =>
            new CSharpGenerator().Generate(catalog,output,"Example.FunctionForm"));
        Assert.Contains("功能表没有可读取的候选记录",error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>原无主键功能表可把虚拟表单字段交给业务按钮，外表负责提供真实候选。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog();
        catalog.ConfigColumns.Where(column=>column.LogicalType=="id").ToList().ForEach(column=>column.LogicalType="int64");
        catalog.ConfigTables[0].YzProjectId = catalog.YzProjectId;
        catalog.ConfigTables.Add(new() { Id="2", Name="汇总表单", TableName="summary_form", YzProjectId="100", IsFunctionTable=1 });
        catalog.YzAdminTypes.Add(new() { Id="70", Name="业务后台", YzProjectId="100" });
        catalog.ConfigPages[0].YzProjectId="100";
        catalog.ConfigPages[0].YzAdminTypeId="70";
        catalog.ConfigPages[0].Extra["type"]=JsonSerializer.SerializeToElement(2);
        catalog.ConfigPages[0].WhereSql+=" AND name <> '停用'";
        catalog.ConfigPages.Add(new() { Id="102", Name="汇总表单", ConfigTableId="2", YzProjectId="100", YzAdminTypeId="70", IsCopy=1, WhereSql="yz_project_id=@current_yz_project_id" });
        catalog.ConfigColumns.Add(new() { Id="23", ConfigTableId="2", ColumnName="title", Name="标题", DefaultValue="业务汇总", IsVirtualColumn=1,
            Extra=new() { ["isAutoPostBack"]=JsonSerializer.SerializeToElement(1) } });
        catalog.ConfigColumns.Add(new() { Id="24", ConfigTableId="2", ColumnName="customer_id", Name="客户", LogicalType="int64",
            ControlKind="List", ControlDetail="""{"tableId":"1","table":"customer","nameField":"name"}""" });
        catalog.ConfigColumns.Add(new() { Id="25", ConfigTableId="2", ColumnName="customer_ids", Name="多选客户",
            LogicalType="string", ColumnLength="max", ControlKind="MultiSelectPage", LinkConfigTableId="1", IsVirtualColumn=1,
            ControlDetail="""{"pageId":"101","nameField":"name","buttonText":"选择"}""" });
        catalog.ConfigColumns.Add(new() { Id="26", ConfigTableId="2", ColumnName="approved_ids", Name="复选客户",
            LogicalType="string", ColumnLength="max", ControlKind="MultiCheckBoxDatabase", LinkConfigTableId="1", IsVirtualColumn=1,
            ControlDetail="""{"pageId":"101","nameField":"name","whereSql":"remark = @formParameters.summaryForm.title"}""",
            Extra=new() { ["isAutoPostBackQuery"]=JsonSerializer.SerializeToElement(1) } });
        catalog.ConfigColumns.Add(new() { Id="37", ConfigTableId="2", ColumnName="attachments", Name="附件",
            LogicalType="string", ColumnLength="max", ControlKind="FileLibrary", IsVirtualColumn=1,
            ControlDetail="""{"accessMode":"PROTECTED","maxCount":3,"maxSizeMb":5,"extensions":["txt"]}""" });
        catalog.ConfigPageColumns.Add(new() { Id="117", ConfigPageId="102", ConfigColumnId="37", IsEdit=1 });
        catalog.ConfigPageColumns.Add(new() { Id="116", ConfigPageId="102", ConfigColumnId="26", IsEdit=1 });
        catalog.ConfigPageColumns.Add(new() { Id="115", ConfigPageId="102", ConfigColumnId="25", IsEdit=1 });
        catalog.ConfigPageColumns.Add(new() { Id="113", ConfigPageId="102", ConfigColumnId="23", IsEdit=1, IsRequired=1 });
        catalog.ConfigPageColumns.Add(new() { Id="114", ConfigPageId="102", ConfigColumnId="24", IsEdit=1 });
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new { id="63", configPageId="102", configTableId="2", yzProjectId="100",
            name="汇总", place="Edit", actionType=1, sql="/admin/custom/report/preview", visibleScheme="Normal", saveOption="Save", isNeedConfirm=1 }));
        catalog.ConfigMenus.Add(new() { Id="82", Name="汇总表单", YzProjectId="100", YzAdminTypeId="70", Kind="GENERATED_PAGE", ConfigPageId="102", IsShowCount=1 });
        return catalog;
    }

    /// <summary>共享范围冲突和错误字段映射在打包前报告。</summary>
    [Theory]
    [InlineData("shared")]
    [InlineData("alias")]
    public void Function_form_cascade_rejects_conflicting_scopes_or_aliases(string kind)
    {
        var catalog=WithCascades(Catalog());
        var branch=catalog.ConfigColumns.Single(column=>column.Id=="28");
        branch.ControlDetail=kind=="shared"
            ?branch.ControlDetail.Replace("\"parentField\":\"country_id\"","\"parentField\":\"country_id\",\"nameField\":\"alt_name\"",StringComparison.Ordinal)
            :branch.ControlDetail.Replace("\"parentField\":\"country_id\"","\"parentField\":\"country_id\",\"idPropertyName\":\"title\"",StringComparison.Ordinal);
        var error=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,"Example.FunctionForm",SourceRoot()));
        Assert.Contains(kind=="shared"?"共享级联的上级路径":"属性映射",error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>同一原功能表包含固定自树和共享国家地区的长短跨表路径，所有槽位留在表单。</summary>
    private static MetadataCatalog WithCascades(MetadataCatalog catalog)
    {
        foreach(var (id,name,page,parent,parentTable,mode) in new (string,string,string,string?,string,int)[]
        {
            ("3","function_tree_node","201","parent_node_id","3",1),
            ("4","function_country","301",null,"",1),
            ("5","function_area","401","country_id","4",0),
            ("6","function_place","501","area_id","5",1)
        })
        {
            catalog.ConfigTables.Add(new(){Id=id,TableName=name,Name=name,YzProjectId="100",IsRongyu=mode});
            catalog.ConfigPages.Add(new(){Id=page,ConfigTableId=id,Name=name,YzProjectId="100",YzAdminTypeId="70",WhereSql="yz_project_id=@current_yz_project_id"});
            catalog.ConfigColumns.AddRange([
                new(){Id=id+"01",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id=id+"02",ConfigTableId=id,ColumnName="name",YzProjectId="100"},
                new(){Id=id+"03",ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id=id+"04",ConfigTableId=id,ColumnName="delete_no",LogicalType="datetime"},
                new(){Id=id+"05",ConfigTableId=id,ColumnName="alt_name",YzProjectId="100"}]);
            catalog.ConfigPageColumns.AddRange([
                new(){Id=page+"02",ConfigPageId=page,ConfigColumnId=id+"02",IsShow=1},
                new(){Id=page+"05",ConfigPageId=page,ConfigColumnId=id+"05",IsShow=1}]);
            if(parent is null)continue;
            catalog.ConfigColumns.Add(new(){Id=id+"06",ConfigTableId=id,ColumnName=parent,LogicalType="int64",LinkConfigTableId=parentTable});
            catalog.ConfigPageColumns.Add(new(){Id=page+"06",ConfigPageId=page,ConfigColumnId=id+"06",IsShow=1});
        }
        var country=new{key="country",table="function_country",columnPrefix="country"};
        var area=new{key="area",table="function_area",columnPrefix="area",parentField="country_id"};
        var place=new{key="place",table="function_place",columnPrefix="place",parentField="area_id"};
        catalog.ConfigColumns.AddRange([
            new(){Id="27",ConfigTableId="2",ColumnName="tree_node_id",LogicalType="int64",LinkConfigTableId="3",IsVirtualColumn=1,
                ControlKind="CascadeTree",DefaultValue="9007199254740993",ControlDetail=
                """{"sourceMode":"selfTree","selfTree":{"table":"function_tree_node","parentField":"parent_node_id","nameField":"name"}}"""},
            new(){Id="28",ConfigTableId="2",ColumnName="area_id",LogicalType="int64",LinkConfigTableId="5",IsVirtualColumn=1,
                ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{country,area}})},
            new(){Id="29",ConfigTableId="2",ColumnName="place_id",LogicalType="string",LinkConfigTableId="6",IsVirtualColumn=1,
                ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{country,area,place}})}]);
        foreach(var id in new[]{"27","28","29"})
            catalog.ConfigPageColumns.Add(new(){Id="102"+id,ConfigPageId="102",ConfigColumnId=id,IsEdit=1});
        return catalog;
    }

    private static string SourceRoot([CallerFilePath] string file = "") =>
        Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ??
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));

    /// <summary>只回收本测试创建的独占工作目录。</summary>
    public void Dispose() { if (Directory.Exists(output)) Directory.Delete(output,true); }
}
