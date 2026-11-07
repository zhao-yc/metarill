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
    [Fact]
    public async Task Derived_match_compiles_as_hidden_read_dependency_and_separate_lock_plan()
    {
        var catalog=Catalog(true);catalog.ConfigPages.Single(page=>page.Id=="101").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("customer_notes");
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="16")){field.IsShow=0;field.IsEdit=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}
        var model=GenerationModel.Create(catalog,"Example.DerivedScope");var table=model.Tables.Single(table=>table.Source.Id=="1");var page=table.Pages.Single(page=>page.Source.Id=="101");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-exact-scope");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="metarill_business_test",ConnectionString="Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;"});
            var actor=new RuntimeActor(1,"离线派生范围",false,catalog.YzProjectId,page.Source.YzAdminTypeId);var authorization=new Authorization(actor);
            var type=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,[session]);
            var service=Activator.CreateInstance(type,[session,mapper,new SnowflakeIdGenerator(862),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            var plan=Assert.IsType<NonRedundantReadPlan>(type.GetField("ExactReadPlan101",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));Assert.Equal("string",Assert.Single(plan.OutputTypes).Value);Assert.Equal("customer_notes",Assert.Single(plan.OutputTypes).Key);
            var filters=(List<FixedReadFilter>)type.GetMethod("ReadFilters101",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(service,[new PageRequest{ParentId="71"},actor,"export"])!;
            Assert.Single(filters,filter=>filter.Field=="customer_notes"&&filter.Comparison==FixedReadComparison.In);Assert.Equal("export",authorization.LastOperation);
            var vo=assembly.GetType(model.Namespace+".Generated.VOs."+page.Name+"Vo",true)!;Assert.Null(vo.GetProperty("CustomerNotes"));
            // 反射调用须显式占位可选参数，保留生成方法默认启用真实事务锁的合同。
            var scope=type.GetMethod("Scope101",BindingFlags.NonPublic|BindingFlags.Instance)!;var locking=Assert.Single(scope.GetParameters().Skip(3));Assert.Equal("exactLocking",locking.Name);Assert.True(locking.IsOptional);Assert.Equal(true,locking.DefaultValue);
            var error=Assert.Throws<TargetInvocationException>(()=>scope.Invoke(service,[actor,"71","update",Type.Missing]));Assert.IsType<InvalidOperationException>(error.InnerException);Assert.Contains("真实写事务",error.InnerException!.Message);
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
    /// <summary>范围测试替身仅提供服务端值，和实际Web认证/范围聚合的验收分开。</summary>
    private sealed class Authorization(RuntimeActor actor):IRuntimeAuthorization
    {
        internal bool Restricted=true;internal string Value="[\"只允许甲\",\"只允许乙\"]";internal string LastOperation="";
        /// <summary>当前身份固定于本测试上下文。</summary>
        public RuntimeActor RequireActor()=>actor;
        /// <summary>本类只测试计划消费，不冒充角色权限决策。</summary>
        public void RequirePagePermission(string pageId,string operation){}
        /// <summary>记录计划传来的固定动作，并提供原JSON数组形状。</summary>
        public ExactDataManageValues ResolveExactDataManage(string pageId,string operation,string managerField)
        {Assert.Equal("101",pageId);Assert.Equal("real_name",managerField);LastOperation=operation;return Restricted?new(true,JsonSerializer.SerializeToElement(Value)):ExactDataManageValues.Unrestricted;}
    }
}
