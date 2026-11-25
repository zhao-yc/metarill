using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享级联的原生成解析／导出实际执行；R0与派生只编译，不用模拟SQL冒充实库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelSharedGenerationTests
{
    private const string Prefix="shared_excel",Namespace="Example.SharedExcel";
    /// <summary>物理R1实际执行路径往返；R1派生与全局物理混合目标另核对完整发布、固定名称计划及原生成编译。</summary>
    [Theory]
    [InlineData("shared-r1")][InlineData("shared-r1", " > ")][InlineData("short-r1")][InlineData("global-r1")][InlineData("global-no-project-r1")]
    [InlineData("shared-r0")][InlineData("short-r0")][InlineData("derived-r1")][InlineData("derived-global-r1")][InlineData("derived-short-r0")]
    [InlineData("shared-r1",null,"edit")][InlineData("derived-global-r1",null,"edit")]
    public async Task Shared_paths_compile_and_physical_R1_roundtrips_original_cells(string profile, string? displayJoiner = null, string? exportMode = null)
    {
        var definition=Definition(profile);var catalog=Catalog(profile);
        if(exportMode is not null)
            catalog.ConfigPages.Single(page=>page.Id=="201").Extra["exportMethod"]=JsonSerializer.SerializeToElement(exportMode);
        if (displayJoiner is not null)
        {
            var control = catalog.ConfigColumns.Single(column => column.Id == "25");
            var overlay = JsonNode.Parse(control.ControlDetail)!.AsObject();
            overlay["displayJoiner"] = displayJoiner;
            var field = catalog.ConfigPageColumns.Single(column => column.ConfigPageId == "201" && column.ConfigColumnId == "25");
            field.Extra["controlDetail"] = JsonSerializer.SerializeToElement(overlay.ToJsonString());
        }
        var before=JsonSerializer.Serialize(catalog);
        var model=GenerationModel.Create(catalog,Namespace);var source=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(source.Pages);
        var group=Assert.Single(SharedCrossTableCompilation.ForPage(model,source,page).Groups);Assert.Equal(2,group.Plans.Count);
        var proof=CrossTableExcelApplicationPreparer.RequireCurrent(catalog,Namespace);
        Assert.Empty(proof.Gaps);
        Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(proof.Baseline,Namespace)));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-cross-excel");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType(Namespace+".Generated.Controllers.SharedExcelSourcePage201Controller"));
            var partial=Path.Combine(generated,"Services/SharedExcelSourceService.cs");await File.AppendAllTextAsync(partial,"\n// 共享Excel保留的手写扩展。\n");var bytes=await File.ReadAllBytesAsync(partial);
            new CSharpGenerator().Generate(catalog,generated,Namespace);Assert.Equal(bytes,await File.ReadAllBytesAsync(partial));Assert.Equal(before,JsonSerializer.Serialize(catalog));
            new FrontendPackager().Validate(catalog);
            if (definition.Redundant == 1 && definition.Derived)
            {
                var service = assembly.GetType(Namespace + ".Generated.Services.SharedExcelSourceService", true)!;
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
                Assert.NotNull(service.GetMethod("Import201Async"));
                Assert.NotNull(service.GetMethod("Export201Async"));
                var namePlan = Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    service.GetField("CrossTargetPlan201_25_0", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                Assert.Equal("string", namePlan.OutputTypes["name"]);
                Assert.Contains("共享级联", catalog.ConfigMenus.First(menu => menu.ConfigPageId == "201").Name);
            }
            if(definition.Redundant==1&&!definition.Derived)await Exercise(assembly,catalog,definition.Shorter,definition.Global,definition.NoProject,displayJoiner ?? "/");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>文本根和末级与整数层混用，原 Excel 入口保留共享父范围。</summary>
    [Theory]
    [InlineData(null,false,false)][InlineData("country_id",false,false)][InlineData("place_id",true,false)]
    [InlineData(null,false,true)][InlineData("country_id",false,true)][InlineData("place_id",true,true)]
    [InlineData("country_id",false,false,true)][InlineData("place_id",false,false,true)]
    public async Task Shared_text_keys_drive_original_write_and_workbook(string? fixedParent,bool global,bool sparse,bool derived=false)
    {
        var catalog=Catalog(derived?"derived-r1":global?"global-r1":"shared-r1");
        if(sparse)catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        var main=catalog.ConfigColumns.Single(column=>column.Id=="25");
        CrossTableExcelLosslessGenerationTests.AddBusinessKeys(catalog,main,primaryLevel:1);
        var warehouse=catalog.ConfigColumns.Single(column=>column.Id=="80");
        var warehouseDetail=JsonNode.Parse(warehouse.ControlDetail)!;
        warehouseDetail["levels"]![0]!["idField"]="business_key";
        warehouse.ControlDetail=warehouseDetail.ToJsonString();
        foreach(var column in catalog.ConfigColumns.Where(column=>column.ColumnName=="business_key"||column.Id=="45"))
        {
            column.LogicalType="string";
            column.ColumnLength="50";
        }
        if(global)
            catalog.ConfigIndexColumns.RemoveAll(column=>
                (column.GetProperty("configIndexId").GetString() is "business_index_3" or "business_index_5")&&
                (column.GetProperty("configColumnId").GetString() is "33" or "53"));
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");
        page.IsNew=page.IsSave=1;
        page.Extra.Remove("type");
        page.Extra["importMethod"]=JsonSerializer.SerializeToElement(fixedParent=="country_id"?"OverWriteByFirstColumn":"OnlyNew");
        page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(fixedParent=="place_id"?"edit":"list");
        if(fixedParent is not null)page.WhereSql+=$" AND {fixedParent}=@parent_id";
        var model=GenerationModel.Create(catalog,Namespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var group=Assert.Single(SharedCrossTableCompilation.ForPage(model,source,Assert.Single(source.Pages)).Groups);
        Assert.All(group.Coordinator.Branches,branch=>Assert.True(branch.Levels[0].TextKey));
        if(derived)Assert.Contains(RelationPropagationCompilation.DerivedBindings(model),binding=>binding.TargetKey=="business_key");
        else if(!sparse)Assert.Contains(RelationPropagationCompilation.Bindings(model),binding=>binding.TargetKey=="business_key");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-cross-text");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            if(derived)
            {
                var generated=assembly.GetType(Namespace+".Generated.Services.SharedExcelSourceService",true)!;
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(generated.TypeHandle);
                var maintenance=assembly.GetType(Namespace+".Generated.GeneratedRelationMaintenance",true)!;
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(maintenance.TypeHandle);
                Assert.NotNull(generated.GetMethod("Import201Async"));
                Assert.NotNull(generated.GetMethod("Export201Async"));
                return;
            }
            var guard=new StopBeforeExcelWrite();
            using var runtime=new SharedExcelReadTransport(assembly,Namespace,"SharedExcelSource",guard:guard,mysqlDialect:true);
            Seed("Country",11,"国甲",key:"001");
            Seed("Country",12,"国乙",key:"1");
            Seed("Area",21,"区甲","SharedExcelCountryId","001");
            Seed("Area",22,"区乙","SharedExcelCountryId","1");
            Seed("Place",31,"城甲","SharedExcelAreaId",21,key:"City-A");
            Seed("Place",32,"城乙","SharedExcelAreaId",22,key:"City-B");
            Seed("Warehouse",41,"仓甲","SharedExcelAreaId",21);
            Seed("Warehouse",42,"仓乙","SharedExcelAreaId",22);
            if(!sparse)runtime.Add("SharedExcelSource",new Dictionary<string,object?>{{"Id",101L},{"Name","同名"},
                {"YzProjectId",catalog.YzProjectId},{"CountryId","001"},{"CountryName","国甲"},{"AreaId","21"},
                {"AreaName","区甲"},{"PlaceId","City-A"},{"PlaceName","城甲"},{"WarehouseId","41"},{"WarehouseName","仓甲"}});
            if(!sparse&&fixedParent=="country_id")
                runtime.Add("SharedExcelSource",new Dictionary<string,object?>{{"Id",102L},{"Name","同名"},
                    {"YzProjectId",catalog.YzProjectId},{"CountryId","1"},{"CountryName","国乙"},{"AreaId","22"},
                    {"AreaName","区乙"},{"PlaceId","City-B"},{"PlaceName","城乙"},{"WarehouseId","42"},{"WarehouseName","仓乙"}});
            var parentId=fixedParent switch{"country_id"=>"001","place_id"=>"City-A",_=>null};
            var input=new Dictionary<string,object?>{{"名称","同名"},{"仓库路径","国甲/区甲/仓甲"}};
            if(fixedParent!="place_id")input["主路径"]="国甲/区甲/城甲";
            if(sparse&&fixedParent=="country_id")
            {
                // 覆盖定位使用固定的 MySQL 稀疏查询，离线传输验证相同生成代码的单元格解析和父键。
                var parsed=await runtime.Parse(input,parentId);
                Assert.Equal("City-A",parsed["placeId"].GetString());
                Assert.Equal("001",parsed["countryId"].GetString());
                var otherRoot=new Dictionary<string,object?>(input)
                {
                    ["主路径"]="国乙/区乙/城乙",
                    ["仓库路径"]="国乙/区乙/仓乙"
                };
                await Assert.ThrowsAsync<PageImportException>(()=>runtime.Parse(otherRoot,parentId));
                return;
            }
            var stop=await Assert.ThrowsAsync<BusinessRuleException>(()=>runtime.Import([input],parentId));
            Assert.Same(guard.Stop,stop);
            Assert.Equal(fixedParent=="country_id"?RecordMutationOperation.Update:RecordMutationOperation.Create,guard.Validation!.Operation);
            if(fixedParent=="country_id")Assert.Equal(101L,guard.Validation.RecordId);
            var invalid=new Dictionary<string,object?>(input){["仓库路径"]="国乙/区乙/仓乙"};
            await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([invalid],parentId));
            if(sparse)return;
            var exported=await runtime.Export(new PageRequest{ParentId=parentId});
            var row=Assert.Single(exported.Rows);
            Assert.Equal("国甲/区甲/城甲",row[exported.Headers.ToList().IndexOf("主路径")]);
            Assert.Equal("国甲/区甲/仓甲",row[exported.Headers.ToList().IndexOf("仓库路径")]);
            Assert.Contains(runtime.Reads,read=>read=="binary:BusinessKey");
            Assert.Empty(runtime.Transport.Commands);

            void Seed(string suffix,long id,string name,string? parent=null,object? parentId=null,string? key=null)
            {
                var row=new Dictionary<string,object?>{{"Id",id},{"Name",name},{"Status",1},{"YzProjectId",catalog.YzProjectId}};
                if(parent is not null)row[parent]=parentId;
                if(key is not null)row["BusinessKey"]=key;
                runtime.Add("SharedExcel"+suffix,row);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>跨表级联候选与原 Excel 路径共用标量文本，布尔原始单元格也可还原。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scalar_cross_labels_roundtrip_in_generated_shared_paths(bool temporal)
    {
        var catalog=Catalog("shared-r1");
        var country=temporal
            ? (Kind:"date",Value:(object)new DateTime(2026,11,18),Text:"2026-11-18")
            : (Kind:"bool",Value:(object)true,Text:"true");
        var area=temporal
            ? (Kind:"datetime",Value:(object)new DateTime(2026,11,18,13,14,15).AddTicks(1234560),Text:"2026-11-18 13:14:15.123456")
            : (Kind:"decimal",Value:(object)1234567890.123456m,Text:"1234567890.123456");
        var place=temporal
            ? (Kind:"time",Value:(object)new TimeSpan(1,3,12,0).Add(TimeSpan.FromTicks(1234560)),Text:"27:12:00.123456")
            : (Kind:"double",Value:(object)0.125d,Text:"0.125");
        foreach(var (id,kind) in new[]{("32",country.Kind),("42",area.Kind),("52",place.Kind)})
            catalog.ConfigColumns.Single(column=>column.Id==id).LogicalType=kind;
        new FrontendPackager().Validate(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-typed-cross-excel");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            const long project=9007199254740993L;
            var actor=new RuntimeActor(901,"跨表名称",false,catalog.YzProjectId,"5");
            using var runtime=new SharedExcelReadTransport(assembly,Namespace,"SharedExcelSource",actor:actor,authorization:new CrossListOnly(actor));
            runtime.Add("SharedExcelCountry",new Dictionary<string,object?>{["Id"]=11L,["Name"]=country.Value,["Status"]=1,["YzProjectId"]=project});
            runtime.Add("SharedExcelArea",new Dictionary<string,object?>{["Id"]=21L,["Name"]=area.Value,["Status"]=1,["YzProjectId"]=project,["SharedExcelCountryId"]=11L});
            runtime.Add("SharedExcelPlace",new Dictionary<string,object?>{["Id"]=31L,["Name"]=place.Value,["Status"]=1,["YzProjectId"]=project,["SharedExcelAreaId"]=21L});
            runtime.Add("SharedExcelWarehouse",new Dictionary<string,object?>{["Id"]=41L,["Name"]="仓库",["Status"]=1,["YzProjectId"]=project,["SharedExcelAreaId"]=21L});
            runtime.Add("SharedExcelSource",new Dictionary<string,object?>{["Id"]=101L,["Name"]="来源",["YzProjectId"]=project,["CountryId"]=11L,["AreaId"]=21L,["PlaceId"]=31L,["WarehouseId"]=41L});

            var option=Assert.Single(await runtime.CrossOptions("25",new CascadeTreeOptionsRequest{LevelKey="place",
                SelectedPath=JsonSerializer.SerializeToElement(new{countryId="11",areaId="21"})}));
            Assert.Equal("31",option.Id);
            Assert.Equal(place.Text,option.Text);
            var excel=await runtime.Export();
            var row=Assert.Single(excel.Rows);
            var main=row[excel.Headers.ToList().IndexOf("主路径")];
            var second=row[excel.Headers.ToList().IndexOf("仓库路径")];
            Assert.Equal(string.Join("/",country.Text,area.Text,place.Text),main);
            Assert.Equal(string.Join("/",country.Text,area.Text,"仓库"),second);
            AssertSlots(await runtime.Parse(new(){["主路径"]=main,["仓库路径"]=second}),"11","21","31","41",false);
            Assert.Equal("31",Text((await runtime.Parse(new(){["主路径"]=place.Text}))["placeId"]));
            if(!temporal)
                Assert.Equal("11",Text((await runtime.Parse(new(){["主路径"]=true}))["countryId"]));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>候选只允许实际来源页和主路径的三个目标页。</summary>
    private sealed class CrossListOnly(RuntimeActor actor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string page,string operation)
        {
            Assert.Equal("list",operation);
            Assert.Contains(page,new[]{"201","301","401","501"});
        }
    }

    /// <summary>实际生成导入按表字段排序锁定匹配，首列留空时不能从另一条共享路径推断覆盖对象。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Redundant_cross_overwrite_uses_the_declared_first_cell(bool pathFirst)
    {
        var catalog=Catalog("short-r1");
        catalog.ConfigPages.Single(page=>page.Id=="201").Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        catalog.ConfigColumns.Single(column=>column.Id==(pathFirst?"25":"22")).SortNo=-1;
        var model=GenerationModel.Create(catalog,Namespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-overwrite");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var runtime=new SharedExcelReadTransport(assembly,Namespace,"SharedExcelSource");
            const long project=9007199254740993;
            runtime.Add("SharedExcelCountry",new Dictionary<string,object?>{{"Id",11L},{"Name","国甲"},{"Status",1},{"YzProjectId",project}});
            runtime.Add("SharedExcelArea",new Dictionary<string,object?>{{"Id",21L},{"Name","区甲"},{"SharedExcelCountryId",11L},{"YzProjectId",project},{"Status",1}});
            runtime.Add("SharedExcelWarehouse",new Dictionary<string,object?>{{"Id",41L},{"Name","仓甲"},{"SharedExcelAreaId",21L},{"YzProjectId",project},{"Status",1}});
            foreach(var id in new[]{101L,102L,103L})
                runtime.Add("SharedExcelSource",new Dictionary<string,object?>{{"Id",id},{"Name","重名"},{"CountryId",11L},{"AreaId",21L},{"YzProjectId",id==103?project+1:project}});
            var header=pathFirst?"主路径":"名称";
            var row=new Dictionary<string,object?>{{"仓库路径","国甲/区甲/仓甲"},{header,pathFirst?"国甲/区甲":"重名"}};
            var ambiguous=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([row]));
            Assert.Equal((PageImportError.AmbiguousPageOverwrite,(int?)1,header),(ambiguous.Code!.Value,ambiguous.RowNumber,ambiguous.ColumnName));
            Assert.Contains(("SharedExcelSource","ToListAsync",2),runtime.ReadCalls);
            Assert.Contains("lock:SharedExcelSource",runtime.Reads);
            foreach(var value in new object?[]{null,"  ",pathFirst?"国甲/":""})
            {
                row[header]=value;
                var missing=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([row]));
                Assert.Equal((PageImportError.MissingOverwriteKey,(int?)1,header),(missing.Code!.Value,missing.RowNumber,missing.ColumnName));
            }
            row.Remove(header);
            var omitted=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([row]));
            Assert.Equal(PageImportError.MissingOverwriteKey,omitted.Code);
            Assert.Equal(5,runtime.Transport.Rollbacks);
            Assert.Equal(0,runtime.Transport.Commits);
            Assert.Empty(runtime.Transport.Commands);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>单链和共享链的 Excel 共用发布的物理父键，整条固定末级仍由页面绑定。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Redundant_parent_cross_excel_uses_the_bound_source_scope(bool independent)
    {
        var catalog=SharedRedundantParentGenerationTests.Catalog(Prefix,"string","int64",false,0,false);
        if(independent)
        {
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="80");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="80"||field.ConfigPageId=="205");
            catalog.ConfigPages.RemoveAll(page=>page.Id=="205");
            catalog.ConfigMenus.RemoveAll(menu=>menu.ConfigPageId=="205");
        }
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="91"))
        {field.IsShow=0;field.IsEdit=0;}
        foreach(var (id,name) in new[]{("22","名称"),("25","主路径"),("80","仓库路径")})
        {
            if(independent&&id=="80")continue;
            catalog.ConfigColumns.Single(column=>column.Id==id).Name=name;
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId==id))
            {field.IsShow=1;field.IsEdit=1;}
        }
        catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-1;
        foreach(var (id,import,export) in new[]{("202","OnlyNew","list"),("203","OverWriteByFirstColumn","list"),("204",independent?"OverWriteByFirstColumn":"OnlyNew","edit"),("206","OnlyNew","list")})
        {
            var page=catalog.ConfigPages.Single(page=>page.Id==id);
            page.IsImport=1;page.IsExport=1;
            page.Extra["importMethod"]=JsonSerializer.SerializeToElement(import);
            page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(export);
        }
        var model=GenerationModel.Create(catalog,Namespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        if(independent)
        {
            var parents=Metarill.Data.Queries.RedundantSharedParents.Compile(catalog,"2","204");
            Assert.Equal(new[]{"country","area","place"},Assert.Single(parents).Value.Levels.Select(level=>level.Prefix));
            var leafPage=catalog.ConfigPages.Single(page=>page.Id=="204");
            var mode=leafPage.Extra["importMethod"];
            leafPage.Extra["importMethod"]=JsonSerializer.SerializeToElement("unknown");
            Assert.Contains(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,Namespace)),
                gap=>gap.Reason==CrossTableExcelRenderer.ImportAcceptancePending);
            leafPage.Extra["importMethod"]=mode;
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-parent-excel");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var sameName=new Dictionary<string,object?>{{"名称","同名"},{"主路径","国甲/区甲/城甲"}};
            if(independent)
            {
                var appendGuard=new StopBeforeExcelWrite();
                using var append=Runtime(assembly,"202",appendGuard);
                var appended=await Assert.ThrowsAsync<BusinessRuleException>(()=>append.Import([
                    new(){{"名称","追加部分路径"},{"主路径","国甲/区甲/"}}
                ],"11"));
                Assert.Same(appendGuard.Stop,appended);
                Assert.Equal(RecordMutationOperation.Create,appendGuard.Validation!.Operation);
                Assert.Contains("place_id",appendGuard.Validation.SubmittedFields);
                var conflict=await Assert.ThrowsAsync<PageImportException>(()=>append.Import([
                    new(){{"名称","另一根路径"},{"主路径","国乙/区乙/城乙"}}
                ],"11"));
                Assert.Equal(PageImportError.Relation,conflict.Code);
                Assert.Equal(2,append.Transport.Rollbacks);
            }

            var overwriteGuard=new StopBeforeExcelWrite();
            using var middle=Runtime(assembly,"203",overwriteGuard);
            var stopped=await Assert.ThrowsAsync<BusinessRuleException>(()=>middle.Import([sameName],"21"));
            Assert.Same(overwriteGuard.Stop,stopped);
            Assert.Equal(RecordMutationOperation.Update,overwriteGuard.Validation!.Operation);
            Assert.Equal(101L,overwriteGuard.Validation.RecordId);
            Assert.Contains(("SharedExcelSource","ToListAsync",1),middle.ReadCalls);
            Assert.Equal(1,middle.Transport.Rollbacks);
            var export=await middle.Export(new PageRequest{ParentId="21"});
            Assert.Equal("国甲/区甲/城甲",Assert.Single(export.Rows)[export.Headers.ToList().IndexOf("主路径")]);
            Assert.Equal(1,middle.Snapshots);
            var locks=middle.Reads.Count(read=>read.StartsWith("lock:",StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>middle.Export(new PageRequest{ParentId="99"}));
            Assert.Equal(locks,middle.Reads.Count(read=>read.StartsWith("lock:",StringComparison.Ordinal)));

            var leafGuard=new StopBeforeExcelWrite();
            using var leaf=Runtime(assembly,"204",leafGuard);
            var fixedField=await Assert.ThrowsAsync<PageImportException>(()=>leaf.Import([sameName],"31"));
            Assert.Equal(PageImportError.UnknownHeader,fixedField.Code);
            if(independent)
            {
                var row=new Dictionary<string,object?>{{"名称","同名"}};
                stopped=await Assert.ThrowsAsync<BusinessRuleException>(()=>leaf.Import([row],"31"));
                Assert.Same(leafGuard.Stop,stopped);
                Assert.Equal(RecordMutationOperation.Update,leafGuard.Validation!.Operation);
                Assert.Equal(101L,leafGuard.Validation.RecordId);
                Assert.DoesNotContain("place_id",leafGuard.Validation.SubmittedFields);
                row["名称"]=" ";
                var empty=await Assert.ThrowsAsync<PageImportException>(()=>leaf.Import([row],"31"));
                Assert.Equal((PageImportError.MissingOverwriteKey,(int?)1,"名称"),(empty.Code!.Value,empty.RowNumber,empty.ColumnName));
            }
            else
            {
                var conflict=await Assert.ThrowsAsync<PageImportException>(()=>leaf.Import([
                    new(){{"名称","越界"},{"仓库路径","国乙/区乙/仓乙"}}
                ],"31"));
                Assert.Equal((PageImportError.Relation,(int?)1,"仓库路径"),(conflict.Code!.Value,conflict.RowNumber,conflict.ColumnName));
            }
            Assert.Equal(independent?3:2,leaf.Transport.Rollbacks);
            export=await leaf.Export(new PageRequest{ParentId="31"});
            Assert.Equal("国甲/区甲/城甲",Assert.Single(export.Rows)[export.Headers.ToList().IndexOf("主路径")]);

            using var compound=Runtime(assembly,"206");
            export=await compound.Export(new PageRequest{ParentId="11"});
            Assert.Single(export.Rows);
            Assert.Equal(0,compound.ExportLockCount);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }

        // 同名记录横跨不同源父；末级和复合来源条件使用相同数据，避免靠夹具过滤碰巧通过。
        SharedExcelReadTransport Runtime(Assembly assembly,string pageId,IRecordMutationGuard? guard=null)
        {
            var runtime=new SharedExcelReadTransport(assembly,Namespace,"SharedExcelSource",pageId:pageId,guard:guard);
            const long project=9007199254740993;
            Seed("Country",11,"国甲");Seed("Country",12,"国乙");
            Seed("Area",21,"区甲","SharedExcelCountryId",11);Seed("Area",22,"区乙","SharedExcelCountryId",12);
            Seed("Place",31,"城甲","SharedExcelAreaId",21);Seed("Place",32,"城乙","SharedExcelAreaId",22);
            Seed("Warehouse",41,"仓甲","SharedExcelAreaId",21);Seed("Warehouse",42,"仓乙","SharedExcelAreaId",22);
            Source(101,11,21,31,41,11,project);
            Source(102,12,22,32,42,12,project);
            Source(103,11,21,31,41,11,project+1);
            Source(104,11,22,32,42,12,project);
            return runtime;

            void Seed(string suffix,long id,string name,string? parent=null,long parentId=0)
            {
                var values=new Dictionary<string,object?>{{"Id",id},{"Name",name},{"Status",1},{"YzProjectId",project}};
                if(parent is not null)values[parent]=parentId;
                runtime.Add("SharedExcel"+suffix,values);
            }
            void Source(long id,long country,long area,long place,long warehouse,long owner,long projectId)
            {
                var values=new Dictionary<string,object?>{{"Id",id},{"Name","同名"},{"CountryId",country},
                    {"AreaId",area},{"PlaceId",place},{"OwnerCountryId",owner},{"YzProjectId",projectId}};
                if(!independent)values["WarehouseId"]=warehouse;
                runtime.Add("SharedExcelSource",values);
            }
        }
    }

    /// <summary>同一目标表用于根和末级时，原 Excel 按层还原父键、名称及选择深度。</summary>
    [Theory]
    [InlineData(1,false,"OnlyNew","list")]
    [InlineData(1,false,"OverWriteByFirstColumn","edit")]
    [InlineData(0,true,"OverWriteByFirstColumn","edit")]
    [InlineData(1,true,"OnlyNew","list")]
    public async Task Independent_repeated_target_roles_publish_original_excel(int redundant,bool derived,string importMode,string exportMode)
    {
        const string prefix="repeated_excel",rootNamespace="Example.RepeatedExcel",entity="RepeatedExcelSource";
        var catalog=RepeatedTargetExcelCatalog(prefix,redundant,derived);
        catalog.ConfigColumns.Single(column=>column.Id=="25").SortNo=-1;
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");
        page.IsImport=1;page.IsExport=1;
        page.Extra["importMethod"]=JsonSerializer.SerializeToElement(importMode);
        page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(exportMode);
        var model=GenerationModel.Create(catalog,rootNamespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-repeated-target-excel");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),rootNamespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(rootNamespace+".Generated.Services."+entity+"Service",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            if(derived)
            {
                if(redundant==0)Assert.NotNull(service.GetField("ImportMatchPlan201",BindingFlags.NonPublic|BindingFlags.Static)?.GetValue(null));
                return;
            }

            var guard=new StopBeforeExcelWrite();
            using var runtime=new SharedExcelReadTransport(assembly,rootNamespace,entity,guard:guard);
            const long project=9007199254740993;
            runtime.Add("RepeatedExcelCountry",new Dictionary<string,object?>{{"Id",11L},{"Name","国甲"},{"LeafLabel","城甲"},
                {"RepeatedExcelAreaId",21L},{"YzProjectId",project},{"Status",1}});
            runtime.Add("RepeatedExcelArea",new Dictionary<string,object?>{{"Id",21L},{"Name","区甲"},{"RepeatedExcelCountryId",11L},{"YzProjectId",project},{"Status",1}});
            foreach(var depth in new[]{0,1,2})
                runtime.Add(entity,new Dictionary<string,object?>{{"Id",101L+depth},{"Name","记录"+depth},{"YzProjectId",project},
                    {"CountryId",11L},{"AreaId",depth>=1?21L:null},{"PlaceId",depth==2?11L:null}});

            var export=await runtime.Export();
            var nameIndex=export.Headers.ToList().IndexOf("记录");
            var pathIndex=export.Headers.ToList().IndexOf("路径");
            Assert.True(nameIndex>=0&&pathIndex>=0);
            var texts=export.Rows.ToDictionary(row=>row[nameIndex]!.ToString()!,row=>row[pathIndex]!.ToString()!);
            string[] expected=["国甲//","国甲/区甲/","国甲/区甲/城甲"];
            foreach(var depth in new[]{0,1,2})
            {
                Assert.Equal(expected[depth],texts["记录"+depth]);
                if(importMode=="OverWriteByFirstColumn"&&depth<2)
                {
                    // 末级作为覆盖键时，部分路径仍可导出，但不能从根层的同主键推断末级。
                    var missing=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([new(){{"路径",expected[depth]}}]));
                    Assert.Equal((PageImportError.MissingOverwriteKey,(int?)1,"路径"),(missing.Code!.Value,missing.RowNumber,missing.ColumnName));
                    continue;
                }
                var values=await runtime.Parse(new(){{"路径",expected[depth]}});
                Assert.Equal("11",values["countryId"].ToString());
                Assert.Equal(depth>=1?"21":"",values["areaId"].ToString());
                Assert.Equal(depth==2?"11":"",values["placeId"].ToString());
            }
            await Assert.ThrowsAsync<PageImportException>(()=>runtime.Parse(new(){{"路径","国甲/区甲/国甲"}}));
            Assert.Equal(0,runtime.ExportLockCount);
            var stopped=await Assert.ThrowsAsync<BusinessRuleException>(()=>runtime.Import([new(){{"路径",expected[2]}}]));
            Assert.Same(guard.Stop,stopped);
            Assert.Equal(importMode=="OnlyNew"?RecordMutationOperation.Create:RecordMutationOperation.Update,guard.Validation!.Operation);
            if(importMode!="OnlyNew")Assert.Equal(103L,guard.Validation.RecordId);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>同一目标表的根和末级可用相同请求ID，但来源筛选和导入只能绑定配置指定的层。</summary>
    [Theory]
    [InlineData(1,false,3)]
    [InlineData(0,false,3)]
    [InlineData(0,true,2)]
    [InlineData(1,true,3)]
    public async Task Repeated_target_roles_bind_the_configured_source_parent(int redundant,bool derived,int depth)
    {
        const string prefix="repeated_excel",rootNamespace="Example.ScopedRepeatedExcel",entity="RepeatedExcelSource";
        var catalog=RepeatedTargetExcelCatalog(prefix,redundant,derived);
        catalog.ConfigColumns.Single(column=>column.Id==(depth==2?"25":"22")).SortNo=-1;
        if(depth==2)
        {
            var tree=catalog.ConfigColumns.Single(column=>column.Id=="25");
            var config=JsonNode.Parse(tree.ControlDetail)!;
            config["levels"]!.AsArray().RemoveAt(1);
            tree.ControlDetail=config.ToJsonString();
            catalog.ConfigColumns.Single(column=>column.Id=="35").LinkConfigTableId="3";
        }
        var source=catalog.ConfigPages.Single(page=>page.Id=="201");
        var scopes=new List<(string Page,string Column,string Parent,string Import,string Export)>
        {
            ("201","country_id","11","OnlyNew","list"),
            ("203","place_id","11","OverWriteByFirstColumn","edit"),
            ("204","place_id","11","OverWriteByFirstColumn","")
        };
        if(depth==3)scopes.Add(("202","area_id","21","OverWriteByFirstColumn","list"));
        else scopes.Add(("205","country_id","11","OverWriteByFirstColumn","edit"));
        foreach(var scope in scopes.Where(item=>item.Page!="201"))
        {
            var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;
            page.Id=scope.Page;
            catalog.ConfigPages.Add(page);
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray())
            {
                var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                copy.Id=scope.Page+"_"+field.Id;
                copy.ConfigPageId=scope.Page;
                catalog.ConfigPageColumns.Add(copy);
            }
        }
        foreach(var (id,column,_,import,export) in scopes)
        {
            var page=catalog.ConfigPages.Single(page=>page.Id==id);
            page.WhereSql+=$" AND {column}=@parent_id";
            page.IsImport=1;
            page.Extra["importMethod"]=JsonSerializer.SerializeToElement(import);
            if(export.Length==0)continue;
            page.IsExport=1;
            page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(export);
        }
        var model=GenerationModel.Create(catalog,rootNamespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var importOnly=catalog.ConfigPages.Single(page=>page.Id=="204");
        importOnly.Extra["importMethod"]=JsonSerializer.SerializeToElement("unknown");
        Assert.Contains(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,rootNamespace)),
            gap=>gap.PageId=="204"&&gap.Reason==CrossTableExcelRenderer.ImportAcceptancePending);
        importOnly.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-scoped-repeated-target-excel");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),rootNamespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(rootNamespace+".Generated.Services."+entity+"Service",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            var actor=new RuntimeActor(901,"父范围验证",false,catalog.YzProjectId,"5");
            foreach(var (id,column,parent,_,export) in scopes)
            {
                Assert.NotNull(service.GetMethod("Import"+id+"Async"));
                Assert.Equal(export.Length>0,service.GetMethod("Export"+id+"Async") is not null);
                if(redundant==1)
                {
                    var plan=Assert.Single(Metarill.Data.Queries.RedundantSharedParents.Compile(catalog,"2",id)).Value;
                    Assert.Equal(column,plan.ColumnName);
                    Assert.Equal(column[..^3],plan.Levels[^1].Prefix);
                    Assert.Equal(column=="place_id"?depth:column=="area_id"?2:1,plan.Levels.Count);
                    continue;
                }

                var filters=(List<Metarill.Data.Queries.FixedReadFilter>)service.GetMethod("ReadFilters"+id,
                    BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[new PageRequest{ParentId=parent},actor])!;
                var identity=Assert.Single(filters,filter=>filter.Field is "country_id" or "area_id" or "place_id");
                Assert.Equal(column,identity.Field);
                Assert.Equal(parent,identity.Value?.ToString());
                Assert.NotNull(service.GetField("LogicalParentPlan"+id,BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null));
                if(id=="205")
                {
                    var match=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                        service.GetField("ImportMatchPlan205",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null));
                    Assert.Equal("string",match.OutputTypes["place_id"]);
                }
            }
            if(derived)return;
            const long project=9007199254740993;
            var appendGuard=new StopBeforeExcelWrite();
            using var root=Runtime("201",appendGuard);
            var conflict=await Assert.ThrowsAsync<PageImportException>(()=>root.Import([
                new(){{"记录","跨父"},{"路径","国乙/区乙/城乙"}}],"11"));
            Assert.Equal((PageImportError.Relation,(int?)1,"路径"),(conflict.Code!.Value,conflict.RowNumber,conflict.ColumnName));
            var appended=await Assert.ThrowsAsync<BusinessRuleException>(()=>root.Import([
                new(){{"记录","部分路径"},{"路径","国甲/区甲/"}}],"11"));
            Assert.Same(appendGuard.Stop,appended);
            Assert.Equal(RecordMutationOperation.Create,appendGuard.Validation!.Operation);
            if(redundant==0)return;

            var list=await root.Export(new PageRequest{ParentId="11"});
            Assert.Equal(2,list.Rows.Count);
            Assert.Empty((await root.Export(new PageRequest{ParentId="14"})).Rows);
            var overwriteGuard=new StopBeforeExcelWrite();
            using var middle=Runtime("202",overwriteGuard);
            var overwritten=await Assert.ThrowsAsync<BusinessRuleException>(()=>middle.Import([
                new(){{"记录","同名"},{"路径","国甲/区甲/城甲"}}],"21"));
            Assert.Same(overwriteGuard.Stop,overwritten);
            Assert.Equal((RecordMutationOperation.Update,103L),(overwriteGuard.Validation!.Operation,overwriteGuard.Validation.RecordId));
            var leafGuard=new StopBeforeExcelWrite();
            using var leaf=Runtime("203",leafGuard);
            var detail=await leaf.Export(new PageRequest{ParentId="11"});
            Assert.Equal("国甲/区甲/城甲",Assert.Single(detail.Rows)[detail.Headers.ToList().IndexOf("路径")]);
            Assert.Equal(0,leaf.ExportLockCount);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>leaf.Export(new PageRequest{ParentId="14"}));
            var fixedColumn=await Assert.ThrowsAsync<PageImportException>(()=>leaf.Import([
                new(){{"记录","同名"},{"路径","国甲/区甲/城甲"}}],"11"));
            Assert.Equal(PageImportError.UnknownHeader,fixedColumn.Code);
            overwritten=await Assert.ThrowsAsync<BusinessRuleException>(()=>leaf.Import([new(){{"记录","同名"}}],"11"));
            Assert.Same(leafGuard.Stop,overwritten);
            Assert.Equal((RecordMutationOperation.Update,103L),(leafGuard.Validation!.Operation,leafGuard.Validation.RecordId));
            Assert.DoesNotContain("place_id",leafGuard.Validation.SubmittedFields);

            SharedExcelReadTransport Runtime(string pageId,IRecordMutationGuard guard)
            {
                var runtime=new SharedExcelReadTransport(assembly,rootNamespace,entity,pageId:pageId,actor:actor,guard:guard);
                runtime.Add("RepeatedExcelCountry",new Dictionary<string,object?>{{"Id",11L},{"Name","国甲"},{"LeafLabel","城甲"},
                    {"RepeatedExcelAreaId",21L},{"YzProjectId",project},{"Status",1}});
                runtime.Add("RepeatedExcelCountry",new Dictionary<string,object?>{{"Id",12L},{"Name","国乙"},{"LeafLabel","城乙"},
                    {"RepeatedExcelAreaId",22L},{"YzProjectId",project},{"Status",1}});
                runtime.Add("RepeatedExcelCountry",new Dictionary<string,object?>{{"Id",14L},{"Name","仅根"},{"YzProjectId",project},{"Status",1}});
                runtime.Add("RepeatedExcelArea",new Dictionary<string,object?>{{"Id",21L},{"Name","区甲"},{"RepeatedExcelCountryId",11L},{"YzProjectId",project},{"Status",1}});
                runtime.Add("RepeatedExcelArea",new Dictionary<string,object?>{{"Id",22L},{"Name","区乙"},{"RepeatedExcelCountryId",12L},{"YzProjectId",project},{"Status",1}});
                if(redundant==1)
                {
                    Source(101,11,null,null,project);
                    Source(103,11,21,11,project);
                    Source(104,12,22,12,project);
                    Source(105,11,21,11,project+1);
                }
                return runtime;

                void Source(long id,long country,long? area,long? leaf,long owner)=>runtime.Add(entity,
                    new Dictionary<string,object?>{{"Id",id},{"Name","同名"},{"CountryId",country},{"AreaId",area},{"PlaceId",leaf},{"YzProjectId",owner}});
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>级联根和末级共享目标表，固定名称与父字段仍属于各自角色。</summary>
    private static MetadataCatalog RepeatedTargetExcelCatalog(string prefix,int redundant,bool derived)
    {
        var catalog=derived?NonRedundantCrossTargetsTests.Catalog(prefix,"string","int64","repeated"):
            RedundantMultiNameCacheTests.Published(CrossTableNameCacheGenerationTests.Catalog(prefix,"string","int64",false));
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=redundant;
        var tree=catalog.ConfigColumns.Single(column=>column.Id=="25");
        if(!derived)
        {
            NonRedundantCrossTargetsTests.RepeatTarget(catalog,prefix,"int64");
            catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="leaf_label",LogicalType="string",ColumnLength="100",YzProjectId=catalog.YzProjectId});
            catalog.ConfigPageColumns.Add(new(){Id="30138",ConfigPageId="301",ConfigColumnId="38",YzProjectId=catalog.YzProjectId});
            var config=JsonNode.Parse(tree.ControlDetail)!;
            config["levels"]![2]!["nameField"]="leaf_label";
            tree.ControlDetail=config.ToJsonString();
        }
        tree.Name="路径";
        catalog.ConfigColumns.Single(column=>column.Id=="22").Name="记录";
        return catalog;
    }

    /// <summary>目标表在同一共享链重复使用时，依旧按两个分支及实际父字段读写原 Excel。</summary>
    [Theory]
    [InlineData("shared-r1",false)]
    [InlineData("short-r0",false)]
    [InlineData("derived-r1",true)]
    [InlineData("derived-short-r0",true)]
    public async Task Shared_repeated_targets_publish_scoped_and_unscoped_excel(string profile,bool derived)
    {
        var catalog=Catalog(profile);
        var table=catalog.ConfigTables.Single(table=>table.Id=="2");
        var shortPath=catalog.ConfigColumns.Single(column=>column.Id=="25").ColumnName=="area_id";
        var nameFirst=derived&&table.IsRongyu==0;
        var main=catalog.ConfigColumns.Single(column=>column.Id=="25");
        var warehouse=catalog.ConfigColumns.Single(column=>column.Id=="80");
        var leafColumn=main.ColumnName;
        catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=nameFirst?-3:0;
        main.SortNo=-2;
        warehouse.SortNo=-1;
        ConfigureRepeatedRole();

        var original=catalog.ConfigPages.Single(page=>page.Id=="201");
        var pages=new (string Id,string? Parent,string Import,string Export)[]
        {
            ("201",null,"OnlyNew","list"),
            ("202",null,"OverWriteByFirstColumn","edit"),
            ("203","country_id","OnlyNew","list"),
            ("204",shortPath?"country_id":"area_id","OverWriteByFirstColumn","list"),
            ("205",leafColumn,"OverWriteByFirstColumn","edit")
        };
        foreach(var entry in pages)
        {
            var page=original;
            if(entry.Id!="201")
            {
                page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;
                page.Id=entry.Id;
                catalog.ConfigPages.Add(page);
                foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray())
                {
                    var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                    copy.Id=entry.Id+"_"+field.Id;
                    copy.ConfigPageId=entry.Id;
                    catalog.ConfigPageColumns.Add(copy);
                }
            }
            page.Extra["importMethod"]=JsonSerializer.SerializeToElement(entry.Import);
            page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(entry.Export);
            if(entry.Parent is not null)page.WhereSql+=$" AND {entry.Parent}=@parent_id";
        }
        var model=GenerationModel.Create(catalog,Namespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var source=model.Tables.Single(value=>value.Source.Id=="2");
        foreach(var page in source.Pages)
        {
            var group=Assert.Single(SharedCrossTableCompilation.ForPage(model,source,page).Groups);
            Assert.Equal(2,group.Plans.Count);
            Assert.Contains(group.Plans,plan=>plan.Levels.Select(level=>level.Read.Target.Source.Id).Distinct().Count()<plan.Levels.Count);
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-repeated-excel");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(Namespace+".Generated.Services.SharedExcelSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            foreach(var (id,parent,_,_) in pages)
            {
                Assert.NotNull(service.GetMethod("Import"+id+"Async"));
                Assert.NotNull(service.GetMethod("Export"+id+"Async"));
                if(table.IsRongyu==0&&parent is not null)
                    Assert.NotNull(service.GetField("LogicalParentPlan"+id,BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null));
            }
            if(table.IsRongyu==0&&!nameFirst)
                foreach(var (id,key) in new[]{("202",leafColumn),("204",leafColumn),("205","warehouse_id")})
                    Assert.Contains(key,Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                        service.GetField("ImportMatchPlan"+id,BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null)).OutputTypes.Keys);
            if(derived)return;

            var first=shortPath?"国甲/区甲":"国甲/区甲/城甲";
            const string second="国甲/区甲/仓甲";
            foreach(var (page,parent) in new (string Page,string? Parent)[]{("201",null),("203","11")})
            {
                var guard=new StopBeforeExcelWrite();
                using var runtime=Runtime(page,guard);
                var conflict=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([
                    new(){{"名称","冲突"},{"主路径",first},{"仓库路径","国乙/区乙/仓乙"}}
                ],parent));
                Assert.Equal(PageImportError.Relation,conflict.Code);
                var added=await Assert.ThrowsAsync<BusinessRuleException>(()=>runtime.Import([
                    new(){{"名称","追加"},{"主路径",first},{"仓库路径",second}}
                ],parent));
                Assert.Same(guard.Stop,added);
                Assert.Equal(RecordMutationOperation.Create,guard.Validation!.Operation);
                if(table.IsRongyu==1)
                    Assert.Equal(parent is null?3:2,(await runtime.Export(new PageRequest{ParentId=parent})).Rows.Count);
            }

            if(table.IsRongyu==0)
            {
                // 仓库路径也提供了短控件的末级ID。省略首列不能由它推断覆盖键。
                foreach(var (page,parent) in new (string Page,string? Parent)[]{("202",null),("204","11")})
                {
                    using var runtime=Runtime(page,new StopBeforeExcelWrite());
                    var missing=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([
                        new(){{"仓库路径",second}}
                    ],parent));
                    Assert.Equal((PageImportError.MissingOverwriteKey,(int?)1,"主路径"),(missing.Code!.Value,missing.RowNumber,missing.ColumnName));
                }
                return;
            }

            foreach(var (page,parent) in new (string Page,string? Parent)[]{("202",null),("204","21"),("205","11")})
            {
                var guard=new StopBeforeExcelWrite();
                using var runtime=Runtime(page,guard);
                var row=new Dictionary<string,object?>{{"仓库路径",second}};
                if(page!="205")row["主路径"]=first;
                var updated=await Assert.ThrowsAsync<BusinessRuleException>(()=>runtime.Import([row],parent));
                Assert.Same(guard.Stop,updated);
                Assert.Equal((RecordMutationOperation.Update,103L),(guard.Validation!.Operation,guard.Validation.RecordId));
                var exported=await runtime.Export(new PageRequest{ParentId=parent,SelectedIds=["103"]});
                Assert.Equal(first,Assert.Single(exported.Rows)[exported.Headers.ToList().IndexOf("主路径")]);
                Assert.Equal(second,Assert.Single(exported.Rows)[exported.Headers.ToList().IndexOf("仓库路径")]);
                if(page=="205")
                {
                    row["主路径"]=first;
                    var fixedColumn=await Assert.ThrowsAsync<PageImportException>(()=>runtime.Import([row],parent));
                    Assert.Equal(PageImportError.UnknownHeader,fixedColumn.Code);
                    await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Export(new PageRequest{ParentId="14"}));
                }
            }

            SharedExcelReadTransport Runtime(string page,IRecordMutationGuard guard)
            {
                var runtime=new SharedExcelReadTransport(assembly,Namespace,"SharedExcelSource",pageId:page,guard:guard);
                const long project=9007199254740993;
                foreach(var (id,name,label,area) in new[]{(11L,"国甲",shortPath?"区甲":"城甲",21L),(12L,"国乙",shortPath?"区乙":"城乙",22L)})
                {
                    var country=new Dictionary<string,object?>{{"Id",id},{"Name",name},{"RepeatLabel",label},{"Status",1},{"YzProjectId",project}};
                    country[shortPath?"SharedExcelCountryId":"SharedExcelAreaId"]=shortPath?id:area;
                    runtime.Add("SharedExcelCountry",country);
                }
                runtime.Add("SharedExcelCountry",new Dictionary<string,object?>{{"Id",14L},{"Name","仅根"},{"Status",1},{"YzProjectId",project}});
                if(!shortPath)
                {
                    runtime.Add("SharedExcelArea",new Dictionary<string,object?>{{"Id",21L},{"Name","区甲"},{"SharedExcelCountryId",11L},{"Status",1},{"YzProjectId",project}});
                    runtime.Add("SharedExcelArea",new Dictionary<string,object?>{{"Id",22L},{"Name","区乙"},{"SharedExcelCountryId",12L},{"Status",1},{"YzProjectId",project}});
                }
                foreach(var (id,name,parent) in new[]{(41L,"仓甲",shortPath?11L:21L),(42L,"仓乙",shortPath?12L:22L)})
                    runtime.Add("SharedExcelWarehouse",new Dictionary<string,object?>{{"Id",id},{"Name",name},{"SharedExcelAreaId",parent},{"Status",1},{"YzProjectId",project}});
                if(table.IsRongyu==1)
                {
                    Source(101,11,null,null,null,project);
                    Source(103,11,21,11,41,project);
                    Source(104,12,22,12,42,project);
                    Source(105,11,21,11,41,project+1);
                }
                return runtime;

                void Source(long id,long country,long? area,long? place,long? storage,long owner)=>runtime.Add("SharedExcelSource",
                    new Dictionary<string,object?>{{"Id",id},{"Name","同名"},{"CountryId",country},{"AreaId",area},{"PlaceId",place},
                        {"WarehouseId",storage},{"YzProjectId",owner}});
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        void ConfigureRepeatedRole()
        {
            // 主链末级复用国家表，短主链的末级同时是仓库链的中层。
            var foreign=shortPath?Prefix+"_country_id":Prefix+"_area_id";
            catalog.ConfigColumns.Add(new(){Id="35",ConfigTableId="3",ColumnName=foreign,
                LogicalType=shortPath?"string":"int64",LinkConfigTableId=shortPath?"3":"4",YzProjectId=catalog.YzProjectId});
            catalog.ConfigPageColumns.Add(new(){Id="30135",ConfigPageId="301",ConfigColumnId="35",YzProjectId=catalog.YzProjectId});
            if(!derived)
            {
                catalog.ConfigColumns.Add(new(){Id="39",ConfigTableId="3",ColumnName="repeat_label",LogicalType="string",
                    ColumnLength="100",YzProjectId=catalog.YzProjectId});
                catalog.ConfigPageColumns.Add(new(){Id="30139",ConfigPageId="301",ConfigColumnId="39",YzProjectId=catalog.YzProjectId});
            }
            main.LinkConfigTableId="3";
            SetRole(main,shortPath?1:2);
            if(shortPath)
            {
                SetRole(warehouse,1);
                catalog.ConfigColumns.Single(column=>column.Id=="85").LinkConfigTableId="3";
            }

            void SetRole(ColumnMetadata control,int index)
            {
                var json=JsonNode.Parse(control.ControlDetail)!;
                var level=json["levels"]![index]!;
                level["table"]=Prefix+"_country";
                level["parentField"]=foreign;
                if(!derived)level["nameField"]="repeat_label";
                control.ControlDetail=json.ToJsonString();
            }
        }
    }

    /// <summary>在真实生成写入的 Mapper 调用前取证，离线测试不模拟 FreeSql 写入。</summary>
    internal sealed class StopBeforeExcelWrite:IRecordMutationGuard
    {
        internal BusinessRuleException Stop { get; }=new("测试到达写入前的业务守卫。");
        internal RecordMutationValidation? Validation { get; private set; }
        public Task BeforeMutationAsync(RecordMutationPreparation preparation,CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public Task BeforeWriteAsync(RecordMutationValidation validation,CancellationToken cancellationToken=default)
        {
            Validation=validation;
            throw Stop;
        }
    }

    /// <summary>冗余同页两种级联在原列表或详情列下导出，隐藏祖先仅用于还原路径。</summary>
    [Theory]
    [InlineData("list")]
    [InlineData("edit")]
    public async Task Redundant_mixed_cascades_publish_and_roundtrip_original_excel(string exportMode)
    {
        const string prefix = "mixed_excel";
        const string ns = "Example.MixedRedundantExcel";
        var catalog = CascadeMixedRequiredTests.Catalog(prefix);
        RedundantMultiNameCacheTests.Published(catalog);
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
        sourcePage.IsImport = 1;
        sourcePage.IsExport = 1;
        sourcePage.Extra["exportMethod"] = JsonSerializer.SerializeToElement(exportMode);
        catalog.ConfigColumns.AddRange([
            new() { Id="91", ConfigTableId="2", ColumnName="list_note", Name="仅列表列", LogicalType="string" },
            new() { Id="92", ConfigTableId="2", ColumnName="detail_note", Name="仅详情列", LogicalType="string" }
        ]);
        catalog.ConfigPageColumns.AddRange([
            new() { Id="20191", ConfigPageId="201", ConfigColumnId="91", IsShow=1, IsEdit=0 },
            new() { Id="20192", ConfigPageId="201", ConfigColumnId="92", IsShow=0, IsEdit=1 }
        ]);
        foreach (var (id, title) in new[] { ("25", "地区路径"), ("81", "分类路径") })
            catalog.ConfigColumns.Single(column => column.Id == id).Name = title;
        Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog, ns)));
        new FrontendPackager().Validate(catalog);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-r1-mixed-cascade-excel");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            if(exportMode=="edit")
            {
                var application=Path.Combine(directory,"application");
                Assert.True(new ApplicationPackager().Create(catalog,application,ns,NonRedundantApplicationFixture.Root()).FrontendIncluded);
                var controller=await File.ReadAllTextAsync(Path.Combine(application,
                    "backend/generated/Controllers/MixedExcelSourcePage201Controller.g.cs"));
                Assert.Contains("[HttpPost(\"initExport\")]",controller);
                Assert.Contains("Actor(\"export\")",controller);
            }
            using var runtime = new SharedExcelReadTransport(assembly, ns, "MixedExcelSource");
            runtime.Add("MixedExcelCountry", new Dictionary<string, object?>
                { ["Id"] = 11, ["Name"] = "国家", ["Status"] = 1, ["YzProjectId"] = 9007199254740993L });
            runtime.Add("MixedExcelArea", new Dictionary<string, object?>
                { ["Id"] = 21, ["Name"] = "地区", ["MixedExcelCountryId"] = 11, ["Status"] = 1, ["YzProjectId"] = 9007199254740993L });
            runtime.Add("MixedExcelPlace", new Dictionary<string, object?>
                { ["Id"] = 31, ["Name"] = "地点", ["MixedExcelAreaId"] = 21, ["Status"] = 1, ["YzProjectId"] = 9007199254740993L });
            runtime.Add("MixedExcelTree", new Dictionary<string, object?>
                { ["Id"] = 41, ["Name"] = "分类", ["Status"] = 1, ["YzProjectId"] = 9007199254740993L });
            runtime.Add("MixedExcelTree", new Dictionary<string, object?>
                { ["Id"] = 42, ["Name"] = "子类", ["ParentMixedExcelTreeId"] = 41, ["Status"] = 1, ["YzProjectId"] = 9007199254740993L });
            runtime.Add("MixedExcelSource", new Dictionary<string, object?>
                { ["Id"] = 101, ["CountryId"] = 11, ["AreaId"] = 21, ["PlaceId"] = 31, ["TreeId"] = 42,
                    ["ListNote"] = "列表值", ["DetailNote"] = "详情值", ["YzProjectId"] = 9007199254740993L });
            runtime.Add("MixedExcelSource", new Dictionary<string, object?>
                { ["Id"] = 102, ["CountryId"] = 11, ["DetailNote"] = "异项目", ["YzProjectId"] = 9007199254740994L });

            var excel = await runtime.Export();
            var row = Assert.Single(excel.Rows);
            var detail = exportMode == "edit";
            var header = detail ? "仅详情列" : "仅列表列";
            Assert.DoesNotContain(detail ? "仅列表列" : "仅详情列", excel.Headers);
            Assert.DoesNotContain("countryId",excel.Headers);
            Assert.DoesNotContain("areaId",excel.Headers);
            Assert.Contains(header, excel.Headers);
            Assert.Equal(detail ? "详情值" : "列表值", row[excel.Headers.ToList().IndexOf(header)]);
            Assert.Equal(1, runtime.Snapshots);
            Assert.Equal(0, runtime.ExportLockCount);
            var cross = row[excel.Headers.ToList().IndexOf("地区路径")];
            var self = row[excel.Headers.ToList().IndexOf("分类路径")];
            Assert.Equal("国家/地区/地点", cross);
            Assert.Equal("分类/子类", self);
            var parsed = await runtime.Parse(new() { ["分类路径"] = self, ["地区路径"] = cross });
            Assert.Equal("42", parsed["treeId"].ToString());
            Assert.Equal("11", parsed["countryId"].ToString());
            Assert.Equal("21", parsed["areaId"].ToString());
            Assert.Equal("31", parsed["placeId"].ToString());
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>共享上级的全局派生名称由两个分支使用同一发布页和同一逻辑槽。</summary>
    [Fact]
    public async Task Shared_global_derived_level_has_one_fixed_scope_for_both_paths()
    {
        const string ns = "Example.SharedGlobalDerivedExcel";
        var catalog = Catalog("derived-short-r0");
        var target = catalog.ConfigTables.Single(table => table.Id == "4");
        var producer = catalog.ConfigTables.Single(table => table.Id == "6");
        foreach (var table in new[] { target, producer })
            table.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        catalog.ConfigPages.Single(page => page.ConfigTableId == target.Id).WhereSql = "status=1";
        catalog.ConfigPages.Single(page => page.ConfigTableId == producer.Id).WhereSql = "";
        // 原地区外键用于同一全局候选时，非冗余子表自己的编辑页也需发布实际 List。
        var leafParent = catalog.ConfigColumns.Single(column => column.Id == "55");
        leafParent.ControlKind = "List";
        leafParent.ControlDetail = JsonSerializer.Serialize(new { tableId = target.Id, pageId = "401", nameField = "name" });
        catalog.ConfigColumns.Add(new()
        {
            Id = "shared_company_name", ConfigTableId = target.Id, YzProjectId = catalog.YzProjectId,
            ColumnName = "company_name", LogicalType = "string",
            Extra = new() { ["isJoinColumn"] = JsonSerializer.SerializeToElement(1), ["columnFullName"] = JsonSerializer.SerializeToElement("company.name") }
        });

        var model = GenerationModel.Create(catalog, ns);
        var source = model.Tables.Single(table => table.Source.Id == "2");
        var page = Assert.Single(source.Pages);
        var group = Assert.Single(SharedCrossTableCompilation.ForPage(model, source, page).Groups);
        Assert.Equal(2, group.Plans.Count);
        Assert.All(group.Plans, plan =>
        {
            Assert.Equal(target.Id, plan.Levels[1].Read.Target.Source.Id);
            Assert.Equal("401", plan.Levels[1].Read.Page.Source.Id);
            Assert.True(plan.Levels[1].Read.DerivedName);
        });
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-shared-global-derived-excel");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.SharedExcelSourceService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>标题只用于独占元数据，原页面生成器／模板及字段归属保持。</summary>
    internal static MetadataCatalog Catalog(string profile)
    {
        var d=Definition(profile);var catalog=SharedCrossTableGenerationTests.Catalog(Prefix,d.Type,d.Parent,d.Shorter,d.Derived);
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=d.Redundant;
        if(d.Global)GlobalSharedCrossGenerationTests.ApplyGlobals(catalog,d.Derived?8:15,d.NoProject);
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsExport=1;
        foreach(var (id,name) in new[]{("22","名称"),("25","主路径"),("80","仓库路径")})
        {catalog.ConfigColumns.Single(column=>column.Id==id).Name=name;var field=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==id);field.IsShow=1;field.IsEdit=1;}
        return catalog;
    }
    /// <summary>明确每个组合，不把未执行的派生／R0 SQL写作解析通过。</summary>
    private static (int Redundant,string Type,string Parent,bool Shorter,bool Derived,bool Global,bool NoProject) Definition(string profile)=>profile switch
    {
        "shared-r1"=>(1,"string","int64",false,false,false,false),"short-r1"=>(1,"int64","string",true,false,false,false),
        "global-r1"=>(1,"string","string",false,false,true,false),"global-no-project-r1"=>(1,"int64","int64",true,false,true,true),
        "shared-r0"=>(0,"string","int64",false,false,false,false),"short-r0"=>(0,"int64","string",true,false,false,false),
        "derived-r1"=>(1,"string","int64",false,true,false,false),
        "derived-global-r1"=>(1,"int64","string",false,true,true,false),"derived-short-r0"=>(0,"int64","string",true,true,false,false),
        _=>throw new ArgumentOutOfRangeException(nameof(profile))
    };
    /// <summary>真实解析器处理两条共享路径，空单元格保留另一控件共享祖先，互斥选择整行拒绝。</summary>
    private static async Task Exercise(Assembly assembly,MetadataCatalog catalog,bool shorter,bool global,bool noProject,string joiner)
    {
        using var runtime=new SharedExcelReadTransport(assembly,Namespace,"SharedExcelSource");
        var primary = shorter ? string.Join(joiner, "国甲", "区甲") : string.Join(joiner, "国甲", "区甲", "城甲");
        var partial = shorter ? primary : string.Join(joiner, "国甲", "区甲", "");
        var root = shorter ? string.Join(joiner, "国甲", "") : string.Join(joiner, "国甲", "", "");
        Seed("Country",11,"国甲");Seed("Country",12,"国乙");Seed("Area",21,"区甲","SharedExcelCountryId",11);Seed("Area",22,"区乙","SharedExcelCountryId",12);
        Seed("Place",31,"城甲","SharedExcelAreaId",21);Seed("Place",32,"城乙","SharedExcelAreaId",22);Seed("Warehouse",41,"仓甲","SharedExcelAreaId",21);Seed("Warehouse",42,"仓乙","SharedExcelAreaId",22);
        Seed("Country",14,"国甲",status:0);Seed("Country",16,"国甲",deleted:true);
        if(!noProject)Seed("Country",19,"跨项目国",project:2);
        foreach(var reverse in new[]{false,true})
        {
            var ordered=new[]{new KeyValuePair<string,object?>("主路径",primary),new("仓库路径","国甲/区甲/仓甲")};
            var values=await runtime.Parse((reverse?ordered.Reverse():ordered).ToDictionary(pair=>pair.Key,pair=>pair.Value));
            AssertSlots(values,"11","21",shorter?null:"31","41",shorter);
            foreach(var conflict in new[]{new[]{"国乙/区乙/仓乙",primary},new[]{"国甲/区甲/仓甲",root}})
            {
                var input=new[]{new KeyValuePair<string,object?>("主路径",conflict[1]),new("仓库路径",conflict[0])};
                await Assert.ThrowsAsync<PageImportException>(()=>runtime.Parse((reverse?input.Reverse():input).ToDictionary(pair=>pair.Key,pair=>pair.Value)));
            }
        }
        AssertSlots(await runtime.Parse(new Dictionary<string,object?>{{"主路径",""},{"仓库路径","国甲/区甲/仓甲"}}),"11","21",null,"41",shorter);
        AssertSlots(await runtime.Parse(new Dictionary<string,object?>{{"主路径",primary},{"仓库路径",""}}),"11","21",shorter?null:"31",null,shorter);
        AssertSlots(await runtime.Parse(new Dictionary<string,object?>{{"主路径",partial},{"仓库路径","国甲/区甲/"}}),"11","21",null,null,shorter);
        AssertSlots(await runtime.Parse(new Dictionary<string,object?>{{"主路径",null},{"仓库路径",""}}),null,null,null,null,shorter);
        var only=await runtime.Parse(new Dictionary<string,object?>{{"主路径",primary}});Assert.False(only.ContainsKey("warehouseId"));
        if(!noProject)
        {
            if(global)Assert.Equal("19",Text((await runtime.Parse(new Dictionary<string,object?>{{"主路径",shorter?"跨项目国/":"跨项目国//"}}))["countryId"]));
            else await Assert.ThrowsAsync<PageImportException>(()=>runtime.Parse(new Dictionary<string,object?>{{"主路径","跨项目国//"}}));
        }
        Source(101,"两条完整",11,21,shorter?null:31,41);Source(102,"主已清末级",11,21,null,41);Source(103,"仓已清末级",11,21,shorter?null:31,null);Source(104,"共同根",11,null,null,null);Source(105,"全部清空",null,null,null,null);
        var locks=runtime.Reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal));
        var exported=await runtime.Export();var names=exported.Headers.ToList().IndexOf("名称");var main=exported.Headers.ToList().IndexOf("主路径");var second=exported.Headers.ToList().IndexOf("仓库路径");Assert.True(names>=0&&main>=0&&second>=0);
        var cells=exported.Rows.ToDictionary(row=>row[names]!.ToString()!,row=>(Main:row[main]?.ToString(),Second:row[second]?.ToString()));
        Assert.Equal((primary,"国甲/区甲/仓甲"),cells["两条完整"]);Assert.Equal((partial,"国甲/区甲/仓甲"),cells["主已清末级"]);Assert.Equal((primary,"国甲/区甲/"),cells["仓已清末级"]);Assert.Equal((root,"国甲//"),cells["共同根"]);Assert.Equal(((string?)null,(string?)null),cells["全部清空"]);
        foreach(var pair in cells)
        {
            var values=await runtime.Parse(new Dictionary<string,object?>{{"主路径",pair.Value.Main},{"仓库路径",pair.Value.Second}});
            if(pair.Key=="共同根")AssertSlots(values,"11",null,null,null,shorter);
            else if(pair.Key=="全部清空")AssertSlots(values,null,null,null,null,shorter);
            else AssertSlots(values,"11","21",shorter||pair.Key=="主已清末级"?null:"31",pair.Key=="仓已清末级"?null:"41",shorter);
        }
        Assert.Equal(1,runtime.Snapshots);Assert.Equal(locks,runtime.ExportLockCount);Assert.Empty(runtime.Transport.Commands);
        Assert.Contains("scope:status",runtime.Reads);Assert.Contains(runtime.Reads,value=>value.StartsWith("lock:",StringComparison.Ordinal));
        // 故障放在导出反查的目标列表内：拒权／取消／读取超时必须原样越过输入错误转换。
        foreach(var failure in new Exception[]{new UnauthorizedAccessException("受控拒权"),new OperationCanceledException("受控取消"),new TimeoutException("受控目标读取超时")})
        {
            runtime.FindFailure=failure;var observed=await Record.ExceptionAsync(()=>runtime.Export());
            Assert.Same(failure,observed);Assert.Null(runtime.FindFailure);
        }
        // 导出也必须拒绝同父重名，不能只在导入入口检查唯一性。
        Seed("Warehouse",43,"仓甲","SharedExcelAreaId",21);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Export());
        Assert.True(Assert.IsType<PageImportException>(error.InnerException).IsInputError);Assert.Equal(5,runtime.Snapshots);
        return;
        void Seed(string suffix,long id,string name,string? parent=null,long parentId=0,int status=1,long project=9007199254740993,bool deleted=false)
        {
            var fields=new Dictionary<string,object?>{{"Id",id},{"Name",name},{"Status",status}};
            if(!noProject)fields["YzProjectId"]=project;if(deleted)fields["DeleteNo"]=DateTime.UtcNow;if(parent is not null)fields[parent]=parentId;
            runtime.Add("SharedExcel"+suffix,fields);
        }
        void Source(long id,string name,long? country,long? area,long? place,long? warehouse)
        {
            var fields=new Dictionary<string,object?>{{"Id",id},{"Name",name},{"YzProjectId",9007199254740993L},{"CountryId",country},{"AreaId",area},{"WarehouseId",warehouse}};if(!shorter)fields["PlaceId"]=place;
            runtime.Add("SharedExcelSource",fields);
        }
    }
    /// <summary>只核对真实解析结果中的ID；短控件没有独立城市槽。</summary>
    private static void AssertSlots(Dictionary<string,JsonElement> values,string? country,string? area,string? place,string? warehouse,bool shorter)
    {Assert.Equal(country,Text(values["countryId"]));Assert.Equal(area,Text(values["areaId"]));if(!shorter)Assert.Equal(place,Text(values["placeId"]));Assert.Equal(warehouse,Text(values["warehouseId"]));}
    /// <summary>精确整数文本和null分开比较。</summary>
    private static string? Text(JsonElement value)=>value.ValueKind==JsonValueKind.Null?null:value.ToString();
}

