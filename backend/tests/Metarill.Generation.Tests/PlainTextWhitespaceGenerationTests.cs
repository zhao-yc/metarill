using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原Java实际依赖发现的空白差异必须穿过真正生成的R1查询和R0过滤入口。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class PlainTextWhitespaceGenerationTests
{
    /// <summary>不打开数据库；确认Unicode输入是否进入原LIKE，不能用源码包含断言替代实际入口。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public async Task Actual_generated_text_filters_follow_original_java_blank_rules(int redundant)
    {
        var catalog=redundant==0?NonRedundantGeneratedFixture.Catalog("mt_unicode"):GeneratorTests.Catalog();
        catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=redundant;
        var model=GenerationModel.Create(catalog,"Example.UnicodeFilter");var table=model.Tables.Single(table=>table.Source.Id=="1");var page=table.Pages.First();
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-unicode-filter");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            if(redundant==1)await File.WriteAllTextAsync(Path.Combine(directory,"QueryProbe.cs"),"""
using System.Reflection;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Core.Persistence;
using Metarill.Data;
using Example.UnicodeFilter.Generated.Entities;
using Example.UnicodeFilter.Generated.Mappers;
using Example.UnicodeFilter.Generated.Services;
public static class QueryProbe
{
    // 直接调用实际生成的查询方法；不可达连接确保本测试不会执行数据库请求。
    public static bool HasLike(string value)
    {
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
        var service=new CustomerService(session,new CustomerMapper(session),new SnowflakeIdGenerator(3),Array.Empty<IRecordMutationObserver>());
        var request=new PageRequest{Filters=new(){["name"]=JsonSerializer.SerializeToElement(value)}};
        var method=typeof(CustomerService).GetMethod("Query101",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var query=(ISelect<Customer>)method.Invoke(service,new object[]{request,new RuntimeActor(1,"空白规则验收",false,"100","5")})!;
        return query.ToSql().Contains(" LIKE ",StringComparison.Ordinal);
    }
}
""");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var serviceType=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;
            var cases=new(string Value,bool HasLike)[]{("\u0085",true),("\u00a0",true),("\u2007",true),("\u202f",true),("\u001c",false),("\u001d",false),("\u001e",false),("\u001f",false),(" \t\r\n",false),("\u2003",false),("",false),("\u001ctext\u00a0",true)};
            foreach(var item in cases)
            {
                if(redundant==1)Assert.Equal(item.HasLike,(bool)assembly.GetType("QueryProbe",true)!.GetMethod("HasLike")!.Invoke(null,[item.Value])!);
                else
                {
                    var request=new PageRequest{ParentId="71",Filters=new(){["name"]=JsonSerializer.SerializeToElement(item.Value)}};
                    var actual=Assert.IsType<List<FixedReadFilter>>(serviceType.GetMethod("ReadFilters"+page.Source.Id,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[request,new RuntimeActor(1,"空白规则验收",false,"54","5")]));
                    var name=actual.Where(filter=>filter.Field=="name").ToArray();Assert.Equal(item.HasLike?1:0,name.Length);
                    if(item.HasLike){Assert.Equal(FixedReadComparison.LikePattern,name[0].Comparison);Assert.Equal(item.Value,name[0].Value);}
                }
            }
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}