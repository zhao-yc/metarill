using System.Reflection;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行实际生成程序集与ORM SQL，不连接数据库；不能代替正式Web/实库范围验收。</summary>
public sealed class ExactDataManageGenerationTests
{
    /// <summary>普通及含联接读取模型实际编译；隐藏匹配依赖只附加到SQL，不进入VO。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Actual_generated_scope_is_parameterized_and_operation_specific(bool nonredundant)
    {
        var catalog=Catalog(nonredundant);var model=GenerationModel.Create(catalog,"Example.ExactScope");var table=model.Tables.Single(table=>table.Source.Id=="1");var page=table.Pages.Single(page=>page.Source.Id=="101");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-exact-scope");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            var source=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services",table.Name+"Service.g.cs"));
            Assert.Contains("RecordMutationOperation.Import ? \"import\" : \"update\"",source);
            Assert.Contains("\"export\"",source);Assert.Contains("\"delete\"",source);Assert.Contains("\"copy\"",source);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="metarill_business_test",ConnectionString="Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;"});
            var actor=new RuntimeActor(1,"离线范围",false,catalog.YzProjectId,page.Source.YzAdminTypeId);var authorization=new Authorization(actor);
            var entity=assembly.GetType(model.Namespace+".Generated.Entities."+table.Name,true)!;var serviceType=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;
            var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,[session]);
            var service=Activator.CreateInstance(serviceType,[session,mapper,new SnowflakeIdGenerator(862),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            var scope=serviceType.GetMethod("Scope101",BindingFlags.Instance|BindingFlags.NonPublic)!;var parent=nonredundant?"71":null;
            string Sql(string operation)=>(string)typeof(ExactDataManageGenerationTests).GetMethod(nameof(TypedSql),BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(entity).Invoke(null,[scope.Invoke(service,[actor,parent,operation])!])!;
            var sql=Sql("update");Assert.Contains("`"+Match(nonredundant)+"`",sql);Assert.DoesNotContain("只允许甲",sql);Assert.DoesNotContain("只允许乙",sql);Assert.Contains("IN",sql,StringComparison.OrdinalIgnoreCase);Assert.Equal("update",authorization.LastOperation);
            authorization.Restricted=false;var wide=Sql("detail");Assert.DoesNotContain("`"+Match(nonredundant)+"` IN",wide,StringComparison.OrdinalIgnoreCase);Assert.Equal("detail",authorization.LastOperation);
            authorization.Restricted=true;authorization.Value="[]";var empty=System.Text.RegularExpressions.Regex.Replace(Sql("delete"),@"\s+","");Assert.True(empty.Contains("1=2")||empty.Contains("1=0")||empty.Contains("0=1"));
            authorization.Value="[\"只允许甲\",\"只允许乙\"]";
            if(nonredundant)
            {
                Assert.Null(serviceType.GetMethod("MutationResult101Async",BindingFlags.Public|BindingFlags.Instance));
                var mutationResult=serviceType.GetMethod("MutationResult101Async",BindingFlags.NonPublic|BindingFlags.Instance)!;
                await Assert.ThrowsAsync<InvalidOperationException>(async()=>await (Task)mutationResult.Invoke(service,[1L,parent,actor,CancellationToken.None])!);
                Assert.DoesNotContain(serviceType.GetMethods(),method=>method.GetParameters().Any(parameter=>parameter.Name=="authorizationOperation"));
                var compiled=NonRedundantReadCompilation.ForPage(model,table,page);Assert.Contains(Match(true),compiled.Dependencies);Assert.DoesNotContain(compiled.Outputs,output=>output.Storage==Match(true));
                var filters=(List<FixedReadFilter>)serviceType.GetMethod("ReadFilters101",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[new PageRequest{ParentId=parent},actor,"export"])!;
                var exact=Assert.Single(filters,filter=>filter.Field==Match(true));Assert.Equal(FixedReadComparison.In,exact.Comparison);Assert.Equal(new object?[]{"只允许甲","只允许乙"},Assert.IsAssignableFrom<IReadOnlyList<object?>>(exact.Value));Assert.Equal("export",authorization.LastOperation);
            }
            else
            {
                var request=new PageRequest();var query=serviceType.GetMethod("Query101",BindingFlags.Instance|BindingFlags.NonPublic)!;
                query.Invoke(service,[request,actor,"export"]);Assert.Equal("export",authorization.LastOperation);
                authorization.Value="[{}]";var error=Assert.Throws<TargetInvocationException>(()=>scope.Invoke(service,[actor,parent,"update"]));Assert.IsType<ArgumentException>(error.InnerException);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>隐藏派生匹配参与实际生成程序集的读取过滤及私有锁计划，不进入本页VO。</summary>
    [Theory]
    [InlineData(false,true)][InlineData(true,true)][InlineData(true,false)]
    public async Task Derived_match_compiles_as_hidden_read_dependency_and_separate_lock_plan(bool scopedTarget,bool withParent)
    {
        var catalog=Catalog(true);
        var metadataPage=catalog.ConfigPages.Single(page=>page.Id=="101");
        metadataPage.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("customer_notes");
        const long storeId=9007199254741993;
        if(scopedTarget)
        {
            catalog.ConfigTables.Single(target=>target.Id=="2").IsRongyu=0;
            catalog.ConfigColumns.AddRange([
                new(){Id="217",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"},
                new(){Id="218",ConfigTableId="2",ColumnName="status",LogicalType="int32"}
            ]);
            // 有父的单选页与无父的 List 页都按实际目标页匹配角色值。
            var targetPage=catalog.ConfigPages.Single(target=>target.Id=="201");
            if(!withParent)
            {
                targetPage.WhereSql="";
                targetPage.Extra["type"]=JsonSerializer.SerializeToElement(0);
                catalog.ConfigColumns.Single(column=>column.Id=="15").ControlKind="List";
            }
            targetPage.WhereSql+=(withParent?" AND ":"")+"store_id=@current_store_id AND status=2";
        }
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="16")){field.IsShow=0;field.IsEdit=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}
        var model=GenerationModel.Create(catalog,"Example.DerivedScope");var table=model.Tables.Single(table=>table.Source.Id=="1");var page=table.Pages.Single(page=>page.Source.Id=="101");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-exact-scope");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="metarill_business_test",ConnectionString="Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;"});
            var actor=new RuntimeActor(1,"离线派生范围",false,catalog.YzProjectId,page.Source.YzAdminTypeId){StoreId=scopedTarget?storeId:null};var authorization=new Authorization(actor);
            var type=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,[session]);
            var service=Activator.CreateInstance(type,[session,mapper,new SnowflakeIdGenerator(862),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            var plan=Assert.IsType<NonRedundantReadPlan>(type.GetField("ExactReadPlan101",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));Assert.Equal("string",Assert.Single(plan.OutputTypes).Value);Assert.Equal("customer_notes",Assert.Single(plan.OutputTypes).Key);
            var filters=(List<FixedReadFilter>)type.GetMethod("ReadFilters101",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[new PageRequest{ParentId="71"},actor,"export"])!;
            Assert.Single(filters,filter=>filter.Field=="customer_notes"&&filter.Comparison==FixedReadComparison.In);Assert.Equal("export",authorization.LastOperation);
            var vo=assembly.GetType(model.Namespace+".Generated.VOs."+page.Name+"Vo",true)!;Assert.Null(vo.GetProperty("CustomerNotes"));
            // 反射调用须显式占位可选参数，保留生成方法默认启用真实事务锁的合同。
            var scope=type.GetMethod("Scope101",BindingFlags.NonPublic|BindingFlags.Instance)!;var locking=Assert.Single(scope.GetParameters().Skip(3));Assert.Equal("exactLocking",locking.Name);Assert.True(locking.IsOptional);Assert.Equal(true,locking.DefaultValue);
            var error=Assert.Throws<TargetInvocationException>(()=>scope.Invoke(service,[actor,"71","update",Type.Missing]));Assert.IsType<InvalidOperationException>(error.InnerException);Assert.Contains("真实写事务",error.InnerException!.Message);
            if(scopedTarget)
            {
                // 执行实际生成的不加锁记录入口，核对写前锁定复用的同一个匹配计划。
                var entity=assembly.GetType(model.Namespace+".Generated.Entities."+table.Name,true)!;
                var toSql=typeof(ExactDataManageGenerationTests).GetMethod(nameof(TypedSql),BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(entity);
                string Sql(RuntimeActor current,string operation)=>(string)toSql.Invoke(null,
                    [scope.Invoke(service,[current,"71",operation,false])!])!;
                foreach(var operation in new[]{"update","delete","copy","import"})
                {
                    var sql=Sql(actor,operation);
                    Assert.Equal(operation,authorization.LastOperation);
                    Assert.Contains(".`store_id`",sql);
                    Assert.Contains(".`status`",sql);
                    Assert.Matches(@"@mt_e_\w+_constant_\w+",sql);
                    Assert.DoesNotContain(storeId.ToString(),sql);
                    Assert.DoesNotContain("只允许甲",sql);
                    Assert.Equal(withParent,sql.Contains("BINARY CAST(b.`group_id` AS CHAR)",StringComparison.Ordinal));
                    Assert.DoesNotContain("FOR UPDATE",sql,StringComparison.OrdinalIgnoreCase);
                }
                authorization.Actor=actor with{StoreId=null};
                var denied=Assert.Throws<TargetInvocationException>(()=>Sql(authorization.Actor,"update"));
                Assert.IsType<UnauthorizedAccessException>(denied.InnerException);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未配置精确字段的旧页面不增加授权依赖、参数或调用；源元数据不变。</summary>
    [Fact]
    public void Existing_unconfigured_pages_keep_the_original_service_contract()
    {
        var catalog=GeneratorTests.Catalog();var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.NoExact");var source=BackendRenderer.Render(model).Single(file=>file.Path=="Services/CustomerService.g.cs").Content;
        Assert.DoesNotContain("ResolveExactDataManage",source);Assert.DoesNotContain("string authorizationOperation",source);Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }
    /// <summary>坏声明、缺后台或未知字段均在渲染期拒绝，不输出看似成功的无限制查询。</summary>
    [Theory]
    [InlineData("expression")][InlineData("missing-field")][InlineData("missing-admin")][InlineData("array-field")]
    public void Unproven_scope_declarations_do_not_silently_disappear(string scenario)
    {
        var catalog=Catalog(false);var page=catalog.ConfigPages[0];
        switch(scenario)
        {
            case "expression":page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name OR 1=1");break;
            case "missing-field":page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("unknown");break;
            case "missing-admin":catalog.YzAdminTypes.Clear();break;
            case "array-field":catalog.ConfigColumns.Single(column=>column.ColumnName=="name").ControlKind="FileLibrary";break;
        }
        var model=GenerationModel.Create(catalog,"Example.InvalidExact");
        var expected=scenario switch{"expression"=>typeof(ArgumentException),"array-field"=>typeof(NotSupportedException),_=>typeof(InvalidOperationException)};
        var error=Assert.Throws(expected,()=>BackendRenderer.Render(model).ToArray());Assert.Contains("精确管理",error.Message);
    }
    /// <summary>实际生成的单选 Excel 读取目标页的派生匹配计划，普通候选保持原入口。</summary>
    [Theory]
    [InlineData(false, "OnlyNew", "list", false)]
    [InlineData(true, "OverWriteByFirstColumn", "edit", false)]
    [InlineData(false, "OnlyNew", "list", true)]
    [InlineData(true, "OverWriteByFirstColumn", "edit", true)]
    public async Task Redundant_selection_excel_uses_target_derived_exact_plan(bool fixedTarget, string importMode,
        string exportMode, bool derivedDisplay)
    {
        const string generatedNamespace = "Example.ExcelDerivedExact";
        var catalog = RedundantMultiNameCacheTests.Published(RelationOptionsContractTests.Catalog());
        catalog.YzAdminTypes.Single().Extra["exactDataManageMatchField"] = JsonSerializer.SerializeToElement("realName");
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = 0;
        catalog.ConfigTables.Single(table => table.Id == "1").IsRongyu = 0;
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
        sourcePage.WhereSql = "yz_project_id=@current_yz_project_id";
        sourcePage.IsImport = sourcePage.IsExport = 1;
        sourcePage.Extra["importMethod"] = JsonSerializer.SerializeToElement(importMode);
        sourcePage.Extra["exportMethod"] = JsonSerializer.SerializeToElement(exportMode);
        var foreign = catalog.ConfigColumns.Single(column => column.Id == "25");
        foreign.ControlKind = "SelectPage";
        foreign.ControlDetail = JsonSerializer.Serialize(new { pageId = "301", tableId = "3",
            nameField = derivedDisplay ? "parent_alias" : "name" });
        foreign.Name = "目标名称";
        foreign.SortNo = -10;

        var targetPage = catalog.ConfigPages.Single(page => page.Id == "301");
        targetPage.WhereSql = "yz_project_id=@current_yz_project_id" + (fixedTarget ? " AND status=3" : "");
        targetPage.Extra["type"] = JsonSerializer.SerializeToElement(1);
        targetPage.Extra["exactDataManageMatchField"] = JsonSerializer.SerializeToElement("parent_name");
        var innerForeign = catalog.ConfigColumns.Single(column => column.Id == "36");
        innerForeign.ControlKind = "List";
        innerForeign.ControlDetail = """{"tableId":"1","nameField":"name"}""";
        innerForeign.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("101");
        if (fixedTarget)
        {
            catalog.ConfigPages.Single(page => page.Id == "101").WhereSql += " AND store_id=@current_store_id";
            catalog.ConfigColumns.Add(new() { Id = "15", ConfigTableId = "1", YzProjectId = catalog.YzProjectId,
                ColumnName = "store_id", LogicalType = "int64" });
        }
        catalog.ConfigColumns.AddRange([
            new() { Id = "37", ConfigTableId = "3", YzProjectId = catalog.YzProjectId, ColumnName = "parent_name", LogicalType = "string",
                Extra = new() { ["isJoinColumn"] = JsonSerializer.SerializeToElement(1), ["columnFullName"] = JsonSerializer.SerializeToElement("parent.name") } },
            new() { Id = "38", ConfigTableId = "3", YzProjectId = catalog.YzProjectId, ColumnName = "status", LogicalType = "int32" }
        ]);
        if (derivedDisplay)
        {
            catalog.ConfigColumns.AddRange([
                new() { Id = "16", ConfigTableId = "1", YzProjectId = catalog.YzProjectId, ColumnName = "alias", LogicalType = "string" },
                new() { Id = "39", ConfigTableId = "3", YzProjectId = catalog.YzProjectId, ColumnName = "parent_alias", LogicalType = "string",
                    Extra = new() { ["isJoinColumn"] = JsonSerializer.SerializeToElement(1), ["columnFullName"] = JsonSerializer.SerializeToElement("parent.alias") } }
            ]);
            catalog.ConfigPageColumns.Add(new() { Id = "30139", ConfigPageId = "301", ConfigColumnId = "39",
                YzProjectId = catalog.YzProjectId, IsEdit = 0 });
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-single-excel-exact");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, generated, generatedNamespace);
            new FrontendPackager().Validate(catalog);
            var source = await File.ReadAllTextAsync(Path.Combine(generated, "Services/RelationSourceService.g.cs"));
            if (derivedDisplay)
            {
                Assert.Contains("DerivedOptionsRows201_25Async(request.ParentId,groupTargetId.Key", source);
                Assert.Contains("cancellationToken,locking:true", source);
                Assert.Contains("condition:scope.Condition,currentRead:true", source);
            }
            else
            {
                Assert.Contains("RelationTargetService.ExactReadPlan301", source);
                Assert.Contains("groupTargetId.Key, actor, cancellationToken, excelScope: true", source);
                Assert.Contains("actor,cancellationToken,true,false, excelScope: true", source);
                Assert.Contains("query.ForUpdate().Limit(2)", source);
            }
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session = new DatabaseSession(new()
            {
                Provider = "mysql",
                ExpectedDatabase = "metarill_business_test",
                ConnectionString = "Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;"
            });
            var actor = new RuntimeActor(1, "单选 Excel 权限", false, catalog.YzProjectId, sourcePage.YzAdminTypeId)
            {
                StoreId = 9007199254741987
            };
            var authorization = new Authorization(actor, "301");
            var type = assembly.GetType(generatedNamespace + ".Generated.Services.RelationSourceService", true)!;
            var mapper = Activator.CreateInstance(assembly.GetType(generatedNamespace + ".Generated.Mappers.RelationSourceMapper", true)!, [session]);
            var service = Activator.CreateInstance(type, [session, mapper, new SnowflakeIdGenerator(862),
                Array.Empty<IRecordMutationObserver>(), Array.Empty<IRecordMutationGuard>(), authorization])!;
            var targetType = assembly.GetType(generatedNamespace + ".Generated.Services.RelationTargetService", true)!;
            var match = Assert.IsType<NonRedundantReadPlan>(targetType.GetField("ExactReadPlan301", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
            Assert.Equal("parent_name", Assert.Single(match.OutputTypes).Key);
            if (derivedDisplay)
            {
                var names = Assert.IsType<NonRedundantReadPlan>(type.GetField("DerivedOptionsPlan201_25",
                    BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
                Assert.Equal(new[] { "id", "parent_alias" }, names.OutputTypes.Keys.Order());
                var bound = names.BindAccount(actor);
                var mysql = typeof(NonRedundantReadPlan).GetProperty("MySql", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bound)!;
                var expression = mysql.GetType().GetMethod("Expression", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.Contains(".\u0060alias\u0060", (string)expression.Invoke(mysql, ["parent_alias"])!);
                Assert.Contains(".\u0060name\u0060", (string)expression.Invoke(mysql, ["parent_name"])!);
                if (fixedTarget)
                {
                    var parameters = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(mysql.GetType()
                        .GetProperty("Constants", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mysql));
                    Assert.Contains(actor.StoreId, parameters.Values);
                }
                var derivedScope = type.GetMethod("DerivedOptionsScope201_25Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var scoped = (Task<(List<FixedReadFilter> Filters, FixedConditionSqlPlan? Condition)>)derivedScope.Invoke(service,
                    [null, null, actor, CancellationToken.None, null, false])!;
                var result = await scoped;
                var exact = Assert.Single(result.Filters, filter => filter.Field == "parent_name");
                Assert.Equal(FixedReadComparison.In, exact.Comparison);
                Assert.Equal(new object?[] { "只允许甲", "只允许乙" }, Assert.IsAssignableFrom<IReadOnlyList<object?>>(exact.Value));
                Assert.Equal("list", authorization.LastOperation);

                var importer = type.GetMethod("ImportRelation201_25Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var rejected = (Task)importer.Invoke(service, [JsonSerializer.SerializeToElement("候选"), null, actor, CancellationToken.None])!;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rejected);
                authorization.Value = "[]";
                var emptyScope = (Task<(List<FixedReadFilter> Filters, FixedConditionSqlPlan? Condition)>)derivedScope.Invoke(service,
                    [null, null, actor, CancellationToken.None, null, false])!;
                Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<object?>>((await emptyScope).Filters.Single(filter => filter.Field == "parent_name").Value));
                return;
            }
            var entity = assembly.GetType(generatedNamespace + ".Generated.Entities.RelationTarget", true)!;
            var toSql = typeof(ExactDataManageGenerationTests).GetMethod(nameof(TypedSql), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entity);
            var scope = type.GetMethod("OptionsScope201_25Async", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var sql = (string)toSql.Invoke(null, [await Query(false, true)])!;
            Assert.Equal("list", authorization.LastOperation);
            Assert.Contains("EXISTS (SELECT 1", sql);
            Assert.Contains("relation_parent", sql);
            Assert.Contains("_value_0", sql);
            Assert.DoesNotContain("只允许甲", sql);
            if (fixedTarget)
            {
                Assert.Contains(".`status`", sql);
                Assert.Contains(".`store_id`", sql);
                Assert.DoesNotContain(actor.StoreId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), sql);
            }
            var normal = (string)toSql.Invoke(null, [await Query(false, false)])!;
            Assert.DoesNotContain("_value_0", normal);
            authorization.Value = "[]";
            var empty = (string)toSql.Invoke(null, [await Query(false, true)])!;
            Assert.Contains("1=0", empty);
            authorization.Value = "[\"只允许甲\"]";
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Query(true, true));
            Assert.Contains("真实写事务", error.Message);

            async Task<object> Query(bool locking, bool excelScope)
            {
                var task = (Task)scope.Invoke(service, [null, null, actor, CancellationToken.None, locking, false, excelScope])!;
                await task;
                return task.GetType().GetProperty("Result")!.GetValue(task)!;
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>独立和共享级联权限按目标页逻辑值匹配，联接名称沿实际中间页读取。</summary>
    [Theory]
    [InlineData(false, "countryId", "country_id")]
    [InlineData(true, "countryId", "country_id")]
    [InlineData(true, "countryName", "country_name")]
    [InlineData(false, "countryId", "country_id", "country_name")]
    [InlineData(true, "countryName", "country_name", "area_name")]
    [InlineData(false, "countryId", "country_id", "country_name", true)]
    [InlineData(true, "countryName", "country_name", "area_name", true)]
    [InlineData(false, "areaId", "area_id", null, true, true)]
    [InlineData(false, "areaId", "area_id", "country_name", true, true)]
    [InlineData(true, "areaName", "area_name", "country_name", false, true, true)]
    public async Task Redundant_selection_excel_uses_target_cascade_exact_plan(bool shared, string key, string field,
        string? fixedField = null, bool physicalName = false, bool logicalParent = false, bool independentParent = false)
    {
        const string generatedNamespace = "Example.ExcelCascadeExact";
        var catalog = shared ? SharedCrossExactScopeGenerationTests.Catalog(key)
            : RedundantMultiNameCacheTests.Published(NonRedundantCrossTableGenerationTests.Catalog("exact_single", "string", "int64"));
        var targetPage = catalog.ConfigPages.Single(page => page.Id == "201");
        targetPage.Extra["type"] = JsonSerializer.SerializeToElement(1);
        targetPage.Extra["exactDataManageMatchField"] = JsonSerializer.SerializeToElement(key);
        if (fixedField is not null) targetPage.WhereSql += $" AND {fixedField}='固定候选'";
        if (logicalParent) targetPage.WhereSql += " AND country_id=@parent_id";
        var project = catalog.YzProjectId;
        var admin = targetPage.YzAdminTypeId;
        var label = physicalName ? "name" : "dictionary_name";
        catalog.YzAdminTypes.Single(type => type.Id == admin).Extra["exactDataManageMatchField"] = JsonSerializer.SerializeToElement("realName");
        catalog.ConfigTables.AddRange([
            new() { Id = "990", YzProjectId = project, TableName = "exact_excel_dictionary", IsRongyu = 0 },
            new() { Id = "991", YzProjectId = project, TableName = "exact_excel_consumer", IsRongyu = 1 }
        ]);
        catalog.ConfigColumns.AddRange([
            new() { Id = "99001", YzProjectId = project, ConfigTableId = "990", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 },
            new() { Id = "99002", YzProjectId = project, ConfigTableId = "990", ColumnName = "name", LogicalType = "string" },
            new() { Id = "99003", YzProjectId = project, ConfigTableId = "990", ColumnName = "yz_project_id", LogicalType = "int64" },
            new() { Id = "99004", YzProjectId = project, ConfigTableId = "990", ColumnName = "store_id", LogicalType = "int64" },
            new() { Id = "99101", YzProjectId = project, ConfigTableId = "991", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 },
            new() { Id = "99102", YzProjectId = project, ConfigTableId = "991", ColumnName = "target_id", LogicalType = "int64",
                Name = "目标名称", LinkConfigTableId = "2", ControlKind = "SelectPage",
                ControlDetail = JsonSerializer.Serialize(new { pageId = "201", tableId = "2", nameField = label }) },
            new() { Id = "99103", YzProjectId = project, ConfigTableId = "991", ColumnName = "yz_project_id", LogicalType = "int64" },
            new() { Id = "99005", YzProjectId = project, ConfigTableId = "2", ColumnName = "dictionary_id", LogicalType = "int64",
                LinkConfigTableId = "990", ControlKind = "List", ControlDetail = """{"tableId":"990","nameField":"name"}""",
                Extra = new() { ["linkConfigPageId"] = JsonSerializer.SerializeToElement("9901") } },
            new() { Id = "99006", YzProjectId = project, ConfigTableId = "2", ColumnName = "dictionary_name", LogicalType = "string",
                Extra = new() { ["isJoinColumn"] = JsonSerializer.SerializeToElement(1), ["columnFullName"] = JsonSerializer.SerializeToElement("dictionary.name") } }
        ]);
        catalog.ConfigPages.AddRange([
            new() { Id = "9901", YzProjectId = project, YzAdminTypeId = admin, ConfigTableId = "990",
                WhereSql = "yz_project_id=@current_yz_project_id" + (shared ? " AND store_id=@current_store_id" : "") },
            new() { Id = "9911", YzProjectId = project, YzAdminTypeId = admin, ConfigTableId = "991",
                WhereSql = "yz_project_id=@current_yz_project_id", IsImport = 1, IsExport = 1,
                Extra = new() { ["importMethod"] = JsonSerializer.SerializeToElement(shared ? "OverWriteByFirstColumn" : "OnlyNew"),
                    ["exportMethod"] = JsonSerializer.SerializeToElement(shared ? "edit" : "list") } }
        ]);
        catalog.ConfigPageColumns.AddRange([
            new() { Id = "99012", YzProjectId = project, ConfigPageId = "9901", ConfigColumnId = "99002" },
            new() { Id = "99112", YzProjectId = project, ConfigPageId = "9911", ConfigColumnId = "99102" },
            new() { Id = "20199006", YzProjectId = project, ConfigPageId = "201", ConfigColumnId = "99006", IsEdit = 0 }
        ]);
        if (logicalParent)
        {
            catalog.ConfigColumns.Add(new() { Id = "99104", YzProjectId = project, ConfigTableId = "991",
                ColumnName = "country_id", Name = "父级", LogicalType = "int64", LinkConfigTableId = "3" });
            if (independentParent)
                catalog.ConfigPageColumns.Add(new() { Id = "99114", YzProjectId = project, ConfigPageId = "9911", ConfigColumnId = "99104" });
            else
                catalog.ConfigPages.Single(page => page.Id == "9911").WhereSql += " AND country_id=@parent_id";
            Assert.Throws<NotSupportedException>(() => NonRedundantReads.CompileCrossPageCandidate(catalog, "2", "201", ["id"], []));
        }
        var model = GenerationModel.Create(catalog, generatedNamespace);
        var table = model.Tables.Single(item => item.Source.Id == "2");
        var exact = ExactDataManageCompilation.Resolve(model, table, table.Pages.Single(item => item.Source.Id == "201"))!;
        Assert.Equal(shared ? 2 : 1, exact.CrossPlans!.Count);
        Assert.Equal(field, exact.Column.Source.ColumnName);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-single-excel-cross-exact");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), generatedNamespace);
            new FrontendPackager().Validate(catalog);
            var source = await File.ReadAllTextAsync(Path.Combine(directory, "generated/Services/ExactExcelConsumerService.g.cs"));
            Assert.Contains(logicalParent ? "CompileExactSingleParentCandidate" :
                fixedField is not null ? "CompileRelationMembership" : shared ? "CompileProjectPage" : "CompileCross", source);
            if (logicalParent)
            {
                Assert.Contains("ApplyCandidate(query,session,DerivedOptionsPlan9911_99102.BindAccount(actor)", source);
                Assert.Contains("""ResolveExactDataManage("201", "list", "real_name")""", source);
                Assert.Contains("Service.ExactReadPlan201", source);
                Assert.Contains(independentParent ? "relationContext.TryGetValue(\"countryId\"" : "targetParentId??=parentId", source);
            }
            if (fixedField is not null)
            {
                if (!logicalParent) Assert.Contains("ApplyCondition(query,session,DerivedOptionsPlan9911_99102.BindAccount(actor)", source);
                Assert.Contains("Service.ExactReadPlan201", source);
                Assert.Contains("locking:locking", source);
            }
            Assert.Contains("DerivedOptionsRows9911_99102Async(request.ParentId,groupTargetId.Key", source);
            Assert.Contains("condition:scope.Condition,currentRead:true", source);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var actor = new RuntimeActor(1, "级联精确范围", false, project, admin) { StoreId = 9007199254741987 };
            const long number = 9007199254741989;
            var nameField = field.EndsWith("_name", StringComparison.Ordinal);
            var authorization = new Authorization(actor, "201") { Value = nameField ? "[\"范围甲\"]" : "[\"9007199254741989\"]" };
            using var session = new DatabaseSession(new()
            {
                Provider = "mysql", ExpectedDatabase = "metarill_business_test",
                ConnectionString = "Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;"
            });
            var type = assembly.GetType(generatedNamespace + ".Generated.Services.ExactExcelConsumerService", true)!;
            var mapper = Activator.CreateInstance(assembly.GetType(generatedNamespace + ".Generated.Mappers.ExactExcelConsumerMapper", true)!, [session]);
            var service = Activator.CreateInstance(type, [session, mapper, new SnowflakeIdGenerator(862),
                Array.Empty<IRecordMutationObserver>(), Array.Empty<IRecordMutationGuard>(), authorization])!;
            var plan = Assert.IsType<NonRedundantReadPlan>(type.GetField("DerivedOptionsPlan9911_99102",
                BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
            Assert.Equal(new[] { "id", label }.Order(), plan.OutputTypes.Keys.Order());
            var mysql = typeof(NonRedundantReadPlan).GetProperty("MySql", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plan.BindAccount(actor))!;
            var expression = typeof(ExactDataManageSql).GetMethod("CrossExpression", BindingFlags.Static | BindingFlags.NonPublic)!;
            var sql = (string)expression.Invoke(null, [mysql, field])!;
            Assert.Contains("CASE WHEN", sql, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual($"b.\u0060{field}\u0060", sql);
            if (logicalParent)
            {
                var parentField = mysql.GetType().GetProperty("RequiredMultiParent", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.Equal("country_id", parentField.GetValue(mysql));
                var entityType = assembly.GetType(generatedNamespace + ".Generated.Entities." + table.Name, true)!;
                var query = typeof(ExactDataManageGenerationTests).GetMethod(nameof(TypedParentSql),
                    BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entityType);
                var parentSql = (string)query.Invoke(null, [session, plan, actor, "9007199254741991"])!;
                Assert.Contains("combined_parent_parent", parentSql);
                Assert.DoesNotContain("9007199254741991", parentSql);
                Assert.Contains("WHERE", parentSql, StringComparison.OrdinalIgnoreCase);
                var parentGuard = typeof(MySqlNonRedundantReader).GetMethod("RequireMultiParentFilter",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                var rejected = Assert.Throws<TargetInvocationException>(() => parentGuard.Invoke(null,
                    [mysql, new[] { FixedReadFilter.ForCrossExact(field, [number]) }]));
                Assert.IsType<InvalidOperationException>(rejected.InnerException);
                return;
            }
            var scope = type.GetMethod("DerivedOptionsScope9911_99102Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var scoped = (Task<(List<FixedReadFilter> Filters, FixedConditionSqlPlan? Condition)>)scope.Invoke(service,
                [null, null, actor, CancellationToken.None, null, false])!;
            var result = await scoped;
            if (fixedField is not null)
            {
                var condition = Assert.IsType<FixedConditionSqlPlan>(result.Condition);
                using var command = new MySqlConnector.MySqlCommand();
                var predicate = condition.BindConstants(command, session, "fixed_target");
                Assert.Contains("\u0060" + fixedField + "\u0060", predicate);
                Assert.Contains("固定候选", command.Parameters.Cast<System.Data.Common.DbParameter>().Select(parameter => parameter.Value));
                Assert.DoesNotContain("固定候选", predicate);
                var logicalSql = mysql.GetType().GetMethod("Expression", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.NotEqual($"b.\u0060{fixedField}\u0060", (string)logicalSql.Invoke(mysql, [fixedField])!);
            }
            var filter = Assert.Single(result.Filters, candidate => candidate.Field == field);
            Assert.Equal("list", authorization.LastOperation);
            Assert.Equal(FixedReadComparison.In, filter.Comparison);
            object value = exact.Column.Type == "long?" ? number : nameField ? "范围甲" : "9007199254741989";
            Assert.Equal(new object?[] { value }, Assert.IsAssignableFrom<IReadOnlyList<object?>>(filter.Value));
            Assert.True((bool)typeof(FixedReadFilter).GetProperty("SharedExact", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(filter)!);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>标准模型和非冗余正式应用模型共用同一已发布后台声明，不手写业务实体。</summary>
    internal static MetadataCatalog Catalog(bool nonredundant)
    {
        var catalog=nonredundant?NonRedundantApplicationFixture.Catalog("mt_exact_nr"):GeneratorTests.Catalog();
        if(!nonredundant)catalog.YzAdminTypes.Add(new(){Id="10",YzProjectId=catalog.YzProjectId});
        var page=catalog.ConfigPages.Single(page=>page.Id=="101");if(!nonredundant)page.YzAdminTypeId="10";
        if(nonredundant)
        {
            // 原夹具该字段在编辑页可见；本专项显式关闭两个原标志，才用于证明纯隐藏范围依赖。
            var column=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="1"&&column.ColumnName==Match(true));
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId==column.Id))
            {field.IsShow=0;field.IsEdit=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}
        }
        page.IsCopy=1;page.IsExport=1;page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement(Match(nonredundant));
        catalog.YzAdminTypes.Single(admin=>admin.Id==page.YzAdminTypeId).Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("realName");return catalog;
    }
    /// <summary>非冗余样本使用隐藏物理列，证明范围不会顺便扩充响应字段。</summary>
    private static string Match(bool nonredundant)=>nonredundant?"private_payload":"name";
    /// <summary>由FreeSql实际解析实体表达式，测试不复制SQL渲染算法。</summary>
    private static string TypedSql<T>(object query)where T:class=>((ISelect<T>)query).ToSql();
    /// <summary>检查真实适配器对逻辑父参数的相关查询，验证过程无需打开数据库连接。</summary>
    private static string TypedParentSql<T>(DatabaseSession session, NonRedundantReadPlan plan, RuntimeActor actor, string parent) where T : class =>
        NonRedundantCrossTableScopeSql.ApplyCandidate(session.Select<T>(), session, plan.BindAccount(actor),
            "combined_parent", actor.ProjectId, false, parent).ToSql();

    /// <summary>原单选使用隐藏的自树叶名核对权限，选档附加条件只影响导入。</summary>
    [Theory]
    [InlineData(0,false,false)]
    [InlineData(1,false,false)]
    [InlineData(0,false,true)]
    [InlineData(0,true,true)]
    [InlineData(1,true,false)]
    public async Task Tree_exact_single_selection_uses_published_page(int storage,bool logicalParent,bool importCondition)
    {
        const string ns="Example.SingleTreeExact";
        var catalog=RedundantMultiNameCacheTests.Published(
            NonRedundantCrossTableGenerationTests.Catalog("single_tree_exact","string","int64"));
        var project=catalog.YzProjectId;
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="201");
        var admin=targetPage.YzAdminTypeId;
        targetPage.Extra["type"]=JsonSerializer.SerializeToElement(1);
        targetPage.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("tree_name");
        if(logicalParent)targetPage.WhereSql+=" AND country_id=@parent_id";
        catalog.YzAdminTypes.Single(item=>item.Id==admin).Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");

        catalog.ConfigTables.AddRange([
            new(){Id="980",YzProjectId=project,TableName="single_tree_nodes",IsRongyu=1},
            new(){Id="981",YzProjectId=project,TableName="single_tree_consumer",IsRongyu=storage}
        ]);
        var conditions=importCondition?"tree_name <> '仅导入排除'":"";
        catalog.ConfigColumns.AddRange([
            new(){Id="98001",ConfigTableId="980",YzProjectId=project,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="98002",ConfigTableId="980",YzProjectId=project,ColumnName="name",LogicalType="string"},
            new(){Id="98003",ConfigTableId="980",YzProjectId=project,ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="98004",ConfigTableId="980",YzProjectId=project,ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="98005",ConfigTableId="980",YzProjectId=project,ColumnName="parent_single_tree_nodes_id",
                LinkConfigTableId="980",LogicalType="int64"},
            new(){Id="98006",ConfigTableId="980",YzProjectId=project,ColumnName="store_id",LogicalType="int64"},
            new(){Id="98007",ConfigTableId="2",YzProjectId=project,ColumnName="tree_id",LogicalType="string",
                LinkConfigTableId="980",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new
                {
                    sourceMode="selfTree",displayJoiner="/",selfTree=new{table="single_tree_nodes",idField="id",nameField="name",
                        parentField="parent_single_tree_nodes_id",rootMode="blank",filterMatchMode="selfAndDescendants"}
                })},
            new(){Id="98101",ConfigTableId="981",YzProjectId=project,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="98102",ConfigTableId="981",YzProjectId=project,ColumnName="name",LogicalType="string",SortNo=1},
            new(){Id="98103",ConfigTableId="981",YzProjectId=project,ColumnName="target_id",LogicalType="int64",SortNo=2,Name="目标名称",
                LinkConfigTableId="2",ControlKind="SelectPage",ControlDetail=JsonSerializer.Serialize(new
                {pageId="201",tableId="2",nameField="name",whereSql=conditions})},
            new(){Id="98104",ConfigTableId="981",YzProjectId=project,ColumnName="yz_project_id",LogicalType="int64"}
        ]);
        if(logicalParent)catalog.ConfigColumns.Add(new(){Id="98105",ConfigTableId="981",YzProjectId=project,ColumnName="country_id",
            LogicalType="int64",LinkConfigTableId="3"});
        catalog.ConfigPages.AddRange([
            new(){Id="9801",ConfigTableId="980",YzProjectId=project,YzAdminTypeId=admin,
                WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id"},
            new(){Id="9811",ConfigTableId="981",YzProjectId=project,YzAdminTypeId=admin,IsImport=1,IsExport=1,
                WhereSql="yz_project_id=@current_yz_project_id"+(logicalParent?" AND country_id=@parent_id":""),
                Extra=new(){["importMethod"]=JsonSerializer.SerializeToElement(storage==0?"OnlyNew":"OverWriteByFirstColumn"),
                    ["exportMethod"]=JsonSerializer.SerializeToElement(storage==0?"list":"edit")}}
        ]);
        catalog.ConfigPageColumns.AddRange([
            new(){Id="98012",ConfigPageId="9801",ConfigColumnId="98002",YzProjectId=project},
            new(){Id="98015",ConfigPageId="9801",ConfigColumnId="98005",YzProjectId=project},
            new(){Id="98112",ConfigPageId="9811",ConfigColumnId="98102",YzProjectId=project},
            new(){Id="98113",ConfigPageId="9811",ConfigColumnId="98103",YzProjectId=project}
        ]);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-tree-exact");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            new FrontendPackager().Validate(catalog);
            var servicePath=Path.Combine(directory,"generated/Services/SingleTreeConsumerService.g.cs");
            var source=await File.ReadAllTextAsync(servicePath);
            Assert.Contains(logicalParent?"CompileExactSingleParentCandidate":"CompileRelationMembership",source);
            Assert.Contains("Service.ExactReadPlan201",source);
            Assert.Contains("""ResolveExactDataManage("201", "list", "real_name")""",source);
            Assert.Contains("actor, cancellationToken, true, false",source);
            Assert.Contains("DerivedOptionsRows9811_98103Async",source);
            Assert.Contains("currentRead:true",source);
            Assert.Equal(importCondition,source.Contains("scope.Condition.And(importCondition)",StringComparison.Ordinal));
            Assert.DoesNotContain("SingleImportReadPlan9811_98103",source);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type=assembly.GetType(ns+".Generated.Services.SingleTreeConsumerService",true)!;
            const BindingFlags staticFlags=BindingFlags.NonPublic|BindingFlags.Static;
            const BindingFlags instanceFlags=BindingFlags.NonPublic|BindingFlags.Instance;
            var plan=Assert.IsType<NonRedundantReadPlan>(type.GetField("DerivedOptionsPlan9811_98103",staticFlags)!.GetValue(null));
            var actor=new RuntimeActor(1,"叶名权限测试",false,project,admin){StoreId=9007199254741987};
            Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with {StoreId=null}));
            Assert.Equal(new[]{"id","name"},plan.OutputTypes.Keys.Order());
            var mysql=typeof(NonRedundantReadPlan).GetProperty("MySql",instanceFlags)!.GetValue(plan.BindAccount(actor))!;
            var expression=Assert.IsType<string>(mysql.GetType().GetMethod("Expression",instanceFlags)!.Invoke(mysql,["tree_name"]));
            Assert.Contains("WITH RECURSIVE",expression);
            Assert.Contains("`store_id`=@",expression);
            Assert.Contains("`delete_no` IS NULL",expression);
            if(logicalParent)
            {
                Assert.Equal("country_id",mysql.GetType().GetProperty("RequiredMultiParent",instanceFlags)!.GetValue(mysql));
                return;
            }
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="metarill_business_test",
                ConnectionString="Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;"});
            var mapper=Activator.CreateInstance(assembly.GetType(ns+".Generated.Mappers.SingleTreeConsumerMapper",true)!,[session]);
            var authorization=new Authorization(actor,"201");
            var service=Activator.CreateInstance(type,[session,mapper,new SnowflakeIdGenerator(862),
                Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            var read=type.GetMethod("DerivedOptionsScope9811_98103Async",instanceFlags)!;
            var result=await (Task<(List<FixedReadFilter> Filters,FixedConditionSqlPlan? Condition)>)read.Invoke(service,
                [null,null,actor,CancellationToken.None,null,false])!;
            var exact=Assert.Single(result.Filters,item=>item.Field=="tree_name");
            Assert.Equal(FixedReadComparison.In,exact.Comparison);
            Assert.Equal(new object?[]{"只允许甲","只允许乙"},Assert.IsAssignableFrom<IReadOnlyList<object?>>(exact.Value));
            Assert.Equal("list",authorization.LastOperation);
            authorization.Value="[]";
            result=await (Task<(List<FixedReadFilter> Filters,FixedConditionSqlPlan? Condition)>)read.Invoke(service,
                [null,null,actor,CancellationToken.None,null,false])!;
            Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<object?>>(Assert.Single(result.Filters,item=>item.Field=="tree_name").Value));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>范围测试替身仅提供服务端值，和实际Web认证/范围聚合的验收分开。</summary>
    private sealed class Authorization(RuntimeActor actor, string expectedPage = "101"):IRuntimeAuthorization
    {
        internal bool Restricted=true;internal string Value="[\"只允许甲\",\"只允许乙\"]";internal string LastOperation="";
        internal RuntimeActor Actor=actor;
        /// <summary>返回当前测试身份，可切换为缺少归属的真实登录形状。</summary>
        public RuntimeActor RequireActor()=>Actor;
        /// <summary>本类只测试计划消费，不冒充角色权限决策。</summary>
        public void RequirePagePermission(string pageId,string operation){}
        /// <summary>记录计划传来的固定动作，并提供原JSON数组形状。</summary>
        public ExactDataManageValues ResolveExactDataManage(string pageId,string operation,string managerField)
        {Assert.Equal(expectedPage,pageId);Assert.Equal("real_name",managerField);LastOperation=operation;return Restricted?new(true,JsonSerializer.SerializeToElement(Value)):ExactDataManageValues.Unrestricted;}
    }
}
