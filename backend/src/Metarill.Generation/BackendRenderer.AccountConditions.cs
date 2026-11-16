using System.Text;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal static partial class BackendRenderer
{
    /// <summary>新增时补齐省略的所属字段，显式输入和手写钩子仍须通过同一个账号范围。</summary>
    private static string AccountScopeDefaults(TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var (column,value) in AccountScopeFields(table,page).Where(scope=>!scope.Column.IsId))
            result.AppendLine($"        if (!submitted.Contains({Literal(column.Source.ColumnName)})) entity.{column.Property} = {value};");
        return result.ToString();
    }

    /// <summary>提交前的最终实体必须仍归属当前账号，防止客户端或写入钩子迁出当前页面的账号范围。</summary>
    private static string AccountScopeCheck(TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var (column,value) in AccountScopeFields(table,page))
            result.AppendLine($"        if (entity.{column.Property} != {value}) throw new UnauthorizedAccessException(\"记录不属于当前账号的数据范围。\");");
        return result.ToString();
    }

    /// <summary>页面条件在生成时已经验证物理列和变量；这里沿用字符串或整数的固定转换。</summary>
    private static IEnumerable<(ColumnModel Column,string Value)> AccountScopeFields(TableModel table,PageModel page)
    {
        if(!FixedPageConditionCompilation.HasAccountVariables(page))yield break;
        foreach(var term in page.Condition!.Syntax.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
        {
            var column=table.Columns.Single(column=>column.Source.ColumnName==term.Field);
            var value="global::Metarill.Core.Security.AccountConditionVariables.Require(actor, "+Literal(term.Values[0].Text)+")";
            yield return (column,ScopeValue(column,value));
        }
    }
}
