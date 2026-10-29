using Metarill.Core.Contracts;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
namespace Metarill.Runtime.Agent;

/// <summary>按钮与普通记录写只取固定字段；不序列化整行，不读未发布getter或未装载派生默认值。</summary>
internal static class AgentRecordValues
{
    private static readonly ConcurrentDictionary<Type,IReadOnlyDictionary<string,PropertyInfo>> Properties=new();
    /// <summary>别名来自冻结目录，属性必须来自已验证的实际生成实体。</summary>
    internal static IReadOnlyDictionary<string,object?> Read(object record,Type entityType,IReadOnlyDictionary<string,string> names,IEnumerable<string> fields)
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
                if(property.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>()?.IsIgnore==true)
                {
                    if(record is not IGeneratedRuleRecord loaded||!loaded.TryGetLoadedRuleValue(physical,out var value))throw new NotSupportedException("规则引用尚未装载的派生字段，未执行写入。");
                    values[name]=value;continue;
                }
                values[name]=property.GetValue(record);
            }
        return values;
    }
}
