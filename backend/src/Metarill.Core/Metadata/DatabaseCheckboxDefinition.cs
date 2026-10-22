using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>原数据库复选的候选配置；只解析结构，不接受请求SQL或修改发布元数据。</summary>
public sealed record DatabaseCheckboxDefinition(string TableId,string Table,string PageId,string WhereSql,string OrderBy,string LegacyTargetHint)
{
    /// <summary>空定义、旧目标字面值、四段及JSON格式共用解释；四段前两项不参与原候选查询。</summary>
    public static DatabaseCheckboxDefinition Parse(string? text)
    {
        text=text?.Trim()??"";
        if(text.Length==0)return new("","","","","","");
        if(!text.StartsWith('{'))
        {
            var parts=text.Split(';');
            if(parts.Length==4)return new("","","",parts[2].Trim(),parts[3].Trim(),"");
            if(parts.Length!=1)throw new InvalidOperationException("数据库复选旧配置必须为空、目标字面值或四段格式，不能忽略候选条件。");
            MetadataValidation.RequireIdentifier(text);return new("","","","","",text);
        }
        try
        {
            using var document=JsonDocument.Parse(text);var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=root.EnumerateObject().Count())throw new InvalidOperationException("数据库复选配置必须为无重复键对象。");
            var known=new[]{"tableId","table","pageId","nameField","whereSql","orderBy"};
            foreach(var field in root.EnumerateObject())
                if(!known.Contains(field.Name,StringComparer.Ordinal)&&field.Name is not("buttonText" or "placeholder" or "description" or "futureOption")||known.Any(name=>name.Equals(field.Name,StringComparison.OrdinalIgnoreCase)&&name!=field.Name))throw new InvalidOperationException("数据库复选配置包含未证明的查询或大小写映射，不能退化为宽候选。");
            string Read(string name)
            {
                if(!root.TryGetProperty(name,out var value)||value.ValueKind==JsonValueKind.Null)return "";
                return value.ValueKind==JsonValueKind.String?value.GetString()!:value.ValueKind==JsonValueKind.Number&&name is "tableId" or "pageId"&&value.TryGetInt64(out var id)?id.ToString(System.Globalization.CultureInfo.InvariantCulture):throw new InvalidOperationException("数据库复选已知配置字段必须为文本。");
            }
            var label=Read("nameField");if(label.Length>0&&label!="name")throw new NotSupportedException("数据库复选当前名称固定为name，其它名称用途尚待编译。");
            var table=Read("table");if(table.Length>0)MetadataValidation.RequireIdentifier(table);
            return new(Read("tableId"),table,Read("pageId"),Read("whereSql"),Read("orderBy"),"");
        }
        catch(JsonException error){throw new InvalidOperationException("数据库复选配置不是有效JSON。",error);}
    }
    /// <summary>仅供固定读取编译，名称和候选范围各走自己的静态用途；旧目标提示仅在没有明确绑定时使用。</summary>
    public JsonElement ReadDefinition(ColumnMetadata source)
    {
        var table=Table;
        if(table.Length==0&&source.LinkConfigTableId.Length==0&&string.IsNullOrWhiteSpace(source.Extra.GetValueOrDefault("joinTable").ToString()))table=LegacyTargetHint;
        return JsonSerializer.SerializeToElement(new{tableId=TableId,table,pageId=PageId,nameField="name",whereSql=WhereSql,orderBy=OrderBy});
    }
}
