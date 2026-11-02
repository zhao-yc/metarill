using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>逻辑覆盖匹配的生成／固定计划验证；不冒充MySQL写入、并发或正式Full验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantImportMatchTests
{
    /// <summary>物理祖先为空的合法稀疏记录，匹配计划仍读取逻辑祖先；表排序决定首列。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Generated_match_plan_uses_logical_ancestor_and_keeps_minimal_private_outputs(string type)
    {
        var catalog=Catalog(type);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.LogicalImport");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();
        var declared=ImportRenderer.Columns(page);Assert.Equal("name",declared[0].Source.ColumnName);
        var match=NonRedundantImportMatchRenderer.Resolve(model,table,page,ImportDependencyCompilation.ForPage(model,table,page))!;
        Assert.Equal("country_id",match.Key.Source.ColumnName);Assert.Equal(new[]{"country_id","id"},match.Outputs.Order());Assert.Contains("yz_project_id",match.Dependencies);
        var sparse=CrossTableSparseSelection.Compile(table.Source,"25",catalog.ConfigColumns.Where(column=>column.ConfigTableId=="2"));var stored=sparse.PackValidatedPath(new long[]{1,2,3});Assert.Null(stored["country_id"]);Assert.NotNull(stored["place_id"]);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-logical-import-match");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,model.Namespace);
            var hand=Path.Combine(generated,"Services/LogicalOverwriteSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 覆盖匹配再生成保留手写业务。\n");var handBytes=await File.ReadAllBytesAsync(hand);new CSharpGenerator().Generate(catalog,generated,model.Namespace);Assert.Equal(handBytes,await File.ReadAllBytesAsync(hand));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var service=assembly.GetType(model.Namespace+".Generated.Services.LogicalOverwriteSourceService",true)!;
            var plan=(NonRedundantReadPlan)service.GetField("ImportMatchPlan201",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
            Assert.Equal(type,plan.OutputTypes["country_id"]);Assert.Equal(new[]{"country_id","id"},plan.OutputTypes.Keys.Order());
            // 核对真实生成类型装载的适配计划，不用Service源码包含某个方法名作为逻辑读取的证明。
            var mysql=typeof(NonRedundantReadPlan).GetProperty("MySql",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plan)!;
            var expression=(string)mysql.GetType().GetMethod("Expression",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(mysql,new object[]{"country_id"})!;
            Assert.NotEqual("b.`country_id`",expression);Assert.Contains("place_id",expression);
            Assert.NotNull(service.GetMethod("Import201Async"));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>已有R1、追加和普通非跨表导入不换读取链，不扩大任何公开用途。</summary>
    [Theory][InlineData("redundant")][InlineData("append")][InlineData("plain")][InlineData("physical-key")]
    public void Other_import_paths_keep_their_original_match_implementation(string kind)
    {
        var catalog=Catalog("string");if(kind=="redundant")catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        if(kind=="append")catalog.ConfigPages.Single(page=>page.Id=="201").Extra["importMethod"]=JsonSerializer.SerializeToElement("OnlyNew");
        if(kind=="plain")catalog.ConfigColumns.Single(column=>column.Id=="25").ControlKind="";
        if(kind=="physical-key")catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-1000;
        var model=GenerationModel.Create(catalog,"Example.UnchangedImport");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();
        Assert.Null(NonRedundantImportMatchRenderer.Resolve(model,table,page,ImportDependencyCompilation.ForPage(model,table,page)));
    }
    /// <summary>普通／共享六模型都能生成并装载覆盖计划，但原Full覆盖导入门槛仍拒绝。</summary>
    [Fact]
    public async Task Six_model_backend_compiles_overwrite_without_opening_public_Full()
    {
        var catalog=GlobalCachedCrossExcelNativePreparation.Catalog();var pages=catalog.ConfigPages.Where(page=>catalog.ConfigTables.Any(table=>table.Id==page.ConfigTableId&&table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal))).ToArray();Assert.Equal(6,pages.Length);
        // 原附件数组仍不能作首列；明确以原级联控件为首列，确保六模型实际装载逻辑匹配分支。
        foreach(var page in pages){page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigColumns.First(column=>column.ConfigTableId==page.ConfigTableId&&column.ControlKind=="CascadeTree").SortNo=-10000;}
        var model=GenerationModel.Create(catalog,"Example.SixLogicalImports");Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Reason==CrossTableExcelRenderer.ImportAcceptancePending);Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-six-logical-imports");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var page in pages){var table=model.Tables.Single(table=>table.Source.Id==page.ConfigTableId);var service=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;var plan=(NonRedundantReadPlan)service.GetField("ImportMatchPlan"+page.Id,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;Assert.Contains("id",plan.OutputTypes.Keys);Assert.DoesNotContain("attachments",plan.OutputTypes.Keys);}
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>名称首列且无逻辑上下文时继续编译原覆盖路径，不创建额外匹配计划。</summary>
    [Fact]
    public async Task Ordinary_physical_key_keeps_original_generated_query_path()
    {
        var catalog=Catalog("string");catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-1000;var model=GenerationModel.Create(catalog,"Example.PhysicalImport");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();Assert.Null(NonRedundantImportMatchRenderer.Resolve(model,table,page,ImportDependencyCompilation.ForPage(model,table,page)));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-physical-import-match");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var service=assembly.GetType(model.Namespace+".Generated.Services.LogicalOverwriteSourceService",true)!;Assert.Null(service.GetField("ImportMatchPlan201",BindingFlags.NonPublic|BindingFlags.Static));Assert.NotNull(service.GetMethod("Import201Async"));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>内部BackendOnly包必须从自身Core／Data源码编译；不借工作区DLL、不冒充公开Full。</summary>
    [Fact]
    public async Task Backend_only_package_builds_its_own_logical_overwrite_runtime()
    {
        var catalog=Catalog("string");var boundary=Path.Combine(Path.GetTempPath(),"metarill-own-logical-import");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var result=new ApplicationPackager().Create(catalog,directory,"Example.OwnLogicalImport",NonRedundantApplicationFixture.Root(),ApplicationPackageMode.BackendOnly);Assert.False(result.FrontendIncluded);
            await NonRedundantApplicationFixture.BuildAsync(directory,"restore");await NonRedundantApplicationFixture.BuildAsync(directory,"build");
            Assert.True(File.Exists(Path.Combine(directory,"src/Metarill.Data/Queries/NonRedundantReads.cs")));Assert.True(File.Exists(Path.Combine(directory,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>候选父取原记录的逻辑祖先，省略不补交、显式null不退回声明默认；实际调用生成解析器且禁止数据库访问。</summary>
    [Fact]
    public async Task Generated_context_keeps_logical_parent_null_and_omission_separate()
    {
        var catalog=Catalog("string");catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-1000;catalog.ConfigPageColumns.Single(field=>field.Id=="20160").IsShow=1;
        catalog.ConfigTables.Add(new(){Id="6",TableName="logical_overwrite_picker",IsRongyu=1});
        catalog.ConfigColumns.AddRange(new ColumnMetadata[]{new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="62",ConfigTableId="6",ColumnName="name",LogicalType="string"},new(){Id="63",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="64",ConfigTableId="6",ColumnName="delete_no",LogicalType="datetime"},new(){Id="65",ConfigTableId="6",ColumnName="logical_overwrite_country_id",LogicalType="int64",LinkConfigTableId="3"}});
        catalog.ConfigPages.Add(new(){Id="601",ConfigTableId="6",WhereSql="yz_project_id=@current_yz_project_id AND logical_overwrite_country_id=@parent_id"});catalog.ConfigPageColumns.Add(new(){Id="60162",ConfigPageId="601",ConfigColumnId="62"});
        var picker=new ColumnMetadata{Id="71",ConfigTableId="2",ColumnName="picker_id",Name="候选",LogicalType="int64",LinkConfigTableId="6",ControlKind="List",ControlDetail="{\"table\":\"logical_overwrite_picker\",\"tableId\":\"6\",\"nameField\":\"name\"}"};picker.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("601");catalog.ConfigColumns.Add(picker);catalog.ConfigPageColumns.Add(new(){Id="20171",ConfigPageId="201",ConfigColumnId="71",IsEdit=1,IsShow=1});
        var model=GenerationModel.Create(catalog,"Example.ImportContext");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();var dependency=ImportDependencyCompilation.ForPage(model,table,page);Assert.True(dependency.Contextual);Assert.Equal("country_id",Assert.Single(dependency.ContextValues).Source.ColumnName);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-logical-import-context");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            // 存储协议禁止稀疏ID声明DB默认；用手写partial构造默认验证显式null仍覆盖默认。
            await File.WriteAllTextAsync(Path.Combine(directory,"EntityDefaults.cs"),"namespace Example.ImportContext.Generated.Entities; public partial class LogicalOverwriteSource { public LogicalOverwriteSource(){CountryId=\"99\";} }");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var type=assembly.GetType(model.Namespace+".Generated.Services.LogicalOverwriteSourceService",true)!;
            var contextMethod=type.GetMethod("ImportContext201",BindingFlags.NonPublic|BindingFlags.Static)!;
            var defaultEntity=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Entities.LogicalOverwriteSource",true)!)!;Assert.Equal("99",defaultEntity.GetType().GetProperty("CountryId")!.GetValue(defaultEntity));
            var context=contextMethod.Invoke(null,new object[]{new Dictionary<string,object?>{{"country_id","1"}}})!;Assert.Equal("1",context.GetType().GetProperty("CountryId")!.GetValue(context));Assert.NotEqual(defaultEntity.GetType(),context.GetType());
            var nullContext=contextMethod.Invoke(null,new object[]{new Dictionary<string,object?>{{"country_id",null}}})!;Assert.Null(nullContext.GetType().GetProperty("CountryId")!.GetValue(nullContext));
            var constructor=type.GetConstructors().Single();var arguments=new object?[constructor.GetParameters().Length];foreach(var parameter in constructor.GetParameters()){if(parameter.ParameterType==typeof(IEnumerable<IRecordMutationObserver>))arguments[parameter.Position]=Array.Empty<IRecordMutationObserver>();if(parameter.ParameterType==typeof(IEnumerable<IRecordMutationGuard>))arguments[parameter.Position]=Array.Empty<IRecordMutationGuard>();}var service=constructor.Invoke(arguments);var parse=type.GetMethod("ParseImport201Async",BindingFlags.NonPublic|BindingFlags.Instance)!;var actor=new RuntimeActor(1,"上下文离线验证",false,catalog.YzProjectId,"1");
            var values=await Parse("{\"name\":\"只提交名称\"}",context);Assert.Equal(new[]{"name"},values.Keys);
            var error=await Assert.ThrowsAsync<PageImportException>(()=>Parse("{\"候选\":\"某候选\"}",nullContext));Assert.Equal(PageImportError.Relation,error.Code);Assert.Equal("候选",error.ColumnName);
            /// <summary>没有提供session／mapper，正常名称解析与空父拒绝必须在任何数据库操作之前结束。</summary>
            Task<Dictionary<string,JsonElement>> Parse(string json,object existing)=>(Task<Dictionary<string,JsonElement>>)parse.Invoke(service,new object?[]{JsonSerializer.Deserialize<JsonElement>(json),null,actor,existing,false,CancellationToken.None})!;
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>沿用真实跨表目录，仅按元数据显式把祖先原输入列设为覆盖首列，页面列顺序不改。</summary>
    private static MetadataCatalog Catalog(string type)
    {
        var catalog=NonRedundantCrossTableGenerationTests.Catalog("logical_overwrite",type,"int64");var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        catalog.ConfigColumns.Add(new(){Id="60",ConfigTableId="2",ColumnName="country_id",Name="国家ID",LogicalType=type,ColumnLength=type=="string"?"64":"",LinkConfigTableId="3",SortNo=-100});catalog.ConfigPageColumns.Add(new(){Id="20160",ConfigPageId="201",ConfigColumnId="60",IsEdit=1,IsShow=0,SortNo=1000});return catalog;
    }
}
