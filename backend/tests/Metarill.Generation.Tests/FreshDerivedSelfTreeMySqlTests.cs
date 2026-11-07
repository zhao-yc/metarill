using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实Production/Web下验证五控件混合与派生树，不注入开发身份或固定授权器。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class FreshDerivedSelfTreeMySqlTests
{
    /// <summary>沿既有精确双库租约，显式与自动名称分别从空库安装并重启正式宿主。</summary>
    [FreshDerivedMySqlTheory]
    [InlineData(false,"DerivedDeclaredDatabase","derived_declared",966)]
    [InlineData(true,"DerivedAutomaticDatabase","derived_auto",967)]
    public Task Mixed_tree_and_derived_relations_use_real_identity_and_fresh_database(bool automatic,string property,string suffix,int worker)=>FreshDerivedRelationsMySqlTests.Exercise(automatic,property,suffix,worker,true);
}
