using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定候选可跨行归属，生成的普通 CRUD 仍只能读写发布身份自己的行。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed partial class GlobalScopedCandidateTests
{
    private const string ApplicationNamespace="Example.GlobalScopedCandidate";

    [Theory]
    [InlineData(false,0,"int64")]
    [InlineData(false,1,"string")]
    [InlineData(true,0,"string")]
    [InlineData(true,1,"int64")]
    public async Task Generated_global_candidate_and_owned_service_paths_are_distinct(bool multiple,int storage,string projectType)
    {
        var catalog=Catalog(multiple,storage,projectType);
        var model=GenerationModel.Create(catalog,ApplicationNamespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var table=model.Tables.Single(table=>table.Source.Id=="1");
        var selection=table.Pages.Single(page=>page.Source.Id=="10");
        var owned=table.Pages.Single(page=>page.Source.Id=="19");
        Assert.True(NonRedundantReadCompilation.GlobalSelectionPage(table,selection));
        Assert.False(NonRedundantReadCompilation.GlobalSelectionPage(table,owned));
        Assert.Contains(FixedPageQueryCompilation.For(table,owned).IdentityScopes,scope=>scope.Source=="project");

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-scoped-candidate");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ApplicationNamespace);
            var source=string.Join("\\n",Directory.GetFiles(generated,"*.g.cs",SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.Contains("PageAsync(CandidateReadPlan10",source);
            Assert.Contains("StatisticsAsync(CandidateReadPlan10",source);
            Assert.Contains("PageAsync(ReadPlan19",source);
            Assert.Contains("KeyRowsAsync(ReadPlan10",source);
            Assert.Contains("""CompileGlobalCandidate("1","10",""",source);
            Assert.Contains(multiple?"DerivedCheckboxPlan30_38":"DerivedOptionsPlan30_34",source);
            if(multiple)Assert.Contains("KeyRowsAsync(CandidateReadPlan10",source);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally
        {
            if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>全局逻辑编号沿原 List 和 SelectPage 发布，同项目普通 CRUD 保留项目约束。</summary>
    [Theory]
    [InlineData("int32", false, 0)]
    [InlineData("int64", true, 0)]
    [InlineData("int32", true, 1)]
    [InlineData("int64", false, 1)]
    public async Task Global_derived_integer_reaches_both_generated_single_controls(string logicalType, bool rowProject, int sourceStorage)
    {
        var catalog = rowProject ? Catalog(false, 0, "int64") : GlobalDerivedTargetGenerationTests.Catalog(0, "string", "int64");
        catalog.ConfigColumns.Add(new()
        {
            Id = "25", ConfigTableId = "2", YzProjectId = "100", ColumnName = "code", LogicalType = logicalType
        });
        catalog.ConfigPageColumns.Add(new()
        {
            Id = "2025", ConfigPageId = "20", ConfigColumnId = "25", YzProjectId = "100", IsShow = 1
        });
        foreach (var column in catalog.ConfigColumns.Where(column => column.Id is "14" or "35" or "40"))
            column.LogicalType = logicalType;
        catalog.ConfigColumns.Single(column => column.Id == "14").Extra["columnFullName"] = JsonSerializer.SerializeToElement("global_target.code");
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = sourceStorage;
        catalog.ConfigPageColumns.Single(field => field.Id == "3038").Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        catalog.ConfigPageColumns.Single(field => field.Id == "3040").Extra.Remove("isSearch");

        var model = GenerationModel.Create(catalog, ApplicationNamespace);
        var consumer = model.Tables.Single(table => table.Source.Id == "3");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var binding = Assert.Single(SelectionNameFilterCompilation.ForPage(model, consumer, Assert.Single(consumer.Pages)).Bindings);
        var names = new[] { "global_source_global_target_name", "selected_global_source_global_target_name" };
        Assert.Equal(sourceStorage == 0 ? SelectionNameFilterKind.JoinedIntegerLike : SelectionNameFilterKind.IntegerLike, binding.Kind);
        if (sourceStorage == 0)
            Assert.All(NonRedundantReads.Compile(catalog, "3", names).OutputTypes, field => Assert.Equal(logicalType, field.Value));
        else
            AssertManagedIntegerCache(model, "global_target", names);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-integer");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, generated, ApplicationNamespace);
            var source = await File.ReadAllTextAsync(Path.Combine(generated, "Services/CachedConsumerService.g.cs"));
            foreach (var id in new[] { "34", "38" })
            {
                Assert.Contains("DerivedOptionsPlan30_" + id, source);
                Assert.Contains("ImportRelation30_" + id + "Async", source);
                Assert.Contains("DerivedOptionsRows30_" + id + "Async", source);
            }
            Assert.Contains("FixedReadComparison.IntegerLabelContains", source);
            Assert.Contains(rowProject ? "CompileGlobalCandidate" : "CompileRelation", source);
            if (sourceStorage == 1) Assert.Contains("StringNameQuery.ApplyIntegerLike", source);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>全局物理编号在两种目标存储下使用同一发布页和 Excel 查找。</summary>
    [Theory]
    [InlineData(0, "int32", false)]
    [InlineData(0, "int64", true)]
    [InlineData(1, "int32", true)]
    [InlineData(1, "int64", false)]
    public async Task Global_physical_number_reaches_search_selection_and_workbook(int storage, string logicalType, bool rowProject)
    {
        var catalog = PhysicalNumberCatalog(storage, logicalType, rowProject);
        var model = GenerationModel.Create(catalog, ApplicationNamespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        var binding = Assert.Single(SelectionNameFilterCompilation.ForPage(model, owner, Assert.Single(owner.Pages)).Bindings);
        Assert.Equal("selectedGlobalSourceCode", binding.FilterKey);
        Assert.Equal(SelectionNameFilterKind.JoinedIntegerLike, binding.Kind);
        var candidate = NonRedundantReads.CompileGlobalCandidate(catalog, "1", "10", ["id", "code"], ["status"]);
        Assert.Equal(logicalType, candidate.OutputTypes["code"]);

        // 计划必须拒绝另一发布项目，测试连接不可达，确保拒绝发生在连接前。
        using var session = new Metarill.Data.DatabaseSession(new()
        {
            Provider = "mysql", ExpectedDatabase = "offline",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new NonRedundantReader(session)
            .PageAsync(candidate, [], [], 1, 10, projectId: "101"));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-physical-number");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, generated, ApplicationNamespace);
            var service = await File.ReadAllTextAsync(Path.Combine(generated, "Services/CachedConsumerService.g.cs"));
            Assert.Contains("""CompileGlobalCandidate("1","10",new string[]{"id","code"}""", service);
            Assert.Contains("""new("code",FixedReadComparison.IntegerLabelContains,request.Keyword)""", service);
            Assert.Contains("""scope.Filters.Add(new("code", FixedReadComparison.Equal, key));""", service);
            Assert.Contains("DerivedOptionsRows30_34Async", service);
            Assert.Contains("DerivedOptionsRows30_38Async", service);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>全局 R0 来源的原 List 和 SelectPage 读取相同的固定整数名称，保持来源页项目归属。</summary>
    [Theory]
    [InlineData(0, false, "int32", false, false)]
    [InlineData(0, false, "int64", true, true)]
    [InlineData(1, false, "int32", true, false)]
    [InlineData(1, false, "int64", false, true)]
    [InlineData(0, true, "int32", false, true)]
    [InlineData(0, true, "int64", true, false)]
    public async Task Global_R0_source_reads_integer_names_and_keeps_original_workbook(
        int targetStorage, bool derived, string logicalType, bool targetHasProject, bool sourceHasProject)
    {
        var catalog = derived
            ? targetHasProject ? Catalog(false, 0, "string") : GlobalDerivedTargetGenerationTests.Catalog(0, "string", "int64")
            : PhysicalNumberCatalog(targetStorage, logicalType, targetHasProject);
        var source = catalog.ConfigTables.Single(table => table.Id == "3");
        source.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        // 复用编号夹具中的标量页，附件仍由独立的文件所有者模型验证。
        catalog.ConfigColumns.RemoveAll(column => column.Id == "39");
        catalog.ConfigPageColumns.RemoveAll(column => column.ConfigColumnId == "39");
        if (!sourceHasProject)
        {
            catalog.ConfigColumns.RemoveAll(column => column.Id == "32");
            catalog.ConfigPages.Single(page => page.Id == "30").WhereSql = "";
        }
        if (derived)
        {
            catalog.ConfigColumns.Add(new() { Id = "25", ConfigTableId = "2", YzProjectId = "100", ColumnName = "code", LogicalType = logicalType });
            foreach (var column in catalog.ConfigColumns.Where(column => column.Id is "14" or "35" or "40"))
                column.LogicalType = logicalType;
            catalog.ConfigColumns.Single(column => column.Id == "14").Extra["columnFullName"] = JsonSerializer.SerializeToElement("global_target.code");
            catalog.ConfigPageColumns.Single(field => field.Id == "3038").Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
            catalog.ConfigPageColumns.Single(field => field.Id == "3040").Extra.Remove("isSearch");
        }

        var model = GenerationModel.Create(catalog, ApplicationNamespace);
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        var page = Assert.Single(owner.Pages);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        Assert.Equal(SelectionNameFilterKind.JoinedIntegerLike, Assert.Single(SelectionNameFilterCompilation.ForPage(model, owner, page).Bindings).Kind);
        var names = catalog.ConfigColumns.Where(column => column.Id is "35" or "40").Select(column => column.ColumnName).ToArray();
        var read = NonRedundantReads.Compile(catalog, "3", names);
        Assert.All(read.OutputTypes, field => Assert.Equal(logicalType, field.Value));
        Assert.DoesNotContain("cached_consumer", MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns.Keys);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-r0-integer-names");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ApplicationNamespace);
            var service = await File.ReadAllTextAsync(Path.Combine(output, "Services/CachedConsumerService.g.cs"));
            foreach (var id in new[] { "34", "38" })
            {
                Assert.Contains("DerivedOptionsPlan30_" + id, service);
                Assert.Contains("ImportRelation30_" + id + "Async", service);
                Assert.Contains("DerivedOptionsRows30_" + id + "Async(request.ParentId", service);
            }
            Assert.Contains("FixedReadComparison.IntegerLabelContains", service);
            Assert.Contains("actor.ProjectId!=\"100\"", service);
            Assert.Contains("Export30Async", service);
            Assert.Contains("Detail30Async", service);
            Assert.Equal(sourceHasProject, service.Contains("query=query.Where(entity=>entity.YzProjectId==", StringComparison.Ordinal));
            Assert.Equal(sourceHasProject, service.Contains("result.Add(new(\"yz_project_id\",FixedReadComparison.Equal,", StringComparison.Ordinal));
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type = assembly.GetType(ApplicationNamespace + ".Generated.Services.CachedConsumerService", throwOnError: true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            if (derived)
            {
                // 最后一层与元数据整数类型不一致时，生成的联接不能借中间别名掩盖类型错误。
                catalog.ConfigColumns.Single(column => column.Id == "25").LogicalType = logicalType == "int64" ? "int32" : "int64";
                Assert.Throws<NotSupportedException>(() => NonRedundantReads.Compile(catalog, "3", names));
            }
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>R1 来源保存同类型整数缓存，普通或全局目标都挂接原维护和改名传播。</summary>
    [Theory]
    [InlineData(0, "int32", false)]
    [InlineData(1, "int64", false)]
    [InlineData(0, "int64", true)]
    [InlineData(1, "int32", true)]
    public async Task R1_source_integer_cache_is_written_filtered_propagated_and_backfilled(int storage, string logicalType, bool global)
    {
        var catalog = PhysicalNumberCatalog(storage, logicalType, rowProject: true);
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = 1;
        if (!global) catalog.ConfigTables.Single(table => table.Id == "1").Extra.Clear();
        var model = GenerationModel.Create(catalog, ApplicationNamespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        AssertManagedIntegerCache(model, "global_source", ["global_source_code", "selected_global_source_code"]);
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        var page = Assert.Single(owner.Pages);
        Assert.DoesNotContain(page.Writable, column => column.Source.Id is "35" or "40");
        Assert.Equal(SelectionNameFilterKind.IntegerLike, Assert.Single(SelectionNameFilterCompilation.ForPage(model, owner, page).Bindings).Kind);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-r1-integer-cache");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ApplicationNamespace);
            var service = await File.ReadAllTextAsync(Path.Combine(output, "Services/CachedConsumerService.g.cs"));
            Assert.Contains("StringNameQuery.ApplyIntegerLike", service);
            Assert.Contains("AfterMutationAsync", service);
            Assert.Contains("entity.GlobalSourceCode=Metarill.Data.Queries.NonRedundantReads.Value", service);
            Assert.Contains("DerivedOptionsRows30_34Async", service);
            Assert.Contains("ImportRelation30_38Async", service);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>全局 R1 原 List 接通当前值、原工作簿、同事务传播与回填。</summary>
    [Theory]
    [InlineData(0, false, "int32", "string")]
    [InlineData(1, false, "int64", "int64")]
    [InlineData(0, true, "int64", "string")]
    public async Task Global_R1_List_writes_integer_names_from_fixed_targets(int storage, bool derived, string logicalType, string foreignType)
    {
        var catalog = derived ? GlobalDerivedTargetGenerationTests.Catalog(0, foreignType, "int64") : PhysicalNumberCatalog(storage, logicalType, rowProject: false);
        var source = catalog.ConfigTables.Single(table => table.Id == "3");
        source.IsRongyu = 1;
        source.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        catalog.ConfigPages.Single(page => page.Id == "30").WhereSql = "";
        catalog.ConfigColumns.RemoveAll(field => field.Id is "32" or "38" or "40");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "38" or "40");
        catalog.ConfigColumns.Single(field => field.Id == "34").LogicalType = foreignType;
        var cache = catalog.ConfigColumns.Single(field => field.Id == "35");
        cache.LogicalType = logicalType;
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == cache.Id);
        if (storage == 1)
            cache.Extra["columnFullName"] = JsonSerializer.SerializeToElement("global_source_id.code");
        if (derived)
        {
            catalog.ConfigColumns.Add(new() { Id = "25", ConfigTableId = "2", YzProjectId = "100", ColumnName = "code", LogicalType = logicalType });
            var display = catalog.ConfigColumns.Single(field => field.Id == "14");
            display.LogicalType = logicalType;
            display.Extra["columnFullName"] = JsonSerializer.SerializeToElement("global_target.code");
        }

        var model = GenerationModel.Create(catalog, ApplicationNamespace);
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        var page = Assert.Single(owner.Pages);
        Assert.Contains(page.Readable, column => column.Source.Id == cache.Id);
        Assert.DoesNotContain(page.Writable, column => column.Source.Id == cache.Id);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        AssertManagedIntegerCache(model, derived ? "global_target" : "global_source", [cache.ColumnName]);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-r1-integer-cache");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ApplicationNamespace);
            var service = await File.ReadAllTextAsync(Path.Combine(output, "Services/CachedConsumerService.g.cs"));
            Assert.Contains("DerivedOptionsRows30_34Async", service);
            Assert.Contains("ImportRelation30_34Async", service);
            Assert.Contains("FixedReadComparison.IntegerLabelContains", service);
            Assert.Contains("actor.ProjectId!=\"100\"", service);
            Assert.Contains("AfterMutationAsync", service);
            var property = GenerationModel.Pascal(cache.ColumnName);
            Assert.Contains("entity." + property + "=Metarill.Data.Queries.NonRedundantReads.Value", service);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>整数缓存缺少维护标记或类型不符时，生成器停止发布该关系。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void R1_integer_cache_with_an_unmanaged_or_mismatched_column_is_rejected(bool wrongType)
    {
        var catalog = PhysicalNumberCatalog(1, "int64", rowProject: false);
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = 1;
        var cache = catalog.ConfigColumns.Single(column => column.Id == "35");
        if (wrongType) cache.LogicalType = "int32";
        else cache.Extra.Remove("isJoinColumn");
        var model = GenerationModel.Create(catalog, ApplicationNamespace);
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        Assert.Throws<InvalidOperationException>(() => RelationCompilation.ForPage(model, Assert.Single(owner.Pages)));
    }

    /// <summary>保留仓库真实维护目录，只有实际登记的字段与目标才接受生成。</summary>
    private static void AssertManagedIntegerCache(GenerationModel model, string target, string[] fields)
    {
        var maintenance = MixedRelationPropagationCompilation.ForModel(model);
        Assert.Equal(fields, maintenance.MixedSourceColumns["cached_consumer"]);
        Assert.Contains(target, maintenance.MixedTargets);
    }

    /// <summary>目标直接存整数编码，源表使用原外键和标准辅助名称路径。</summary>
    private static MetadataCatalog PhysicalNumberCatalog(int storage, string logicalType, bool rowProject)
    {
        var catalog = rowProject ? Catalog(false, 0, "int64") : GlobalDerivedTargetGenerationTests.Catalog(0, "string", "int64");
        catalog.ConfigTables.Single(table => table.Id == "1").IsRongyu = storage;
        catalog.ConfigColumns.RemoveAll(field => field.Id is "13" or "14");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "13" or "14");
        catalog.ConfigColumns.Add(new()
        {
            Id = "16", ConfigTableId = "1", YzProjectId = "100", ColumnName = "code", LogicalType = logicalType
        });
        catalog.ConfigPageColumns.Add(new() { Id = "1016", ConfigPageId = "10", ConfigColumnId = "16", YzProjectId = "100", IsShow = 1 });
        foreach (var foreign in catalog.ConfigColumns.Where(field => field.Id is "34" or "38"))
            foreign.ControlDetail = foreign.ControlDetail.Replace("global_target_name", "code", StringComparison.Ordinal);
        foreach (var field in catalog.ConfigColumns.Where(field => field.Id is "35" or "40"))
        {
            field.LogicalType = logicalType;
            field.ColumnName = field.ColumnName.Replace("_global_target_name", "_code", StringComparison.Ordinal);
            field.Extra["columnFullName"] = JsonSerializer.SerializeToElement(
                field.Id == "35" ? "global_source.code" : "selected_global_source.code");
        }
        catalog.ConfigPageColumns.Single(field => field.Id == "3038").Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        catalog.ConfigPageColumns.Single(field => field.Id == "3040").Extra.Remove("isSearch");
        return catalog;
    }

    /// <summary>全局物理候选用途不会为普通读和非物理字段开放 R1 固定读取。</summary>
    [Fact]
    public void Physical_candidate_requires_explicit_global_page_and_physical_scalar()
    {
        var catalog = PhysicalNumberCatalog(1, "int64", false);
        Assert.Throws<InvalidOperationException>(() => NonRedundantReads.Compile(catalog, "1", ["id", "code"]));
        var code = catalog.ConfigColumns.Single(field => field.Id == "16");
        code.IsVirtualColumn = 1;
        Assert.Throws<NotSupportedException>(() => NonRedundantReads.CompileGlobalCandidate(catalog, "1", "10", ["id", "code"]));
        code.IsVirtualColumn = 0;
        catalog.ConfigTables.Single(table => table.Id == "1").Extra.Clear();
        Assert.Throws<NotSupportedException>(() => NonRedundantReads.CompileGlobalCandidate(catalog, "1", "10", ["id", "code"]));
    }

    [Fact]
    public void Candidate_plan_rejects_unpublished_or_context_dependent_target_pages()
    {
        var catalog=Catalog(false,0,"int64");
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileGlobalCandidate(catalog,"1","19",["id"]));
        Assert.Throws<InvalidOperationException>(()=>NonRedundantReads.CompileGlobalCandidate(catalog,"1","30",["id"]));
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileGlobalCandidate(catalog,"3","30",["id"]));
        var page=catalog.ConfigPages.Single(page=>page.Id=="10");
        page.WhereSql="yz_project_id=@current_yz_project_id";
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileGlobalCandidate(catalog,"1","10",["id"]));
    }

    /// <summary>同一目标的候选页和普通业务页使用不同固定范围，所有元数据来自真实生成模型。</summary>
    private static MetadataCatalog Catalog(bool multiple,int storage,string projectType)
    {
        var catalog=multiple
            ?GlobalMultipleDerivedTargetGenerationTests.Catalog(storage,false,"int64")
            :GlobalDerivedTargetGenerationTests.Catalog(storage,"int64","int64");
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId=catalog.YzProjectId,
            ColumnName="yz_project_id",Name="项目归属",LogicalType=projectType,ColumnLength=projectType=="string"?"20":""});
        catalog.ConfigPages.Add(new(){Id="19",ConfigTableId="1",Name="项目业务记录",YzProjectId=catalog.YzProjectId,YzAdminTypeId="9",
            IsNew=1,IsDelete=1,IsCopy=1,IsExport=1,WhereSql="yz_project_id=@current_yz_project_id",
            Extra=new(){["type"]=JsonSerializer.SerializeToElement(0)}});
        foreach(var column in catalog.ConfigPageColumns.Where(column=>column.ConfigPageId=="10").ToArray())
            catalog.ConfigPageColumns.Add(new(){Id="19"+column.ConfigColumnId,ConfigPageId="19",ConfigColumnId=column.ConfigColumnId,
                YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=column.IsEdit});
        catalog.ConfigMenus.Add(new(){Id="70019",YzProjectId=catalog.YzProjectId,YzAdminTypeId="9",Name="项目业务记录",Kind="GENERATED_PAGE",ConfigPageId="19"});
        return catalog;
    }
}
