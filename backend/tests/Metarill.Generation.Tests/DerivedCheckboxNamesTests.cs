using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Metarill.Data.Queries;
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
    /// <summary>联接排序只能改变原复选候选的顺序，不能改变已选名称、保存或 Excel 的实际范围。</summary>
    [Theory]
    [InlineData(0, true, "legacy")]
    [InlineData(0, false, "target")]
    [InlineData(1, true, "override")]
    [InlineData(1, false, "legacy")]
    public async Task Checkbox_uses_an_open_join_for_candidate_order(int storage, bool derived, string orderSource)
    {
        const string ns = "Example.CheckboxJoinOrder";
        var catalog = storage == 1 ? RedundantDerivedCheckboxTests.Catalog("relation", false, "int64") : Catalog("relation", false);
        var source = catalog.ConfigColumns.Single(column => column.Id == "25");
        var name = catalog.ConfigColumns.Single(column => column.Id == "32");
        if (!derived)
        {
            name.Extra.Remove("isJoinColumn");
            name.Extra.Remove("columnFullName");
        }

        var dictionary = catalog.ConfigPages.Single(page => page.Id == "401");
        dictionary.WhereSql += " AND priority>=10";
        catalog.ConfigColumns.Add(new() { Id = "4101", YzProjectId = catalog.YzProjectId, ConfigTableId = "4", ColumnName = "priority", LogicalType = "int32" });
        catalog.ConfigPageColumns.Add(new() { Id = "4014101", YzProjectId = catalog.YzProjectId, ConfigPageId = "401", ConfigColumnId = "4101", IsShow = 1 });
        var sort = new ColumnMetadata { Id = "3101", YzProjectId = catalog.YzProjectId, ConfigTableId = "3", ColumnName = "sort_alias", LogicalType = "int32" };
        sort.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        sort.Extra["columnFullName"] = JsonSerializer.SerializeToElement("company.priority");
        catalog.ConfigColumns.Add(sort);
        var sortField = new PageColumnMetadata { Id = "3013101", YzProjectId = catalog.YzProjectId, ConfigPageId = "301", ConfigColumnId = "3101", IsShow = 1 };
        catalog.ConfigPageColumns.Add(sortField);
        const string order = "sort_alias desc, id asc";
        const string condition = "parent_id=@parentId AND segment=@formParameters.segment AND status=1";
        source.ControlDetail = orderSource == "legacy" ? $"甲;乙;{condition};{order}" :
            JsonSerializer.Serialize(new { tableId = "3", pageId = "301", whereSql = condition });
        if (orderSource == "target")
            catalog.ConfigPages.Single(page => page.Id == "301").OrderBy = order;
        if (orderSource == "override")
            catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "201" && field.ConfigColumnId == source.Id)
                .Extra["controlDetail"] = JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new { orderBy = order }));
        catalog = RedundantMultiNameCacheTests.Published(catalog);
        var model = GenerationModel.Create(catalog, ns);
        var owner = model.Tables.Single(table => table.Source.Id == "2");
        var page = owner.Pages.Single(item => item.Source.Id == "201");
        var control = owner.Columns.Single(column => column.Source.Id == source.Id);
        var definition = DatabaseCheckboxCompilation.Resolve(model, owner, page, control);
        Assert.True(definition.Definition is not null, definition.Reason);
        Assert.Equal(derived, definition.Definition.DerivedName);
        Assert.NotNull(definition.Definition.Conditions);
        Assert.NotNull(definition.Definition.SourceParent);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        if (storage == 1)
        {
            var cache = Assert.Single(DatabaseCheckboxCompilation.Caches(model, owner));
            Assert.Equal("name", cache.Value.Source.ColumnName);
        }
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-checkbox-join-order");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.RelationSourceService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            const BindingFlags planField = BindingFlags.Static | BindingFlags.NonPublic;
            const BindingFlags instanceField = BindingFlags.Instance | BindingFlags.NonPublic;
            var ranking = Assert.IsType<NonRedundantReadPlan>(service.GetField("CheckboxSortPlan201_25", planField)!.GetValue(null));
            Assert.Equal(new[] { "id" }, ranking.OutputTypes.Keys);
            var orderFields = Assert.IsType<FixedReadOrder[]>(service.GetField("CheckboxSortOrder201_25", planField)!.GetValue(null));
            Assert.Equal(new[] { new FixedReadOrder("sort_alias", true), new("id", false) }, orderFields);
            Assert.Matches(@"priority\x60?\s*>=\s*@", From(ranking));
            var serviceSource = await File.ReadAllTextAsync(Path.Combine(output, "Services", "RelationSourceService.g.cs"));
            Assert.Contains("InReadSnapshotAsync<IReadOnlyList<DatabaseCheckboxOption>>", serviceSource);
            Assert.Contains("rows.Select(row=> (object?)row.Id)", serviceSource);
            Assert.Contains("if(rows.Count>DatabaseCheckboxValue.MaximumItems)", serviceSource);
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            if (derived)
            {
                var labels = Assert.IsType<NonRedundantReadPlan>(service.GetField("DerivedCheckboxPlan201_25", planField)!.GetValue(null));
                Assert.Equal("string", labels.OutputTypes["name"]);
                var expressions = Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(MySql(labels).GetType()
                    .GetProperty("Expressions", instanceField)!.GetValue(MySql(labels)));
                Assert.DoesNotContain("sort_alias", expressions.Keys);
            }

            object MySql(NonRedundantReadPlan plan) => plan.GetType().GetProperty("MySql", instanceField)!.GetValue(plan)!;
            string From(NonRedundantReadPlan plan) =>
                Assert.IsType<string>(MySql(plan).GetType().GetProperty("From", instanceField)!.GetValue(MySql(plan)));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }

        catalog.ConfigPageColumns.RemoveAll(field => field.Id == sortField.Id);
        model = GenerationModel.Create(catalog, ns);
        owner = model.Tables.Single(table => table.Source.Id == "2");
        page = owner.Pages.Single(item => item.Source.Id == "201");
        control = owner.Columns.Single(column => column.Source.Id == source.Id);
        Assert.Null(DatabaseCheckboxCompilation.Resolve(model, owner, page, control).Definition);
    }

    /// <summary>候选、已选校验和 Excel 使用控件条件与实际目标页的同一交集。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(false,false,"physical")]
    [InlineData(false,false,"joined")]
    [InlineData(true,false,"joined")]
    [InlineData(false,true,"physical")]
    public async Task Checkbox_endpoints_apply_the_bound_target_page(bool derived,bool global,string exact="")
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
        var whereSql="segment=@formParameters.segment AND status=1";
        if(global)
        {
            var target=catalog.ConfigTables.Single(table=>table.Id=="3");
            target.IsRongyu=1;
            target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId=="3"&&column.ColumnName=="yz_project_id");
            source.Extra.Remove("linkConfigPageId");
            whereSql+=" AND parent_id=@parentId";
        }
        source.ControlDetail=JsonSerializer.Serialize(new{tableId="3",pageId=global?null:"301",nameField="name",whereSql});
        var sourceField=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==source.Id);
        sourceField.IsShow=sourceField.IsEdit=1;
        catalog.ConfigColumns.Add(new(){Id="3005",ConfigTableId="3",ColumnName="sheet_status",LogicalType="int32"});
        var published=catalog.ConfigPages.Single(page=>page.Id=="301");
        published.WhereSql=global?"sheet_status=41":published.WhereSql+" AND sheet_status=41";
        published.OrderBy="sheet_status desc";
        if(exact.Length>0)
        {
            published.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
            var match=new ColumnMetadata{Id="3006",ConfigTableId="3",ColumnName="scope_code",LogicalType="string"};
            if(exact=="joined")
            {
                match.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
                match.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");
            }
            catalog.ConfigColumns.Add(match);
        }
        catalog.YzAdminTypes.Clear();
        RedundantMultiNameCacheTests.Published(catalog);
        catalog.YzAdminTypes.Single().Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        var model=GenerationModel.Create(catalog,ns);
        var sourceTable=model.Tables.Single(table=>table.Source.Id=="2");
        var resolution=DatabaseCheckboxCompilation.Resolve(model,sourceTable,sourceTable.Pages.Single(page=>page.Source.Id=="201"),
            sourceTable.Columns.Single(column=>column.Source.Id=="25"));
        Assert.True(resolution.Definition is not null,resolution.Reason);
        var checkbox=resolution.Definition;
        if(exact.Length>0)Assert.Equal("scope_code",Assert.IsType<ExactDataManageModel>(checkbox.ExactTarget).Column.Source.ColumnName);
        Assert.Equal("301",checkbox.PhysicalTargetPage!.Source.Id);
        Assert.Equal("sheet_status",checkbox.Order[0].Column.Source.ColumnName);
        Assert.True(checkbox.Order[0].Descending);
        Assert.Contains(checkbox.Scopes,scope=>scope.Source=="parent"&&scope.Column.Source.ColumnName=="parent_id");

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-excel-page");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var generated=Assert.Single(Directory.GetFiles(Path.Combine(directory,"generated"),"CheckboxExcelScopeSourceService.g.cs",SearchOption.AllDirectories));
            var code=await File.ReadAllTextAsync(generated);
            var read=derived?"DerivedCheckboxRows":"CheckboxScope";
            Assert.Contains(read+"201_25Async(request.ParentId,actor,form",code);
            Assert.Contains(read+"201_25Async(entity.ParentId",code);
            Assert.Contains(read+"201_25Async(parentId,actor,",code);
            Assert.Contains("CheckboxTargetCondition201_25.ApplyConstants",code);
            if(exact.Length>0)
            {
                Assert.Contains("""ResolveExactDataManage("301", "list", "real_name")""",code);
                Assert.Contains("scope_code",code);
                Assert.Contains(derived?"filters.Add(new(":exact=="joined"?"ExactDataManageSql.ApplyDerived":"ExactDataManageSql.Apply(",code);
            }
            Assert.Contains(derived
                ?"condition.And(publishedCondition)"
                :"query=query.Where(row=>row.ParentId==",code);

            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.CheckboxExcelScopeSourceService",true)!;
            const System.Reflection.BindingFlags privateStatic=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            var plan=Assert.IsType<Metarill.Data.Queries.FixedConditionSqlPlan>(
                service.GetField("CheckboxTargetCondition201_25",privateStatic)!.GetValue(null));
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

    /// <summary>普通冗余复选字典按登录归属筛选，冗余来源的维护则从实际记录取归属。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(1, false, true)]
    [InlineData(1, true, true)]
    [InlineData(0, true, false, true)]
    [InlineData(1, true, false, true)]
    [InlineData(1, true, true, true)]
    [InlineData(0, true, false, false, "form")]
    [InlineData(0, true, false, true, "form")]
    [InlineData(1, true, false, true, "form")]
    [InlineData(1, true, true, true, "form")]
    [InlineData(1, true, true, true, "mixed")]
    [InlineData(0, true, false, false, "source-compound")]
    [InlineData(1, true, false, false, "source-compound")]
    [InlineData(1, true, true, true, "source-compound")]
    [InlineData(0, true, false, false, "source-text")]
    [InlineData(1, true, false, false, "source-text")]
    [InlineData(1, true, true, true, "source-text")]
    public async Task Project_stored_checkbox_preserves_published_account_scope(int sourceStorage, bool withParent,
        bool alternateName = false, bool compoundTarget = false, string sourceParentMode = "")
    {
        const string ns = "Example.ProjectStoredCheckbox";
        const string output = "target_names";
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags fields = BindingFlags.Static | BindingFlags.NonPublic;
        const long store = 9007199254740993L;
        var catalog = Catalog("stored_checkbox", false);
        var owner = catalog.ConfigTables.Single(table => table.Id == "2");
        owner.IsRongyu = sourceStorage;
        var target = catalog.ConfigTables.Single(table => table.Id == "3");
        target.IsRongyu = 1;
        var name = catalog.ConfigColumns.Single(column => column.Id == "32");
        name.Extra.Remove("isJoinColumn");
        name.Extra.Remove("columnFullName");
        var relation = catalog.ConfigColumns.Single(column => column.Id == "25");
        relation.ControlDetail = JsonSerializer.Serialize(new
        {
            tableId = "3", pageId = "301", nameField = "name",
            whereSql = sourceStorage == 1 && withParent && !alternateName ? "status = 41" : ""
        });
        foreach (var tableId in new[] { owner.Id, target.Id })
            catalog.ConfigColumns.Add(new()
            {
                Id = tableId + "991", ConfigTableId = tableId, YzProjectId = catalog.YzProjectId,
                ColumnName = "store_id", LogicalType = "int64"
            });
        var scope = "yz_project_id=@current_yz_project_id AND store_id=@current_store_id";
        var parentScope = withParent ? scope + " AND parent_id=@parentId" : scope;
        var compoundSource = sourceParentMode == "source-compound";
        var textSource = sourceParentMode == "source-text";
        if (textSource) catalog.ConfigColumns.Single(column => column.Id == "26").LogicalType = "string";
        if (compoundSource)
            catalog.ConfigColumns.Add(new()
            {
                Id = "2993", ConfigTableId = owner.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = "backup_parent_id", LogicalType = "string", LinkConfigTableId = "1"
            });
        foreach (var sourcePage in catalog.ConfigPages.Where(page => page.ConfigTableId == owner.Id))
        {
            var formParent = sourceParentMode == "form" || sourceParentMode == "mixed" && sourcePage.Id == "201";
            sourcePage.WhereSql = formParent ? scope :
                compoundSource ? parentScope + " AND backup_parent_id=@parentId" : parentScope;
            if (formParent)
                catalog.ConfigPageColumns.Add(new()
                {
                    Id = sourcePage.Id + "26", ConfigPageId = sourcePage.Id, ConfigColumnId = "26",
                    YzProjectId = catalog.YzProjectId, IsShow = 1, IsEdit = 1,
                    Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) }
                });
        }
        if (compoundSource || textSource)
        {
            var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
            if (!alternateName) sourcePage.Extra["importMethod"] = JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            if (textSource) sourcePage.Extra["exportMethod"] = JsonSerializer.SerializeToElement("edit");
        }
        if (sourceStorage == 0 && sourceParentMode == "form" && compoundTarget)
        {
            var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
            sourcePage.Extra["importMethod"] = JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            sourcePage.Extra["exportMethod"] = JsonSerializer.SerializeToElement("edit");
        }
        var dictionaryPage = catalog.ConfigPages.Single(page => page.Id == "301");
        dictionaryPage.WhereSql = parentScope + " AND status=41";
        if (compoundTarget)
        {
            var mainParent = catalog.ConfigColumns.Single(column =>
                column.ConfigTableId == target.Id && column.ColumnName == "parent_id");
            var parentTableId = mainParent.LinkConfigTableId.Length > 0
                ? mainParent.LinkConfigTableId
                : catalog.ConfigTables.Single(table => table.TableName == "stored_checkbox_parent").Id;
            catalog.ConfigColumns.Add(new()
            {
                Id = "3993", ConfigTableId = target.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = "review_parent_id", LogicalType = "string", LinkConfigTableId = parentTableId
            });
            dictionaryPage.WhereSql += " AND review_parent_id=@parentId";
        }
        if (alternateName && withParent)
            catalog.ConfigColumns.Single(column => column.ConfigTableId == target.Id && column.ColumnName == "parent_id")
                .LogicalType = "string";
        if (alternateName)
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = "3992", ConfigTableId = target.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = "title", LogicalType = "string"
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "3013992", ConfigPageId = "301", ConfigColumnId = "3992",
                YzProjectId = catalog.YzProjectId, IsShow = 1, IsEdit = 1
            });
            catalog.ConfigColumns.Add(new()
            {
                Id = "2992", ConfigTableId = owner.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = "target_titles", LogicalType = "string", ColumnLength = "max",
                Extra = new()
                {
                    ["isJoinColumn"] = JsonSerializer.SerializeToElement(1),
                    ["columnFullName"] = JsonSerializer.SerializeToElement("target.title")
                }
            });
            var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
            var copy = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(sourcePage))!;
            copy.Id = "204";
            if (sourceParentMode == "mixed") copy.WhereSql = parentScope;
            catalog.ConfigPages.Add(copy);
            foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == "201" &&
                         (sourceParentMode != "mixed" || field.ConfigColumnId != "26")).ToArray())
            {
                var mapped = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                mapped.Id = "204" + field.Id;
                mapped.ConfigPageId = "204";
                if (mapped.ConfigColumnId == relation.Id)
                    mapped.Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"nameField":"title"}""");
                catalog.ConfigPageColumns.Add(mapped);
            }
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "2042992", ConfigPageId = "204", ConfigColumnId = "2992",
                YzProjectId = catalog.YzProjectId, IsShow = 1, IsEdit = 0
            });
        }
        var actor = new Metarill.Core.Security.RuntimeActor(7, "合成门店", false, catalog.YzProjectId, "5") { StoreId = store };
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-project-stored-checkbox");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var model = GenerationModel.Create(catalog, ns);
            Assert.Empty(GenerationCapabilityAudit.Collect(model));
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.StoredCheckboxSourceService", true)!;
            var code = File.ReadAllText(Path.Combine(directory, "generated/Services/StoredCheckboxSourceService.g.cs"));
            Assert.Contains("CheckboxTargetCondition201_25.BindAccount(actor).ApplyConstants", code);
            Assert.Contains("CheckboxOptions201_25Async", code);
            Assert.Contains("ValidateCheckbox201_25Async", code);
            if (compoundSource || textSource)
            {
                var parentColumn = compoundSource ? "backup_parent_id" : "parent_id";
                var parentProperty = compoundSource ? "BackupParentId" : "ParentId";
                var scopeCall = sourceStorage == 0
                    ? "query=NonRedundantReads.ApplyTextParent"
                    : "query = global::Metarill.Data.Queries.NonRedundantReads.ApplyTextParent";
                Assert.Contains($"""{scopeCall}(query,session,"{parentColumn}",parentId!);""", code);
                Assert.Contains($"if(entity.{parentProperty}!=long.Parse(parentId!, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))", code);
                if (!alternateName)
                    Assert.Contains("var existing = await Scope201(actor, request.ParentId)", code);
                Assert.NotNull(service.GetMethod("Copy201Async"));
                var validate = service.GetMethod("ValidateParents201Async", instance)!;
                var ownerService = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(service);
                foreach (var invalid in new[] { "071", "71 ", "+71", "0", "9223372036854775808" })
                {
                    var arguments = new object?[] { invalid, actor, CancellationToken.None }
                        .Concat(validate.GetParameters().Skip(3).Select(parameter => parameter.DefaultValue)).ToArray();
                    var error = await Assert.ThrowsAsync<ArgumentException>(async () => await (Task)validate.Invoke(ownerService, arguments)!);
                    Assert.Contains("规范正整数父键", error.Message);
                }
                if (sourceStorage == 0)
                {
                    const string largeParent = "9007199254740993";
                    var filters = service.GetMethod("ReadFilters201", fields)!;
                    var request = new Metarill.Core.Contracts.PageRequest { ParentId = largeParent };
                    var result = Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(filters.Invoke(null, [request, actor]));
                    Assert.Equal(FixedReadFilter.ForTextParent(parentColumn, largeParent), Assert.Single(result, filter => filter.Field == parentColumn));
                    Assert.Contains($"""filters.Add(FixedReadFilter.ForTextParent("{parentColumn}",parentId!));""", code);
                    request.ParentId = "071";
                    Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => filters.Invoke(null, [request, actor])).InnerException);
                }
            }
            if (compoundSource)
            {
                Assert.Contains("submitted.Contains(\"backup_parent_id\")", code);
                Assert.Contains("string.Equals(entity.ParentId?.ToString(CultureInfo.InvariantCulture),entity.BackupParentId,StringComparison.Ordinal)", code);
            }
            if (sourceParentMode is "form" or "mixed")
            {
                var sourceTable = model.Tables.Single(table => table.Source.Id == owner.Id);
                var sourceField = sourceTable.Columns.Single(column => column.Source.Id == relation.Id);
                var page = sourceTable.Pages.Single(page => page.Source.Id == "201");
                var definition = DatabaseCheckboxCompilation.Resolve(model, sourceTable, page, sourceField).Definition;
                Assert.Equal("26", definition?.FormParentField?.Source.Id);
                Assert.Contains("var rows=selected.Length==0&&string.IsNullOrWhiteSpace(entity.ParentId", code);
                Assert.Contains("submitted.Contains(\"parent_id\")", code);
                var list = File.ReadAllText(Path.Combine(directory, "generated/frontend/pages/yzp/stored_checkbox_source-list-201.vue"));
                var detail = File.ReadAllText(Path.Combine(directory, "generated/frontend/pages/yzp/stored_checkbox_source-detail-201.vue"));
                Assert.Contains("\"formParentField\":\"parentId\"", list);
                Assert.Contains("this.$watch(()=>this.entity.parentId", detail);
                Assert.Contains("parentId:optionParent", detail);
                if (sourceStorage == 0)
                {
                    var dependency = ImportDependencyCompilation.ForPage(model, sourceTable, page);
                    Assert.Equal("26", dependency.Parents[relation.Id].Source.Id);
                    var order = dependency.Order.Select(column => column.Source.Id).ToList();
                    Assert.InRange(order.IndexOf("26"), 0, order.IndexOf(relation.Id) - 1);
                    Assert.Contains("var targetParentId=relationContext[\"parentId\"]", code);
                    Assert.Contains("CheckboxScope201_25Async(targetParentId,actor", code);
                    Assert.Contains("if(cell.GetArrayLength()==0&&string.IsNullOrWhiteSpace(targetParentId))", code);
                    Assert.Contains("values[entity.Id]=entity.TargetIds is null?null:\"[]\";continue;", code);
                }
            }
            if (compoundTarget)
                Assert.Contains("ApplyTextParent(query,session,\"review_parent_id\"", code);
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            var condition = Assert.IsType<FixedConditionSqlPlan>(
                service.GetField("CheckboxTargetCondition201_25", fields)!.GetValue(null));
            Assert.Throws<UnauthorizedAccessException>(() => condition.BindAccount(actor with { StoreId = null }));
            if (sourceStorage == 0)
            {
                var plan = Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan201", fields)!.GetValue(null));
                Assert.True(plan.RequiresAccount);
                Assert.Throws<UnauthorizedAccessException>(() => plan.BindAccount(actor with { StoreId = null }));
                var bound = plan.BindAccount(actor);
                var read = bound.GetType().GetProperty("MySql", instance)!.GetValue(bound)!;
                var predicate = Assert.Single(Property<IReadOnlyDictionary<string, string>>(read, "ArrayNamePredicates")).Value;
                Assert.Contains("multi_value.`store_id`", predicate);
                Assert.Contains(store, Property<IReadOnlyDictionary<string, object?>>(read, "Constants").Values);
                Assert.Equal(compoundTarget, predicate.Contains("review_parent_id", StringComparison.Ordinal));
                Assert.Equal(compoundSource, predicate.Contains("backup_parent_id", StringComparison.Ordinal));
                Assert.Equal(withParent, predicate.Contains("b.`parent_id`", StringComparison.Ordinal));
            }
            else
            {
                var maintenance = Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns + ".Generated.GeneratedRelationMaintenance", true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains(output, maintenance.MixedSourceColumns[owner.TableName]);
                var plans = Property<System.Collections.IDictionary>(maintenance, "Plans");
                var published = plans[owner.Id]!;
                Assert.Null(published.GetType().GetProperty("Error")!.GetValue(published));
                var read = published.GetType().GetProperty("Read")!.GetValue(published)!;
                Assert.False(Property<bool>(read, "RequiresAccount"));
                var sql = Assert.IsType<string>(read.GetType().GetMethod("Expression", instance)!.Invoke(read, [output]));
                Assert.Contains("b.`store_id`", sql);
                Assert.Contains("multi_value.`store_id`", sql);
                Assert.Equal(withParent, sql.Contains("b.`parent_id`", StringComparison.Ordinal));
                var dependencies = Property<IReadOnlyList<string>>(read, "SourceDependencies");
                Assert.Contains("store_id", dependencies);
                if (withParent) Assert.Contains("parent_id", dependencies);
                if (compoundSource)
                {
                    Assert.Contains("backup_parent_id", dependencies);
                    Assert.Contains("backup_parent_id", sql);
                }
                Assert.Equal(compoundTarget, sql.Contains("review_parent_id", StringComparison.Ordinal));
                if (alternateName)
                {
                    Assert.Contains("target_titles", maintenance.MixedSourceColumns[owner.TableName]);
                    Assert.Contains("CheckboxTargetCondition204_25.BindAccount(actor).ApplyConstants", code);
                    if (withParent) Assert.Contains("ApplyTextParent(query,session,\"parent_id\"", code);
                    Assert.NotNull(service.GetMethod("Import204Async"));
                    Assert.NotNull(service.GetMethod("Export204Async"));
                    var title = Assert.IsType<string>(read.GetType().GetMethod("Expression", instance)!
                        .Invoke(read, ["target_titles"]));
                    Assert.Contains("multi_value.`title`", title);
                    Assert.Contains("b.`store_id`", title);
                    Assert.Equal(withParent, title.Contains("b.`parent_id`", StringComparison.Ordinal));
                }
                Assert.Contains(Property<System.Collections.IEnumerable>(read, "Reverse").Cast<object>(), item =>
                    Equals(target.TableName, item.GetType().GetProperty("TargetTable")!.GetValue(item)) &&
                    Assert.IsAssignableFrom<IReadOnlyDictionary<string, IReadOnlySet<string>>>(
                        item.GetType().GetProperty("Outputs")!.GetValue(item)).TryGetValue(output, out var changes) &&
                    changes.Contains("store_id") && changes.Contains("name") &&
                    (!compoundTarget || changes.Contains("review_parent_id")));
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }

        if (sourceParentMode is "form" or "mixed")
        {
            var field = catalog.ConfigPageColumns.Single(setting => setting.ConfigPageId == "201" && setting.ConfigColumnId == "26");
            catalog.ConfigPageColumns.Remove(field);
            var invalid = GenerationModel.Create(catalog, ns);
            var sourceTable = invalid.Tables.Single(table => table.Source.Id == owner.Id);
            var sourceField = sourceTable.Columns.Single(column => column.Source.Id == relation.Id);
            var sourcePage = sourceTable.Pages.Single(page => page.Source.Id == "201");
            Assert.Null(DatabaseCheckboxCompilation.Resolve(invalid, sourceTable, sourcePage, sourceField).Definition);
            catalog.ConfigPageColumns.Add(field);
        }

        if (sourceStorage == 1)
        {
            var hidden = catalog.ConfigPages.Single(page => page.Id == "202");
            if (compoundSource)
            {
                var originalScope = hidden.WhereSql;
                hidden.WhereSql = originalScope.Replace(" AND backup_parent_id=@parentId", "", StringComparison.Ordinal);
                var incomplete = GenerationModel.Create(catalog, ns);
                var badParent = Assert.Throws<InvalidOperationException>(() => RelationPropagationCompilation.DerivedBindings(incomplete));
                Assert.Contains("所有来源入口一致的父键", badParent.Message);
                hidden.WhereSql = originalScope;
            }
            hidden.WhereSql = hidden.WhereSql.Replace(" AND store_id=@current_store_id", "", StringComparison.Ordinal);
            var model = GenerationModel.Create(catalog, ns);
            var error = Assert.Throws<InvalidOperationException>(() => RelationPropagationCompilation.DerivedBindings(model));
            Assert.Contains("物理账号归属", error.Message);
        }

        if (compoundTarget)
        {
            var conflicting = catalog.ConfigColumns.Single(column => column.Id == "3993");
            conflicting.LinkConfigTableId = "4";
            var error = Assert.Throws<InvalidOperationException>(() =>
                NonRedundantReads.IsProjectStoredCheckboxAccountPage(catalog, target, dictionaryPage));
            Assert.Contains("同一真实父表", error.Message);
        }

        static T Property<T>(object item, string property) =>
            Assert.IsAssignableFrom<T>(item.GetType().GetProperty(property, instance)!.GetValue(item));
    }

    /// <summary>业务名称投影和筛选执行显式物理目标页条件，并按原数组顺序保留未命中位置。</summary>
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    public void Checkbox_business_names_and_filter_follow_the_published_physical_page(int targetStorage, bool derived, bool withParent)
    {
        const string ns = "Example.CheckboxPageRead";
        var catalog = Catalog("checkbox_page_read", false);
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = targetStorage;
        var targetName = catalog.ConfigColumns.Single(column => column.Id == "32");
        if (!derived)
        {
            targetName.Extra.Remove("isJoinColumn");
            targetName.Extra.Remove("columnFullName");
        }
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
        var targetPage = catalog.ConfigPages.Single(page => page.Id == "301");
        sourcePage.WhereSql = "yz_project_id=@current_yz_project_id" + (withParent ? " AND parent_id=@parentId" : "");
        targetPage.WhereSql = sourcePage.WhereSql + " AND status=41";
        catalog.ConfigColumns.Single(column => column.Id == "25").ControlDetail =
            """{"tableId":"3","pageId":"301","nameField":"name"}""";

        var model = GenerationModel.Create(catalog, ns);
        var source = model.Tables.Single(table => table.Source.Id == "2");
        var page = source.Pages.Single(item => item.Source.Id == "201");
        var definition = Assert.Single(DatabaseCheckboxCompilation.ForPage(model, source, page));
        Assert.Equal("301", definition.PhysicalTargetPage?.Source.Id);
        var view = NonRedundantReadCompilation.ForPage(model, source, page);
        var binding = Assert.Single(view.MultiTargets);
        Assert.True(binding.ApplyPageScope);
        Assert.Equal("201", binding.SourcePageId);
        Assert.Equal("301", binding.TargetPageId);

        var names = definition.Names.Source.ColumnName;
        var plan = NonRedundantReadCompilation.CompilePage(model, source, ["id", names], [names], [], view.SingleTargets, view.MultiTargets);
        const System.Reflection.BindingFlags internalInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var sql = plan.GetType().GetProperty("MySql", internalInstance)!.GetValue(plan)!;
        var expressions = ReadProperty<IReadOnlyDictionary<string, string>>("Expressions");
        var predicate = Assert.Single(ReadProperty<IReadOnlyDictionary<string, string>>("ArrayNamePredicates")).Value;
        var expression = expressions[names];
        Assert.Contains("CASE WHEN", expression);
        Assert.Contains("multi_value.`status`", expression);
        Assert.Contains("multi_value.`status`", predicate);
        Assert.Contains("multi_value.`delete_no`", expression);
        Assert.Contains("multi_value.`yz_project_id`", predicate);
        Assert.Contains("CAST(multi_ids.member_id AS BINARY)", expression);
        Assert.Contains("CAST(checkbox_ids.member_id AS BINARY)", predicate);
        Assert.Equal(withParent, expression.Contains("b.`parent_id`", StringComparison.Ordinal));
        Assert.Equal(withParent, predicate.Contains("b.`parent_id`", StringComparison.Ordinal));
        var constants = ReadProperty<IReadOnlyDictionary<string, object?>>("Constants");
        var status = Assert.Single(constants);
        Assert.Equal(41, status.Value);
        Assert.Contains(status.Key, expression);
        Assert.Contains(status.Key, predicate);

        // 运行时按页批量回显时也带同一条件和实际来源父键，不依赖内联聚合。
        var projection = ReadProperty<System.Collections.IDictionary>("MultiProjections")[names]!;
        var projectionType = projection.GetType();
        var batchScope = Assert.IsType<string>(projectionType.GetProperty("TargetScope")!.GetValue(projection));
        var owner = projectionType.GetProperty("Owner")!.GetValue(projection);
        Assert.Contains(status.Key, batchScope);
        if (withParent)
        {
            Assert.NotNull(owner);
            var fields = Assert.IsAssignableFrom<IReadOnlyList<string>>(owner.GetType().GetProperty("Fields")!.GetValue(owner));
            Assert.Equal(["parent_id"], fields);
        }
        else Assert.Null(owner);

        var generated = DatabaseCheckboxRenderer.Service(model, source, page);
        Assert.Contains("CheckboxTargetCondition201_25.ApplyConstants", generated);
        Assert.Contains("CheckboxOptions201_25Async", generated);
        Assert.Contains("ValidateCheckbox201_25Async", generated);
        var excel = ExportRenderer.Service(model, source, page);
        Assert.Contains("ExportCheckbox201_25Async", excel);

        T ReadProperty<T>(string name) => Assert.IsAssignableFrom<T>(sql.GetType().GetProperty(name, internalInstance)!.GetValue(sql));
    }


    /// <summary>回显和名称筛选使用原控件固定条件，支持独立控件和显式目标页叠加。</summary>
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(1, true, true, true)]
    public void Checkbox_business_names_apply_control_form_constants_and_actor(int targetStorage,bool pageScope,bool controlParent,bool customParent=false)
    {
        var catalog=Catalog("checkbox_control_read",false);
        catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=targetStorage;
        var label=catalog.ConfigColumns.Single(column=>column.Id=="32");
        if(targetStorage==1)
        {
            label.Extra.Remove("isJoinColumn");
            label.Extra.Remove("columnFullName");
        }
        var field=catalog.ConfigColumns.Single(column=>column.Id=="25");
        field.Extra.Remove("linkConfigPageId");
        catalog.ConfigColumns.Single(column=>column.Id=="28").LogicalType="int64";
        catalog.ConfigColumns.Single(column=>column.Id=="38").LogicalType="int64";
        catalog.ConfigColumns.Add(new(){Id="3051",ConfigTableId="3",ColumnName="assigned_id",LogicalType="int64",YzProjectId=catalog.YzProjectId});
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        var sourceParentField=customParent?"parent_key":"parent_id";
        var targetParentField=customParent?"parent_key":"parent_id";
        if(customParent)
        {
            catalog.ConfigColumns.Single(column=>column.Id=="26").ColumnName=sourceParentField;
            var parent=catalog.ConfigColumns.Single(column=>column.Id=="36");
            parent.ColumnName=targetParentField;
            parent.LogicalType="int32";
            catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql=catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql.Replace("parent_id",sourceParentField,StringComparison.Ordinal);
        }
        sourcePage.WhereSql="yz_project_id=@current_yz_project_id"+(controlParent?" AND "+sourceParentField+"=@parentId":"");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="301");
        targetPage.WhereSql="yz_project_id=@current_yz_project_id AND status<>48";
        var whereSql="status=41 AND segment=@formParameters.checkboxControlReadSource.segment AND assigned_id=@current_yz_manager_id"+
            (controlParent?" AND "+targetParentField+"=@parentId":"");
        field.ControlDetail=JsonSerializer.Serialize(new {tableId="3",pageId=pageScope?"301":null,nameField="name",whereSql});
        var model=GenerationModel.Create(catalog,"Example.ControlRead");
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=source.Pages.Single(page=>page.Source.Id=="201");
        var definition=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,source,page));
        var view=NonRedundantReadCompilation.ForPage(model,source,page);
        var binding=Assert.Single(view.MultiTargets);
        Assert.Equal(pageScope,binding.ApplyPageScope);
        Assert.True(binding.ApplyControlScope);
        Assert.Equal(pageScope?"301":null,binding.TargetPageId);

        var name=definition.Names.Source.ColumnName;
        var plan=NonRedundantReadCompilation.CompilePage(model,source,["id",name],[name],[],view.SingleTargets,view.MultiTargets);
        Assert.True(plan.RequiresAccount);
        const long accountId=9007199254740998L;
        var bound=plan.BindAccount(new Metarill.Core.Security.RuntimeActor(accountId,"测试账号",false,catalog.YzProjectId,"5"));
        const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var sql=bound.GetType().GetProperty("MySql",hidden)!.GetValue(bound)!;
        var expression=Read<IReadOnlyDictionary<string,string>>("Expressions")[name];
        var predicate=Read<IReadOnlyDictionary<string,string>>("ArrayNamePredicates")[name];
        var projection=Read<System.Collections.IDictionary>("MultiProjections")[name]!;
        var batch=Assert.IsType<string>(projection.GetType().GetProperty("TargetScope")!.GetValue(projection));
        var parameters=Read<IReadOnlyDictionary<string,object?>>("Constants");
        Assert.Contains(accountId,parameters.Values);
        Assert.Contains(41,parameters.Values);
        Assert.Equal(pageScope,parameters.Values.Contains(48));
        foreach(var command in new[]{expression,predicate,batch})
        {
            Assert.Contains("multi_value.`segment`=b.`segment`",command);
            Assert.Contains("multi_value.`assigned_id`=",command);
            Assert.Equal(controlParent,command.Contains("b.`"+sourceParentField+"`",StringComparison.Ordinal));
            Assert.All(parameters.Keys,key=>Assert.Contains(key,command));
        }
        var owner=projection.GetType().GetProperty("Owner")!.GetValue(projection)!;
        Assert.True(Assert.IsType<bool>(owner.GetType().GetProperty("PreserveScalarTypes")!.GetValue(owner)));
        var fields=Assert.IsAssignableFrom<IReadOnlyList<string>>(owner.GetType().GetProperty("Fields")!.GetValue(owner));
        Assert.Equal(controlParent?[sourceParentField,"segment"]:new[]{"segment"},fields);


        T Read<T>(string property)=>Assert.IsAssignableFrom<T>(sql.GetType().GetProperty(property,hidden)!.GetValue(sql));
    }


    /// <summary>全局字典名称和筛选复用真实发布页，不从本项目列推断全局资格或项目限定。</summary>
    [Theory]
    [InlineData(1, false, "none", false, false)]
    [InlineData(1, true, "project", true, false)]
    [InlineData(1, false, "form", true, false)]
    [InlineData(0, true, "none", false, false)]
    [InlineData(0, true, "project", false, false)]
    [InlineData(0, false, "form", true, false)]
    [InlineData(0, false, "form", true, true)]
    [InlineData(0, false, "account-form", true, true)]
    [InlineData(0, false, "none", false, false, "joined")]
    [InlineData(0, true, "project", true, true, "physical")]
    [InlineData(0, false, "account-form", true, false, "joined")]
    [InlineData(0, true, "form", true, false, "joined")]
    [InlineData(0, false, "none", true, false, "joined", true)]
    [InlineData(0, false, "account-form", true, false, "joined", true)]
    [InlineData(0, true, "project", true, false, "physical", false, 0)]
    [InlineData(0, false, "none", true, false, "joined", false, 2, true)]
    [InlineData(0, true, "none", true, false, "joined", true, 2, true)]
    public async Task Checkbox_global_business_names_follow_the_released_dictionary(
        int storageMode,bool rowProject,string condition,bool explicitPage,bool derived,string alternateName="",bool alternateInnerPage=false,
        int exactPageType=-1,bool alternateDictionary=false)
    {
        const string ns="Example.GlobalCheckboxRead";
        var catalog=Catalog("checkbox_global_read",false);
        var target=catalog.ConfigTables.Single(table=>table.Id=="3");
        target.IsRongyu=storageMode;
        target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(!rowProject)catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==target.Id&&column.ColumnName=="yz_project_id");
        var name=catalog.ConfigColumns.Single(column=>column.Id=="32");
        if(!derived)
        {
            name.Extra.Remove("isJoinColumn");
            name.Extra.Remove("columnFullName");
        }
        if(exactPageType>=0&&!derived&&alternateName=="physical")
        {
            // 纯物理场景没有中间联接，避免普通字典业务页发布夹具遗留的关联。
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="37");
            catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="37");
        }
        if(derived||alternateName=="joined")
        {
            var company=catalog.ConfigTables.Single(table=>table.Id=="4");
            company.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==company.Id&&column.ColumnName=="yz_project_id");
            var companyPage=catalog.ConfigPages.Single(page=>page.ConfigTableId==company.Id);
            companyPage.WhereSql="";
            if(alternateName=="joined")
            {
                if(condition=="account-form")
                {
                    company.IsRongyu=0;
                    companyPage.Extra["type"]=JsonSerializer.SerializeToElement(2);
                }
                catalog.ConfigColumns.AddRange([
                    new(){Id="4051",ConfigTableId=company.Id,ColumnName="caption",LogicalType="string",YzProjectId=catalog.YzProjectId},
                    new(){Id="4052",ConfigTableId=company.Id,ColumnName="label_enabled",LogicalType="int32",YzProjectId=catalog.YzProjectId}
                ]);
                companyPage.WhereSql="label_enabled=7";
                if(alternateInnerPage)
                {
                    companyPage.WhereSql="label_enabled=17";
                    catalog.ConfigPages.Add(new(){Id="402",ConfigTableId=company.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",WhereSql="label_enabled=7"});
                    catalog.ConfigPageColumns.Add(new(){Id="40242",ConfigPageId="402",ConfigColumnId="42",YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
                    var companyLink=catalog.ConfigColumns.Single(column=>column.Id=="37");
                    companyLink.ControlDetail="""{"tableId":"4","pageId":"401","nameField":"name"}""";
                    catalog.ConfigPageColumns.Single(column=>column.Id=="30137").Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"pageId":"402"}""");
                    catalog.ConfigPages.Add(new(){Id="303",ConfigTableId="3",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",WhereSql="status=48"});
                    catalog.ConfigPageColumns.Add(new(){Id="30337",ConfigPageId="303",ConfigColumnId="37",YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
                }
            }
        }
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="301");
        var account=condition=="account-form";
        const long manager=9007199254740997L;
        targetPage.WhereSql="status=48";
        if(account)
        {
            catalog.ConfigColumns.Add(new(){Id="3052",ConfigTableId="3",ColumnName="handler_id",LogicalType="int64",YzProjectId=catalog.YzProjectId});
            targetPage.WhereSql+=" AND handler_id=@current_yz_manager_id";
        }
        var field=catalog.ConfigColumns.Single(column=>column.Id=="25");
        field.Extra.Remove("linkConfigPageId");
        var where=condition switch
        {
            "form" or "account-form"=>"segment=@formParameters.checkboxGlobalReadSource.segment",
            "project"=>"yz_project_id=@current_yz_project_id",
            _=>""
        };
        field.ControlDetail=JsonSerializer.Serialize(new{tableId="3",nameField="name",pageId=explicitPage?"301":null,whereSql=where});
        var publishedProject=exactPageType==2&&rowProject;
        if(exactPageType>=0)
        {
            if(alternateDictionary)
            {
                // 来源页切换发布页时，原字典的常量不得进入本页候选或名称投影。
                var actual=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(targetPage))!;
                actual.Id="302";
                targetPage.WhereSql="status=47";
                targetPage.Extra["type"]=JsonSerializer.SerializeToElement(0);
                catalog.ConfigPages.Add(actual);
                foreach(var column in catalog.ConfigPageColumns.Where(item=>item.ConfigPageId=="301").ToArray())
                {
                    var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(column))!;
                    copy.Id="302"+copy.ConfigColumnId;
                    copy.ConfigPageId="302";
                    catalog.ConfigPageColumns.Add(copy);
                }
                targetPage=actual;
            }
            targetPage.Extra["type"]=JsonSerializer.SerializeToElement(exactPageType);
            targetPage.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
            if(publishedProject)targetPage.WhereSql+=" AND yz_project_id=@current_yz_project_id";
            catalog.ConfigColumns.Add(new(){Id="3054",ConfigTableId=target.Id,YzProjectId=catalog.YzProjectId,ColumnName="scope_code",LogicalType="string"});
            catalog.YzAdminTypes.Single().Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        }
        PageColumnMetadata? labelSetting=null;
        if(alternateName.Length>0)
        {
            // 发布页原条件照常执行，简称可使用已发布的另一条物理列或联接。
            var label=new ColumnMetadata
            {
                Id="3053",ConfigTableId="3",ColumnName="short_label",LogicalType="string",YzProjectId=catalog.YzProjectId
            };
            if(alternateName=="joined")
            {
                label.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
                label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.caption");
            }
            catalog.ConfigColumns.Add(label);
            labelSetting=new(){Id=targetPage.Id+"3053",ConfigPageId=targetPage.Id,ConfigColumnId=label.Id,YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=0};
            catalog.ConfigPageColumns.Add(labelSetting);
            catalog.ConfigPageColumns.Single(setting=>setting.ConfigPageId=="201"&&setting.ConfigColumnId==field.Id)
                .Extra["controlDetail"]=alternateDictionary?JsonSerializer.SerializeToElement(new{pageId=targetPage.Id,nameField="short_label"}):
                    JsonSerializer.SerializeToElement("""{"nameField":"short_label"}""");
        }
        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=source.Pages.Single(page=>page.Source.Id=="201");
        var resolved=DatabaseCheckboxCompilation.Resolve(model,source,page,source.Columns.Single(column=>column.Source.Id==field.Id));
        Assert.True(resolved.Definition is not null,resolved.Reason);
        var definition=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,source,page));
        Assert.Equal(targetPage.Id,definition.NameTargetPage?.Source.Id);
        if(exactPageType>=0)
            Assert.Equal("scope_code",Assert.IsType<ExactDataManageModel>(definition.ExactTarget).Column.Source.ColumnName);
        Assert.Equal(alternateName.Length>0?"short_label":"name",definition.Name.Source.ColumnName);
        var view=NonRedundantReadCompilation.ForPage(model,source,page);
        var binding=Assert.Single(view.MultiTargets);
        Assert.True(binding.ApplyPageScope);
        Assert.Equal(condition!="none",binding.ApplyControlScope);
        Assert.Equal(targetPage.Id,binding.TargetPageId);

        var names=definition.Names.Source.ColumnName;
        var plan=NonRedundantReadCompilation.CompilePage(model,source,["id",names],[names],[],view.SingleTargets,view.MultiTargets);
        Assert.Equal(account,plan.RequiresAccount);
        var testActor=new Metarill.Core.Security.RuntimeActor(manager,"测试账号",false,catalog.YzProjectId,"5");
        if(account)plan=plan.BindAccount(testActor);
        const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var sql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
        var projection=Read<System.Collections.IDictionary>("MultiProjections")[names]!;
        var scope=Assert.IsType<string>(projection.GetType().GetProperty("TargetScope")!.GetValue(projection));
        var output=Read<IReadOnlyDictionary<string,string>>("Expressions")[names];
        var predicate=Read<IReadOnlyDictionary<string,string>>("ArrayNamePredicates")[names];
        var constants=Read<IReadOnlyDictionary<string,object?>>("Constants");
        var status=Assert.Single(constants,pair=>Equals(pair.Value,48));
        if(alternateDictionary)Assert.DoesNotContain(47,constants.Values);
        Assert.Equal(account,constants.Values.Contains(manager));
        foreach(var command in new[]{scope,output,predicate})
        {
            Assert.Contains("multi_value.`status`",command);
            Assert.Contains("multi_value.`delete_no` IS NULL",command);
            Assert.Contains(status.Key,command);
            Assert.Equal(condition=="project"||publishedProject,command.Contains("multi_value.`yz_project_id`=@project_scope_number",StringComparison.Ordinal));
            Assert.Equal(condition is "form" or "account-form",command.Contains("multi_value.`segment`=b.`segment`",StringComparison.Ordinal));
            if(account)
            {
                Assert.Contains("multi_value.`handler_id`",command);
                Assert.Contains(Assert.Single(constants,pair=>Equals(pair.Value,manager)).Key,command);
            }
        }
        if(alternateName=="joined")
        {
            Assert.Contains("checkbox_global_read_company",output);
            Assert.Contains("label_enabled",output);
            Assert.Contains("label_enabled",predicate);
            Assert.Contains("caption",output);
            if(alternateInnerPage)
            {
                foreach(var command in new[]{output,predicate})
                    Assert.Contains(constants,pair=>Equals(pair.Value,7)&&command.Contains(pair.Key,StringComparison.Ordinal));
                Assert.DoesNotContain(17,constants.Values);
            }
        }
        else if(alternateName=="physical")Assert.Contains("multi_value.`short_label`",output);
        else if(derived)Assert.Contains("checkbox_global_read_company",output);
        var generated=DatabaseCheckboxRenderer.Service(model,source,page);
        Assert.Contains("CheckboxOptions201_25Async",generated);
        Assert.Contains("ValidateCheckbox201_25Async",generated);
        Assert.Contains("ExportCheckbox201_25Async",ExportRenderer.Service(model,source,page));
        if(alternateName.Length>0&&exactPageType<0)Assert.Contains("CompileScopedCheckboxLabel",generated);
        if(exactPageType>=0)
        {
            Assert.Contains($"ResolveExactDataManage(\"{targetPage.Id}\", \"list\", \"real_name\")",generated);
            Assert.Contains("scope_code",generated);
        }
        if(account||alternateName.Length>0)
        {
            var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-checkbox-compiled");
            var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
            try
            {
                new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
                var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
                var service=assembly.GetType(ns+".Generated.Services.CheckboxGlobalReadSourceService",true)!;
                var information=service.GetField("ReadPlan201",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
                Assert.NotNull(information);
                var installed=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(information.GetValue(null));
                Assert.Equal(account,installed.RequiresAccount);
                if(alternateName.Length>0)
                {
                    // 精简发布目录也应保留常量字典限制，候选及回显选择同一名称。
                    var installedSql=installed.GetType().GetProperty("MySql",hidden)!.GetValue(installed)!;
                    var expressions=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(installedSql.GetType()
                        .GetProperty("Expressions",hidden)!.GetValue(installedSql));
                    Assert.Contains("multi_value.`status`",expressions[names]);
                    var candidate=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service
                        .GetField("DerivedCheckboxPlan201_25",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
                    Assert.Contains("short_label",candidate.OutputTypes.Keys);
                    if(exactPageType>=0)
                    {
                        var candidateSql=candidate.GetType().GetProperty("MySql",hidden)!.GetValue(candidate)!;
                        var candidateConstants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(candidateSql.GetType()
                            .GetProperty("Constants",hidden)!.GetValue(candidateSql));
                        Assert.Contains(48,candidateConstants.Values);
                        Assert.DoesNotContain(47,candidateConstants.Values);
                        if(publishedProject)
                            Assert.Contains("b.`yz_project_id`=@project_scope_number",Assert.IsType<string>(candidateSql.GetType()
                                .GetProperty("Scope",hidden)!.GetValue(candidateSql)));
                        var export=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service
                            .GetField("ExportReadPlan201",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
                        var exportSql=export.GetType().GetProperty("MySql",hidden)!.GetValue(export)!;
                        var exportConstants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(exportSql.GetType()
                            .GetProperty("Constants",hidden)!.GetValue(exportSql));
                        Assert.Contains(48,exportConstants.Values);
                        Assert.DoesNotContain(47,exportConstants.Values);
                        Assert.NotNull(service.GetMethod("Import201Async"));
                        Assert.NotNull(service.GetMethod("Export201Async"));
                        var full=Path.Combine(directory,"app");
                        Assert.True(new ApplicationPackager().Create(catalog,full,ns,NonRedundantApplicationFixture.Root()).FrontendIncluded);
                        foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(full,operation);
                    }
                    if(alternateName=="joined")
                    {
                        Assert.Contains("label_enabled",expressions[names]);
                        if(candidate.RequiresAccount)candidate=candidate.BindAccount(testActor);
                        var candidateSql=candidate.GetType().GetProperty("MySql",hidden)!.GetValue(candidate)!;
                        var candidateOutputs=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(candidateSql.GetType()
                            .GetProperty("Expressions",hidden)!.GetValue(candidateSql));
                        var candidateFrom=Assert.IsType<string>(candidateSql.GetType().GetProperty("From",hidden)!.GetValue(candidateSql));
                        Assert.Contains("label_enabled",candidateOutputs["short_label"]+candidateFrom);
                        if(alternateInnerPage)
                        {
                            var scopeValues=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                                candidateSql.GetType().GetProperty("Constants",hidden)!.GetValue(candidateSql));
                            Assert.Contains(7,scopeValues.Values);
                            Assert.DoesNotContain(17,scopeValues.Values);
                            Assert.NotNull(service.GetMethod("Import201Async"));
                            Assert.NotNull(service.GetMethod("Export201Async"));
                            var export=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service
                                .GetField("ExportReadPlan201",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
                            Assert.Equal(account,export.RequiresAccount);
                            if(export.RequiresAccount)export=export.BindAccount(testActor);
                            var exportSql=export.GetType().GetProperty("MySql",hidden)!.GetValue(export)!;
                            var exportConstants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                                exportSql.GetType().GetProperty("Constants",hidden)!.GetValue(exportSql));
                            Assert.Contains(7,exportConstants.Values);
                            Assert.DoesNotContain(17,exportConstants.Values);
                        }
                    }
                }
            }
            finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
        }
        if(labelSetting is not null)
        {
            labelSetting.IsShow=0;
            var hiddenLabel=GenerationModel.Create(catalog,ns);
            var hiddenTable=hiddenLabel.Tables.Single(table=>table.Source.Id=="2");
            Assert.Null(DatabaseCheckboxCompilation.Resolve(hiddenLabel,hiddenTable,hiddenTable.Pages.Single(page=>page.Source.Id=="201"),
                hiddenTable.Columns.Single(column=>column.Source.Id==field.Id)).Definition);
            Assert.Throws<InvalidOperationException>(()=>NonRedundantReadCompilation.CompilePage(model,source,
                ["id",names],[names],[],view.SingleTargets,view.MultiTargets));
            labelSetting.IsShow=1;
        }
        if(storageMode==1&&condition=="project"&&explicitPage)
        {
            model.Catalog.ConfigPages.Add(new(){Id="302",ConfigTableId="3",YzProjectId=catalog.YzProjectId,WhereSql="status=1"});
            Assert.Throws<InvalidOperationException>(()=>NonRedundantReadCompilation.CompilePage(model,source,["id",names],[names],[],
                view.SingleTargets,[binding with {TargetPageId="302"}]));
        }

        T Read<T>(string name)=>Assert.IsAssignableFrom<T>(sql.GetType().GetProperty(name,hidden)!.GetValue(sql));
    }


    /// <summary>原数据库复选按实际发布页合并物理父范围、账号角色范围和静态条件。</summary>
    [Theory]
    [InlineData(0,0,false)]
    [InlineData(0,2,true)]
    [InlineData(1,0,true)]
    [InlineData(1,2,false)]
    [InlineData(0,0,true,true)]
    [InlineData(1,2,true,true)]
    [InlineData(0,0,true,false,true)]
    [InlineData(0,2,false,false,true)]
    [InlineData(1,0,false,false,true)]
    [InlineData(1,2,true,false,true)]
    [InlineData(0,0,true,false,true,true)]
    [InlineData(0,2,false,false,true,true)]
    public async Task Global_exact_stored_checkbox_keeps_the_actual_parent(int sourceStorage,int pageType,bool dictionaryProject,
        bool idLabel=false,bool alternateName=false,bool differentTarget=false)
    {
        const string ns="Example.ExactStoredCheckbox";
        var catalog=Catalog("exact_stored_checkbox",false);
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=sourceStorage;
        var target=catalog.ConfigTables.Single(table=>table.Id=="3");
        target.IsRongyu=1;
        target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        var name=catalog.ConfigColumns.Single(column=>column.Id=="32");
        name.Extra.Remove("isJoinColumn");
        name.Extra.Remove("columnFullName");
        // 物理字典只保留用例所需的标签、范围和父键。
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="37"||!dictionaryProject&&
            column.ConfigTableId==target.Id&&column.ColumnName=="yz_project_id");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="37"||!dictionaryProject&&field.ConfigColumnId=="33");
        var dictionary=catalog.ConfigPages.Single(page=>page.Id=="301");
        dictionary.WhereSql=(dictionaryProject?"yz_project_id=@current_yz_project_id AND ":"")+"parent_id=@parentId AND status=38";
        dictionary.Extra["type"]=JsonSerializer.SerializeToElement(pageType);
        dictionary.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
        catalog.ConfigColumns.Add(new(){Id="3080",YzProjectId=catalog.YzProjectId,ConfigTableId="3",ColumnName="scope_code",LogicalType="string"});
        catalog.YzAdminTypes.Single().Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        var control=catalog.ConfigColumns.Single(column=>column.Id=="25");
        control.ControlDetail=JsonSerializer.Serialize(new{tableId="3",pageId="301",nameField=idLabel?"id":"name"});
        if(pageType==2)control.Extra["filterControlKind"]=JsonSerializer.SerializeToElement("CheckBox");
        var field=catalog.ConfigPageColumns.Single(item=>item.ConfigPageId=="201"&&item.ConfigColumnId=="25");
        field.IsShow=field.IsEdit=1;
        field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        if(idLabel)
        {
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="27");
            catalog.ConfigPageColumns.RemoveAll(item=>item.ConfigColumnId=="27");
        }

        var alternateTargetId=differentTarget?"302":"301";
        var alternateParent=differentTarget?"dictionary_parent_id":"parent_id";
        var alternateScope=differentTarget?"alternate_scope_code":"scope_code";
        if(differentTarget)
        {
            // 两个发布页使用不同的父字段、常量和值匹配字段，不能借用表级声明。
            var other=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(dictionary))!;
            other.Id=alternateTargetId;
            other.WhereSql=dictionary.WhereSql.Replace("parent_id",alternateParent,StringComparison.Ordinal)
                .Replace("38","39",StringComparison.Ordinal);
            other.Extra["type"]=JsonSerializer.SerializeToElement(pageType==0?2:0);
            other.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement(alternateScope);
            catalog.ConfigPages.Add(other);
            catalog.ConfigColumns.AddRange([
                new(){Id="3082",YzProjectId=catalog.YzProjectId,ConfigTableId="3",ColumnName=alternateScope,LogicalType="string"},
                new(){Id="3083",YzProjectId=catalog.YzProjectId,ConfigTableId="3",ColumnName=alternateParent,LogicalType="int64",LinkConfigTableId="1"}]);
        }
        if(alternateName)
        {
            // 两个原来源页共用实际父键及目标页，第二页只替换名称投影。
            catalog.ConfigColumns.Add(new(){Id="3081",YzProjectId=catalog.YzProjectId,ConfigTableId="3",
                ColumnName="short_name",LogicalType="string"});
            catalog.ConfigPageColumns.Add(new(){Id=alternateTargetId+"3081",YzProjectId=catalog.YzProjectId,ConfigPageId=alternateTargetId,
                ConfigColumnId="3081",IsShow=1,IsEdit=1});
            if(sourceStorage==1)
            {
                catalog.ConfigColumns.Add(new(){Id="2081",YzProjectId=catalog.YzProjectId,ConfigTableId="2",
                    ColumnName="target_short_names",LogicalType="string",ColumnLength="max",Extra=new(){
                        ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("target.short_name")}});
                catalog.ConfigPageColumns.Add(new(){Id="2022081",YzProjectId=catalog.YzProjectId,ConfigPageId="202",
                    ConfigColumnId="2081",IsShow=1});
            }
            catalog.ConfigPageColumns.Add(new(){Id="20225",YzProjectId=catalog.YzProjectId,ConfigPageId="202",
                ConfigColumnId="25",IsShow=1,IsEdit=1,Extra=new(){
                    ["isSearch"]=JsonSerializer.SerializeToElement(1),
                    ["controlDetail"]=differentTarget?JsonSerializer.SerializeToElement(new{pageId=alternateTargetId,nameField="short_name"}):
                        JsonSerializer.SerializeToElement(new{nameField="short_name"})}});
            var alternatePage=catalog.ConfigPages.Single(item=>item.Id=="202");
            alternatePage.IsExport=alternatePage.IsImport=1;
        }

        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=source.Pages.Single(page=>page.Source.Id=="201");
        var resolution=DatabaseCheckboxCompilation.Resolve(model,source,page,source.Columns.Single(column=>column.Source.Id=="25"));
        Assert.True(resolution.Definition is not null,resolution.Reason);
        if(alternateName)
        {
            var alternate=DatabaseCheckboxCompilation.Resolve(model,source,source.Pages.Single(item=>item.Source.Id=="202"),
                source.Columns.Single(column=>column.Source.Id=="25"));
            Assert.True(alternate.Definition is not null,alternate.Reason);
            Assert.Equal("short_name",alternate.Definition.Name.Source.ColumnName);
            Assert.Equal(alternateTargetId,alternate.Definition.LogicalTargetPage?.Source.Id);
            Assert.Equal(alternateScope,Assert.IsType<ExactDataManageModel>(alternate.Definition.ExactTarget).Column.Source.ColumnName);
            Assert.Contains(alternate.Definition.Scopes,scope=>scope.Source=="parent"&&scope.Column.Source.ColumnName==alternateParent);
            if(differentTarget)
                Assert.Throws<InvalidOperationException>(()=>NonRedundantReads.CompileProjectPage(catalog,"2",["target_names"],
                    [],[],[],[new("25","202","301",true)]));
        }
        var checkbox=resolution.Definition;
        Assert.Equal("scope_code",Assert.IsType<ExactDataManageModel>(checkbox.ExactTarget).Column.Source.ColumnName);
        Assert.Equal("301",checkbox.LogicalTargetPage?.Source.Id);
        Assert.Contains(checkbox.Scopes,scope=>scope.Source=="parent"&&scope.Column.Source.ColumnName=="parent_id");

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-exact-stored-checkbox");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var code=await File.ReadAllTextAsync(Path.Combine(generated,"Services/ExactStoredCheckboxSourceService.g.cs"));
            Assert.Contains("""ResolveExactDataManage("301", "list", "real_name")""",code);
            Assert.Contains("DerivedCheckboxRows201_25Async(request.ParentId,actor,form,request.Selected",code);
            Assert.Contains("DerivedCheckboxRows201_25Async(entity.ParentId",code);
            Assert.Contains(sourceStorage==0?"var matches=await DerivedCheckboxRows201_25Async(parentId,actor,":
                "DatabaseCheckboxValue.ProjectIds(DatabaseCheckboxValue.ReadWrite(cell))",code);
            if(sourceStorage==1)
                Assert.Contains(pageType==2?"ApplyJsonArrayHasAnyId":"ApplyJsonArrayHasId",code);
            else if(idLabel)Assert.Contains("name=DatabaseCheckboxValue.ReadId(item)",code);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.ExactStoredCheckboxSourceService",true)!;
            const System.Reflection.BindingFlags staticFields=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField("DerivedCheckboxPlan201_25",staticFields)!.GetValue(null));
            var sql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
            var scope=Assert.IsType<string>(sql.GetType().GetProperty("Scope",hidden)!.GetValue(sql));
            Assert.Equal("parent_id",sql.GetType().GetProperty("RequiredMultiParent",hidden)!.GetValue(sql));
            Assert.Contains("exact_stored_checkbox_parent",scope);
            Assert.Equal(dictionaryProject,scope.Contains("b.`yz_project_id`",StringComparison.Ordinal));
            Assert.Contains(38,Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                sql.GetType().GetField("constants",hidden)!.GetValue(sql)).Values);
            Assert.Contains("scope_code",plan.OutputTypes.Keys.Concat(Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                sql.GetType().GetProperty("Expressions",hidden)!.GetValue(sql)).Keys));
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            if(alternateName)
            {
                var alternatePlan=Assert.IsType<NonRedundantReadPlan>(
                    service.GetField("DerivedCheckboxPlan202_25",staticFields)!.GetValue(null));
                Assert.Equal("string",alternatePlan.OutputTypes["short_name"]);
                var readPlan=alternatePlan.GetType().GetProperty("MySql",hidden)!.GetValue(alternatePlan)!;
                if(differentTarget)
                {
                    Assert.Equal(alternateParent,readPlan.GetType().GetProperty("RequiredMultiParent",hidden)!.GetValue(readPlan));
                    var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                        readPlan.GetType().GetField("constants",hidden)!.GetValue(readPlan)).Values;
                    Assert.Contains(39,constants);
                    Assert.DoesNotContain(38,constants);
                    var fields=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                        readPlan.GetType().GetProperty("Expressions",hidden)!.GetValue(readPlan));
                    Assert.Contains(alternateScope,fields.Keys);
                    Assert.DoesNotContain("scope_code",fields.Keys);
                }
                else Assert.Equal(scope,readPlan.GetType().GetProperty("Scope",hidden)!.GetValue(readPlan));
                Assert.NotNull(service.GetMethod("Import202Async"));
                Assert.NotNull(service.GetMethod("Export202Async"));
                Assert.Contains("DerivedCheckboxRows202_25Async(entity.ParentId",code);
                if(sourceStorage==0)
                {
                    var sourcePlan=Assert.IsType<NonRedundantReadPlan>(
                        service.GetField("ReadPlan202",staticFields)!.GetValue(null));
                    var sourceSql=sourcePlan.GetType().GetProperty("MySql",hidden)!.GetValue(sourcePlan)!;
                    var projection=Assert.IsType<string>(sourceSql.GetType().GetMethod("Expression",hidden)!
                        .Invoke(sourceSql,["target_names"]));
                    Assert.Contains("multi_value.`short_name`",projection);
                    Assert.Contains("multi_value.`"+alternateParent+"`",projection);
                    if(differentTarget)
                    {
                        var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                            sourceSql.GetType().GetField("constants",hidden)!.GetValue(sourceSql)).Values;
                        Assert.Contains(39,constants);
                        Assert.DoesNotContain(38,constants);
                    }
                }
                else
                {
                    Assert.Equal("string",plan.OutputTypes["short_name"]);
                    Assert.Equal("string",alternatePlan.OutputTypes["name"]);
                    Assert.Contains("CacheShortName",code);
                }
                Assert.Contains($"ResolveExactDataManage(\"{alternateTargetId}\", \"list\", \"real_name\")",
                    code[(code.IndexOf("DerivedCheckboxPlan202_25=",StringComparison.Ordinal))..]);
            }
            if(sourceStorage==1&&!idLabel)
            {
                // 回填只从已存来源父键和发布条件计算，不依赖执行人的角色集合。
                var maintenance=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                var published=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    maintenance.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("exact_stored_checkbox_target",published.MixedTargets);
                var consumers=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    published.GetType().GetProperty("Plans",hidden)!.GetValue(published));
                var read=consumers["2"]!.GetType().GetProperty("Read")!.GetValue(consumers["2"])!;
                var labels=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["target_names"]));
                Assert.Contains("b.`parent_id`",labels);
                Assert.Contains("exact_stored_checkbox_parent",labels);
                Assert.Equal(dictionaryProject,labels.Contains("multi_value.`yz_project_id`",StringComparison.Ordinal));
                Assert.DoesNotContain("`scope_code`",labels);
                if(alternateName)
                {
                    var alternateLabels=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!
                        .Invoke(read,["target_short_names"]));
                    Assert.Contains("multi_value.`short_name`",alternateLabels);
                    Assert.Contains("multi_value.`parent_id`",alternateLabels);
                    Assert.DoesNotContain("`scope_code`",alternateLabels);
                }
            }
            if(!idLabel&&(((sourceStorage==0)==(pageType==0))||differentTarget))
            {
                var full=Path.Combine(directory,"app");
                Assert.True(new ApplicationPackager().Create(catalog,full,ns,NonRedundantApplicationFixture.Root()).FrontendIncluded);
                var list=await File.ReadAllTextAsync(Path.Combine(full,"frontend/pages/yzp/exact_stored_checkbox_source-list-201.vue"));
                Assert.Contains("loadOriginalArrayCheckboxOptions",list);
                Assert.Contains("findNameIdTarget25",list);
                Assert.Contains("\"requiresParent\":true",list);
                if(differentTarget)
                {
                    var alternateList=await File.ReadAllTextAsync(Path.Combine(full,"frontend/pages/yzp/exact_stored_checkbox_source-list-202.vue"));
                    Assert.Contains("findNameIdTarget25",alternateList);
                    Assert.Contains("\"requiresParent\":true",alternateList);
                }
                foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(full,operation);
            }
            if(alternateName)
            {
                // 备用名称从真实发布页撤下后，不能继续通过原来源页选择它。
                var published=catalog.ConfigPageColumns.Single(item=>item.Id==alternateTargetId+"3081");
                published.IsShow=published.IsEdit=0;
                var hiddenModel=GenerationModel.Create(catalog,ns);
                var hiddenSource=hiddenModel.Tables.Single(table=>table.Source.Id=="2");
                var hiddenResolution=DatabaseCheckboxCompilation.Resolve(hiddenModel,hiddenSource,
                    hiddenSource.Pages.Single(item=>item.Source.Id=="202"),
                    hiddenSource.Columns.Single(column=>column.Source.Id=="25"));
                Assert.Null(hiddenResolution.Definition);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>实际生成服务与维护目录均保留无行项目列的全局字典条件，原 ID 导入仍走写入校验。</summary>
    [Theory]
    [InlineData(1,false)]
    [InlineData(0,false)]
    [InlineData(0,true)]
    [InlineData(1,false,true)]
    [InlineData(0,true,true)]
    public async Task Redundant_checkbox_global_dictionary_preserves_published_conditions(int targetStorage,bool derivedName,bool exact=false)
    {
        const string ns="Example.ReleasedCheckboxCache";
        var catalog=Catalog("released_checkbox_cache",false);
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        var target=catalog.ConfigTables.Single(table=>table.Id=="3");
        target.IsRongyu=targetStorage;
        target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==target.Id&&column.ColumnName=="yz_project_id");
        var company=catalog.ConfigTables.Single(table=>table.Id=="4");
        company.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==company.Id&&column.ColumnName=="yz_project_id");
        catalog.ConfigPages.Single(page=>page.ConfigTableId==company.Id).WhereSql="";
        if(!derivedName)
        {
            // 物理标签场景无需附带另一套全局 List 缓存。
            catalog.ConfigColumns.Single(column=>column.Id=="32").Extra.Clear();
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="37");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="37");
        }
        var dictionary=catalog.ConfigPages.Single(page=>page.Id=="301");
        dictionary.WhereSql="status=38";
        if(exact)
        {
            dictionary.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("status");
            catalog.YzAdminTypes.Single().Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        }
        catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail=
            """{"tableId":"3","nameField":"name","whereSql":"segment=@formParameters.segment"}""";

        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=source.Pages.Single(page=>page.Source.Id=="201");
        var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForService(model,source,page));
        Assert.Equal(derivedName,checkbox.DerivedName);
        Assert.Equal("301",checkbox.GlobalPage?.Source.Id);
        var renderer=DatabaseCheckboxRenderer.Service(model,source,page);
        Assert.Contains(derivedName
            ?exact?"CompileGlobalExactSingleCandidate":"CompileScopedCheckboxLabel"
            :"query=CheckboxGlobalCondition201_25.ApplyConstants",renderer);
        if(exact)Assert.Contains("""ResolveExactDataManage("301", "list", "real_name")""",renderer);
        Assert.Contains("CheckboxCondition201_25",renderer);
        Assert.Contains("ValidateCheckbox201_25Async",renderer);

        var bindings=RelationPropagationCompilation.Definitions(derivedName?
            RelationPropagationCompilation.DerivedBindings(model):RelationPropagationCompilation.Bindings(model));
        var published=RelationPropagationCompilation.PublishedMaintenanceMetadata(model,bindings);
        Assert.Equal("status=38",published.ConfigPages.Single(item=>item.Id=="301").WhereSql);
        _=MixedRelationPropagationCompilation.ForModel(model);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-released-checkbox-cache");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.ReleasedCheckboxCacheSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            if(derivedName&&!exact)
            {
                // 固定字典条件由静态读计划持有，核对生成后的快照和实际查询。
                const System.Reflection.BindingFlags staticFields=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var executionCatalog=assembly.GetType(ns+".Generated.GeneratedNonRedundantReadCatalog",true)!;
                var snapshot=Assert.IsType<MetadataCatalog>(executionCatalog.GetField("Snapshot",staticFields)!.GetValue(null));
                Assert.Equal("status=38",snapshot.ConfigPages.Single(item=>item.Id=="301").WhereSql);
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    service.GetField("DerivedCheckboxPlan201_25",staticFields)!.GetValue(null));
                var sql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
                Assert.Contains("b.`status`",Assert.IsType<string>(sql.GetType().GetProperty("Scope",hidden)!.GetValue(sql)));
                Assert.Contains(38,Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    sql.GetType().GetField("constants",hidden)!.GetValue(sql)).Values);
            }
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>控件直接限定物理外键时，候选、保存和 Excel 可以绑定来源级联还原的同一父级。</summary>
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(true,false,false)]
    [InlineData(false,true,false)]
    [InlineData(true,true,false)]
    [InlineData(false,false,true)]
    [InlineData(true,true,true)]
    [InlineData(true,true,true,true)]
    [InlineData(false,false,false,false,"physical")]
    [InlineData(true,false,false,false,"independent")]
    [InlineData(true,false,true,false,"physical")]
    [InlineData(true,true,true,false,"independent")]
    [InlineData(true,false,false,false,"","joined")]
    [InlineData(true,false,true,false,"physical","physical")]
    [InlineData(true,true,true,false,"","joined")]
    [InlineData(true,false,true,false,"","logical")]
    [InlineData(true,false,false,false,"","joined","inner-literal")]
    public async Task Checkbox_control_parent_uses_the_actual_source_cascade(bool derived,bool globalParent,bool scopeFromPage,
        bool overridePage=false,string compound="",string alternateName="",string dictionaryMode="")
    {
        const string ns="Example.CheckboxControlParent";
        var catalog=NonRedundantCrossTableGenerationTests.Catalog("checkbox_control_parent","int64","int64");
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        sourcePage.Extra.Remove("type");
        sourcePage.IsImport=sourcePage.IsExport=1;
        sourcePage.WhereSql+=" AND country_id=@parentId";
        if(derived)sourcePage.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        var parent=catalog.ConfigTables.Single(table=>table.Id=="3");
        if(compound=="physical")
        {
            catalog.ConfigColumns.Add(new(){Id="980",ConfigTableId="2",ColumnName="owner_country_id",
                LinkConfigTableId=parent.Id,LogicalType="int64"});
            sourcePage.WhereSql+=" AND owner_country_id=@parentId";
        }
        else if(compound=="independent")
        {
            var first=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ControlKind=="CascadeTree");
            var path=JsonNode.Parse(first.ControlDetail)!;
            foreach(var level in path["levels"]!.AsArray())
                level!["columnPrefix"]="alternate_"+level["columnPrefix"]!.GetValue<string>();
            catalog.ConfigColumns.Add(new(){Id="980",ConfigTableId="2",LinkConfigTableId=first.LinkConfigTableId,
                ColumnName="alternate_place_id",LogicalType=first.LogicalType,
                ControlKind="CascadeTree",ControlDetail=path.ToJsonString()});
            catalog.ConfigPageColumns.Add(new(){Id="201980",ConfigPageId=sourcePage.Id,ConfigColumnId="980",IsShow=1,IsEdit=1});
            sourcePage.WhereSql+=" AND alternate_country_id=@parentId";
        }
        if(globalParent)
        {
            parent.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==parent.Id&&column.ColumnName=="yz_project_id");
            catalog.ConfigPages.Single(page=>page.ConfigTableId==parent.Id).WhereSql="status=1";
        }
        catalog.ConfigTables.Add(new(){Id="9",TableName="checkbox_control_parent_target",IsRongyu=derived?0:1});
        var targetScope="yz_project_id=@current_yz_project_id AND sheet_status=2";
        if(scopeFromPage)targetScope+=" AND target_scope_id=@parentId";
        var candidateId=overridePage?"902":"901";
        if(overridePage)
            catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="9",WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigPages.Add(new(){Id=candidateId,ConfigTableId="9",WhereSql=targetScope});
        var targetParent=new ColumnMetadata {Id="96",ConfigTableId="9",ColumnName="target_scope_id",
            LogicalType=derived?"string":"int64",LinkConfigTableId=parent.Id};
        if(globalParent&&derived)
        {
            targetParent.ControlKind="List";
            targetParent.ControlDetail="{\"tableId\":\"3\",\"pageId\":\"301\",\"nameField\":\"name\"}";
        }
        var name=new ColumnMetadata {Id="92",ConfigTableId="9",ColumnName="name",LogicalType="string"};
        catalog.ConfigColumns.AddRange([
            new(){Id="26",ConfigTableId="2",ColumnName="country_id",Name="国家",LogicalType="int64",LinkConfigTableId=parent.Id},
            new(){Id="28",ConfigTableId="2",ColumnName="selected_ids",Name="复选",LogicalType="string",
                ControlKind="MultiCheckBoxDatabase",LinkConfigTableId="9",
                ControlDetail=JsonSerializer.Serialize(new{tableId="9",pageId="901",
                    whereSql=scopeFromPage?"status=1":"target_scope_id=@parentId AND status=1"})},
            new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            name,
            new(){Id="93",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="94",ConfigTableId="9",ColumnName="status",LogicalType="int32"},
            new(){Id="95",ConfigTableId="9",ColumnName="sheet_status",LogicalType="int32"},
            targetParent]);
        if(derived)
        {
            name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("label.name");
            catalog.ConfigColumns.Add(new(){Id="97",ConfigTableId="9",ColumnName="label_id",LogicalType="int64",
                ControlKind="List",LinkConfigTableId="4",ControlDetail="{\"tableId\":\"4\",\"pageId\":\"401\",\"nameField\":\"name\"}"});
            catalog.ConfigPageColumns.Add(new(){Id=candidateId+"97",ConfigPageId=candidateId,ConfigColumnId="97",IsShow=1,IsEdit=1});
        }
        catalog.ConfigPageColumns.AddRange([
            new(){Id="20128",ConfigPageId="201",ConfigColumnId="28",IsShow=1,IsEdit=1,
                Extra=new(){["isDetailShow"]=JsonSerializer.SerializeToElement(1)}},
            new(){Id=candidateId+"92",ConfigPageId=candidateId,ConfigColumnId="92",IsShow=1}]);
        if(overridePage)
            catalog.ConfigPageColumns.Single(field=>field.Id=="20128").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement("{\"pageId\":902}");
        var logicalTarget=alternateName=="logical";
        var joinedName=alternateName is "joined" or "logical";
        if(alternateName.Length>0)
        {
            // 原辅助数组保留表级名称的字段；本页按自己的文本名称回显和筛选。
            var candidate=catalog.ConfigPages.Single(page=>page.Id==candidateId);
            candidate.Extra["type"]=JsonSerializer.SerializeToElement(2);
            if(logicalTarget)
            {
                candidate.WhereSql=candidate.WhereSql.Replace("target_scope_id=@parentId","target_country_id=@parentId",StringComparison.Ordinal);
                var path=JsonNode.Parse(catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail)!;
                foreach(var level in path["levels"]!.AsArray())
                    level!["columnPrefix"]="target_"+level["columnPrefix"]!.GetValue<string>();
                catalog.ConfigColumns.Add(new(){Id="990",ConfigTableId="9",ColumnName="target_place_id",LogicalType="int64",
                    ControlKind="CascadeTree",LinkConfigTableId="5",ControlDetail=path.ToJsonString()});
                catalog.ConfigPageColumns.Add(new(){Id=candidateId+"990",ConfigPageId=candidateId,ConfigColumnId="990",IsShow=1,IsEdit=1});
            }
            catalog.ConfigPageColumns.Single(field=>field.Id=="20128").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement("""{"nameField":"short_label"}""");
            var label=new ColumnMetadata {Id="98",ConfigTableId="9",ColumnName="short_label",LogicalType="string"};
            catalog.ConfigColumns.Add(label);
            catalog.ConfigPageColumns.Add(new(){Id=candidateId+"98",ConfigPageId=candidateId,ConfigColumnId="98",IsShow=1});
            if(joinedName)
            {
                label.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
                label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("alias_ref.caption");
                catalog.ConfigTables.Add(new(){Id="10",TableName="checkbox_control_parent_alias_dictionary",
                    IsRongyu=dictionaryMode=="inner-literal"?1:0});
                catalog.ConfigColumns.AddRange([
                    new(){Id="101",ConfigTableId="10",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                    new(){Id="102",ConfigTableId="10",ColumnName="caption",LogicalType="string"},
                    new(){Id="103",ConfigTableId="10",ColumnName="yz_project_id",LogicalType="int64"},
                    new(){Id="104",ConfigTableId="10",ColumnName="enabled_flag",LogicalType="int32"},
                    new(){Id="99",ConfigTableId="9",ColumnName="alias_ref_id",LogicalType="int64",ControlKind="List",
                        LinkConfigTableId="10",ControlDetail="""{"tableId":"10","pageId":"1001","nameField":"caption"}"""}
                ]);
                catalog.ConfigPages.Add(new(){Id="1001",ConfigTableId="10",WhereSql="yz_project_id=@current_yz_project_id AND enabled_flag=1",
                    Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});
                catalog.ConfigPageColumns.Add(new(){Id="100102",ConfigPageId="1001",ConfigColumnId="102",IsShow=1});
            }
            catalog.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName="selected_names",LogicalType="string",
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("selected.name")}});
            catalog.ConfigPageColumns.Add(new(){Id="20129",ConfigPageId="201",ConfigColumnId="29",IsShow=1,
                Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
            if(scopeFromPage)sourcePage.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            else sourcePage.Extra.Remove("exportMethod");
        }
        RedundantMultiNameCacheTests.Published(catalog);
        if(overridePage)
        {
            var target=catalog.ConfigTables.Single(item=>item.Id=="9");
            Assert.False(Metarill.Data.Queries.NonRedundantReads.IsScopedMultiTarget(catalog,target,"901"));
            Assert.True(Metarill.Data.Queries.NonRedundantReads.IsScopedMultiTarget(catalog,target,candidateId));
        }
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=table.Pages.Single(item=>item.Source.Id=="201");
        var resolution=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="28"));
        Assert.True(resolution.Definition is not null,resolution.Reason);
        var checkbox=resolution.Definition!;
        Assert.True(checkbox.LogicalSourceParent);
        if(alternateName.Length>0)
        {
            Assert.Equal("short_label",checkbox.Name.Source.ColumnName);
            Assert.Equal("selected_names",checkbox.Names.Source.ColumnName);
        }
        if(globalParent&&scopeFromPage||logicalTarget)
            Assert.Equal(candidateId,checkbox.LogicalTargetPage!.Source.Id);
        else
            Assert.Null(checkbox.LogicalTargetPage);
        Assert.Equal(parent.Id,Assert.Single(checkbox.SourceParents).Source.Id);
        Assert.Contains(checkbox.Scopes,scope=>scope.Source=="parent"&&
            (logicalTarget?scope.Column.Source.ColumnName=="target_country_id":scope.Column.Source.Id==targetParent.Id));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-control-logical-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var generated=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/CheckboxControlParentSourceService.g.cs"));
            Assert.Contains("LogicalParentId201(parentId)",generated);
            Assert.Contains("logicalRelationParentId: parentId",generated);
            if(compound=="independent")
                Assert.Contains("CrossReader201_980(actor,locking,false).PathAsync(0,",generated);
            if(compound=="physical")
                Assert.Contains("submitted.Contains(\"owner_country_id\")",generated);
            if(overridePage)Assert.Contains("CompileMultiGlobalParent(\"9\",\"902\")",generated);
            Assert.Contains("CountryId=Metarill.Data.Queries.NonRedundantReads.Value<long?>(row,\"country_id\")",generated);
            if(logicalTarget)
            {
                Assert.Contains("CompileScopedCheckboxLabel",generated);
                Assert.Contains("new(\"target_country_id\",FixedReadComparison.Equal,long.Parse(parentId!",generated);
            }
            else Assert.Contains(checkbox.DerivedName?"new(\"target_scope_id\",FixedReadComparison.Equal,parentId!)":
                derived?"row.TargetScopeId==parentId!":"row.TargetScopeId==long.Parse(parentId!",generated);
            if(alternateName.Length>0)
            {
                Assert.Contains("SelectedNames",generated);
                Assert.Contains("FixedReadComparison.JsonArrayAnyContains",generated);
                Assert.Contains("logicalRelationParentId: parentId",generated);
                Assert.DoesNotContain("entity.SelectedNames=",generated);
            }
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.CheckboxControlParentSourceService",true)!;
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service
                .GetField("ExportReadPlan201",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
            var sqlPlan=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
            if(alternateName.Length>0)
            {
                var readPlan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service
                    .GetField("ReadPlan201",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
                var readSql=readPlan.GetType().GetProperty("MySql",hidden)!.GetValue(readPlan)!;
                var projections=(System.Collections.IDictionary)readSql.GetType().GetProperty("MultiProjections",hidden)!.GetValue(readSql)!;
                Assert.True(projections.Contains("selected_names"));
                var projection=projections["selected_names"];
                Assert.NotNull(projection);
                string Part(string key)=>Assert.IsType<string>(projection.GetType().GetProperty(key)!.GetValue(projection));
                Assert.Contains(logicalTarget?"target_place_id":"target_scope_id",Part("TargetScope")+Part("NameExpression")+Part("Joins"));
                if(joinedName)
                {
                    Assert.Contains("alias_dictionary",Part("Joins"));
                    Assert.Contains("enabled_flag",Part("NameExpression")+Part("Joins"));
                }
                else Assert.Contains("short_label",Part("NameExpression"));
            }
            var parentExpression=Assert.IsType<string>(sqlPlan.GetType().GetMethod("Expression",hidden)!.Invoke(sqlPlan,["country_id"]));
            Assert.StartsWith("CASE WHEN",parentExpression);
            Assert.Contains(" THEN ",parentExpression);
            if(compound=="independent")
            {
                var alternate=Assert.IsType<string>(sqlPlan.GetType().GetMethod("Expression",hidden)!
                    .Invoke(sqlPlan,["alternate_country_id"]));
                Assert.StartsWith("CASE WHEN",alternate);
            }

            if(compound.Length>0)
            {
                var term=compound=="physical"?"owner_country_id=@parentId":"alternate_country_id=@parentId";
                var prior=sourcePage.WhereSql;
                sourcePage.WhereSql=prior.Replace(term,"area_id=@parentId",StringComparison.Ordinal);
                var invalid=GenerationModel.Create(catalog,ns);
                var owner=invalid.Tables.Single(item=>item.Source.Id=="2");
                var bad=DatabaseCheckboxCompilation.Resolve(invalid,owner,owner.Pages.Single(item=>item.Source.Id=="201"),
                    owner.Columns.Single(column=>column.Source.Id=="28"));
                Assert.Null(bad.Definition);
                sourcePage.WhereSql=prior;
            }

            if(compound=="independent"&&!globalParent)
            {
                // 完整包必须继续生成原复选控件、原候选端点和全部后端依赖。
                foreach(var publishedPage in catalog.ConfigPages)
                    catalog.ConfigMenus.Add(new(){Id="700"+publishedPage.Id,YzProjectId=catalog.YzProjectId,
                        YzAdminTypeId=publishedPage.YzAdminTypeId,Name="页面"+publishedPage.Id,
                        Kind="GENERATED_PAGE",ConfigPageId=publishedPage.Id});
                var output=Path.Combine(directory,"full");
                new ApplicationPackager().Create(catalog,output,ns,NonRedundantApplicationFixture.Root());
                foreach(var operation in new[]{"restore","build"})
                    await NonRedundantApplicationFixture.BuildAsync(output,operation);
                var detail=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/checkbox_control_parent_source-detail-201.vue"));
                Assert.Contains("<uni-data-checkbox multiple",detail);
                Assert.Contains("findNameIdSelected28",detail);
            }
            if(alternateName.Length>0)
            {
                var label=catalog.ConfigPageColumns.Single(field=>field.Id==candidateId+"98");
                label.IsShow=label.IsEdit=0;
                var blocked=GenerationModel.Create(catalog,ns);
                var source=blocked.Tables.Single(item=>item.Source.Id=="2");
                Assert.Null(DatabaseCheckboxCompilation.Resolve(blocked,source,source.Pages.Single(),
                    source.Columns.Single(column=>column.Source.Id=="28")).Definition);
                label.IsShow=label.IsEdit=1;
            }
            if(logicalTarget)
                catalog.ConfigPages.Single(item=>item.Id==candidateId).WhereSql=
                    "yz_project_id=@current_yz_project_id AND target_area_id=@parentId";
            else targetParent.LinkConfigTableId="4";
            model=GenerationModel.Create(catalog,ns);
            table=model.Tables.Single(item=>item.Source.Id=="2");
            page=table.Pages.Single(item=>item.Source.Id=="201");
            var rejected=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="28"));
            Assert.Null(rejected.Definition);
            Assert.Contains("同一真实父表",rejected.Reason);
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
