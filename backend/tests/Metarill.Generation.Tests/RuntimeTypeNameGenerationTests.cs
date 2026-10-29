using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>业务类型可与运行时同名；真实编译而非通过改元数据/重命名实体绕过。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RuntimeTypeNameGenerationTests
{
    /// <summary>一次编译整个同名模型并检查真实签名，覆盖普通写入、日期、JSON与partial。</summary>
    [Fact]
    public async Task Generated_business_types_keep_names_and_bind_runtime_types_unambiguously()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-runtime-type-binding");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=Catalog();var before=JsonSerializer.Serialize(catalog);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.TypeBinding");
            var hand=Path.Combine(directory,"generated/Services/RuntimeActorService.cs");await File.AppendAllTextAsync(hand,"\n// 手写扩展保留两个同名类型的明确身份。\npublic partial class RuntimeActorService { partial void BeforeSave(global::Example.TypeBinding.Generated.Entities.RuntimeActor entity,global::Metarill.Core.Security.RuntimeActor actor,bool creating,global::System.Collections.Generic.ISet<string> submittedFields) { entity.Name=actor.AccountName+\"：DateTime/RuntimeActor\"; } }\n");var handwritten=await File.ReadAllBytesAsync(hand);
            for(var round=0;round<2;round++){new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.TypeBinding");Assert.Equal(handwritten,await File.ReadAllBytesAsync(hand));}
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var table in catalog.ConfigTables)
            {
                var name=GenerationModel.Pascal(table.TableName);var page=catalog.ConfigPages.Single(page=>page.ConfigTableId==table.Id).Id;var entity=assembly.GetType("Example.TypeBinding.Generated.Entities."+name,true)!;Assert.Equal(typeof(DateTime?),entity.GetProperty("InsertTime")!.PropertyType);Assert.Equal(typeof(string),entity.GetProperty("Payload")!.PropertyType);
                var service=assembly.GetType("Example.TypeBinding.Generated.Services."+name+"Service",true)!;Assert.Contains(service.GetConstructors().Single().GetParameters(),parameter=>parameter.ParameterType==typeof(DatabaseSession));Assert.Contains(service.GetMethod("Create"+page+"Async")!.GetParameters(),parameter=>parameter.ParameterType==typeof(RuntimeActor));
            }
            var actorEntity=Activator.CreateInstance(assembly.GetType("Example.TypeBinding.Generated.Entities.RuntimeActor",true)!)!;var actorService=assembly.GetType("Example.TypeBinding.Generated.Services.RuntimeActorService",true)!;
            actorService.GetMethod("BeforeSave",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(actorService),[actorEntity,new RuntimeActor(8,"保持字面值",true,"77",""),true,new HashSet<string>()]);Assert.Equal("保持字面值：DateTime/RuntimeActor",actorEntity.GetType().GetProperty("Name")!.GetValue(actorEntity));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>派生多值规则、名称装载和导入导出同样采用明确实体身份，覆盖实体命名空间内的CLR遮蔽。</summary>
    [Theory]
    [InlineData("i_read_only_dictionary","invalid_operation_exception")]
    [InlineData("system","convert")]
    public async Task Derived_multi_relation_rule_entities_compile_with_framework_names(string sourceName,string targetName)
    {
        var catalog=NonRedundantMultiSelectionTests.Catalog();
        catalog.ConfigTables.Single(table=>table.Id=="2").TableName=sourceName;
        catalog.ConfigTables.Single(table=>table.Id=="3").TableName=targetName;
        catalog.ConfigTables.Single(table=>table.Id=="1").TableName="hash_set";
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");
        page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("targetNames");
        page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);page.IsImport=1;page.IsExport=1;
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-runtime-rule-binding");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var before=JsonSerializer.Serialize(catalog);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.RuleBinding");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entity=assembly.GetType("Example.RuleBinding.Generated.Entities."+GenerationModel.Pascal(sourceName),true)!;
            Assert.True(typeof(Metarill.Core.Contracts.IGeneratedRuleRecord).IsAssignableFrom(entity));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>导入规范化只能作用于整行指令，不能改写业务默认值和注释的相同文字。</summary>
    [Fact]
    public void Binding_preserves_using_text_inside_literals_and_comments()
    {
        var model=GenerationModel.Create(Catalog(),"Example.TypeBinding");
        const string directive="using Example.TypeBinding.Generated.Entities;";
        var source=directive+"\r\nnamespace Example;\r\n// "+directive+"\r\nconst string text=\""+directive+"\";\r\n";
        Assert.Equal(source[(directive.Length+2)..],RuntimeTypeBinding.BindEntities(model,source));
    }
    /// <summary>仅构造独立测试元数据，原命名和字段不因编译器冲突而删减。</summary>
    internal static MetadataCatalog Catalog()
    {
        var model=new MetadataCatalog{YzProjectId="77"};var index=1000;
        foreach(var name in new[]{"business_calendar","file_upload_lifetime","runtime_actor","page_request","json_serializer","json_element","date_time","task","database_session","query_spec","record_mutation_context","i_record_mutation_observer","i_id_generator","dictionary","hash_set","cancellation_token","r","i_record_mutation_guard","invalid_operation_exception","convert","json_value_kind","argument_exception","table","column","json_property_name_attribute","list","string_comparer","system","i_read_only_dictionary"})
        {
            var table=(++index).ToString();var page=(++index).ToString();model.ConfigTables.Add(new(){Id=table,TableName=name,IsRongyu=1,YzProjectId="77"});model.ConfigPages.Add(new(){Id=page,ConfigTableId=table,YzProjectId="77",IsNew=1,IsSave=1,IsCopy=1,IsDelete=1,IsImport=1});
            foreach(var (column,type) in new[]{("id","int64"),("name","string"),("payload","json"),("insert_time","datetime"),("update_time","datetime"),("delete_no","datetime")})
            {
                var id=(++index).ToString();model.ConfigColumns.Add(new(){Id=id,ConfigTableId=table,ColumnName=column,LogicalType=type,IsDbNotNull=column=="id"?1:0,YzProjectId="77"});model.ConfigPageColumns.Add(new(){Id=(++index).ToString(),ConfigPageId=page,ConfigColumnId=id,ColumnName=column,IsEdit=column=="name"||column=="payload"?1:0,IsShow=1,YzProjectId="77"});
            }
        }
        return model;
    }
}
