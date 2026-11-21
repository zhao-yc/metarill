using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

/// <summary>详情只读条件的静态表达式及最小字段依赖；不把原元数据文本直接插入Vue脚本。</summary>
internal sealed partial record PageReadOnlyCompilation(string JavaScript,IReadOnlyList<ColumnModel> Dependencies,bool UsesObjectPaths,bool UsesIntegerComparisons,bool UsesBooleanOptions)
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
        var paths=rule.Fields.Where(field=>field.Contains('.')).Select((field,index)=>(Field:field,Local:"path"+index)).ToArray();
        var booleans = rule.Fields.Where(field => !field.Contains('.') && BackendRenderer.UsesBooleanOptionWireFormat(columns[field]))
            .Select((field, index) => (Field: field, Local: "booleanOption" + index)).ToArray();
        var integers=false;
        var javascript=rule.ToJavaScript(field=>
        {
            if(field.Contains('.'))return paths.Single(path=>path.Field==field).Local;
            if (BackendRenderer.UsesBooleanOptionWireFormat(columns[field]))
                return booleans.Single(option => option.Field == field).Local;
            return EntityAccess(columns[field].JsonName);
        }, field=>!field.Contains('.')&&columns[field].Type is ("long" or "long?"), (operation,left,right)=>
        {
            integers=true;
            return "this.originalReadOnlyCompare("+JsonSerializer.Serialize(operation)+","+left+","+right+")";
        });
        if(paths.Length>0)
        {
            // 已保存记录缺少条件值时维持只读；新增仍能输入对象，显式 null 保留原比较语义。
            var parameters=string.Join(",",paths.Select(path=>path.Local));
            var missing=string.Join(" || ",paths.Select(path=>path.Local+" === undefined"));
            var bindings=paths.Select(path=>
            {
                var parts=path.Field.Split('.');
                return "this.originalReadOnlyPath("+JsonSerializer.Serialize(columns[parts[0]].JsonName)+","+JsonSerializer.Serialize(parts[1..])+")";
            });
            javascript="(("+parameters+") => (Boolean(this.id) && ("+missing+")) || ("+javascript+"))("+string.Join(",",bindings)+")";
        }
        if (booleans.Length > 0)
        {
            // 按底层布尔类型比较。先读取全部依赖，使无效回显不能被短路分支忽略。
            var parameters = string.Join(",", booleans.Select(option => option.Local));
            var bindings = booleans.Select(option => "this.originalReadOnlyBoolean(" + EntityAccess(columns[option.Field].JsonName) + ")");
            javascript = "((" + parameters + ") => (" + javascript + "))(" + string.Join(",", bindings) + ")";
        }
        if (integers || booleans.Length > 0) javascript = "this.originalCheckedReadOnly(() => (" + javascript + "))";
        return new(javascript,Array.AsReadOnly(columns.Values.Distinct().ToArray()),paths.Length>0,integers,booleans.Length>0);
    }

    private static string EntityAccess(string name) =>
        PlainProperty().IsMatch(name) ? "this.entity." + name : "this.entity[" + JsonSerializer.Serialize(name) + "]";
    /// <summary>已有普通字段继续保持原点号访问；其它合法JSON属性采用固定转义括号，不能生成脚本语法。</summary>
    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$",RegexOptions.CultureInvariant)]private static partial Regex PlainProperty();
}
