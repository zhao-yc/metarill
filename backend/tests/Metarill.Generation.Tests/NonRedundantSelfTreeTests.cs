using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>非冗余自树沿原生成器和控件，只持久化叶子ID并批量派生名称／完整路径。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantSelfTreeTests
{
    /// <summary>两种ID及父键交叉编译，显式／隐式名称都不写数据库、严格DTO或手写扩展。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","int64",false)]
    [InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)]
    [InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public async Task Generates_scoped_reads_filters_and_only_physical_selected_id(string sourceType,string parentType,bool automatic)
    {
        var catalog=Catalog("nr_tree",sourceType,parentType,automatic);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.NonRedTree");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);
        Assert.DoesNotContain(MetadataStoragePolicy.ForTable(catalog,table.Source).PhysicalColumns,column=>column.ColumnName=="tree_name");Assert.DoesNotContain(page.Writable,column=>column.Source.ColumnName=="tree_name");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-self");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entity=assembly.GetTypes().Single(type=>type.Name=="NrTreeSource");var attribute=entity.GetProperty("TreeName")!.GetCustomAttributes(typeof(FreeSql.DataAnnotations.ColumnAttribute),false).Cast<FreeSql.DataAnnotations.ColumnAttribute>().Single();Assert.True(attribute.IsIgnore);
            var create=assembly.GetTypes().Single(type=>type.Name=="NrTreeSourcePage201CreateRequest");Assert.Null(create.GetProperty("TreeName"));var vo=assembly.GetTypes().Single(type=>type.Name=="NrTreeSourcePage201Vo");Assert.NotNull(vo.GetProperty("TreeName"));Assert.NotNull(vo.GetProperty("TreeIdDisplayText"));
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/NrTreeSourceService.g.cs"));Assert.Contains("ReadCascadeFilters201Async",service);Assert.Contains("ProjectRead201Async",service);Assert.Contains("PathsAsync",service);Assert.Contains("session.InReadSnapshotAsync",service);Assert.Equal(before,JsonSerializer.Serialize(catalog));
            // 固定非冗余独立目标子集解除控件门禁；完整包仍要求显式项目／后台发布目录。
            Assert.DoesNotContain(GenerationCapabilityAudit.Collect(model),gap=>gap.Column=="tree_id");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>全局树按固定目标条件发布，来源继续按项目；非物理名称和带项目变量的目标仍拒绝。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false,true,true)]
    [InlineData(true,true,true)]
    [InlineData(false,true,true,"string")]
    [InlineData(true,true,true,"int64")]
    [InlineData(true,true,true,"string",true)]
    [InlineData(true,true,true,"int64",true)]
    public async Task Explicit_global_self_tree_publishes_with_original_fixed_scope(bool keepsProjectColumn,bool redundantSource,bool globalSource=false,string? sourceProjectType=null,bool projectTarget=false)
    {
        var catalog = RedundantMultiNameCacheTests.Published(Catalog("nr_tree", automatic: true));
        var target = catalog.ConfigTables.Single(table => table.Id == "3");
        if (!projectTarget) target.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        var targetPage = catalog.ConfigPages.Single(page => page.ConfigTableId == target.Id);
        if (!keepsProjectColumn)
        {
            var project = catalog.ConfigColumns.Single(column => column.ConfigTableId == target.Id && column.ColumnName == "yz_project_id");
            catalog.ConfigColumns.Remove(project);
            catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == project.Id);
        }
        targetPage.WhereSql = projectTarget && sourceProjectType=="string"
            ? "yz_project_id=@current_yz_project_id AND status=1" : "status=1";
        if (!catalog.ConfigColumns.Any(column => column.ConfigTableId == target.Id && column.ColumnName == "status"))
            catalog.ConfigColumns.Add(new() { Id = "global_tree_status", ConfigTableId = target.Id, ColumnName = "status", LogicalType = "int32" });
        if(redundantSource)catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        if(globalSource)
        {
            catalog.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            if(sourceProjectType is null)
                catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id");
            else
            {
                catalog.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id").LogicalType=sourceProjectType;
                catalog.ConfigPages.Single(page=>page.Id=="201").IsDelete=1;
            }
            // 未声明显式项目条件时，生成的来源范围也必须使用真实行项目。
            catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="";
        }
        var model = GenerationModel.Create(catalog, "Example.GlobalTree");
        var source = model.Tables.Single(table => table.Source.Id == "2");
        var page = Assert.Single(source.Pages);
        var scope = CascadeTreeReadRenderer.Service(model, source, page);
        Assert.Contains("CascadeScope201_25", scope);
        Assert.Contains("RequirePagePermission(\"301\",\"list\")", scope);
        if(projectTarget)Assert.Contains("row.YzProjectId",scope);
        else Assert.DoesNotContain("row.YzProjectId", scope);
        if(redundantSource)Assert.Equal(targetPage.Id,Assert.Single(SelfTreePropagationCompilation.ForTable(model,source)).SelfTreePageId);
        if(globalSource)
        {
            Assert.True(GlobalCachedSourceCompilation.BindsActor(model));
            Assert.Contains("nr_tree_tree",MixedRelationPropagationCompilation.ForModel(model).MixedTargets);
        }
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-self-tree");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), model.Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(model.Namespace + ".Generated.Services.NrTreeSourceService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            if(globalSource)
            {
                var validate=service.GetMethod("ValidateActor",BindingFlags.NonPublic|BindingFlags.Static)!;
                var wrong=new RuntimeActor(1,"其它项目",true,"1","5");
                var refused=Assert.Throws<TargetInvocationException>(()=>validate.Invoke(null,[wrong]));
                Assert.IsType<UnauthorizedAccessException>(refused.InnerException);
            }
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            using var runtime = new SharedExcelReadTransport(assembly, model.Namespace, "NrTreeSource");
            AddNode(11, "上级");
            AddNode(12, "启用子项", 11);
            AddNode(13, "禁用子项", 11, 0);
            if(projectTarget)
            {
                AddNode(14,"启用子项",11,project:"2");
                AddNode(15,"其他项目子项",11,project:"2");
            }
            var header = ImportRenderer.Header(source.Columns.Single(column => column.Source.Id == "25"));
            var value = await runtime.Parse(new() { [header] = "上级/启用子项" });
            Assert.Equal("12", value["treeId"].GetString());
            await Assert.ThrowsAsync<Metarill.Core.Contracts.PageImportException>(() => runtime.Parse(new() { [header] = "上级/禁用子项" }));
            if(projectTarget)
                await Assert.ThrowsAsync<Metarill.Core.Contracts.PageImportException>(()=>runtime.Parse(new(){[header]="上级/其他项目子项"}));

            if(sourceProjectType is not null)
            {
                Assert.Equal("yz_project_id",Assert.Single(FixedPageQueryCompilation.For(source,page).IdentityScopes).Column.Source.ColumnName);
                var code=File.ReadAllText(Path.Combine(directory,"generated/Services/NrTreeSourceService.g.cs"));
                Assert.Equal(4,code.Split("不能修改来源记录项目归属。",StringSplitOptions.None).Length-1);
                Assert.Contains("entity.YzProjectId = long.Parse(actor.ProjectId",code);
                runtime.Add("NrTreeSource",new Dictionary<string,object?>{["Id"]=100L,["Name"]="本项目",["YzProjectId"]=catalog.YzProjectId,["TreeId"]="12"});
                runtime.Add("NrTreeSource",new Dictionary<string,object?>{["Id"]=101L,["Name"]="其他项目",["YzProjectId"]="2",["TreeId"]="12"});
                var row=Assert.Single((await runtime.Export()).Rows);
                Assert.Contains("本项目",row);
                Assert.Contains("上级/启用子项",row);
            }

            // 全局候选允许不同归属的节点；项目内候选使用同一启用条件并要求真实行项目。
            void AddNode(long id, string name, long? parent = null, int status = 1, string? project=null)
            {
                var row = new Dictionary<string, object?>
                    { ["Id"] = id, ["Name"] = name, ["ParentNrTreeTreeId"] = parent, ["Status"] = status };
                if (keepsProjectColumn) row["YzProjectId"] = project??(projectTarget?catalog.YzProjectId:"2");
                runtime.Add("NrTreeTree", row);
            }

            targetPage.WhereSql = keepsProjectColumn&&!projectTarget ? "yz_project_id=@current_yz_project_id" : "status=@parent_id";
            Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>候选、Excel 反查和导出的非字符串名称使用同一精确文本合同。</summary>
    [Theory]
    [InlineData("bool",true)]
    [InlineData("decimal",false)]
    [InlineData("double",true)]
    [InlineData("date",false)]
    [InlineData("datetime",true)]
    [InlineData("time",false)]
    [InlineData("bool",true,true)]
    [InlineData("double",true,true)]
    [InlineData("time",true,true)]
    public async Task Scalar_self_tree_labels_roundtrip_through_generated_options_and_excel(string kind,bool globalTarget,bool cachedLabel=false)
    {
        var catalog=Catalog("typed_tree",automatic:true);
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        if(globalTarget)
        {
            var target=catalog.ConfigTables.Single(table=>table.Id=="3");
            target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="status=1";
        }
        var label=catalog.ConfigColumns.Single(column=>column.Id=="32");
        label.LogicalType=kind;
        if(cachedLabel)ConfigurePublishedLabelCache(catalog,label);
        RedundantMultiNameCacheTests.Published(catalog);
        const string ns="Example.TypedSelfTree";
        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        (object value,string expected)=kind switch
        {
            "bool" when cachedLabel=>((object)false,"false"),
            "bool"=>((object)true,"true"),
            "decimal"=>((object)1234567890.123456m,"1234567890.123456"),
            "double"=>((object)0.125d,"0.125"),
            "date"=>((object)new DateTime(2026,11,18),"2026-11-18"),
            "datetime"=>((object)new DateTime(2026,11,18,13,14,15).AddTicks(1234560),"2026-11-18 13:14:15.123456"),
            "time"=>((object)new TimeSpan(1,3,12,0).Add(TimeSpan.FromTicks(1234560)),"27:12:00.123456"),
            _=>throw new InvalidOperationException("未知测试名称类型。")
        };
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-typed-self-tree");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var actor=new RuntimeActor(9,"自树候选",false,catalog.YzProjectId,"5");
            using var runtime=new SharedExcelReadTransport(assembly,ns,"TypedTreeSource",actor:actor,
                authorization:new CascadeTreeReadTests.TreeListOnly(actor));
            const long key=9007199254740998;
            var fields=new Dictionary<string,object?>{["Id"]=key,[GenerationModel.Pascal(label.ColumnName)]=value,["Status"]=1};
            if(!cachedLabel)fields["YzProjectId"]=catalog.YzProjectId;
            runtime.Add("TypedTreeTree",fields);
            runtime.Add("TypedTreeSource",new Dictionary<string,object?>{["Id"]=1L,["Name"]="来源",["TreeId"]=key,["YzProjectId"]=catalog.YzProjectId});
            var request=new CascadeTreeOptionsRequest{LevelKey="selfTree-0",SelectedPath=JsonSerializer.SerializeToElement(new{parentId=""})};
            var option=Assert.Single(await runtime.CascadeOptions("25",request));
            Assert.Equal(expected,option.Name);
            Assert.Equal(option.Name,option.Text);
            Assert.Equal("9007199254740998",option.Id);
            var path=await runtime.CascadePath("25",new(){LeafId=JsonSerializer.SerializeToElement(option.Id)});
            Assert.Equal(expected,Assert.Single(path.Path).Name);
            var header=ImportRenderer.Header(model.Tables.Single(table=>table.Source.Id=="2").Columns.Single(column=>column.Source.Id=="25"));
            var row=await runtime.Parse(new(){[header]=kind=="bool"?value:expected});
            Assert.Equal(option.Id,row["treeId"].GetString());
            Assert.Contains(expected,Assert.Single((await runtime.Export()).Rows));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }


    /// <summary>树只读叶名来自独立的全局 List 同类型物理副本，保留真实父字段及发布页。</summary>
    private static void ConfigurePublishedLabelCache(MetadataCatalog catalog,ColumnMetadata label)
    {
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="33");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="33");
        label.ColumnName="dict_name";
        label.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("dict.name");
        var control=catalog.ConfigColumns.Single(column=>column.Id=="25");
        control.ControlDetail=control.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"dict_name\"",StringComparison.Ordinal);
        catalog.ConfigTables.Add(new(){Id="6",TableName="typed_tree_dict",IsRongyu=1,
            Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}});
        catalog.ConfigColumns.AddRange([
            new(){Id="38",ConfigTableId="3",ColumnName="dict_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="6",
                ControlDetail="{\"tableId\":\"6\",\"pageId\":\"601\",\"nameField\":\"name\"}"},
            new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="62",ConfigTableId="6",ColumnName="name",LogicalType=label.LogicalType}
        ]);
        catalog.ConfigPages.Add(new(){Id="601",ConfigTableId="6"});
        catalog.ConfigPageColumns.Add(new(){Id="60162",ConfigPageId="601",ConfigColumnId="62",IsShow=1});
    }

    /// <summary>只有物理账号条件时，独立包仍要保留来源页的归属证明。</summary>
    [Fact]
    public void Project_tree_account_filter_keeps_source_scope_in_published_metadata()
    {
        var catalog=Catalog("account_tree",automatic:true);
        var source=catalog.ConfigTables.Single(table=>table.Id=="2");
        source.IsRongyu=1;
        source.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        foreach(var page in catalog.ConfigPages)page.WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id";
        catalog.ConfigColumns.AddRange([
            new(){Id="2010",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"},
            new(){Id="3010",ConfigTableId="3",ColumnName="store_id",LogicalType="int64"}
        ]);
        RedundantMultiNameCacheTests.Published(catalog);
        var model=GenerationModel.Create(catalog,"Example.ProjectAccountTree");
        var binding=Assert.Single(RelationPropagationCompilation.DerivedBindings(model));
        Assert.Equal("301",binding.SelfTreePageId);
        var definitions=RelationPropagationCompilation.Definitions([binding]);
        var published=RelationPropagationCompilation.PublishedMaintenanceMetadata(model,definitions);
        Assert.Contains("@current_store_id",published.ConfigPages.Single(page=>page.Id=="201").WhereSql);
        Assert.Contains("@current_store_id",published.ConfigPages.Single(page=>page.Id=="301").WhereSql);
        Assert.Contains("tree_name",MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns["account_tree_source"]);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
    }

    /// <summary>独立父项和树目标分别限定范围，Excel 和 CRUD 共用同一生成父校验。</summary>
    [Theory]
    [InlineData("string", "OnlyNew")]
    [InlineData("int64", "OverWriteByFirstColumn")]
    [InlineData("string", "OnlyNew", "int64")]
    [InlineData("int64", "OverWriteByFirstColumn", "string")]
    [InlineData("string", "OnlyNew", "int64", true)]
    [InlineData("int64", "OverWriteByFirstColumn", "string", true)]
    public async Task Global_cached_self_tree_honors_independent_published_parent(string foreignType, string importMode, string? projectType=null, bool projectParent=false)
    {
        const string ns = "Example.GlobalTreeParent";
        var catalog = Catalog("parent_tree", automatic: true);
        foreach (var table in catalog.ConfigTables)
            table.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = 1;
        if(projectType is null)catalog.ConfigColumns.RemoveAll(column => column.ConfigTableId == "2" && column.ColumnName == "yz_project_id");
        else catalog.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id").LogicalType=projectType;
        var page = catalog.ConfigPages.Single(item => item.Id == "201");
        page.WhereSql = "family_id=@parent_id";
        page.IsDelete = 1;
        page.Extra["importMethod"] = JsonSerializer.SerializeToElement(importMode);
        catalog.ConfigPages.Single(item => item.Id == "301").WhereSql = "status=1";
        catalog.ConfigTables.Add(new()
        {
            Id = "4", TableName = "parent_tree_family", IsRongyu = 1,
            Extra = new() { ["scopeKind"] = JsonSerializer.SerializeToElement("global") }
        });
        catalog.ConfigColumns.AddRange([
            new() { Id = "41", ConfigTableId = "4", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 },
            new() { Id = "42", ConfigTableId = "4", ColumnName = "name", LogicalType = "string" },
            new() { Id = "43", ConfigTableId = "4", ColumnName = "status", LogicalType = "int32" },
            new() { Id = "44", ConfigTableId = "4", ColumnName = "delete_no", LogicalType = "datetime" },
            new()
            {
                Id = "271", ConfigTableId = "2", ColumnName = "family_id", LogicalType = foreignType,
                LinkConfigTableId = "4", ControlKind = "List",
                ControlDetail = JsonSerializer.Serialize(new { tableId = "4", pageId = "401", nameField = "name" })
            },
            new()
            {
                Id = "272", ConfigTableId = "2", ColumnName = "family_name", LogicalType = "string",
                Extra = new() { ["isJoinColumn"] = JsonSerializer.SerializeToElement(1), ["columnFullName"] = JsonSerializer.SerializeToElement("family.name") }
            }
        ]);
        if(projectParent)
        {
            catalog.ConfigTables.Single(table=>table.Id=="4").Extra.Remove("scopeKind");
            catalog.ConfigColumns.Add(new(){Id="45",ConfigTableId="4",ColumnName="yz_project_id",LogicalType=foreignType});
        }
        catalog.ConfigPages.Add(new() { Id = "401", ConfigTableId = "4", WhereSql = projectParent?"yz_project_id=@current_yz_project_id AND status=1":"status=1" });
        catalog.ConfigPageColumns.Add(new() { Id = "4001", ConfigPageId = "401", ConfigColumnId = "42" });
        RedundantMultiNameCacheTests.Published(catalog);

        var model = GenerationModel.Create(catalog, ns);
        var source = model.Tables.Single(table => table.Source.Id == "2");
        var compiledPage = Assert.Single(source.Pages);
        var parent = Assert.IsType<GlobalCachedSourceBinding>(GlobalCachedParentCompilation.ForSelfTree(model, source, compiledPage));
        Assert.Equal("401", parent.Page.Source.Id);
        Assert.Equal("271", parent.Foreign.Source.Id);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        if(projectType is not null)
        {
            var published=RelationPropagationCompilation.PublishedCascadePages(model,RelationPropagationCompilation.Definitions(
                RelationPropagationCompilation.DerivedBindings(model)));
            Assert.Contains("201",published);
            Assert.Contains("401",published);
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("family_name",maintenance.MixedSourceColumns["parent_tree_source"]);
            Assert.Contains("tree_name",maintenance.MixedSourceColumns["parent_tree_source"]);
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-self-tree-parent");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var code = File.ReadAllText(Path.Combine(output, "Services/ParentTreeSourceService.g.cs"));
            Assert.Contains("GlobalTreeParentCondition201_271", code);
            Assert.Contains("await ValidateParents201Async(request.ParentId, actor, cancellationToken", code);
            Assert.Contains("await ValidateParents201Async(parentId,actor,cancellationToken);", code);
            var parentValue = "long.Parse(parentId!, CultureInfo.InvariantCulture)" + (foreignType == "string" ? ".ToString(CultureInfo.InvariantCulture)" : "");
            Assert.Equal(2, code.Split($"if(entity.FamilyId!={parentValue})", StringSplitOptions.None).Length - 1);

            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var runtime = new SharedExcelReadTransport(assembly, ns, "ParentTreeSource");
            const string accepted = "9007199254741991";
            const string disabled = "9007199254741992";
            const string removed = "9007199254741993";
            const string secondParent = "9007199254741995";
            const string foreignProjectParent = "9007199254741996";
            AddParent(accepted,1);
            AddParent(disabled,0);
            AddParent(removed,1,new DateTime(2026,10,1));
            AddParent(secondParent,1);
            if(projectParent)AddParent(foreignProjectParent,1,project:"2");

            void AddParent(string id,int status,DateTime? deleted=null,string? project=null)
            {
                var values=new Dictionary<string,object?>{["Id"]=id,["Status"]=status,["DeleteNo"]=deleted};
                if(projectParent)values["YzProjectId"]=project??catalog.YzProjectId;
                runtime.Add("ParentTreeFamily",values);
            }
            AddSource(1L,"允许行",accepted,catalog.YzProjectId);
            AddSource(2L,"其他父行",secondParent,catalog.YzProjectId);
            if(projectType is not null)AddSource(3L,"其他项目行",accepted,"2");
            if(projectParent)AddSource(4L,"引用了其他项目的父记录",foreignProjectParent,catalog.YzProjectId);

            void AddSource(long id,string name,string family,string project)
            {
                var values=new Dictionary<string,object?>{["Id"]=id,["Name"]=name,["FamilyId"]=family};
                if(projectType is not null)values["YzProjectId"]=project;
                runtime.Add("ParentTreeSource",values);
            }

            var export = await runtime.Export(new PageRequest { ParentId = accepted });
            Assert.Contains("允许行", Assert.Single(export.Rows));
            Assert.Equal(0, runtime.ExportLockCount);
            Assert.True(runtime.Snapshots > 0);
            foreach (var excluded in new[] { disabled, removed, "9007199254741994", foreignProjectParent })
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Export(new PageRequest { ParentId = excluded }));
                await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Import([], excluded));
            }
            await Assert.ThrowsAsync<ArgumentException>(() => runtime.Export(new PageRequest { ParentId = "0" + accepted }));
            Assert.Equal("导入成功0条数据", await runtime.Import([], accepted));
            Assert.Contains("lock:ParentTreeFamily", runtime.Reads);

            catalog.ConfigPages.Single(item => item.Id == "401").WhereSql = "status=@parent_id";
            var rejected = Assert.Throws<NotSupportedException>(() => new FrontendPackager().Validate(catalog));
            Assert.Contains("条件", rejected.Message);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>同一全局表的直接父项和独立书签共用祖先结构，普通写入仍按存在的行项目列限制。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Global_same_table_tree_and_bookmark_keep_generated_topology(bool rowProject)
    {
        const string ns="Example.GlobalOwnTree";
        var catalog=RedundantMultiNameCacheTests.Published(CascadeTreeSameRowTests.Catalog("published_tree"));
        var table=Assert.Single(catalog.ConfigTables);
        table.IsRongyu=0;
        table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(!rowProject)catalog.ConfigColumns.RemoveAll(column=>column.ColumnName=="yz_project_id");
        var parent=catalog.ConfigColumns.Single(column=>column.ColumnName=="parent_id");
        parent.ControlKind="CascadeTree";
        parent.ControlDetail=catalog.ConfigColumns.Single(column=>column.ColumnName=="bookmark_id").ControlDetail;
        foreach(var page in catalog.ConfigPages)page.WhereSql=page.Id=="202"?"status=1":"";
        var project=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(project));
        new FrontendPackager().Validate(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-own-tree");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var code=File.ReadAllText(Path.Combine(generated,"Services/PublishedTreeNodeService.g.cs"));
            Assert.Contains("CascadeTopology202_5",code);
            Assert.Contains("CascadeReader202_6",code);
            Assert.Equal(rowProject,code.Contains("query.Where(entity=>entity.YzProjectId==",StringComparison.Ordinal));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.PublishedTreeNodeService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("CascadeOptions202_5Async"));
            Assert.NotNull(service.GetMethod("CascadePath202_6Async"));
            Assert.NotNull(service.GetMethod("Import203Async"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>目标页逻辑条件进入原候选、图、写入与 Excel，并随发布目录冻结。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(false,false,true)]
    [InlineData(false,true,true)]
    [InlineData(true,false,true)]
    [InlineData(true,false,true,true)]
    [InlineData(false,false,true,true)]
    [InlineData(false,true,true,true)]
    [InlineData(false,true,true,true,"decimal")]
    public async Task Fixed_cross_condition_checks_every_self_tree_node(bool global,bool derivedName,bool cache=false,bool globalSource=false,string nameKind="string")
    {
        const string ns="Example.FixedSelfTreeNode";
        var catalog=NonRedundantCrossTableGenerationTests.Catalog("scoped_tree","string","int64");
        var target=catalog.ConfigTables.Single(table=>table.Id=="2");
        var targetPage=catalog.ConfigPages.Single(page=>page.ConfigTableId==target.Id);
        if(global)target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        targetPage.WhereSql=(global?"":"yz_project_id=@current_yz_project_id AND ")+"country_name <> '限制节点' AND store_id=@current_store_id";
        catalog.ConfigColumns.AddRange([
            new(){Id="27",ConfigTableId="2",ColumnName="node_parent_id",LogicalType="int64"},
            new(){Id="28",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"},
            new(){Id="29",ConfigTableId="2",ColumnName="country_name",LogicalType="string"},
            new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="92",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="93",ConfigTableId="9",ColumnName="name",LogicalType="string"},
            new(){Id="94",ConfigTableId="9",ColumnName="node_id",LogicalType="string",ControlKind="CascadeTree",LinkConfigTableId="2",
                ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table=target.TableName,parentField="node_parent_id",nameField=derivedName?"company_title":"name"}})},
            new(){Id="95",ConfigTableId="9",ColumnName="node_name",LogicalType="string"}
        ]);
        catalog.ConfigTables.Add(new(){Id="9",TableName="scoped_tree_consumer",IsRongyu=cache?1:0});
        catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="9",IsExport=1,IsImport=1,
            WhereSql=cache?(globalSource&&global?"":"yz_project_id=@current_yz_project_id AND ")+"store_id=@current_store_id":""});
        if(globalSource)
        {
            catalog.ConfigTables.Single(item=>item.Id=="9").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            if(global)catalog.ConfigColumns.RemoveAll(column=>column.Id=="92");
        }
        if(cache)catalog.ConfigColumns.Add(new(){Id="96",ConfigTableId="9",ColumnName="store_id",LogicalType="int64"});
        catalog.ConfigPageColumns.Add(new(){Id="20127",ConfigPageId="201",ConfigColumnId="27"});
        foreach(var id in new[]{"93","94","95"})
            catalog.ConfigPageColumns.Add(new(){Id="901"+id,ConfigPageId="901",ConfigColumnId=id,IsShow=1,IsEdit=id=="95"?0:1,
                Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(id=="95"?1:0)}});
        if(derivedName)
        {
            catalog.ConfigColumns.AddRange([
                new(){Id="210",ConfigTableId="2",ColumnName="company_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="3",
                    ControlDetail=JsonSerializer.Serialize(new{tableId="3",pageId="301",nameField=nameKind=="string"?"name":"typed_title"})},
                new(){Id="211",ConfigTableId="2",ColumnName="company_title",LogicalType=nameKind,
                    Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement(nameKind=="string"?"company.name":"company.typed_title")}}
            ]);
            if(nameKind!="string")
            {
                catalog.ConfigColumns.Add(new(){Id="3910",ConfigTableId="3",ColumnName="typed_title",LogicalType=nameKind});
                catalog.ConfigPageColumns.Add(new(){Id="3013910",ConfigPageId="301",ConfigColumnId="3910"});
            }
            catalog.ConfigPageColumns.Add(new(){Id="201211",ConfigPageId="201",ConfigColumnId="211",IsShow=1,IsEdit=0});
        }
        RedundantMultiNameCacheTests.Published(catalog);
        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        if(cache)
        {
            var binding=Assert.Single(RelationPropagationCompilation.DerivedBindings(model),item=>item.Source.Source.Id=="9");
            Assert.Equal("201",binding.SelfTreePageId);
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("scoped_tree_source",maintenance.MixedTargets);
            Assert.Contains("scoped_tree_country",maintenance.MixedTargets);
            Assert.Contains("node_name",maintenance.MixedSourceColumns["scoped_tree_consumer"]);
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-scoped-tree-candidate");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var code=File.ReadAllText(Path.Combine(generated,"Services/ScopedTreeConsumerService.g.cs"));
            Assert.Contains("CompileCrossPageCandidate(\"2\",\"201\"",code);
            Assert.Contains("ApplyCandidate(query,session,DerivedCascadePlan901_94.BindAccount(actor)",code);
            Assert.Contains("CurrentRowsAsync(DerivedCascadePlan901_94.BindAccount(actor)",code);
            Assert.Contains("DerivedCascadeRows901_94Async",code);
            Assert.Contains("AllRowsAsync(DerivedCascadePlan901_94.BindAccount(actor)",code);
            Assert.Contains("DerivedCascadeOrder901_94,null,false,ct",code);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.ScopedTreeConsumerService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("CascadeOptions901_94Async"));
            Assert.NotNull(service.GetMethod("CascadePath901_94Async"));
            Assert.NotNull(service.GetMethod("Import901Async"));
            Assert.NotNull(service.GetMethod("Export901Async"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>真实生成页和原级联组件共同在Vue3挂载，网络受控；不替代浏览器或正式宿主验收。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public Task Original_self_tree_vue_keeps_paths_selection_clear_readonly_and_physical_write_contract(bool automatic)=>ExerciseOriginalVue(automatic,false);
    /// <summary>相同原组件探针用于物理和派生目标，不复制或简化UI。</summary>
    internal static async Task ExerciseOriginalVue(bool automatic,bool derived)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-self-vue");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(derived?DerivedSelfTreeTests.Catalog("nr_tree","string","int64",automatic):Catalog("nr_tree",automatic:automatic),output,"Example.TreeVue");
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};start.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.Root(),"backend/tests/Metarill.Generation.Tests/NonRedundantSelfTreeVueProbe.mjs"));start.ArgumentList.Add(output);
            using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>从包内基础源码独立构建原前端宿主，实际本地更新继续保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public Task Full_self_tree_package_builds_and_updates_without_replacing_original_component_or_handwritten_code(bool automatic)=>ExerciseFull(automatic,false);
    /// <summary>派生目标与原目标共用真实完整包构建／更新验收。</summary>
    internal static async Task ExerciseFull(bool automatic,bool derived)
    {
        var catalog=derived?DerivedSelfTreeTests.Catalog("nr_tree","string","int64",automatic):Catalog("nr_tree",automatic:automatic);
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId,Name="自树后台"});foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="5";
        catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="树关系",Kind="GENERATED_PAGE",ConfigPageId="201"});
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-self-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.TreeApplication")));var package=new ApplicationPackager().Create(catalog,output,"Example.TreeApplication",NonRedundantApplicationFixture.Root());Assert.True(package.FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var component="frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue";Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),await File.ReadAllBytesAsync(Path.Combine(output,component)));Assert.DoesNotContain("$set",await File.ReadAllTextAsync(Path.Combine(output,component)));
            using var profile=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var subset=Assert.Single(profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray());Assert.Equal("CascadeTree",subset.GetProperty("controlKind").GetString());Assert.Equal("selfTree",subset.GetProperty("sourceMode").GetString());Assert.False(subset.GetProperty("sameTableSupported").GetBoolean());Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());Assert.Equal(derived,subset.GetProperty("derivedNameSupported").GetBoolean());
            var handwritten=Path.Combine(output,"backend/generated/Services/NrTreeSourceService.cs");await File.AppendAllTextAsync(handwritten,"\n// 自树包手写保护标记。\n");var bytes=await File.ReadAllBytesAsync(handwritten);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.TreeApplication",NonRedundantApplicationFixture.Root());var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(handwritten));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包仍拒绝同表额外范围、来源父参数和缺失目标发布归属，独立后端已验不能提前解除所有组合。</summary>
    [Theory][InlineData("sameTableExtraScope")][InlineData("sourceParent")][InlineData("targetRealm")]
    public void Package_subset_keeps_unproven_combinations_and_unpublished_target_gated(string kind)
    {
        var catalog=kind=="sameTableExtraScope"?CascadeTreeSameRowTests.Catalog():Catalog("nr_tree");foreach(var table in catalog.ConfigTables.Where(table=>table.Id=="1"||table.Id=="2"))table.IsRongyu=0;
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId});foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="5";
        if(kind=="sameTableExtraScope"){var control=catalog.ConfigColumns.Single(column=>column.Id=="6");control.ControlDetail=control.ControlDetail.Replace("\"selfTree\":{","\"selfTree\":{\"whereSql\":\"status=1\",",StringComparison.Ordinal);Assert.Contains("whereSql",control.ControlDetail);}
        if(kind=="targetRealm")catalog.ConfigPages.Single(page=>page.Id=="301").YzProjectId="";
        if(kind=="sourceParent")catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND id=@parent_id";
        var model=GenerationModel.Create(catalog,"Example.GatedTree");Assert.NotEmpty(GenerationCapabilityAudit.Collect(model));
    }
    /// <summary>不能把尚未证明的名称用途和目标派生范围落成宽查询或无效物理SQL。</summary>
    [Theory]
    [InlineData("scopeChangingOverlay")][InlineData("nameCondition")][InlineData("virtualName")][InlineData("conflictingNamePath")][InlineData("derivedTargetCondition")][InlineData("nestedName")]
    public void Rejects_unproven_name_dependencies_and_derived_target_scope(string kind)
    {
        var catalog=Catalog("nr_tree");
        if(kind=="scopeChangingOverlay")
        {
            var targetPage=catalog.ConfigPages.Single(page=>page.Id=="301");
            catalog.ConfigColumns.Add(new(){Id="84",ConfigTableId="3",ColumnName="alternate_name",LogicalType="string"});
            catalog.ConfigPageColumns.Add(new(){Id="30184",ConfigPageId=targetPage.Id,ConfigColumnId="84",IsShow=1});
            var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
            var overlay=System.Text.Json.Nodes.JsonNode.Parse(source.ControlDetail)!;
            overlay["selfTree"]!["nameField"]="alternate_name";
            catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25")
                .Extra["controlDetail"]=JsonSerializer.SerializeToElement(overlay.ToJsonString());
            catalog.ConfigPages.Single(page=>page.Id=="201").OrderBy="tree_name asc";
        }
        if(kind=="nameCondition")catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND tree_name='隐藏'";
        if(kind=="virtualName")catalog.ConfigColumns.Single(column=>column.Id=="26").IsVirtualColumn=1;
        if(kind=="conflictingNamePath")catalog.ConfigColumns.Single(column=>column.Id=="26").Extra["columnFullName"]=JsonSerializer.SerializeToElement("other.name");
        if(kind=="derivedTargetCondition")
        {
            catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;
            catalog.ConfigColumns.Single(column=>column.Id=="37").Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("parent.name")};
        }
        if(kind=="nestedName")
        {
            catalog.ConfigTables.Add(new(){Id="4",TableName="nr_consumer",IsRongyu=0});
            catalog.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="43",ConfigTableId="4",ColumnName="ref_id",LogicalType="int64",LinkConfigTableId="2"},new(){Id="44",ConfigTableId="4",ColumnName="ref_label",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("ref.tree_name")}}]);
            Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.Compile(catalog,"4",["ref_label"]));return;
        }
        Assert.Throws<InvalidOperationException>(()=>{var model=GenerationModel.Create(catalog,"Example.RejectedTree");var table=model.Tables.Single(table=>table.Source.Id=="2");NonRedundantReadCompilation.ForPage(model,table,Assert.Single(table.Pages));});
    }
    /// <summary>旧冗余目标的声明联接列仍是真实物理条件，不被新非冗余范围门禁误伤。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public async Task Redundant_target_materialized_scope_remains_a_physical_condition(int sourceStorage)
    {
        var catalog=Catalog("scope_tree");catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=sourceStorage;
        catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="enabled",LogicalType="int32"});catalog.ConfigColumns.Single(column=>column.Id=="37").Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("parent_scope_tree_tree.enabled")};
        var model=GenerationModel.Create(catalog,"Example.PhysicalTreeScope");var table=model.Tables.Single(table=>table.Source.Id=="2");Assert.NotNull(CascadeTreeReadCompilation.Resolve(model,table,Assert.Single(table.Pages),table.Columns.Single(column=>column.Source.Id=="25")).Definition);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-physical-tree-scope");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>普通投影仍显式延迟图名称；查询依赖取得固定叶名表达式，精确匹配仍独立拒绝。</summary>
    [Fact]
    public void Deferred_names_require_explicit_graph_projection_or_fixed_query_proof()
    {
        var catalog=Catalog("nr_tree");
        Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.Compile(catalog,"2",["tree_name"]));
        Assert.Equal("string",Metarill.Data.Queries.NonRedundantReads.Compile(catalog,"2",["tree_name"],deferCascadeNames:true).OutputTypes["tree_name"]);
        Assert.Equal("id",Assert.Single(NonRedundantReads.Compile(catalog,"2",["id"],["tree_name"],deferCascadeNames:true).OutputTypes).Key);
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileMatch(catalog,"2","tree_name"));
    }
    /// <summary>生成项目真实编译，独立物理树和派生叶名共用原页面查询及账号范围。</summary>
    [Theory]
    [InlineData(false, "list")]
    [InlineData(true, "edit")]
    public async Task Fixed_leaf_name_queries_use_the_original_tree_scope(bool derived, string exportMode)
    {
        const string application = "Example.FixedLeafSearch";
        var catalog = derived ? DerivedSelfTreeTests.Catalog("query_tree", "int64", "string", false)
            : RedundantMultiNameCacheTests.Published(Catalog("query_tree", "string", "int64"));
        if (derived) catalog.ConfigColumns.Single(column => column.Id == "42").LogicalType = "int64";
        if (derived) catalog.ConfigColumns.Single(column => column.Id == "32").LogicalType = "int64";
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
        sourcePage.OrderBy = "tree_name asc";
        sourcePage.Extra["exportMethod"] = JsonSerializer.SerializeToElement(exportMode);
        var nameField = catalog.ConfigPageColumns.SingleOrDefault(field => field.ConfigPageId == sourcePage.Id && field.ConfigColumnId == "26");
        if (nameField is null)
        {
            nameField = new() { Id = "20126", ConfigPageId = "201", ConfigColumnId = "26", YzProjectId = catalog.YzProjectId };
            catalog.ConfigPageColumns.Add(nameField);
        }
        nameField.IsShow = 1;
        nameField.IsEdit = 1;
        nameField.Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        nameField.Extra["isSort"] = JsonSerializer.SerializeToElement(1);
        var target = catalog.ConfigPages.Single(page => page.Id == "301");
        target.WhereSql += " AND store_id=@current_store_id";
        catalog.ConfigColumns.Add(new() { Id = "93", ConfigTableId = "3", YzProjectId = catalog.YzProjectId, ColumnName = "store_id", LogicalType = "int64" });

        var model = GenerationModel.Create(catalog, application);
        var table = model.Tables.Single(table => table.Source.Id == "2");
        var page = Assert.Single(table.Pages);
        Assert.Equal(target.Id, Assert.Single(NonRedundantReadCompilation.SelfTreeNameQueries(model, table, page)).TargetPage.Source.Id);
        Assert.Empty(ReadProjectionCompilation.ForPage(model, table, page).List.Gaps);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-fixed-tree-name");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var generated = Path.Combine(directory, "generated");
        try
        {
            new CSharpGenerator().Generate(catalog, generated, application);
            var code = File.ReadAllText(Path.Combine(generated, "Services/QueryTreeSourceService.g.cs"));
            Assert.Contains("StatisticsAsync(ReadPlan201.BindAccount(actor)", code);
            Assert.Contains("RowsAsync(ExportReadPlan201.BindAccount(actor)", code);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type = assembly.GetType(application + ".Generated.Services.QueryTreeSourceService", throwOnError: true)!;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var actor = new RuntimeActor(8, "测试用户", false, catalog.YzProjectId, sourcePage.YzAdminTypeId) { StoreId = 29 };
            var request = new PageRequest { Filters = new() { ["treeName"] = JsonSerializer.SerializeToElement("甲%乙") } };
            var filters = Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(type.GetMethod("ReadFilters201", flags)!.Invoke(null, [request, actor]));
            Assert.Equal(new FixedReadFilter("tree_name", FixedReadComparison.LikePattern, "甲%乙"),
                Assert.Single(filters, filter => filter.Field == "tree_name"));
            var order = type.GetMethod("ReadOrder201", flags)!;
            Assert.Equal(new[] { new FixedReadOrder("tree_name"), new FixedReadOrder("id", true) },
                Assert.IsAssignableFrom<IReadOnlyList<FixedReadOrder>>(order.Invoke(null, [request])));
            request.OrderBy = new() { Field = "treeName", Order = "descending" };
            Assert.Equal(new FixedReadOrder("tree_name", true),
                Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FixedReadOrder>>(order.Invoke(null, [request]))));

            foreach (var planField in new[] { "ReadPlan201", "ExportReadPlan201" })
            {
                var plan = Assert.IsType<NonRedundantReadPlan>(type.GetField(planField, flags)!.GetValue(null));
                Assert.NotSame(plan, plan.BindAccount(actor));
                Assert.Throws<UnauthorizedAccessException>(() => plan.BindAccount(actor with { StoreId = null }));
                // 读取已生成的实际查询表达式，验证祖先递归、软删除与叶名计算均在分页前的 SQL 中。
                var mysql = typeof(NonRedundantReadPlan).GetProperty("MySql", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plan);
                var expression = Assert.IsType<string>(typeof(MySqlNonRedundantReadPlan)
                    .GetMethod("Expression", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(mysql, ["tree_name"]));
                Assert.Contains("WITH RECURSIVE", expression);
                Assert.Contains("JSON_CONTAINS", expression);
                Assert.Contains(".`store_id`=@", expression);
                Assert.Contains(".`delete_no` IS NULL", expression);
                Assert.Equal(derived, expression.Contains(" AS CHAR)", StringComparison.Ordinal) && expression.Contains("`company_id`", StringComparison.Ordinal));
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>元数据仍是唯一来源，自动叶子名只在逻辑模型展开，不新建配置行。</summary>
    internal static MetadataCatalog Catalog(string prefix,string sourceType="string",string parentType="int64",bool automatic=false)
    {
        var catalog=CascadeTreeWriteTests.Catalog(prefix,sourceType,parentType);catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        if(automatic){catalog.ConfigColumns.RemoveAll(column=>column.Id=="26");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="26");}
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsExport=1;catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);return catalog;
    }
}
