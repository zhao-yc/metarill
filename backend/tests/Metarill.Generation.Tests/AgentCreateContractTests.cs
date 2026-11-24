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
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    public async Task Published_schema_describes_actual_parent_sorting_and_filters(bool hasParent, int storage)
    {
        const long owner=9007199254740993L;
        var catalog=GeneratorTests.Catalog();
        catalog.ConfigTables.Single().IsRongyu=storage;
        foreach (var column in catalog.ConfigColumns.Where(column=>column.LogicalType=="id"))
            column.LogicalType="int64";
        var page=catalog.ConfigPages.Single();
        page.PageSize=hasParent?20:0;
        var selection=hasParent&&storage==0;
        if (selection) page.Extra["type"]=JsonSerializer.SerializeToElement(2);
        if (!hasParent) catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="12").Extra.Remove("isSort");
        page.IsNew=page.IsImport=page.IsExport=1;
        page.WhereSql += " AND store_id=@current_store_id";
        if (hasParent) page.WhereSql += " AND parent_id=@parentId";
        else page.WhereSql += " AND name <> '@parentId'";
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="12").IsRequired=1;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="13").IsRequired=1;
        catalog.ConfigColumns.Single(column=>column.Id=="13").Extra["visibleCondition"]=
            JsonSerializer.SerializeToElement("""{"conditions":[{"field":"ownerID","operator":"eq","value":9007199254740993}]}""");
        catalog.ConfigColumns.AddRange([
            new(){Id="16",ConfigTableId="1",ColumnName="owner_ID",LogicalType="int64"},
            new(){Id="17",ConfigTableId="1",ColumnName="default_marker",LogicalType="string",DefaultValue="默认值"},
            new(){Id="18",ConfigTableId="1",ColumnName="initial_marker",LogicalType="string"},
            new(){Id="19",ConfigTableId="1",ColumnName="parent_id",LogicalType="int64",LinkConfigTableId="1"},
            new(){Id="20",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"},
            new(){Id="21",ConfigTableId="1",ColumnName="booked_on",LogicalType="date"},
            new(){Id="22",ConfigTableId="1",ColumnName="active",LogicalType="bool",ControlKind="CheckBox"}
        ]);
        foreach(var field in new[]{"11","15","16","17","18","19","20"})
            catalog.ConfigPageColumns.Add(new(){Id="21"+field,ConfigPageId=page.Id,ConfigColumnId=field,IsEdit=1,IsShow=1,IsRequired=1});
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="18").Extra["initValue"]=JsonSerializer.SerializeToElement("初始值");
        if (hasParent)
            catalog.ConfigColumns.Single(column=>column.Id=="16").Extra["isSort"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigColumns.Single(column=>column.Id=="14").Extra["isSort"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="16").Extra["isInFilter"]=JsonSerializer.SerializeToElement(1);
        foreach (var id in new[] { "21", "22" })
            catalog.ConfigPageColumns.Add(new() {Id="31"+id,ConfigPageId=page.Id,ConfigColumnId=id,IsShow=1,IsEdit=0,
                Extra=new() {["isInFilter"]=JsonSerializer.SerializeToElement(1)}});

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
            Assert.False(Assert.IsType<Optional<long?>>(request.GetProperty("ParentId")!.GetValue(dto)).IsSpecified);
            Assert.False(Assert.IsType<Optional<long?>>(request.GetProperty("StoreId")!.GetValue(dto)).IsSpecified);
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("""{"owner_ID":"9007199254740993"}""",request,JsonDefaults.Options));
            var index=new GeneratedCapabilityIndex(actions,new Resources(catalog));
            var directoryCatalog=new AgentCapabilityCatalog(index,new Authorization());
            var capability=directoryCatalog.Describe("page:101:create");
            Assert.Equal("/admin/auto/customer/101/add",capability.Path);
            Assert.Equal(hasParent,Assert.Single(capability.Parameters,parameter=>parameter is { Name:"parentId", Binding:"query" }).Required);
            using var document=JsonDocument.Parse(JsonSerializer.Serialize(capability.RequestSchema));
            var schema=document.RootElement;
            Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            var properties=schema.GetProperty("properties").EnumerateObject().Select(property=>property.Name).ToHashSet(StringComparer.Ordinal);
            Assert.True(properties.SetEquals(["name","remark","ownerID","defaultMarker","initialMarker","parentId","storeId"]));
            var required=schema.GetProperty("required").EnumerateArray().Select(value=>value.GetString()).ToArray();
            Assert.Equal(hasParent?new[]{"name","ownerID"}:new[]{"name","ownerID","parentId"},required.Order(StringComparer.Ordinal));
            Assert.All(required,name=>Assert.Contains(name!,properties));

            foreach (var action in new[] { "list", "count", "export", "import" })
            {
                var published=directoryCatalog.Describe("page:101:"+action);
                Assert.DoesNotContain(published.Parameters,parameter=>parameter.Binding=="query"&&parameter.Name=="parentId");
                using var actionSchema=JsonDocument.Parse(JsonSerializer.Serialize(published.RequestSchema));
                var body=actionSchema.RootElement;
                Assert.True(body.GetProperty("properties").TryGetProperty("parentId",out _));
                var requiredBody=body.TryGetProperty("required",out var requiredFields)
                    ? requiredFields.EnumerateArray().Select(field=>field.GetString()).ToArray() : [];
                var expected=new List<string>();
                if (hasParent) expected.Add("parentId");
                if (action=="import") expected.Add("requestJson");
                Assert.Equal(expected,requiredBody.Order(StringComparer.Ordinal));
                if (action=="list")
                {
                    var pageFields=body.GetProperty("properties");
                    var size=pageFields.GetProperty("pageSize");
                    Assert.Equal(hasParent?1:0,size.GetProperty("minimum").GetInt32());
                    Assert.Equal(1000,size.GetProperty("maximum").GetInt32());
                    var number=pageFields.GetProperty("pageNo");
                    Assert.Equal(1,number.GetProperty("minimum").GetInt32());
                    Assert.Equal(selection,number.TryGetProperty("maximum",out var maxPage));
                    if (selection) Assert.Equal(1000000,maxPage.GetInt32());
                    Assert.Equal(!hasParent,body.TryGetProperty("allOf",out var rules));
                    if (!hasParent)
                    {
                        var rule=Assert.Single(rules.EnumerateArray());
                        Assert.Equal(0,rule.GetProperty("if").GetProperty("properties").GetProperty("pageSize").GetProperty("const").GetInt32());
                        Assert.Equal(1,rule.GetProperty("then").GetProperty("properties").GetProperty("pageNo").GetProperty("const").GetInt32());
                    }
                }
                if (action is "list" or "count" or "export")
                {
                    var ids=body.GetProperty("properties").GetProperty("selectedIds");
                    Assert.Equal(selection,ids.TryGetProperty("maxItems",out var maxItems));
                    if (selection) Assert.Equal(200,maxItems.GetInt32());
                }
                if (action is "list" or "count" or "export")
                {
                    var filterSchema=body.GetProperty("properties").GetProperty("filters");
                    Assert.False(filterSchema.GetProperty("additionalProperties").GetBoolean());
                    var filterFields=filterSchema.GetProperty("properties");
                    Assert.Equal(new[] { "active", "bookedOn", "name", "ownerID" },
                        filterFields.EnumerateObject().Select(field=>field.Name).Order(StringComparer.Ordinal));
                    var ownerFilter=filterFields.GetProperty("ownerID").GetProperty("anyOf").EnumerateArray().ToArray();
                    Assert.Contains(ownerFilter,choice=>choice.GetProperty("type").GetString()=="integer");
                    Assert.Contains(ownerFilter,choice=>choice.GetProperty("type").GetString()=="string");
                    Assert.Contains(ownerFilter,choice=>choice.GetProperty("type").GetString()=="array");
                    var dateRange=filterFields.GetProperty("bookedOn").GetProperty("anyOf").EnumerateArray()
                        .Single(choice=>choice.GetProperty("type").GetString()=="array");
                    Assert.Equal(2,dateRange.GetProperty("minItems").GetInt32());
                    Assert.Equal(2,dateRange.GetProperty("maxItems").GetInt32());
                    var booleanChoices=filterFields.GetProperty("active").GetProperty("anyOf").EnumerateArray().ToArray();
                    Assert.Contains(booleanChoices,choice=>choice.GetProperty("type").GetString()=="boolean");
                    Assert.Contains(booleanChoices,choice=>choice.TryGetProperty("enum",out var allowed)&&allowed[0].GetInt32()==0);
                }
                if (action is "list" or "export")
                {
                    var found=body.GetProperty("properties").TryGetProperty("orderBy",out var orderBy);
                    Assert.Equal(hasParent,found);
                    if (!found) continue;
                    var orderByFields=orderBy.GetProperty("properties");
                    foreach (var key in new[] { "column", "field" })
                        Assert.Equal(new[] { "name", "ownerID" }, Choices(orderByFields.GetProperty(key)));
                    Assert.Equal(new[] { "ascending", "descending" }, Choices(orderByFields.GetProperty("order")));
                }
            }

            const string readSelected="page:101:options:selection/selected";
            if (selection)
            {
                var published=directoryCatalog.Describe(readSelected);
                Assert.Equal("/admin/auto/customer/101/selection/selected",published.Path);
                Assert.False(published.ConfirmationRequired);
                using var selectedSchema=JsonDocument.Parse(JsonSerializer.Serialize(published.RequestSchema));
                var selectedBody=selectedSchema.RootElement;
                Assert.False(selectedBody.GetProperty("additionalProperties").GetBoolean());
                Assert.Equal("parentId",Assert.Single(selectedBody.GetProperty("required").EnumerateArray()).GetString());
                var ids=selectedBody.GetProperty("properties").GetProperty("selectedIds");
                Assert.Equal(200,ids.GetProperty("maxItems").GetInt32());
                Assert.Equal("string",ids.GetProperty("items").GetProperty("type").GetString());
                Assert.Throws<UnauthorizedAccessException>(()=>new AgentCapabilityCatalog(index,new Authorization(false)).Describe(readSelected));
            }
            else
                Assert.Throws<UnauthorizedAccessException>(()=>directoryCatalog.Describe(readSelected));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>读取实际能力响应公开的排序候选。</summary>
    private static string?[] Choices(JsonElement property) =>
        property.GetProperty("enum").EnumerateArray().Select(item=>item.GetString()).ToArray();

    private sealed class Resources(MetadataCatalog catalog):IAgentHostResources
    {
        public AgentHostCatalog AgentCatalog{get;}=AgentHostCatalog.ForApplication(catalog);
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[];
        public bool IsPlatformRealm(string project,string admin)=>false;
    }

    private sealed class Authorization(bool allowList=true):IRuntimeAuthorization
    {
        /// <summary>仅提供能力目录所需的固定测试身份。</summary>
        public RuntimeActor RequireActor()=>new(1,"新增字段合同",true,"100","1");
        public void RequirePagePermission(string pageId,string operation)
        {
            Assert.Equal("101",pageId);
            Assert.Contains(operation,new[]{"create","list","count","export","import"});
            if (operation=="list"&&!allowList) throw new UnauthorizedAccessException("测试缺少列表权限。");
        }
    }
}
