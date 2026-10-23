namespace Metarill.Data.Queries;

/// <summary>目标页由生成器固定选择；常量计划不可变，不从HTTP请求提供SQL、页面或字段。</summary>
public sealed record NonRedundantCrossTableTargetScope(string PageId,FixedConditionSqlPlan? Condition=null);

/// <summary>固定跨表来源与各层真实目标页绑定；它不建立身份或授予目标动作权限。</summary>
public sealed class NonRedundantCrossTableScope
{
    /// <summary>复制层序，后续修改调用者数组不能改变已发布的路径计划。</summary>
    public NonRedundantCrossTableScope(string sourceColumnId,IReadOnlyList<NonRedundantCrossTableTargetScope> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if(string.IsNullOrWhiteSpace(sourceColumnId)||levels.Count<2||levels.Any(level=>level is null||string.IsNullOrWhiteSpace(level.PageId)))throw new ArgumentException("跨表读取必须固定来源和完整目标页层序。");
        SourceColumnId=sourceColumnId;Levels=Array.AsReadOnly(levels.ToArray());
    }
    public string SourceColumnId{get;}
    public IReadOnlyList<NonRedundantCrossTableTargetScope> Levels{get;}
}
