using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>冻结关系快照须与输入记录排列无关，且不能改写源配置或业务数组。</summary>
public sealed class GenerationMetadataPublisherTests
{
    /// <summary>实际混合关系目录同时覆盖表、字段、页面、后台及维护按钮次序。</summary>
    [Fact]
    public void Relation_catalog_is_stable_without_reordering_business_arrays_or_mutating_source()
    {
        var catalog=NonRedundantParentedSelfTreeTests.Catalog("nr_publish_order","int64",false);
        catalog.YzAdminTypes.Add(new(){Id="99",YzProjectId=catalog.YzProjectId});
        catalog.ConfigButtons.AddRange(new[]{("902","/admin/sys/system_maintenance/tables"),("901","/admin/sys/system_maintenance/backfill_join_columns")}.Select(item=>JsonSerializer.SerializeToElement(new{id=item.Item1,sql=item.Item2})));
        catalog.ConfigTables[0].Extra["orderedBusinessArray"]=JsonSerializer.SerializeToElement(new[]{new{id="2"},new{id="1"}});
        var source=JsonSerializer.Serialize(catalog);var first=GenerationMetadataPublisher.ForRelations(catalog);
        Assert.Equal(source,JsonSerializer.Serialize(catalog));
        Assert.Equal(new[]{"2","1"},first.ConfigTables.Single(table=>table.Id==catalog.ConfigTables[0].Id).Extra["orderedBusinessArray"].EnumerateArray().Select(item=>item.GetProperty("id").GetString()));
        catalog.ConfigTables.Reverse();catalog.ConfigColumns.Reverse();catalog.ConfigPages.Reverse();catalog.YzAdminTypes.Reverse();catalog.ConfigButtons.Reverse();
        source=JsonSerializer.Serialize(catalog);var second=GenerationMetadataPublisher.ForRelations(catalog);
        Assert.Equal(JsonSerializer.Serialize(first),JsonSerializer.Serialize(second));Assert.Equal(source,JsonSerializer.Serialize(catalog));
    }

    /// <summary>直接发布保留原参数错误，关系编译转换为定义错误；两个边界都拒绝重复键且不改输入。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Duplicate_nested_keys_preserve_publish_and_relation_compile_contracts(bool extensionObject)
    {
        var catalog=GeneratorTests.Catalog();var column=catalog.ConfigColumns.Single(column=>column.ColumnName=="name");
        const string duplicate="{\"tableId\":\"3\",\"tableId\":\"1\"}";
        if(extensionObject)column.Extra["relationOptions"]=JsonSerializer.Deserialize<JsonElement>(duplicate);else column.ControlDetail=duplicate;
        var source=JsonSerializer.Serialize(catalog);var publishError=Assert.Throws<ArgumentException>(()=>GenerationMetadataPublisher.Publish(catalog));
        Assert.Equal("key",publishError.ParamName);Assert.Contains("tableId",publishError.Message);
        var error=Assert.Throws<InvalidOperationException>(()=>GenerationMetadataPublisher.ForRelations(catalog));
        var original=Assert.IsType<ArgumentException>(error.InnerException);Assert.Equal("key",original.ParamName);Assert.Contains("tableId",original.Message);
        Assert.Contains("重复",error.Message);Assert.Equal(source,JsonSerializer.Serialize(catalog));
    }

