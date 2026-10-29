using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;

namespace Metarill.Runtime.Agent;

/// <summary>Web中央入口与Agent共同消费发布显示规则；只读取正式生成实体中规则实际需要的属性。</summary>
internal static class AgentButtonVisibility
{
    private static readonly ConcurrentDictionary<Type,IReadOnlyDictionary<string,PropertyInfo>> Properties=new();
    /// <summary>真实记录范围证明完成后比较，客户端实体/visibleRuleJson不参与；不会将记录值输出到错误中。</summary>
    internal static void Require(AgentPublishedButton button,object record,Type entityType)
    {
        if(button.Rule is not{} rule)return;
        var needed=rule.Fields.Concat(button.ReadOnlyRule?.Fields??[]).Select(field=>field.Split('.')[0]).Distinct(StringComparer.Ordinal).ToArray();
        var values=new Dictionary<string,object?>(StringComparer.Ordinal);
        if(needed.Any(name=>button.RecordNames.ContainsKey(name)))
        {
            var properties=Properties.GetOrAdd(entityType,static type=>
            {
                var result=new Dictionary<string,PropertyInfo>(StringComparer.Ordinal);
                foreach(var property in type.GetProperties(BindingFlags.Instance|BindingFlags.Public))
                {
                    if(property.GetMethod is not{IsPublic:true}||property.GetIndexParameters().Length!=0||property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)continue;
                    var name=property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
                    if(name is null)continue;
                    var physical=property.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>()?.Name;
                    if(!result.TryAdd(string.IsNullOrEmpty(physical)?name:physical,property))throw new InvalidOperationException("业务记录的生成字段绑定存在歧义。");
                }
                return result;
            });
            foreach(var name in needed)
                if(button.RecordNames.TryGetValue(name,out var json))
                {
                    if(!properties.TryGetValue(json,out var property))throw new InvalidOperationException("按钮规则字段缺少实际生成属性，请成套再生成。");
                    if(property.GetCustomAttribute<FreeSql.DataAnnotations.ColumnAttribute>()?.IsIgnore==true)
                        throw new NotSupportedException("按钮规则引用尚未装载的派生字段，未执行业务。");
                    values[name]=property.GetValue(record);
                }
        }
        if(button.ReadOnlyRule is{} readOnly&&readOnly.IsReadOnly(values))throw new BusinessRuleException("当前记录为只读，不能执行可写按钮");
        if(!rule.Matches(values))throw new BusinessRuleException("当前记录不允许执行该按钮");
    }
}
