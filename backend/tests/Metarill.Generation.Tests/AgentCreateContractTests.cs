using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>从实际生成并编译的新增入口发现 DTO 与 Agent 描述，验证字段名、默认值及服务端归属的边界。</summary>
public sealed class AgentCreateContractTests
{
    [Fact]
    public async Task Published_create_schema_requires_only_real_generated_input_names()
    {
        const long owner=9007199254740993L;
        var catalog=GeneratorTests.Catalog();
        var page=catalog.ConfigPages.Single();
        page.IsNew=1;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="12").IsRequired=1;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="13").IsRequired=1;
        catalog.ConfigColumns.Single(column=>column.Id=="13").Extra["visibleCondition"]=
            JsonSerializer.SerializeToElement("""{"conditions":[{"field":"ownerID","operator":"eq","value":9007199254740993}]}""");
        catalog.ConfigColumns.AddRange([
            new(){Id="16",ConfigTableId="1",ColumnName="owner_ID",LogicalType="int64"},
            new(){Id="17",ConfigTableId="1",ColumnName="default_marker",LogicalType="string",DefaultValue="默认值"},
            new(){Id="18",ConfigTableId="1",ColumnName="initial_marker",LogicalType="string"}
        ]);
        foreach(var field in new[]{"11","15","16","17","18"})
            catalog.ConfigPageColumns.Add(new(){Id="21"+field,ConfigPageId=page.Id,ConfigColumnId=field,IsEdit=1,IsShow=1,IsRequired=1});
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="18").Extra["initValue"]=JsonSerializer.SerializeToElement("初始值");

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-agent-create-contract");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.AgentCreate");
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),
                "<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory,includeAgent:true);
            var registrations=new ServiceCollection();
            registrations.AddLogging();
            registrations.AddControllers().AddApplicationPart(assembly);
            using var services=registrations.BuildServiceProvider();
            var actions=services.GetRequiredService<IActionDescriptorCollectionProvider>();
            var add=actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
                .Single(action=>action.ControllerTypeInfo.Assembly==assembly&&action.ActionName=="Add");
            var request=add.MethodInfo.GetCustomAttribute<LegacyPageWriteRequestAttribute>()!.RequestType;
            var dto=JsonSerializer.Deserialize("""{"name":"客户","ownerID":"9007199254740993"}""",request,JsonDefaults.Options);
            Assert.Equal(owner,Assert.IsType<Optional<long?>>(request.GetProperty("OwnerID")!.GetValue(dto)).Value);
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("""{"owner_ID":"9007199254740993"}""",request,JsonDefaults.Options));
            var index=new GeneratedCapabilityIndex(actions,new Resources(catalog));
            var capability=new AgentCapabilityCatalog(index,new Authorization()).Describe("page:101:create");
            Assert.Equal("/admin/auto/customer/101/add",capability.Path);
            using var document=JsonDocument.Parse(JsonSerializer.Serialize(capability.RequestSchema));
            var schema=document.RootElement;
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            var properties=schema.GetProperty("properties").EnumerateObject().Select(property=>property.Name).ToHashSet(StringComparer.Ordinal);
            Assert.True(properties.SetEquals(["name","remark","ownerID","defaultMarker","initialMarker"]));
            var required=schema.GetProperty("required").EnumerateArray().Select(value=>value.GetString()).ToArray();
            Assert.Equal(new[]{"name","ownerID"},required.Order(StringComparer.Ordinal));
            Assert.All(required,name=>Assert.Contains(name!,properties));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    private sealed class Resources(MetadataCatalog catalog):IAgentHostResources
    {
        public AgentHostCatalog AgentCatalog{get;}=AgentHostCatalog.ForApplication(catalog);
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[];
        public bool IsPlatformRealm(string project,string admin)=>false;
    }

    private sealed class Authorization:IRuntimeAuthorization
    {
        /// <summary>仅提供能力目录所需的固定测试身份。</summary>
        public RuntimeActor RequireActor()=>new(1,"新增字段合同",true,"100","1");
        public void RequirePagePermission(string pageId,string operation)
        {
            Assert.Equal("101",pageId);
            Assert.Equal("create",operation);
        }
    }
}
