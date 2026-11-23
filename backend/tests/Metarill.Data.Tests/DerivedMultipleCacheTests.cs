using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>同一派生树目标同时被R1自树和多选消费，数组必须有独立反向依赖和原预算。</summary>
public sealed class DerivedMultipleCacheTests
{
    /// <summary>四键共同验证单值／数组缓存同时进入权威，不得只发布有效自树而忽略多选。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public void Shared_derived_target_has_complete_single_and_array_closures(string type,string parent)
    {
        var c=Catalog(type,parent);var before=JsonSerializer.Serialize(c);var self=SelfTreeCache.Compile(c,"source","foreign");var multi=new RelationCacheDefinition("source","choices","tree","tree_name","choice_names",true);DerivedCrossTableCaches.Validate(c,[self,multi],[]);var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[self,multi]);Assert.Contains("tree_name",plan.MixedSourceColumns["ds_source"]);Assert.Contains("choice_names",plan.MixedSourceColumns["ds_source"]);Assert.Contains("ds_tree",plan.MixedTargets);Assert.Contains("ds_company",plan.MixedTargets);Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[multi]));Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>多值缓存按实际整数终点重算，跟踪中间映射和末级字段的更新。</summary>
    [Theory]
    [InlineData("MultiSelectPage","int32")]
    [InlineData("MultiCheckBoxDatabase","int64")]
    public void Derived_integer_cache_keeps_terminal_dependency_and_types(string kind,string type)
    {
        var catalog=Catalog("string","int64");
        var source=catalog.ConfigColumns.Single(item=>item.Id=="choices");source.ControlKind=kind;
        if(kind=="MultiCheckBoxDatabase")source.ControlDetail="{\"tableId\":\"tree\",\"pageId\":\"201\",\"nameField\":\"name\"}";
        var label=catalog.ConfigColumns.Single(item=>item.Id=="tree_name");
        label.LogicalType=type;label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.reference_code");
        catalog.ConfigColumns.Add(new(){Id="company_code",ConfigTableId="company",ColumnName="reference_code",LogicalType=type});
        // 本轮只验证数组消费者，移除同一个测试目录里的自树控件与缓存。
        catalog.ConfigColumns.RemoveAll(item=>item.Id is "foreign" or "cache");
        var binding=new RelationCacheDefinition("source","choices","tree","tree_name","choice_names",true);
        var plan=RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]);
        Assert.Contains("choice_names",plan.MixedSourceColumns["ds_source"]);
        Assert.Contains("ds_company",plan.MixedTargets);
        var refresh=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileRefresh(catalog,"source",["choice_names"],[binding]);
        Assert.Equal("string",refresh.Types["choice_names"]);
        Assert.Matches(@"CAST\(multi_name_\w+.`reference_code` AS CHAR\)",refresh.MultiProjections["choice_names"].NameExpression!);
        Assert.Contains(refresh.Reverse,item=>item.TargetTable=="ds_company"&&item.Outputs["choice_names"].Contains("reference_code"));
        catalog.ConfigColumns.Single(item=>item.Id=="company_code").LogicalType="string";
        Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(catalog,[],[binding]));
    }

    /// <summary>相邻高位 ID 必须按规范文本精确匹配，两个同表角色各自保留作用域与反向依赖。</summary>
    [Theory]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("string","string")]
    [InlineData("string","string",true)]
    public void Fixed_multihop_labels_freeze_candidate_and_cache_joins(string firstKey,string secondKey,bool repeatedScope=false)
    {
        var catalog=Catalog("string","int64");
        catalog.ConfigColumns.RemoveAll(column=>column.Id is "foreign" or "cache");
        var company=catalog.ConfigColumns.Single(column=>column.Id=="tree_company");
        company.LogicalType=firstKey;
        company.ControlKind="List";
        company.ControlDetail="{\"tableId\":\"company\",\"nameField\":\"id\"}";
        catalog.ConfigColumns.Add(new(){Id="supervisor",ConfigTableId="company",ColumnName="supervisor_id",LogicalType=secondKey,
            ControlKind="SelectPage",LinkConfigTableId="company",ControlDetail="{\"tableId\":\"company\",\"nameField\":\"id\"}"});
        var label=catalog.ConfigColumns.Single(column=>column.Id=="tree_name");
        label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.supervisor.caption");
        var binding=new RelationCacheDefinition("source","choices","tree","tree_name","choice_names",true);
        var choices=catalog.ConfigColumns.Single(column=>column.Id=="choices");
        if(repeatedScope)
            choices.ControlDetail="""{"pageId":"201","nameField":"name","whereSql":"yz_project_id = @current_yz_project_id"}""";
        DerivedCrossTableCaches.Validate(catalog,[binding],[]);

        var candidate=MySqlNonRedundantReadCompiler.Compile(catalog,"tree",["name"]);
        var fixedLabel=MySqlNonRedundantReadCompiler.CompileMultiLabel(catalog,"tree","name");
        Assert.Equal(candidate.From,fixedLabel.From);
        Assert.Contains("LEFT JOIN `ds_company` r0",candidate.From);
        Assert.Contains("LEFT JOIN `ds_company` r1",candidate.From);
        Assert.Contains("r0.`yz_project_id`=@project_scope_number",candidate.From);
        Assert.Contains("r1.`delete_no` IS NULL",candidate.From);
        Assert.Contains(firstKey=="string"?"BINARY CAST(r0.`id` AS CHAR)=BINARY b.`company_id`":"r0.`id`=b.`company_id`",candidate.From);
        Assert.Contains(secondKey=="string"?"BINARY CAST(r1.`id` AS CHAR)=BINARY r0.`supervisor_id`":"r1.`id`=r0.`supervisor_id`",candidate.From);
        Assert.Throws<NotSupportedException>(()=>MySqlNonRedundantReadCompiler.CompileMatch(catalog,"tree","name"));

        var refresh=MySqlNonRedundantReadCompiler.CompileRefresh(catalog,"source",["choice_names"],[binding]);
        var names=refresh.MultiProjections["choice_names"];
        Assert.Contains("LEFT JOIN `ds_company` multi_name_0",names.Joins);
        Assert.Contains("LEFT JOIN `ds_company` multi_name_1",names.Joins);
        if(repeatedScope)
        {
            Assert.Contains("multi_value.`yz_project_id`=@project_scope_number",names.TargetScope);
            choices.ControlDetail="""{"pageId":"201","nameField":"name","whereSql":"company_id=99"}""";
            Assert.Throws<NotSupportedException>(()=>DerivedCrossTableCaches.Validate(catalog,[binding],[]));
        }
        Assert.Contains(refresh.Reverse,item=>item.TargetTable=="ds_company"&&item.Outputs["choice_names"].Contains("supervisor_id"));
        Assert.Contains(refresh.Reverse,item=>item.TargetTable=="ds_company"&&item.Outputs["choice_names"].Contains("caption"));
    }

    /// <summary>数组配置及派生终点必须精确；不能让任意页、缓存或未知SQL成为传播依据。</summary>
    [Theory][InlineData("page")][InlineData("type")][InlineData("where")][InlineData("duplicate")][InlineData("cache")][InlineData("name")][InlineData("foreign")][InlineData("credential")]
    public void Broken_multi_cache_never_publishes_partial_authority(string kind)
    {
        var c=Catalog("string","int64");var foreign=c.ConfigColumns.Single(x=>x.Id=="choices");var d=new RelationCacheDefinition("source","choices","tree","tree_name","choice_names",true);if(kind=="page")foreign.ControlDetail="{\"pageId\":\"missing\",\"nameField\":\"name\"}";if(kind=="type")c.ConfigPages[0].Extra["type"]=JsonSerializer.SerializeToElement(1);if(kind=="where")foreign.ControlDetail=foreign.ControlDetail.Replace("{","{\"whereSql\":\"1=1\",",StringComparison.Ordinal);if(kind=="duplicate")foreign.ControlDetail="{\"pageId\":\"201\",\"pageId\":\"201\"}";if(kind=="cache")c.ConfigColumns.Single(x=>x.Id=="choice_names").ColumnName="unrelated_names";if(kind=="name")foreign.ControlDetail=foreign.ControlDetail.Replace("nameField\":\"name","nameField\":\"unrelated");if(kind=="foreign")foreign.LogicalType="int64";if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="company_name").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="tree_name").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}Assert.ThrowsAny<Exception>(()=>RelationMaintenanceCatalog.CompileWithDerivedCrossTables(c,[],[d]));
    }
    /// <summary>沿自树最小目录增加原type=2页和独立数组；没有业务库操作。</summary>
    internal static MetadataCatalog Catalog(string type,string parent)
    {
        var c=DerivedSelfTreeCacheTests.Catalog(type,parent);c.ConfigColumns.AddRange([new(){Id="choices",ConfigTableId="source",ColumnName="choice_ids",LogicalType="string",ColumnLength="max",ControlKind="MultiSelectPage",LinkConfigTableId="tree",ControlDetail="{\"pageId\":\"201\",\"nameField\":\"name\"}"},new(){Id="choice_names",ConfigTableId="source",ColumnName="choice_names",LogicalType="string",ColumnLength="max"}]);c.ConfigPages.Add(new(){Id="201",ConfigTableId="tree",WhereSql="yz_project_id=@current_yz_project_id",Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});c.ConfigPageColumns.Add(new(){Id="201name",ConfigPageId="201",ConfigColumnId="tree_name"});return c;
    }
}
