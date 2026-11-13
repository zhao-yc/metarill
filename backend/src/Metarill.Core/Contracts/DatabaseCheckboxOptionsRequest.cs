using System.Text.Json;

namespace Metarill.Core.Contracts;

/// <summary>原复选候选只接parentId、formParameters、可选id；未知键和重复键在读取数据库前拒绝。</summary>
public sealed record DatabaseCheckboxOptionsRequest(string? ParentId,JsonElement? FormParameters,long[]? Selected)
{
    /// <summary>未提交id查询完整有界候选；明确null或空数组只能回读空候选。</summary>
    public static DatabaseCheckboxOptionsRequest Read(JsonElement? request)
    {
        if(request is null||request.Value.ValueKind==JsonValueKind.Null)return new(null,null,null);
        var body=request.Value;if(body.ValueKind!=JsonValueKind.Object||body.GetRawText().Length>262144)throw new ArgumentException("数据库复选候选请求必须为有界对象。");
        string? parent=null;JsonElement? form=null;long[]? ids=null;var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in body.EnumerateObject())
        {
            if(!seen.Add(field.Name))throw new ArgumentException("数据库复选候选字段重复。");
            switch(field.Name)
            {
                case "parentId":parent=field.Value.ValueKind==JsonValueKind.Null?null:field.Value.ValueKind==JsonValueKind.String?field.Value.GetString():throw new ArgumentException("parentId必须是字符串或null。");break;
                case "formParameters":form=ReadForm(field.Value);break;
                case "id":ids=DatabaseCheckboxValue.ReadSelected(field.Value);break;
                default:throw new ArgumentException("数据库复选候选未开放请求字段："+field.Name);
            }
        }
        return new(parent,form,ids);
    }
    /// <summary>原缓存兼容对象或一层编码JSON对象，空槽保持缺失；不递归解码或放宽标量。</summary>
    private static JsonElement? ReadForm(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Null)return null;
        if(value.ValueKind==JsonValueKind.String)
        {
            var text=value.GetString();if(string.IsNullOrWhiteSpace(text))return null;
            try{using var document=JsonDocument.Parse(text);value=document.RootElement.Clone();}catch(JsonException error){throw new ArgumentException("表单上下文编码JSON无效。",error);}
        }
        if(value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("表单上下文必须为原对象槽或编码对象文本。");return value.Clone();
    }
}