    /// <summary>无敏感内容的JSON字面量保持原字符，不能把默认值或控件声明重新格式化。</summary>
    [Theory][InlineData("[\"9007199254750003\"]")][InlineData("[\n  \"9007199254750003\"\n]")][InlineData("{ \"ordered\" : [2,1], \"note\" : \"说明\" }")]
    public void Publish_preserves_unchanged_encoded_json_literals(string literal)
    {
        var c=GeneratorTests.Catalog();var column=c.ConfigColumns.Single(column=>column.ColumnName=="name");column.DefaultValue=literal;column.ControlDetail=literal;var before=JsonSerializer.Serialize(c);var published=GenerationMetadataPublisher.Publish(c).Metadata.ConfigColumns.Single(item=>item.Id==column.Id);
        Assert.Equal(literal,published.DefaultValue);Assert.Equal(literal,published.ControlDetail);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>保持原文不能豁免嵌套密码清理，原输入仍由调用方保有且不被发布器改写。</summary>
    [Fact]
    public void Encoded_json_still_removes_nested_secrets_without_mutating_source()
    {
        var c=GeneratorTests.Catalog();var column=c.ConfigColumns.Single(column=>column.ColumnName=="name");column.ControlDetail="{\"ordered\":[2,1],\"password\":\"private-value\",\"nested\":\"{\\\"password\\\":\\\"nested-private\\\",\\\"x\\\":1}\"}";var before=JsonSerializer.Serialize(c);var result=GenerationMetadataPublisher.Publish(c);using var document=JsonDocument.Parse(result.Metadata.ConfigColumns.Single(item=>item.Id==column.Id).ControlDetail);var value=document.RootElement;
        Assert.False(value.TryGetProperty("password",out _));Assert.Equal(new[]{2,1},value.GetProperty("ordered").EnumerateArray().Select(item=>item.GetInt32()));using var nested=JsonDocument.Parse(value.GetProperty("nested").GetString()!);Assert.False(nested.RootElement.TryGetProperty("password",out _));Assert.Equal(1,nested.RootElement.GetProperty("x").GetInt32());Assert.Equal(before,JsonSerializer.Serialize(c));Assert.Equal(2,result.RedactedPaths.Count(path=>path.Contains("password",StringComparison.OrdinalIgnoreCase)));
    }
    /// <summary>正式八模型中R0物理缓存名称仍要求原type=2证明，裁剪后的目录必须能初始化实际多值读计划。</summary>
    [Theory]
    [InlineData(0,0,"int64")][InlineData(0,0,"string")][InlineData(0,1,"int64")][InlineData(0,1,"string")]
    [InlineData(1,0,"int64")][InlineData(1,0,"string")][InlineData(1,1,"int64")][InlineData(1,1,"string")]
    public void Global_array_relation_snapshot_preserves_required_page_type(int targetMode,int sourceMode,string inner)
    {
        var c=new GlobalArrayTargetNativeModel(targetMode,sourceMode,inner).Catalog();var page=c.ConfigPages.Single(p=>p.Id=="10");
        page.Extra["layoutOnly"]=JsonSerializer.SerializeToElement("不得进入固定读计划");var before=JsonSerializer.Serialize(c);
        var published=GenerationMetadataPublisher.ForRelations(c);
        if(targetMode==0)Assert.Equal("2",published.ConfigPages.Single(p=>p.Id=="10").Extra.GetValueOrDefault("type").ToString());
        Assert.DoesNotContain(published.ConfigPages,p=>p.Extra.ContainsKey("layoutOnly"));
        var source=published.ConfigTables.Single(t=>t.Id=="3");var target=published.ConfigTables.Single(t=>t.Id=="1");var field=published.ConfigColumns.Single(f=>f.Id=="34");
        if(sourceMode==0)GlobalMultipleRelationTarget.Require(published,source,field,target);else GlobalMultipleRelationTarget.RequireCached(published,source,field,target);
        if(sourceMode==0)_=Metarill.Data.Queries.NonRedundantReads.Compile(published,"3",["id","global_source_global_target_names","selected_global_source_global_target_names"],deferCascadeNames:true);
        Assert.Equal(before,JsonSerializer.Serialize(c));
        if(targetMode==0){published.ConfigPages.Single(p=>p.Id=="10").Extra["type"]=JsonSerializer.SerializeToElement(1);Assert.Throws<NotSupportedException>(()=>{if(sourceMode==0)GlobalMultipleRelationTarget.Require(published,source,field,target);else GlobalMultipleRelationTarget.RequireCached(published,source,field,target);});}
    }

    /// <summary>直接开放原辅助名称文本筛选时，也必须解码已证明JSON名称，不能在中文转义文本上LIKE。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public async Task Explicit_cached_array_name_filters_use_json_members(int targetMode)
    {
        var c=GlobalMultipleCachedTargetGenerationTests.Catalog(1,false);c.ConfigTables.Single(t=>t.Id=="1").IsRongyu=targetMode;
        var model=GenerationModel.Create(c,"Example.ArrayNameText");var table=model.Tables.Single(t=>t.Source.Id=="3");var page=table.Pages.Single(p=>p.Source.Id=="30");
        Assert.Equal(2,page.Searchable.Count(column=>column.Source.Id is "35" or "40"));Assert.All(page.Searchable.Where(column=>column.Source.Id is "35" or "40"),column=>Assert.True(column.IsManagedMultiNames));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-array-name-text");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(c,output,"Example.ArrayNameText");var service=System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Path.Combine(output,"Services/CachedConsumerService.g.cs")),@"\s+","");
            foreach(var property in new[]{"GlobalSourceGlobalTargetNames","SelectedGlobalSourceGlobalTargetNames"})Assert.Contains("JsonArrayNameQuery.ApplyJsonArrayAnyLike(session,query,entity=>entity."+property+",",service);
            // 探针只在临时手写partial调用真实Query，不执行ADO，不替换真实生成分支。
            await File.WriteAllTextAsync(Path.Combine(output,"Services/NameFilterSqlProbe.cs"),"namespace Example.ArrayNameText.Generated.Services; public partial class CachedConsumerService { public string NameFilterSqlProbe(Metarill.Core.Contracts.PageRequest request,Metarill.Core.Security.RuntimeActor actor)=>Query30(request,actor).ToSql(); }");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(output);
            using var session=new Metarill.Data.DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=offline;Connection Timeout=1"});
            var mapper=Activator.CreateInstance(assembly.GetType("Example.ArrayNameText.Generated.Mappers.CachedConsumerMapper",true)!,session)!;
            var type=assembly.GetType("Example.ArrayNameText.Generated.Services.CachedConsumerService",true)!;var instance=Activator.CreateInstance(type,[session,mapper,new Metarill.Core.Security.SnowflakeIdGenerator(909),Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>(),null,null])!;
            var method=type.GetMethod("NameFilterSqlProbe")!;var actor=new Metarill.Core.Security.RuntimeActor(8,"离线SQL观测",true,"100","");const string keyword="改名_%!甲'";
            foreach(var key in new[]{"globalSourceGlobalTargetNames","selectedGlobalSourceGlobalTargetNames"})
            {
                var request=new Metarill.Core.Contracts.PageRequest{Filters=new(){{key,JsonSerializer.SerializeToElement(keyword)}}};var sql=Assert.IsType<string>(method.Invoke(instance,[request,actor]));
                Assert.Contains("JSON_SEARCH",sql);Assert.Contains("JSON_VALID",sql);Assert.Contains("@mt_json_name_",sql);Assert.Contains("`yz_project_id`",sql);Assert.Contains("`delete_no`",sql);Assert.DoesNotContain(keyword,sql);
                var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>method.Invoke(instance,[request,actor with{ProjectId="101"}]));Assert.IsType<UnauthorizedAccessException>(error.InnerException);
            }
            Assert.Null(session.CurrentTransaction);
        }
        finally{if(Directory.Exists(output))Directory.Delete(output,true);}
    }

}
