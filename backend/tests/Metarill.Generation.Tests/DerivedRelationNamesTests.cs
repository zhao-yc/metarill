using System.Reflection;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>普通R0及已发布R1来源的List使用R0目标的联接名称，候选、原导入导出和写回显必须共用同一计划。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedRelationNamesTests
{
    /// <summary>真实生成编译，派生名称不允许回退为Entity忽略属性上的物理SQL。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Derived_list_name_compiles_candidates_import_and_export(bool parentAndForm,bool cached)
    {
        var catalog=Catalog("dr_names",parentAndForm);if(cached)catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;var before=JsonSerializer.Serialize(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-relation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.DerivedNames");
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            Assert.Contains("DerivedOptionsPlan101_15",service);Assert.DoesNotContain("entity.CompanyName.Contains(keyword)",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.DerivedNames.Generated.Services.DrNamesOrderService"));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.DerivedNames")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>固定自树条件通过原单值候选、保存与 Excel 共用同一已发布计划。</summary>
    [Theory]
    [InlineData(false, false, false, null, false)]
    [InlineData(true, false, true, null, false)]
    [InlineData(false, true, true, null, false)]
    [InlineData(true, true, false, null, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, true, false, true, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(true, true, false, true, false, true)]
    [InlineData(false, true, false, true, true, true)]
    [InlineData(true, false, false, false, true, true)]
    [InlineData(false, false, false, null, true)]
    [InlineData(true, false, true, null, true)]
    [InlineData(false, true, true, null, true, true)]
    [InlineData(true, true, false, null, true, true)]
    public async Task Single_relation_target_leaf_condition_reaches_original_services(bool selection,bool derived,bool parent,
        bool? globalWithProject,bool redundant,bool cross=false)
    {
        const string prefix="dr_names";
        var catalog=Catalog(prefix,parent);
        var target=catalog.ConfigPages.Single(page=>page.Id=="201");
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="15");
        if(selection)
        {
            // 选择页原附加条件已经由目标页执行。去掉该夹具独立的 List 表单条件。
            target.Extra["type"]=JsonSerializer.SerializeToElement(1);
            foreign.ControlKind="SelectPage";
        }
        var label=derived?"company_name":"name";
        var logicalField=cross?"country_name":"tree_name";
        foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="201",nameField=label,
            whereSql=selection?logicalField+" <> '排除叶名'":parent?"segment=@formParameters.segment":""});
        if(!derived)
        {
            var display=catalog.ConfigColumns.Single(column=>column.Id=="113");
            display.ColumnName="customer_name";
            display.Extra["columnFullName"]=JsonSerializer.SerializeToElement("customer.name");
        }

        catalog.ConfigTables.Add(new(){Id="6",TableName=prefix+"_tree",IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="62",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="63",ConfigTableId="6",ColumnName="name",LogicalType="string"},
            new(){Id="64",ConfigTableId="6",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="65",ConfigTableId="6",ColumnName="parent_dr_names_tree_id",LogicalType="int64",LinkConfigTableId="6"},
            new(){Id="216",ConfigTableId="2",ColumnName="tree_id",LogicalType="int64",ControlKind="CascadeTree",LinkConfigTableId="6",
                ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table=prefix+"_tree",parentField="parent_dr_names_tree_id"}})},
            new(){Id="217",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"}
        ]);
        catalog.ConfigPages.Add(new(){Id="601",ConfigTableId="6",WhereSql="yz_project_id=@current_yz_project_id AND name <> '停用树节点'"});
        foreach(var (page,field) in new[]{("601","63"),("601","65"),("201","216")})
            catalog.ConfigPageColumns.Add(new(){Id=page+field,ConfigPageId=page,ConfigColumnId=field});
        if(cross)
        {
            var control=catalog.ConfigColumns.Single(column=>column.Id=="216");
            control.ColumnName="area_id";control.LinkConfigTableId="7";
            control.ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]
            {
                new{table=prefix+"_tree",columnPrefix="country"},
                new{table=prefix+"_area",columnPrefix="area",parentField="dr_names_tree_id"}
            }});
            catalog.ConfigTables.Add(new(){Id="7",TableName=prefix+"_area",IsRongyu=1});
            catalog.ConfigColumns.AddRange([
                new(){Id="71",ConfigTableId="7",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="72",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="73",ConfigTableId="7",ColumnName="name",LogicalType="string"},
                new(){Id="74",ConfigTableId="7",ColumnName="dr_names_tree_id",LogicalType="int64",LinkConfigTableId="6"}
            ]);
            catalog.ConfigPages.Add(new(){Id="701",ConfigTableId="7",WhereSql="yz_project_id=@current_yz_project_id"});
            catalog.ConfigPageColumns.Add(new(){Id="70173",ConfigPageId="701",ConfigColumnId="73"});
            catalog.ConfigPageColumns.Add(new(){Id="70174",ConfigPageId="701",ConfigColumnId="74"});
        }
        target.WhereSql+=" AND "+logicalField+" <> '排除叶名' AND store_id=@current_store_id";
        if(globalWithProject is {} keepProject)
        {
            foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "2" or "4" or "6"))
                table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            target.WhereSql=target.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
            catalog.ConfigPages.Single(page=>page.Id=="401").WhereSql="";
            catalog.ConfigPages.Single(page=>page.Id=="601").WhereSql="name <> '停用树节点'";
            if(!keepProject)
            {
                catalog.ConfigColumns.RemoveAll(column=>column.Id=="22");
                catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="22");
            }
        }
        if(redundant)
        {
            catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;
            if(!selection&&globalWithProject is null&&!parent)
            {
                // 先输入较晚的后台页，物理副本和原候选仍须按已发布页顺序选择201。
                foreign.Extra.Remove("linkConfigPageId");
                catalog.ConfigPages.Insert(0,new(){Id="202",ConfigTableId="2",YzAdminTypeId="5"});
            }
            // 夹具只保留本次要检查的名称副本，原来源父页仍照常校验。
            catalog.ConfigColumns.RemoveAll(column=>column.Id is "16" or "17");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "16" or "17");
            catalog.ConfigColumns.Add(new(){Id="118",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
            {
                page.OrderBy="id desc";
                page.WhereSql+=" AND store_id=@current_store_id";
                if(selection)
                    foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId=="15"))
                        field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            }
        }
        catalog.YzAdminTypes.Clear();
        RedundantMultiNameCacheTests.Published(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-relation-page-scope");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ScopedSingleRelation");
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            var scope=derived?"DerivedOptionsPlan101_15":"OptionsMembership101_15";
            if(globalWithProject is not null)
            {
                Assert.Contains("CompileCrossPageCandidate(\"2\",\"201\"",service);
                Assert.Contains($"ApplyCandidate(query,session,{scope}.BindAccount(actor),\"options_page_101_15\",actor.ProjectId,locking)",service);
                var start=service.IndexOf(">> OptionsScope101_15Async(",StringComparison.Ordinal);
                Assert.True(start>=0);
                var end=service.IndexOf("\n    }",start,StringComparison.Ordinal);
                Assert.Contains("sourceParentKey && entity.YzProjectId == ",service[start..end]);
                Assert.DoesNotContain("query = query.Where(entity => entity.YzProjectId",service[start..end]);
            }
            else
            {
                Assert.Contains("CompileRelationMembership(",service);
                Assert.Contains($"ApplyCondition(query,session,{scope}.BindAccount(actor),OptionsPageCondition101_15.BindAccount(actor)",service);
            }
            Assert.Contains("OptionsSelected101_15Async",service);
            Assert.Contains("OptionsPage101_15Async",service);
            Assert.Contains("OptionsScope104_15Async",service);
            Assert.DoesNotContain("public Task<IReadOnlyList<GeneratedOption>> OptionsSelected104_15Async",service);
            Assert.DoesNotContain("public async Task<IReadOnlyList<GeneratedOption>> OptionsSelected104_15Async",service);
            Assert.Contains("Import101Async",service);
            Assert.Contains("Export101Async",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var name in new[]{"DrNamesOrderService","DrNamesPartyService"})
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    assembly.GetType("Example.ScopedSingleRelation.Generated.Services."+name,true)!.TypeHandle);

            if(redundant)
            {
                var model=GenerationModel.Create(catalog,"Example.ScopedSingleRelation");
                var cache=MixedRelationPropagationCompilation.ForModel(model);
                Assert.Contains("dr_names_tree",cache.MixedTargets);
                Assert.Contains("dr_names_party",cache.MixedTargets);
                if(cross)Assert.Contains("dr_names_area",cache.MixedTargets);
                Assert.Contains("customer_"+label,cache.MixedSourceColumns["dr_names_order"]);
                Assert.Contains("ProjectRelationMutationPropagator(session).AfterMutationAsync",service);
                var published=assembly.GetType("Example.ScopedSingleRelation.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null);
                Assert.Contains("dr_names_tree",Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(published).MixedTargets);
            }
            if(globalWithProject is not null&&!cross)
            {
                catalog.ConfigTables.Single(table=>table.Id=="6").Extra.Remove("scopeKind");
                Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.CompileCrossPageCandidate(
                    catalog,"2","201",["id","name"],[]));
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>业务编号与外键不同，保持联接整数投影、原候选文本及 Excel 精确匹配。</summary>
    [Theory]
    [InlineData("int32", "int", "-2000000001", false)]
    [InlineData("int64", "long", "9007199254740993", true)]
    [InlineData("bool", "bool", "true", false)]
    [InlineData("decimal", "decimal", "1234567890.123456", true)]
    [InlineData("double", "double", "0.125", false)]
    [InlineData("date", "DateTime", "2026-11-18", false)]
    [InlineData("datetime", "DateTime", "2026-11-18 13:14:15.123456", true)]
    [InlineData("time", "TimeSpan", "27:12:00.123456", true)]
    public async Task Derived_list_scalar_name_compiles_lookup_and_preserves_precision(string logicalType, string clrType, string expected, bool contextual)
    {
        var catalog = Catalog("dr_names", contextual);
        UseIntegerName(catalog, logicalType);
        var control = catalog.ConfigColumns.Single(column => column.Id == "15");
        var options = JsonSerializer.Deserialize<Dictionary<string, string>>(control.ControlDetail)!;
        options["orderBy"] = "status desc, company_name desc";
        control.ControlDetail = JsonSerializer.Serialize(options);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-derived-integer-list");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.IntegerList");
            var service = await File.ReadAllTextAsync(Path.Combine(directory, "generated/Services/DrNamesOrderService.g.cs"));
            var integer=logicalType is "int32" or "int64";
            Assert.Contains(integer?"FixedReadComparison.IntegerLabelContains":"FixedReadComparison.TypedLabelContains", service);
            Assert.Contains("FixedReadComparison.Equal, key", service);
            Assert.Contains(integer?"!" + clrType + ".TryParse(label":$"({clrType})MultiValueLabel.Parse(\"{logicalType}\"", service);
            Assert.Contains("""[new("status", true), new("company_name", true), new("id", false)]""", service);
            var exportCell = Assert.Single(service.Split('\n'), line => line.Contains("entity.CustomerId is null ? null : namesCustomerId.GetValueOrDefault(", StringComparison.Ordinal));
            Assert.DoesNotContain("??", exportCell);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type = assembly.GetType("Example.IntegerList.Generated.Services.DrNamesOrderService")!;
            var readOption = type.GetMethod("ReadDerivedOption101_15", BindingFlags.Static | BindingFlags.NonPublic)!;
            object value = logicalType switch
            {
                "int32" => int.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                "int64" => long.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                _ => Metarill.Core.Contracts.MultiValueLabel.Parse(logicalType,expected)
            };
            var row = new Dictionary<string, object?> { ["id"] = 9007199254740995L, ["company_name"] = value };
            var option = readOption.Invoke(null, [row])!;
            Assert.Equal(9007199254740995L, (long)option.GetType().GetProperty("Id")!.GetValue(option)!);
            Assert.Equal(expected, (string)option.GetType().GetProperty("Name")!.GetValue(option)!);
            Assert.Equal(expected, (string)option.GetType().GetProperty("Text")!.GetValue(option)!);
            row["company_name"] = null;
            Assert.Null(option.GetType().GetProperty("Text")!.GetValue(readOption.Invoke(null, [row])));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog, "Example.IntegerList")));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>List 与 SelectPage 共用同一派生业务编号模型，原公司名称仍是字符串。</summary>
    internal static void UseIntegerName(MetadataCatalog catalog, string logicalType)
    {
        // 客户选公司时仍使用原字符串名称，订单选客户时使用独立的公司编号联接列。
        catalog.ConfigColumns.Add(new()
        {
            Id = "45", ConfigTableId = "4", YzProjectId = catalog.YzProjectId, ColumnName = "code", LogicalType = logicalType
        });
        catalog.ConfigPageColumns.Add(new()
        {
            Id = "40145", ConfigPageId = "401", ConfigColumnId = "45", YzProjectId = catalog.YzProjectId
        });
        foreach (var column in catalog.ConfigColumns.Where(column => column.Id is "28" or "113"))
            column.LogicalType = logicalType;
        catalog.ConfigColumns.Single(column => column.Id == "28").Extra["columnFullName"] = JsonSerializer.SerializeToElement("company.code");
    }

    /// <summary>实际完整包包含原List组件和只读名称，重新更新保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_package_keeps_original_list_and_protects_handwritten_service(bool contextual)
    {
        var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var catalog=Catalog("dr_names",contextual);
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog,output,"Example.DerivedNameApplication",root).FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            foreach(var component in new[]{"common/yzp_runtime/remoteRelationOptions.js","components/yzp-relation-select/yzp-relation-select.vue"})
            {
                // 打包器的既有文本合同统一LF；只规范来源行尾，包内其余UTF8字节必须完全相同。
                var original=File.ReadAllText(Path.Combine(root,"frontend/metarill_uni_admin",component)).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
                Assert.Equal(Encoding.UTF8.GetBytes(original),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
            }
            using var profile=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var subsets=profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(item=>item.GetProperty("controlKind").GetString()=="List").ToArray();Assert.Equal(2,subsets.Length);Assert.All(subsets,item=>Assert.True(item.GetProperty("derivedNameSupported").GetBoolean()));
            var hand=Path.Combine(output,"backend/generated/Services/DrNamesOrderService.cs");await File.AppendAllTextAsync(hand,"\n// 派生名称不覆盖手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.DerivedNameApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>隐藏有效关系只生成私有写校验；目标条件不能因控件不可见而消失。</summary>
    [Fact]
    public async Task Hidden_derived_relation_keeps_private_validation_without_public_options()
    {
        var catalog=Catalog("dr_names",true);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="15");var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-hidden");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.HiddenDerivedName");var service=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));Assert.Contains("OptionsCondition101_15",service);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            // 用真实编译类型验证可见性与返回值，不依赖实体名称是否写了完整命名空间。
            var type=assembly.GetType("Example.HiddenDerivedName.Generated.Services.DrNamesOrderService")!;
            var entity=assembly.GetType("Example.HiddenDerivedName.Generated.Entities.DrNamesParty")!;
            var method=type.GetMethod("OptionsScope101_15Async",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.NotNull(method);Assert.True(method.IsPrivate);Assert.Equal(typeof(Task<>).MakeGenericType(typeof(FreeSql.ISelect<>).MakeGenericType(entity)),method.ReturnType);
            Assert.Null(type.GetMethod("Options101_15Async",BindingFlags.Instance|BindingFlags.Public));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明目标、派生范围及隐藏坏声明在写入stage之前拒绝，不能回退宽范围或IsIgnore SQL。</summary>
    [Theory][InlineData("global")][InlineData("derivedScope")][InlineData("virtual")][InlineData("brokenPath")][InlineData("privateName")][InlineData("unpublishedRedundantOwner")][InlineData("hiddenScope")]
    public void Invalid_derived_name_configuration_is_rejected_before_staging(string kind)
    {
        var c=Catalog("dr_names",true);
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind is "derivedScope" or "hiddenScope")c.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND company_name='X'";
        if(kind=="hiddenScope")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="15");
        if(kind=="virtual")c.ConfigColumns.Single(column=>column.Id=="28").IsVirtualColumn=1;
        if(kind=="brokenPath")c.ConfigColumns.Single(column=>column.Id=="28").Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="privateName")c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="28");
        if(kind=="unpublishedRedundantOwner"){var owner=c.ConfigTables.Single(table=>table.Id=="1");owner.IsRongyu=1;owner.YzProjectId="";}
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.RejectedDerivedName",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }

    /// <summary>声明同项目四表：来源订单→非冗余客户→公司名称；原字段和菜单方式均由元数据生成。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool parentAndForm)
    {
        var c=NonRedundantGeneratedFixture.Catalog(prefix);c.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        c.ConfigTables.Add(new(){Id="4",TableName=prefix+"_company",IsRongyu=1});
        c.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="43",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"},new(){Id="44",ConfigTableId="4",ColumnName="name",LogicalType="string"}]);
        c.ConfigPages.Add(new(){Id="401",ConfigTableId="4",YzAdminTypeId="5",WhereSql="yz_project_id=@current_yz_project_id"});
        c.ConfigPageColumns.Add(new(){Id="40144",ConfigPageId="401",ConfigColumnId="44"});
        c.ConfigColumns.Add(new(){Id="27",ConfigTableId="2",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail=JsonSerializer.Serialize(new{tableId="4",nameField="name"})});
        c.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="company_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}});
        c.ConfigColumns.Add(new(){Id="210",ConfigTableId="2",ColumnName="status",LogicalType="int32"});
        foreach(var id in new[]{"27","28","210"})c.ConfigPageColumns.Add(new(){Id="201"+id,ConfigPageId="201",ConfigColumnId=id});
        var relation=c.ConfigColumns.Single(column=>column.Id=="15");relation.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");relation.ControlDetail=JsonSerializer.Serialize(new{tableId="2",table=prefix+"_party",nameField="company_name",whereSql=parentAndForm?"segment=@formParameters.segment":""});
        var target=c.ConfigPages.Single(page=>page.Id=="201");target.WhereSql="yz_project_id=@current_yz_project_id AND status=1";
        if(parentAndForm)
        {
            c.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName="group_id",LogicalType="int64",LinkConfigTableId="3"});c.ConfigPageColumns.Add(new(){Id="20129",ConfigPageId="201",ConfigColumnId="29"});target.WhereSql+=" AND group_id=@parent_id";
            foreach(var (id,table) in new[]{("112","1"),("211","2")}){c.ConfigColumns.Add(new(){Id=id,ConfigTableId=table,ColumnName="segment",Name="分组条件",LogicalType="int32",DefaultValue="1"});c.ConfigPageColumns.Add(new(){Id=table+id,ConfigPageId=table=="1"?"101":"201",ConfigColumnId=id});}
        }
        relation.Name="客户";
        c.ConfigColumns.Add(new(){Id="113",ConfigTableId="1",ColumnName="customer_company_name",Name="客户公司",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("customer.company_name")}});
        c.ConfigPageColumns.Add(new(){Id="101113",ConfigPageId="101",ConfigColumnId="113",IsEdit=0,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1),["isSort"]=JsonSerializer.SerializeToElement(1)}});
        NonRedundantGeneratedFixture.EnableImports(c);c.ConfigPages.Single(page=>page.Id=="101").IsExport=1;
        // 隐藏页故意继承一个不可选默认FK，验证最终服务不能因控件不可见而略过候选条件。
        relation.DefaultValue="9007199254748001";
        var hidden=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(c.ConfigPages.Single(page=>page.Id=="101")))!;hidden.Id="104";hidden.IsImport=0;hidden.IsExport=0;hidden.IsCopy=0;hidden.Extra.Remove("importMethod");c.ConfigPages.Add(hidden);
        foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId!="15").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="104"+field.Id;copy.ConfigPageId="104";c.ConfigPageColumns.Add(copy);}
        RedundantMultiNameCacheTests.Published(c);return c;
    }
}
