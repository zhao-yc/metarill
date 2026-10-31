using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.RegularExpressions;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class DerivedRuleRecordGenerationTests
{
    /// <summary>共享跨表、短链和派生目标的全部父页面实际编译；无匹配记录也保留逻辑范围，不回退成根表宽查询。</summary>
    [Theory]
    [InlineData("string","string",false,false,false)][InlineData("string","int64",false,false,false)]
    [InlineData("int64","string",false,false,false)][InlineData("int64","int64",false,false,false)]
    [InlineData("string","int64",true,false,false)][InlineData("int64","string",true,false,false)]
    [InlineData("string","int64",false,true,false)]
    [InlineData("string","int64",false,false,true)]
    public async Task Shared_cross_rule_precheck_preserves_all_logical_parent_scopes(string type,string parentType,bool shorter,bool derived,bool onlyIds)
    {
        var catalog=SharedCrossParentGenerationTests.Catalog("rule_cross",type,parentType,shorter,derived);
        foreach(var source in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))
        {source.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement(onlyIds?"countryId > 0":"countryId > 0 && (countryName == '禁止' || warehouseName == '封锁')");source.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);source.IsDelete=1;source.IsCopy=1;}
        var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CrossRule");var table=model.Tables.Single(table=>table.Source.Id=="2");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-cross-rule");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var serviceType=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;
            const string parentId="9007199254740881";
            foreach(var page in table.Pages)
            {
                var plan=(NonRedundantReadPlan)serviceType.GetField("RuleRecordPlan"+page.Source.Id,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
                if(!onlyIds){Assert.Contains("country_name",plan.OutputTypes.Keys);Assert.Contains("warehouse_name",plan.OutputTypes.Keys);}
                var transport=new Transport(plan.OutputTypes){ProjectId=catalog.YzProjectId,HasRow=false};using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"},null,transport);
                var actor=new RuntimeActor(8,"共享父范围规则",false,catalog.YzProjectId,page.Source.YzAdminTypeId);var authorization=new Authorization(actor,page.Source.Id);var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,session)!;
                var service=Activator.CreateInstance(serviceType,[session,mapper,new SnowflakeIdGenerator(949),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
                async Task Read(string operation,string? parent=parentId)
                {authorization.Operation=operation;var task=(Task)serviceType.GetMethod("ReadAgentRecord"+page.Source.Id+"Async")!.Invoke(service,[Transport.Id,parent,operation,actor,authorization,CancellationToken.None])!;await task;Assert.Null(task.GetType().GetProperty("Result")!.GetValue(task));}
                foreach(var operation in new[]{"update","delete","copy"})
                {
                    await Read(operation);var command=transport.Commands[^1];Assert.Contains(command.Values,value=>value is long key&&key==Transport.Id);Assert.Contains("metarill_cross_input_",command.Sql);Assert.Contains("metarill_cross_valid_",command.Sql);
                    Assert.DoesNotContain("COUNT(",command.Sql);Assert.DoesNotContain("FOR UPDATE",command.Sql);Assert.DoesNotContain("FOR SHARE",command.Sql);Assert.DoesNotContain(parentId,command.Sql);Assert.DoesNotContain("禁止",command.Sql);Assert.Equal(System.Data.IsolationLevel.RepeatableRead,command.Isolation);
                    if(page.Source.Id=="201")Assert.DoesNotContain(command.Values,value=>Convert.ToString(value,CultureInfo.InvariantCulture)==parentId);else Assert.Contains(command.Values,value=>Convert.ToString(value,CultureInfo.InvariantCulture)==parentId);
                    if(page.Source.Id=="206")Assert.Contains("`owner_country_id`",command.Sql);
                }
                Assert.Equal(3,transport.Commands.Count);Assert.Equal(3,transport.Commits);Assert.Null(session.CurrentTransaction);
                if(page.Source.Id!="201")foreach(var invalid in new string?[]{null,"","0","+1","01","1.0"})
                {var count=transport.Commands.Count;await Assert.ThrowsAsync<ArgumentException>(()=>Read("update",invalid));Assert.Equal(count,transport.Commands.Count);Assert.Null(session.CurrentTransaction);}
                var old=transport.Commands.Count;authorization.Allow=false;await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read("delete"));Assert.Equal(old,transport.Commands.Count);
                authorization.Allow=true;transport.HasRow=true;var corrupt=false;
                object Selected()=>type=="string"?parentId:(object)long.Parse(parentId,CultureInfo.InvariantCulture);
                object? Raw(string name)=>name switch{"warehouse_id"=>Selected(),"place_id" when !shorter=>Selected(),"country_id" when corrupt=>Selected(),_=>null};
                transport.RowValue=(name,sql)=>
                {
                    if(name.StartsWith("metarill_rule_value_",StringComparison.Ordinal))return Selected();
                    if(name.StartsWith("metarill_cross_valid_",StringComparison.Ordinal))return 1L;
                    if(name.StartsWith("metarill_cross_input_",StringComparison.Ordinal))
                    {
                        var match=Regex.Match(sql,@"b\.`(?<field>[a-zA-Z0-9_]+)` AS `"+Regex.Escape(name)+"`");Assert.True(match.Success);return Raw(match.Groups["field"].Value);
                    }
                    if(name is "country_id" or "area_id" or "place_id" or "warehouse_id")
                    {
                        // 固定SQL的物理投影才返回稀疏原值，旧逻辑投影会得到祖先P，必须被下方断言发现。
                        var raw=Regex.IsMatch(sql,@"(?:SELECT |,)b\.`"+name+"` AS `"+name+"`");return raw?Raw(name):Selected();
                    }
                    return name switch{"id"=>Transport.Id,"yz_project_id"=>catalog.YzProjectId,"owner_country_id"=>Selected(),"name"=>"合成记录","country_name"=>"禁止","warehouse_name"=>"封锁",_=>null};
                };
                authorization.Operation="update";var loaded=(Task)serviceType.GetMethod("ReadAgentRecord"+page.Source.Id+"Async")!.Invoke(service,[Transport.Id,parentId,"update",actor,authorization,CancellationToken.None])!;await loaded;
                var entity=loaded.GetType().GetProperty("Result")!.GetValue(loaded)!;var proof=Assert.IsAssignableFrom<IGeneratedRuleRecord>(entity);Assert.True(proof.RequiresLoadedRuleValue("country_id"));Assert.True(proof.TryGetLoadedRuleValue("country_id",out var logicalId));Assert.Equal(parentId,Convert.ToString(logicalId,CultureInfo.InvariantCulture));
                if(!onlyIds){Assert.True(proof.TryGetLoadedRuleValue("country_name",out var country));Assert.Equal("禁止",country);Assert.True(proof.TryGetLoadedRuleValue("warehouse_name",out var warehouse));Assert.Equal("封锁",warehouse);}
                var values=new Dictionary<string,object?>{{"countryId",logicalId},{"countryName","禁止"},{"warehouseName","封锁"}};Assert.True(Metarill.Core.Metadata.ReadOnlyConditionRule.Compile(page.Source.Extra["readOnlyCondition"].GetString()).IsReadOnly(values));
                var forged=Assert.IsAssignableFrom<IGeneratedRuleRecord>(JsonSerializer.Deserialize("{\"id\":\"9007199254740993\",\"countryId\":\"0\",\"generatedRuleValues\":{\"country_id\":0}}",entity.GetType(),JsonDefaults.Options));Assert.True(forged.RequiresLoadedRuleValue("country_id"));Assert.False(forged.TryGetLoadedRuleValue("country_id",out _));
                Assert.Null(entity.GetType().GetProperty("CountryId")!.GetValue(entity));Assert.Null(entity.GetType().GetProperty("AreaId")!.GetValue(entity));Assert.Equal(parentId,Convert.ToString(entity.GetType().GetProperty("WarehouseId")!.GetValue(entity),CultureInfo.InvariantCulture));
                // 必须发布真实生成控制器的写能力，并由实际Agent目录消费逻辑证明；不能只测试Service读取。
                var definitions=new ServiceCollection();definitions.AddLogging();definitions.AddControllers().AddApplicationPart(assembly).AddJsonOptions(options=>JsonDefaults.CopyTo(options.JsonSerializerOptions));definitions.AddSingleton<IRuntimeAuthorization>(authorization);definitions.AddSingleton<IAgentHostResources>(new Resources(catalog));definitions.AddSingleton(serviceType,service);definitions.AddSingleton<GeneratedCapabilityIndex>();definitions.AddSingleton<AgentCapabilityCatalog>();using(var services=definitions.BuildServiceProvider())
                {
                    var action=services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Single(action=>action.ControllerTypeInfo.Assembly==assembly&&action.AttributeRouteInfo?.Template?.EndsWith("/"+page.Source.Id+"/update",StringComparison.Ordinal)==true);
                    var context=new DefaultHttpContext{RequestServices=services};context.Request.Method="POST";context.Request.Path="/"+action.AttributeRouteInfo!.Template;context.Request.ContentType="application/json";context.Request.QueryString=new("?parentId="+parentId);context.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(action),"真实共享规则更新"));
                    var error=await Assert.ThrowsAsync<AgentTaskException>(()=>services.GetRequiredService<AgentCapabilityCatalog>().ValidateRecordWriteInvocationAsync("page:"+page.Source.Id+":update",context,Encoding.UTF8.GetBytes("{\"id\":\"9007199254740993\",\"name\":\"允许回显\"}")));Assert.Equal("YZP_CAPABILITY_DENIED",error.Code);Assert.Equal("not_applied",error.Outcome);
                }
                corrupt=true;var rejected=(Task)serviceType.GetMethod("ReadAgentRecord"+page.Source.Id+"Async")!.Invoke(service,[Transport.Id,parentId,"update",actor,authorization,CancellationToken.None])!;await Assert.ThrowsAsync<InvalidOperationException>(async()=>await rejected);Assert.Null(session.CurrentTransaction);
            }
            Assert.Equal(6,table.Pages.Count);Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
