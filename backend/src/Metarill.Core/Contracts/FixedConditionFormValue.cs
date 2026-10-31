using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>只读取生成计划声明的表单叶值；不展开账号、表名、SQL或任意客户端查询配置。</summary>
public static class FixedConditionFormValue
{
    /// <summary>路径由生成器固定，重复键、缺失、对象和数组整体拒绝；显式null与缺失保持区别。</summary>
    public static JsonElement Read(JsonElement? form,IReadOnlyList<string> path,IReadOnlyList<string>? fallbackPath=null)
    {
        if(form is null||path.Count is <1 or >2)throw Invalid();
        // 原UI按表名缓存，原后端/调用方也有平铺槽；只在声明路径缺失时使用已编译的同一字段别名。
        var current=form.Value;
        if(fallbackPath is not null&&!HasPath(current,path))return Read(form,fallbackPath);

        foreach(var segment in path)
        {
            if(current.ValueKind!=JsonValueKind.Object)throw Invalid();
            var matches=current.EnumerateObject().Where(property=>property.NameEquals(segment)).ToArray();
            if(matches.Length!=1)throw Invalid();current=matches[0].Value;
        }
        if(current.ValueKind is JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array)throw Invalid();
        return current.Clone();
    }

    /// <summary>仅缺失触发别名；明确null、容器或重复键仍由原路径严格拒绝或绑定。</summary>
    private static bool HasPath(JsonElement current,IReadOnlyList<string> path)
    {
        foreach(var segment in path)
        {
            if(current.ValueKind!=JsonValueKind.Object)return true;
            var matches=current.EnumerateObject().Where(property=>property.NameEquals(segment)).ToArray();
            if(matches.Length==0)return false;if(matches.Length!=1)return true;current=matches[0].Value;
        }
        return true;
    }
    /// <summary>写入/导入的上下文只由生成Service建立，变量缺失时不得省略整个条件。</summary>
    public static object? Require(IReadOnlyDictionary<string,object?>? values,string variable)=>values is not null&&values.TryGetValue(variable,out var value)?value:throw Invalid();
    /// <summary>不把表单值或配置文字放进公开错误信息。</summary>
    private static ArgumentException Invalid()=>new("固定候选缺少明确的表单标量上下文。");
}
