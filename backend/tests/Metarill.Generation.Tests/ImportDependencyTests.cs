using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>整行依赖由元数据编译；循环先拒绝，固定目标父和覆盖首列不受页面/JSON排序影响。</summary>
public sealed class ImportDependencyTests
{
    /// <summary>父本身也是名称关系，仍先解析父；匹配键按表排序并递归包含其依赖。</summary>
    [Fact]
    public async Task Match_key_dependencies_follow_table_order_not_page_order()
    {
        var catalog=Fixture();catalog.ConfigColumns.Single(column=>column.Id=="15").SortNo=-10;
        var model=GenerationModel.Create(catalog,"Example.ImportDependency");var table=model.Tables.Single(table=>table.Source.Id=="1");
        var plan=ImportDependencyCompilation.ForPage(model,table,table.Pages.Single(page=>page.Source.Id=="105"));
        Assert.Equal(new[]{"15","19"},plan.MatchFields.Order());Assert.True(plan.Order.ToList().FindIndex(column=>column.Source.Id=="19")<plan.Order.ToList().FindIndex(column=>column.Source.Id=="15"));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-import-match-dependency");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.ImportDependency.Generated.Services.MtImportDepOrderService",true)!.GetMethod("Import105Async"));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>两个名称字段互为目标父时不能产生半套文件，存储模式不改变此约束。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Cyclic_parent_dependency_rejects_before_writing_files(int storageMode)
    {
        var catalog=Fixture();catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=storageMode;
        var customer=catalog.ConfigColumns.Single(column=>column.Id=="15");customer.ControlKind="List";customer.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");
        catalog.ConfigColumns.Single(column=>column.Id=="19").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("301");
        catalog.ConfigColumns.Add(new(){Id="35",ConfigTableId="3",ColumnName="customer_id",Name="候选所属对象",LogicalType="int64",LinkConfigTableId="2"});
        catalog.ConfigPageColumns.Add(new(){Id="30135",ConfigPageId="301",ConfigColumnId="35"});catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="customer_id=@parent_id";
        var model=GenerationModel.Create(catalog,"Example.ImportDependency");var table=model.Tables.Single(table=>table.Source.Id=="1");
        Assert.Contains("循环",Assert.Throws<InvalidOperationException>(()=>ImportDependencyCompilation.ForPage(model,table,table.Pages.Single(page=>page.Source.Id=="104"))).Message);
        var directory=Path.Combine(Path.GetTempPath(),"metarill-import-cycle",Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,model.Namespace));Assert.False(Directory.Exists(directory));
    }
    /// <summary>只读目标父可来自声明默认或原对象，但客户端不能通过Excel覆盖；执行真实生成解析器且不连接数据库。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Readonly_parent_default_compiles_and_cannot_be_submitted_as_an_update(int storageMode)
    {
        var catalog=Fixture();catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=storageMode;
        if(storageMode==1){var customer=catalog.ConfigColumns.Single(column=>column.Id=="15");customer.ControlKind="List";customer.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");}
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="19"&&field.ConfigPageId is "104" or "105")){field.IsEdit=0;field.IsShow=1;}
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-import-readonly-parent");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ImportDependency");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type=assembly.GetType("Example.ImportDependency.Generated.Services.MtImportDepOrderService",true)!;var constructor=type.GetConstructors().Single();var arguments=new object?[constructor.GetParameters().Length];
            var service=constructor.Invoke(arguments);var parse=type.GetMethod("ParseImport105Async",BindingFlags.NonPublic|BindingFlags.Instance)!;var actor=new RuntimeActor(1,"离线依赖",false,"54","5");
            var entity=Activator.CreateInstance(assembly.GetType("Example.ImportDependency.Generated.Entities.MtImportDepOrder",true)!)!;
            var values=await Call("{\"记录名\":\"只提交名称\"}",entity);Assert.Equal(new[]{"name"},values.Keys);
            var error=await Assert.ThrowsAsync<PageImportException>(()=>Call("{\"group_id\":72}",entity));Assert.Equal(PageImportError.UnknownHeader,error.Code);
            // 原对象的显式空父不能退回声明默认71；这一路在访问任何数据库之前就应拒绝。
            error=await Assert.ThrowsAsync<PageImportException>(()=>Call("{\"customer_id\":\"A甲\"}",entity));Assert.Equal(PageImportError.Relation,error.Code);Assert.Equal("customer_id",error.ColumnName);
            /// <summary>只调用正式生成的行解析器，未注入数据库，不能冒充候选或事务实库验证。</summary>
            Task<Dictionary<string,JsonElement>> Call(string json,object existing)=>(Task<Dictionary<string,JsonElement>>)parse.Invoke(service,[JsonSerializer.Deserialize<JsonElement>(json),null,actor,existing,false,CancellationToken.None])!;
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>固定夹具与实际MySQL专项相同，不手写业务CRUD。</summary>
    private static Metarill.Core.Metadata.MetadataCatalog Fixture()
    {var catalog=NonRedundantGeneratedFixture.Catalog("mt_import_dep");NonRedundantGeneratedFixture.EnableSingleSelection(catalog,true);NonRedundantGeneratedFixture.EnableImports(catalog);NonRedundantGeneratedFixture.AddMappedSinglePage(catalog);NonRedundantGeneratedFixture.EnableMappedImports(catalog);return catalog;}
}
