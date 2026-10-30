using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Core.Metadata;

/// <summary>原详情分组定义；保存、复制及生成共用同一结构约束，不依赖前端或数据库供应商。</summary>
public sealed record DetailLayoutConfiguration(int Version,string Mode,IReadOnlyList<DetailLayoutGroupConfiguration> Groups)
{
    /// <summary>旧页面空配置沿用常显；非空存储必须是完整版本协议，解析失败不能降级为无分组。</summary>
    public static DetailLayoutConfiguration Parse(string? stored)
    {
        if(string.IsNullOrWhiteSpace(stored))return new(1,"none",Array.Empty<DetailLayoutGroupConfiguration>());
        try{using var document=JsonDocument.Parse(stored);return Parse(document.RootElement);}
        catch(JsonException error){throw new InvalidOperationException("详情布局JSON格式无效。",error);}
    }

    /// <summary>限制与原YZP一致的64字符机器标识，拒绝重复/未知成员，保留关闭时的分组定义。</summary>
    public static DetailLayoutConfiguration Parse(JsonElement value)
    {
        Object(value,"version","mode","groups");
        if(!value.TryGetProperty("version",out var version)||version.ValueKind!=JsonValueKind.Number||!version.TryGetInt32(out var number)||number!=1)
            throw new InvalidOperationException("详情布局仅支持version=1。");
        var mode=Text(value,"mode");if(mode is not ("none" or "tabs"))throw new InvalidOperationException("详情布局模式无效。");
        if(!value.TryGetProperty("groups",out var groups)||groups.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("详情布局标签集合不能为空或省略。");
        var keys=new HashSet<string>(StringComparer.Ordinal);var names=new HashSet<string>(StringComparer.Ordinal);var result=new List<DetailLayoutGroupConfiguration>();
        foreach(var group in groups.EnumerateArray())
        {
            Object(group,"key","name");var key=Text(group,"key");var name=Text(group,"name").Trim();
            if(!Regex.IsMatch(key,"\\A[A-Za-z][A-Za-z0-9_-]{0,63}\\z",RegexOptions.CultureInvariant)||!keys.Add(key))
                throw new InvalidOperationException("详情布局分组标识必须唯一、以字母开头且最多64字符。");
            if(name.Length is 0 or >100||!names.Add(name))throw new InvalidOperationException("详情布局分组名称不能为空、重复或超过100字符。");
            result.Add(new(key,name));
        }
        return new(1,mode,result.AsReadOnly());
    }

    /// <summary>常显使用空归属；关闭标签仍须保留有效引用，不能把未知组悄悄当常显。</summary>
    public void RequireGroup(string? key)
    {
        if(!string.IsNullOrEmpty(key)&&!Groups.Any(group=>group.Key==key))
            throw new InvalidOperationException("详情布局字段引用了未定义的分组："+key);
    }

    /// <summary>结构成员只允许声明属性，重复键不得由JSON最后值覆盖掩盖。</summary>
    private static void Object(JsonElement value,params string[] allowed)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("详情布局定义必须是对象。");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())
            if(!seen.Add(property.Name)||!allowed.Contains(property.Name,StringComparer.Ordinal))throw new InvalidOperationException("详情布局包含重复或不允许的字段。");
    }

    /// <summary>字符串不隐式转换数字、布尔或null，保存与生成使用同一合同。</summary>
    private static string Text(JsonElement value,string name)
    {
        if(!value.TryGetProperty(name,out var property)||property.ValueKind!=JsonValueKind.String)
            throw new InvalidOperationException("详情布局缺少字符串字段："+name);
        return property.GetString()!;
    }
}

/// <summary>稳定机器标识与显示名称分离，标签改名不改变字段引用。</summary>
public sealed record DetailLayoutGroupConfiguration(string Key,string Name);
