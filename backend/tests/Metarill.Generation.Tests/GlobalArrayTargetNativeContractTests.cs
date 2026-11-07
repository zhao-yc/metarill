using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>准备前核对八个真实目录的完整闭包，不能以缺少名称、条件或维护节点换取可构建。</summary>
public sealed class GlobalArrayTargetNativeContractTests
{
    /// <summary>八库、八worker和生产者／来源／键三维一一对应，原复选独立条件与Excel模式均保留。</summary>
    [Fact]
    public void Eight_models_have_complete_generation_and_distinct_scope()
    {
        Assert.Equal(8,GlobalArrayTargetNativeModel.All.Count);Assert.Equal(8,GlobalArrayTargetNativeModel.All.Select(model=>model.Suffix).Distinct().Count());Assert.Equal(8,GlobalArrayTargetNativeModel.All.Select(model=>model.Worker).Distinct().Count());
        foreach(var item in GlobalArrayTargetNativeModel.All)
        {
            var c=item.Catalog();var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,GlobalArrayTargetNativePreparation.RootNamespace);Assert.Empty(GenerationCapabilityAudit.Collect(model));var table=model.Tables.Single(t=>t.Source.Id=="3");var page=table.Pages.Single(p=>p.Source.Id=="30");var multi=Assert.Single(MultiSelectCompilation.ForPage(model,table,page));var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page));Assert.Equal("10",multi.TargetPage.Source.Id);Assert.True(checkbox.Conditions is not null);Assert.Equal(item.TargetMode==0,checkbox.DerivedName);Assert.Equal(item.InnerType,c.ConfigColumns.Single(field=>field.Id=="13").LogicalType);
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);Assert.Equal(item.TargetMode==1,maintenance.MixedSourceColumns.ContainsKey("global_source"));if(item.SourceMode==1){Assert.True(RelationPropagationCompilation.Supports(model,table,multi.Source));Assert.True(RelationPropagationCompilation.Supports(model,table,checkbox.Source));}Assert.Equal(before,JsonSerializer.Serialize(c));
        }
    }
    /// <summary>顶层重复批准字段不能依赖JSON最后一个属性覆盖语义；无效输入不创建目录。</summary>
    [Fact]
    public void Duplicate_approval_or_invalid_model_is_rejected_before_io()
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-array-duplicate",Guid.NewGuid().ToString("N"));var proofs=new JsonObject();foreach(var item in GlobalArrayTargetNativeModel.All)proofs[item.Suffix]=new JsonObject{{"PackageRoot",Path.Combine(root,item.Suffix)}};var input=new JsonObject{{"Purpose",GlobalArrayTargetNativeLease.Purpose},{"AuthorizationStatus","approved"},{"LeaseStatus","running"},{"ProductCommit",GlobalArrayTargetNativePreparation.ProductCommit},{"RunId","0123456789abcdef"},{"ArrayTargetPreparation",proofs}};
        using var document=JsonDocument.Parse(input.ToJsonString().Replace("\"AuthorizationStatus\":","\"AuthorizationStatus\":\"pending\",\"AuthorizationStatus\":",StringComparison.Ordinal));Assert.Throws<InvalidOperationException>(()=>GlobalArrayTargetNativeLease.Validate(document.RootElement,root));Assert.Throws<InvalidOperationException>(()=>new GlobalArrayTargetNativeModel(2,0,"int64").Catalog());Assert.Throws<InvalidOperationException>(()=>new GlobalArrayTargetNativeModel(0,0,"decimal").Catalog());Assert.False(Directory.Exists(root));
    }
}
