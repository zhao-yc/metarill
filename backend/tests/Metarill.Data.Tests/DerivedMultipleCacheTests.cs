using System.Text.Json;
using Metarill.Core.Metadata;
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
