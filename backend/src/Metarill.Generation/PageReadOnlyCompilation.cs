using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

/// <summary>详情只读条件的静态表达式及最小字段依赖；不把原元数据文本直接插入Vue脚本。</summary>
internal sealed partial record PageReadOnlyCompilation(string JavaScript,IReadOnlyList<ColumnModel> Dependencies)
{
    /// <summary>只绑定本页真实可读字段；嵌套路径必须有明确JSON对象来源，歧义或未知字段拒绝生成。</summary>
    internal static PageReadOnlyCompilation? Compile(PageModel page)
    {
        if(!page.Source.Extra.TryGetValue("readOnlyCondition",out var value)||value.ValueKind==JsonValueKind.Null)return null;
        if(value.ValueKind!=JsonValueKind.String)throw new InvalidOperationException("页面只读条件必须为字符串："+page.Source.Id);
        var source=value.GetString();if(string.IsNullOrWhiteSpace(source))return null;
        ReadOnlyConditionRule rule;
        try{rule=ReadOnlyConditionRule.Compile(source);}catch(ArgumentException error){throw new InvalidOperationException("页面只读条件不是已支持的受限表达式："+page.Source.Id,error);}
        var columns=new Dictionary<string,ColumnModel>(StringComparer.Ordinal);
        foreach(var field in rule.Fields)
        {
            var root=field.Split('.')[0];if(columns.ContainsKey(root))continue;
            var matches=page.Readable.Where(column=>column.JsonName==root||column.Source.ColumnName==root).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("只读条件必须引用本页唯一真实可读字段："+page.Source.Id);
            columns[root]=matches[0];
        }
        foreach(var field in rule.Fields.Where(field=>field.Contains('.')))
            if(!columns[field.Split('.')[0]].Source.LogicalType.Equals("json",StringComparison.OrdinalIgnoreCase)||columns[field.Split('.')[0]].IsJsonArray)
                throw new InvalidOperationException("只读条件嵌套路径必须引用已声明的JSON对象字段："+page.Source.Id);
        var javascript=rule.ToJavaScript(field=>
        {
            var parts=field.Split('.');var name=columns[parts[0]].JsonName;
            var result=PlainProperty().IsMatch(name)?"this.entity."+name:"this.entity["+JsonSerializer.Serialize(name)+"]";
            return result+string.Concat(parts.Skip(1).Select(part=>"["+JsonSerializer.Serialize(part)+"]"));
        });
        return new(javascript,Array.AsReadOnly(columns.Values.Distinct().ToArray()));
    }
    /// <summary>已有普通字段继续保持原点号访问；其它合法JSON属性采用固定转义括号，不能生成脚本语法。</summary>
    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$",RegexOptions.CultureInvariant)]private static partial Regex PlainProperty();
}
