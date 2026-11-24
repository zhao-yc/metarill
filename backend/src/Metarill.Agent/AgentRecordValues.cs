using Metarill.Core.Contracts;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Metarill.Runtime.Agent;

/// <summary>按钮与普通记录写只取固定字段；不序列化整行，不读未发布getter或未装载派生默认值。</summary>
internal static class AgentRecordValues
{
    private static readonly ConcurrentDictionary<Type,IReadOnlyDictionary<string,PropertyInfo>> Properties=new();
    /// <summary>别名来自冻结目录，属性必须来自已验证的实际生成实体。</summary>
    internal static IReadOnlyDictionary<string,object?> Read(object record,Type entityType,IReadOnlyDictionary<string,string> names,
        IEnumerable<string> fields,IReadOnlyDictionary<string,bool> arrayColumns)
    {
        var needed=fields.Select(field=>field.Split('.')[0]).Distinct(StringComparer.Ordinal).ToArray();var values=new Dictionary<string,object?>(StringComparer.Ordinal);
        if(!needed.Any(names.ContainsKey))return values;
        var properties=Properties.GetOrAdd(entityType,static type=>
        {
            var result=new Dictionary<string,PropertyInfo>(StringComparer.Ordinal);
            foreach(var property in type.GetProperties(BindingFlags.Instance|BindingFlags.Public))
            {
                if(property.GetMethod is not{IsPublic:true}||property.GetIndexParameters().Length!=0||property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)continue;
                var name=property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;if(name is null)continue;
                var physical=property.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>()?.Name;
                if(!result.TryAdd(string.IsNullOrEmpty(physical)?name:physical,property))throw new InvalidOperationException("记录的生成字段绑定存在歧义。");
            }
            return result;
        });
        foreach(var name in needed)
            if(names.TryGetValue(name,out var physical))
            {
                if(!properties.TryGetValue(physical,out var property))throw new InvalidOperationException("规则字段缺少实际生成属性，请成套再生成。");
                if(record is IGeneratedRuleRecord loaded)
                {
                    if(loaded.TryGetLoadedRuleValue(physical,out var value)){values[name]=value;continue;}
                    if(loaded.RequiresLoadedRuleValue(physical))throw new NotSupportedException("规则引用尚未装载的派生字段，未执行写入。");
                }
                if(property.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>()?.IsIgnore==true)throw new NotSupportedException("规则引用尚未装载的派生字段，未执行写入。");
                var stored = property.GetValue(record);
                // 物理数组取和原页面相同的类型；已装载的派生规则值由生成读取计划提供。
                values[name] = stored is string text && arrayColumns.TryGetValue(physical, out var legacyMedia)
                    ? legacyMedia ? LegacyMediaValue.Project(text) : RequiredFieldValue.VisibilityArray(text)
                    : stored;
            }
        return values;
    }

    /// <summary>只读按详情回显解释日期；点路径只解码已发布 JSON 列。普通按钮显示仍使用直接装载的值。</summary>
    internal static IReadOnlyDictionary<string,object?> ForReadOnly(IReadOnlyDictionary<string,object?> values,
        IReadOnlyDictionary<string,string> names, IEnumerable<string> fields, AgentPublishedPage page, JsonSerializerOptions options)
    {
        Dictionary<string,object?>? result = null;
        foreach (var group in fields.GroupBy(field => field.Split('.')[0],StringComparer.Ordinal))
        {
            var root = group.Key;
            if (!names.TryGetValue(root,out var physical) || !values.TryGetValue(root,out var value)) continue;

            object? projected;
            if (value is DateTime calendar)
            {
                // 月份在 VO 上有专用转换器，其他日历值使用当前宿主的实际 MVC 响应合同。
                projected = page.RecordMonthColumns.Contains(physical)
                    ? CalendarColumnDefault.Format(calendar,dateOnly:true)
                    : JsonSerializer.SerializeToElement(calendar,options).GetString();
            }
            else if (page.RecordJsonColumns.Contains(physical) && group.Any(field => field.Contains('.')))
            {
                var text = value switch { string scalar => scalar, JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(), _ => null };
                if (text is null) continue;
                projected = RequiredFieldValue.VisibilityObject(text);
            }
            else continue;

            result ??= new(values,StringComparer.Ordinal);
            result[root] = projected;
        }
        return result ?? values;
    }
}