/// <summary>只替代R1物理ORM读取传输，生成方法及表达式真实执行，未知SQL／接口调用立即失败。</summary>
internal sealed class SharedExcelReadTransport:IDisposable
{
    internal readonly TrustedSecurityMemoryProvider Transport=new();internal readonly List<string> Reads=[];
    internal readonly List<(string Entity,string Method,int Count)> ReadCalls=[];
    private readonly Dictionary<Type,List<object>> rows=[];private readonly Assembly assembly;private readonly string rootNamespace;private readonly Type serviceType;private readonly object service;private readonly SnapshotProvider provider;private readonly string sourceEntity;private readonly bool sourceCondition;private readonly string pageId;
    private readonly RuntimeActor actor;
    internal DatabaseSession Session{get;}
    internal int Snapshots=>provider.Snapshots;
    internal Exception? FindFailure{get;set;}
    internal Action<string,int>? AfterRead{get;set;}
    internal int ExportLockCount{get;private set;}
    /// <summary>仅用实际生成Mapper与Service；额外目标list授权、ADO写入和网络访问均拒绝。</summary>
    internal SharedExcelReadTransport(Assembly assembly,string rootNamespace,string serviceName,bool sourceCondition=false,string pageId="201",RuntimeActor? actor=null,IRuntimeAuthorization? authorization=null,IRecordMutationGuard? guard=null,bool mysqlDialect=false)
    {
        this.assembly=assembly;this.rootNamespace=rootNamespace;sourceEntity=serviceName;this.sourceCondition=sourceCondition;this.pageId=pageId;this.actor=actor??new(901,"共享Excel解析",false,"9007199254740993","5");
        var orm=(IFreeSql)Proxy(typeof(IFreeSql),(method,args)=>method.Name=="Select"?Select(method.GetGenericArguments()[0]):throw new InvalidOperationException("共享Excel离线读取禁止ORM写入或联网。"));
        provider=new(Transport,orm,mysqlDialect);Session=new(new(){Provider="mysql",ExpectedDatabase="offline"},orm,provider);
        var mapper=Activator.CreateInstance(assembly.GetType(rootNamespace+".Generated.Mappers."+serviceName+"Mapper",true)!,Session)!;
        serviceType=assembly.GetType(rootNamespace+".Generated.Services."+serviceName+"Service",true)!;
        service=Activator.CreateInstance(serviceType,Session,mapper,new SnowflakeIdGenerator(951),Array.Empty<IRecordMutationObserver>(),guard is null?Array.Empty<IRecordMutationGuard>():new[]{guard},authorization??new ImportOnly(this.actor))!;
    }
    /// <summary>只向明确生成类型赋值，未知属性不被忽略；键值转换保持精确整数。</summary>
    internal void Add(string entity,IReadOnlyDictionary<string,object?> fields)
    {
        var type=assembly.GetType(rootNamespace+".Generated.Entities."+entity,true)!;var row=Activator.CreateInstance(type)!;
        foreach(var pair in fields){var property=type.GetProperty(pair.Key)??throw new InvalidOperationException("离线种子属性未生成："+pair.Key);var target=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;property.SetValue(row,pair.Value is null?null:Convert.ChangeType(pair.Value,target,CultureInfo.InvariantCulture));}
        if(!rows.TryGetValue(type,out var values))rows[type]=values=[];values.Add(row);
    }
    /// <summary>真实私有解析加入原会话事务；不直接调用共享工具伪造生成方法已接线。</summary>
    internal Task<Dictionary<string,JsonElement>> Parse(Dictionary<string,object?> row,string? parentId=null)=>Session.InTransactionAsync(async()=>
    {
        var method=serviceType.GetMethod("ParseImport"+pageId+"Async",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var arguments=method.GetParameters().Select(parameter=>parameter.Name switch{"row"=>(object?)JsonSerializer.SerializeToElement(row),"parentId"=>parentId,"actor"=>actor,"existing"=>null,"keyOnly"=>false,"cancellationToken"=>CancellationToken.None,_=>throw new InvalidOperationException("未知生成解析参数。")}).ToArray();
        try{return await (Task<Dictionary<string,JsonElement>>)method.Invoke(service,arguments)!;}
        catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    });
    /// <summary>通过实际生成的公开入口调用整批导入，仍禁止替身执行未知写入。</summary>
    internal async Task<string> Import(IReadOnlyList<Dictionary<string,object?>> rows,string? parentId=null)
    {
        var method=serviceType.GetMethod("Import"+pageId+"Async")!;
        var requestType=method.GetParameters()[0].ParameterType;
        var request=Activator.CreateInstance(requestType)!;
        requestType.GetProperty("RequestJson")!.SetValue(request,JsonSerializer.SerializeToElement(rows));
        requestType.GetProperty("ParentId")!.SetValue(request,parentId);
        return await (Task<string>)method.Invoke(service,[request,actor,CancellationToken.None])!;
    }
    /// <summary>读取实际生成的精确过滤器，不复制权限筛选逻辑。</summary>
    internal IReadOnlyList<FixedReadFilter> ReadFilters(PageRequest request,string operation)=>
        (IReadOnlyList<FixedReadFilter>)serviceType.GetMethod("ReadFilters"+pageId,BindingFlags.Instance|BindingFlags.NonPublic)!
            .Invoke(service,[request,actor,operation])!;

    /// <summary>执行公开树候选入口，核对完整层读取和读快照。</summary>
    internal Task<IReadOnlyList<Metarill.Core.Cascades.CascadeTreeOption>> CascadeOptions(string fieldId,CascadeTreeOptionsRequest request)=>
        (Task<IReadOnlyList<Metarill.Core.Cascades.CascadeTreeOption>>)serviceType.GetMethod("CascadeOptions"+pageId+"_"+fieldId+"Async")!
            .Invoke(service,[request,actor,CancellationToken.None])!;


    /// <summary>通过生成的公开入口读取跨表候选。</summary>
    internal Task<IReadOnlyList<Metarill.Core.Cascades.CrossTableOption>> CrossOptions(string fieldId,CascadeTreeOptionsRequest request)=>
        (Task<IReadOnlyList<Metarill.Core.Cascades.CrossTableOption>>)serviceType.GetMethod("CrossOptions"+pageId+"_"+fieldId+"Async")!
            .Invoke(service,[request,actor,CancellationToken.None])!;

    /// <summary>通过真实生成方法检查公开路径响应，内部 Excel 路径仍走原入口。</summary>
    internal Task<Metarill.Core.Cascades.CascadeTreePath> CascadePath(string fieldId,CascadeTreePathRequest request)=>
        (Task<Metarill.Core.Cascades.CascadeTreePath>)serviceType.GetMethod("CascadePath"+pageId+"_"+fieldId+"Async")!
            .Invoke(service,[request,actor,CancellationToken.None])!;

    /// <summary>公开导出方法实际产生原列及单元格，记录导出结束时的锁数。</summary>
    internal async Task<PageExportRequest> Export(PageRequest? request=null)
    {
        try{return await (Task<PageExportRequest>)serviceType.GetMethod("Export"+pageId+"Async")!.Invoke(service,[request??new PageRequest(),actor,100,CancellationToken.None])!;}
        finally{ExportLockCount=Reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal));}
    }
    /// <summary>表达式按真实委托执行；固定SQL只接受明确的状态或来源复合条件，不忽略范围。</summary>
    private object Select(Type type)
    {
        var predicates=new List<Func<object,bool>>();var orders=new List<(Func<object,object?> Key,bool Desc)>();var limit=int.MaxValue;var distinct=false;object query=null!;
        query=Proxy(typeof(ISelect<>).MakeGenericType(type),(method,args)=>
        {
            args??=[];
            if(method.Name is "WithTransaction" or "As")return query;
            if(method.Name=="Where")
            {
                if(args.OfType<LambdaExpression>().SingleOrDefault() is{} expression){var compiled=expression.Compile();predicates.Add(row=>(bool)compiled.DynamicInvoke(row)!);}
                else
                {
                    var sql=Assert.IsType<string>(args[0]);var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(args[1]);
                    predicates.Add(FixedWhere(type,sql,parameters));
                }
                return query;
            }
            if(method.Name is "OrderBy" or "OrderByDescending")
            {
                var expression=Assert.Single(args.OfType<LambdaExpression>());var compiled=expression.Compile();orders.Add((row=>compiled.DynamicInvoke(row),method.Name=="OrderByDescending"));return query;
            }
            if(method.Name=="Limit"){limit=Assert.IsType<int>(args[0]);return query;}
            if(method.Name=="Distinct"){distinct=true;return query;}
            if(method.Name=="ForUpdate"){Assert.NotNull(Session.CurrentTransaction);Reads.Add("lock:"+type.Name);return query;}
            if(method.Name is not("ToListAsync" or "FirstAsync" or "AnyAsync"))throw new InvalidOperationException("共享Excel离线传输未实现："+method.Name);
            if(method.Name=="ToListAsync"&&type.Name=="SharedExcelCountry"&&FindFailure is{} failure){FindFailure=null;throw failure;}
            IEnumerable<object> filtered=rows.GetValueOrDefault(type,[]).Where(row=>predicates.All(predicate=>predicate(row)));
            if(method.Name=="AnyAsync")
            {
                var exists=filtered.Any();ReadCalls.Add((type.Name,method.Name,exists?1:0));return Task.FromResult(exists);
            }
            IOrderedEnumerable<object>? ordered=null;foreach(var order in orders)ordered=ordered is null?order.Desc?filtered.OrderByDescending(order.Key):filtered.OrderBy(order.Key):order.Desc?ordered.ThenByDescending(order.Key):ordered.ThenBy(order.Key);
            filtered=(ordered??filtered).Take(limit);var projection=args.OfType<LambdaExpression>().SingleOrDefault();var output=projection?.ReturnType??type;var compiledProjection=projection?.Compile();
            if(method.Name=="FirstAsync"){var row=filtered.FirstOrDefault();ReadCalls.Add((type.Name,method.Name,row is null?0:1));return TaskValue(output,row is null||compiledProjection is null?row:compiledProjection.DynamicInvoke(row));}
            var projections=filtered.Select(row=>compiledProjection is null?row:compiledProjection.DynamicInvoke(row));
            var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(output))!;
            foreach(var value in distinct?projections.Distinct():projections)list.Add(value);
            ReadCalls.Add((type.Name,method.Name,list.Count));AfterRead?.Invoke(type.Name,limit);return TaskValue(list.GetType(),list);
        });return query;
    }
    /// <summary>只解释status或明确来源模型的三个AND参数比较；括号／运算／字段／值均固定，未知语法拒绝。</summary>
    private Func<object,bool> FixedWhere(Type type,string sql,IReadOnlyDictionary<string,object?> parameters)
    {
        // 文本键按字节精确匹配，数值字符串不得参与隐式等值。
        var binary=Regex.Match(sql,@"^BINARY (?:(?:\(\x60a\x60\.\x60(?<field>[a-z_]+)\x60\))|(?:\x60(?<field>[a-z_]+)\x60))(?:(?:=BINARY @(?<parameter>[A-Za-z0-9_]+))|(?: IN \(BINARY @(?<parameter>[A-Za-z0-9_]+)(?:,BINARY @(?<parameter>[A-Za-z0-9_]+))*\)))$");
        if(binary.Success)
        {
            Assert.Equal(binary.Groups["parameter"].Captures.Count,parameters.Count);
            var accepted=binary.Groups["parameter"].Captures.Select(capture=>Assert.IsType<string>(parameters[capture.Value])).ToHashSet(StringComparer.Ordinal);
            var property=type.GetProperty(GenerationModel.Pascal(binary.Groups["field"].Value))!;
            Reads.Add("binary:"+property.Name);
            return row=>property.GetValue(row) is string value&&accepted.Contains(value);
        }
        if(sql.StartsWith("EXISTS (SELECT 1 FROM `shared_excel_",StringComparison.Ordinal))
        {
            Assert.Equal(sourceEntity,type.Name);
            Assert.Contains("REGEXP '[^[:space:]]'",sql);
            Assert.Contains("BINARY ",sql);
            // 独立数据层回归验证完整相关谓词；这里的已发布叶子通过生成的 ValidateParents 处理。
            return _=>true;
        }
        // 仅处理本测试明确声明的物理精确范围列，检查参数绑定和真实字段读值。
        const string exactPattern=@"^\x60scope_key\x60 IN \((@[A-Za-z0-9_]+(?:,@[A-Za-z0-9_]+)*)\)$";
        var exact=Regex.Match(sql,exactPattern);
        if(exact.Success)
        {
            Assert.Equal(sourceEntity,type.Name);
            var keys=exact.Groups[1].Value.Split(',').Select(key=>key[1..]).ToArray();
            Assert.Equal(keys.Length,parameters.Count);
            var accepted=keys.Select(key=>parameters[key]).ToArray();
            var property=type.GetProperty("ScopeKey")!;
            return row=>accepted.Contains(property.GetValue(row));
        }
        const string comparison=@"\x60(status|code|amount)\x60\s*(>=|=)\s*@([A-Za-z0-9_]+)";
        var matches=Regex.Matches(sql,comparison).Cast<Match>().ToArray();var structure=Regex.Replace(Regex.Replace(sql,comparison,"#"),@"\s+","");
        Assert.Matches(@"^\(*#\)*(?:AND\(*#\)*)*$",structure);
        var balance=0;foreach(var value in structure){if(value=='(')balance++;if(value==')')balance--;Assert.True(balance>=0,"固定SQL括号次序无效。");}Assert.Equal(0,balance);
        Assert.Equal(matches.Length,parameters.Count);Assert.Equal(matches.Length,matches.Select(match=>match.Groups[1].Value).Distinct(StringComparer.Ordinal).Count());
        if(matches.Length!=1){Assert.True(sourceCondition&&type.Name==sourceEntity,"来源复合条件只在明确模型生效。");Assert.Equal(3,matches.Length);Assert.Equal(new[]{"amount","code","status"},matches.Select(match=>match.Groups[1].Value).Order(StringComparer.Ordinal));}
        else Assert.Equal("status",matches[0].Groups[1].Value);
        var tests=new List<Func<object,bool>>();
        foreach(var match in matches)
        {
            var field=match.Groups[1].Value;var operation=match.Groups[2].Value;var expected=parameters[match.Groups[3].Value];
            Assert.Equal(field=="amount"?">=":"=",operation);
            if(field=="code")Assert.Equal("e",Assert.IsType<string>(expected));else Assert.Equal(field=="amount"?0.02m:1m,Convert.ToDecimal(expected,CultureInfo.InvariantCulture));
            var property=type.GetProperty(GenerationModel.Pascal(field))??throw new InvalidOperationException("固定条件属性缺失。");
            tests.Add(row=>property.GetValue(row) is{} value&&(field=="code"?string.Equals(Convert.ToString(value,CultureInfo.InvariantCulture),(string?)expected,StringComparison.Ordinal):operation==">="?Convert.ToDecimal(value,CultureInfo.InvariantCulture)>=Convert.ToDecimal(expected,CultureInfo.InvariantCulture):Convert.ToDecimal(value,CultureInfo.InvariantCulture)==Convert.ToDecimal(expected,CultureInfo.InvariantCulture)));
            Reads.Add("scope:"+field);
        }
        return row=>tests.All(test=>test(row));
    }
    /// <summary>只释放当前离线会话。</summary>
    public void Dispose()=>Session.Dispose();
    /// <summary>通用异步返回只包装传输结果。</summary>
    private static object TaskValue(Type type,object? value)=>typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null,[value])!;
    /// <summary>只创建传输接口代理，不替换生成业务对象。</summary>
    private static object Proxy(Type type,Func<MethodInfo,object?[]?,object?> action){var proxy=DispatchProxy.Create(type,typeof(GeneratedMutationGuardRun.CallProxy));((GeneratedMutationGuardRun.CallProxy)proxy).Action=action;return proxy;}
    /// <summary>解析／路径导出不增加目标list要求；公共入口来源授权另由正式用例覆盖。</summary>
    private sealed class ImportOnly(RuntimeActor actor):IRuntimeAuthorization
    {public RuntimeActor RequireActor()=>actor;public void RequirePagePermission(string page,string operation)=>throw new UnauthorizedAccessException("离线解析不能新增目标list权限。");}
    /// <summary>快照调用只记录请求，内部仍为离线ADO，不证明MySQL隔离和锁。</summary>
    private sealed class SnapshotProvider(TrustedSecurityMemoryProvider inner,IFreeSql orm,bool mysqlDialect):IDatabaseProvider
    {
        internal int Snapshots;public string Name=>mysqlDialect?"mysql":"offline";public DatabaseCapabilities Capabilities=>inner.Capabilities;
        public void Validate(DatabaseOptions options)=>inner.Validate(options);public IFreeSql CreateOrm(DatabaseOptions options)=>orm;public DbConnection CreateConnection(DatabaseOptions options)=>inner.CreateConnection(options);
        public string QuoteIdentifier(string identifier)=>inner.QuoteIdentifier(identifier);public string ParameterPlaceholder(string name)=>inner.ParameterPlaceholder(name);public string BuildSelectForUpdate(string sql)=>inner.BuildSelectForUpdate(sql);
        public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken token=default){Snapshots++;return connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,token);}
        public Task EnsureReadSnapshotAsync(DbTransaction transaction,CancellationToken token=default)=>throw new InvalidOperationException("离线传输不证明嵌套快照。");
    }
}
