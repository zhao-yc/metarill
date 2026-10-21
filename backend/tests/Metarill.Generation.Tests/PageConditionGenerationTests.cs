using System.Reflection;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行实际生成Scope和ORM SQL，不以解析通过冒充数据库或浏览器范围验收。</summary>
public sealed class PageConditionGenerationTests
{
    /// <summary>隐藏物理条件、精确整数、原LIKE及包含AND/引号的字符串进入参数，不能扩展VO。</summary>
    [Fact]
    public async Task Actual_generated_scope_binds_page_constants_without_exposing_hidden_columns()
    {
        var catalog=Catalog();catalog.ConfigPages[0].WhereSql="(yz_project_id=@current_yz_project_id AND status IN (1,2,NULL)) AND name='A AND B,O''Neil' AND remark IS NULL AND id>=9007199254740993";
        var before=JsonSerializer.Serialize(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-page-conditions");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.PageConditions");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            var type=assembly.GetType("Example.PageConditions.Generated.Services.CustomerService",true)!;var mapper=Activator.CreateInstance(assembly.GetType("Example.PageConditions.Generated.Mappers.CustomerMapper",true)!,[session]);
            var args=new object?[type.GetConstructors().Single().GetParameters().Length];args[0]=session;args[1]=mapper;args[2]=new SnowflakeIdGenerator(861);args[3]=Array.Empty<IRecordMutationObserver>();var service=Activator.CreateInstance(type,args)!;
            var query=type.GetMethod("Scope101",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,[new RuntimeActor(8,"离线SQL",false,"100","5"),null])!;
            var entity=assembly.GetType("Example.PageConditions.Generated.Entities.Customer",true)!;var sql=(string)typeof(PageConditionGenerationTests).GetMethod(nameof(Sql),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(entity).Invoke(null,[query])!;
            Assert.Contains("page_101_",sql);Assert.Contains("IN (",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("NULL",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("O'Neil",sql);Assert.DoesNotContain("9007199254740993",sql);
            Assert.Null(assembly.GetType("Example.PageConditions.Generated.VOs.CustomerPage101Vo",true)!.GetProperty("Status"));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未知变量、原生代码、语句及越界类型仍原子拒绝，不能把新增支持当作任意SQL开关。</summary>
    [Theory]
    [InlineData("status=1 OR id=1")][InlineData("status=@current_yz_manager_id")][InlineData("status=@formParameters.status")]
    [InlineData("yz_project_id=@parentId")][InlineData("status=@current_yz_project_id")][InlineData("status IN (@parentId,1)")]
    [InlineData("missing=1")][InlineData("status=1.5")][InlineData("status=2147483648")][InlineData("name=1")]
    [InlineData("status=coalesce(1,2)")][InlineData("lqw.eq(1,1)")][InlineData("status=1;DELETE FROM customer")]
    public void Unproved_page_conditions_reject_before_any_generation_write(string definition)
    {
        var catalog=Catalog();catalog.ConfigPages[0].WhereSql=definition;var directory=Path.Combine(Path.GetTempPath(),"metarill-page-rejected",Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.PageRejected"));Assert.False(Directory.Exists(directory));
    }
    /// <summary>来源控件只可重复目标页已执行的常量，不能以相似字段或更窄来源条件扩大候选。</summary>
    [Theory]
    [InlineData("status=1",true)][InlineData("status=2",false)][InlineData("status>=1",false)]
    [InlineData("status=1 AND yz_project_id=@current_yz_project_id",true)][InlineData("status=1 AND id=1",false)]
    public void Selection_proof_requires_each_actual_target_page_term(string extra,bool expected)
    {
        var catalog=Catalog();catalog.ConfigPages[0].WhereSql="status=1";var model=GenerationModel.Create(catalog,"Example.Proof");var table=Assert.Single(model.Tables);var page=Assert.Single(table.Pages);
        Assert.Equal(expected,FixedPageQueryCompilation.TryProveSelection(FixedPageQueryCompilation.For(table,page),new("101","name","选择",extra,""),out _));
    }
    /// <summary>非冗余条件依赖不进入公开输出，写后读取另有固定事务入口，记录可离开筛选页。</summary>
    [Fact]
    public async Task Nonredundant_hidden_conditions_compile_and_keep_mutation_echo_separate()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_page_nonred");catalog.ConfigPages[0].WhereSql+=" AND status=2";var hidden=catalog.ConfigPageColumns.Single(f=>f.ConfigPageId=="101"&&f.ConfigColumnId=="18");hidden.IsEdit=0;hidden.IsShow=0;hidden.Extra.Clear();
        var model=GenerationModel.Create(catalog,"Example.PageNonred");var table=model.Tables.Single(t=>t.Source.Id=="1");var page=table.Pages.Single(p=>p.Source.Id=="101");var plan=NonRedundantReadCompilation.ForPage(model,table,page);
        Assert.Contains("status",plan.Dependencies);Assert.DoesNotContain(plan.Outputs,o=>o.Storage=="status");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-page-nonred");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType(model.Namespace+".Generated.Services.MtPageNonredOrderService",true)!.GetMethod("MutationResult101Async",BindingFlags.Instance|BindingFlags.NonPublic));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原版本无常量的页面不增加静态计划，保持现有生成与运行时路径。</summary>
    [Fact]
    public void Identity_only_page_keeps_the_existing_scope_contract()
    {var model=GenerationModel.Create(Catalog(),"Example.NoConstants");var page=Assert.Single(Assert.Single(model.Tables).Pages);Assert.Null(page.Condition);Assert.Single(page.Scopes);}
    /// <summary>多选只能重复目标已执行的常量；合法分支实际编译带固定条件的当前读验证。</summary>
    [Theory]
    [InlineData("",true)][InlineData("name LIKE '可用%'",true)][InlineData("name LIKE '其它%'",false)]
    public async Task Multi_selection_requires_the_actual_target_condition(string extra,bool expected)
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog();catalog.ConfigPages.Single(p=>p.Id=="301").WhereSql+=" AND name LIKE '可用%'";
        var source=catalog.ConfigColumns.Single(c=>c.Id=="25");var definition=System.Text.Json.Nodes.JsonNode.Parse(source.ControlDetail)!;definition["whereSql"]=extra;source.ControlDetail=definition.ToJsonString();
        var model=GenerationModel.Create(catalog,"Example.MultiPageCondition");var table=model.Tables.Single(t=>t.Source.Id=="2");var page=table.Pages.Single(p=>p.Source.Id=="201");
        var compiled=MultiSelectCompilation.Resolve(model,table,page,table.Columns.Single(c=>c.Source.Id=="25"));Assert.Equal(expected,compiled.Definition is not null);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-multi-page-condition");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            if(!expected){Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace));Assert.False(Directory.Exists(directory));return;}
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/RelationSourceService.g.cs"));Assert.Contains("MultiPageCondition201_25",service);Assert.Contains("query=query.ForUpdate();",service);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    private static string Sql<T>(object query)where T:class=>((ISelect<T>)query).ToSql();
    private static MetadataCatalog Catalog()
    {var catalog=GeneratorTests.Catalog();catalog.ConfigColumns.Add(new(){Id="16",ConfigTableId="1",ColumnName="status",LogicalType="int32"});return catalog;}
}
