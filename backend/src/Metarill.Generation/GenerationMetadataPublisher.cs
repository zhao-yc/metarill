using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

public sealed record PublishedGenerationMetadata(MetadataCatalog Metadata, IReadOnlyList<string> RedactedPaths);

/// <summary>独立包和离线请求共享同一发布脱敏规则；不读取环境文件，也不按普通说明文本猜测凭据。</summary>
public static class GenerationMetadataPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };

    /// <summary>复制完整元数据后清除敏感配置与密码默认值，字段定义和原始输入保持不变。</summary>
    public static PublishedGenerationMetadata Publish(MetadataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var json = JsonNode.Parse(JsonSerializer.Serialize(catalog, Json))!;
        var redactions = new List<string>();
        Scrub(json, "$", redactions);
        var sensitiveColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in json["configColumns"]?.AsArray() ?? [])
        {
            if (column is not JsonObject item || !IsSensitiveKey(item["columnName"]?.ToString() ?? "")) continue;
            sensitiveColumns.Add(item["id"]?.ToString() ?? "");
            foreach (var key in new[] { "defaultValue", "initValue" }) if (item[key] is not null) { item[key] = ""; redactions.Add("$.configColumns[" + item["id"] + "]." + key); }
        }
        foreach (var field in json["configPageColumns"]?.AsArray() ?? [])
            if (field is JsonObject item && sensitiveColumns.Contains(item["configColumnId"]?.ToString() ?? ""))
                foreach (var key in new[] { "defaultValue", "initValue" }) if (item[key] is not null) { item[key] = ""; redactions.Add("$.configPageColumns[" + item["id"] + "]." + key); }
        return new(json.Deserialize<MetadataCatalog>(Json)!, redactions.Distinct().Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>关系执行目录先脱敏，再去掉品牌、菜单及布局；纯配置更新不因此强制更新后端生成代码。</summary>
    internal static MetadataCatalog ForRelations(MetadataCatalog catalog)
    {
        // 关系编译维持生成器的定义错误合同；直接发布保留原ArgumentException及原门禁边界。
        MetadataCatalog published;
        try { published=Publish(catalog).Metadata; }
        catch (ArgumentException error) { throw new InvalidOperationException("关系元数据含重复或无效的JSON属性。", error); }
        published.YzProjectName="Metarill";published.PrimaryColor="#0ab3ad";
        // R1派生多选的固定页／名称是传播证明的一部分，仅保留所需声明，不携带界面配置或配置口令。
        var multiProof=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        foreach(var field in published.ConfigColumns.Where(c=>c.ControlKind=="MultiSelectPage"&&published.ConfigTables.Any(t=>t.Id==c.ConfigTableId&&t.IsRongyu==1)))
        {
            using var detail=JsonDocument.Parse(string.IsNullOrWhiteSpace(field.ControlDetail)?"{}":field.ControlDetail);var root=detail.RootElement;if(root.ValueKind!=JsonValueKind.Object)continue;var pageId=root.TryGetProperty("pageId",out var id)?id.ToString():field.Extra.GetValueOrDefault("linkConfigPageId").ToString();var targetPage=published.ConfigPages.SingleOrDefault(p=>p.Id==pageId);if(targetPage is null)continue;var target=published.ConfigTables.Single(t=>t.Id==targetPage.ConfigTableId);if(target.IsRongyu!=0)continue;var name=root.TryGetProperty("nameField",out var n)&&n.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(n.GetString())?n.GetString()!:"name";var value=published.ConfigColumns.SingleOrDefault(c=>c.ConfigTableId==target.Id&&c.ColumnName==name);if(value is null||MetadataStoragePolicy.ForTable(published,target).IsPhysical(value))continue;if(!multiProof.TryGetValue(pageId,out var names))multiProof[pageId]=names=new(StringComparer.Ordinal);names.Add(value.Id);
        }
        published.ConfigMenus=[];published.ConfigCustomPages=[];published.ConfigPageColumns=published.ConfigPageColumns.Where(f=>multiProof.TryGetValue(f.ConfigPageId,out var names)&&names.Contains(f.ConfigColumnId)).Select(f=>new PageColumnMetadata{Id=f.Id,ConfigPageId=f.ConfigPageId,ConfigColumnId=f.ConfigColumnId,YzProjectId=f.YzProjectId}).OrderBy(f=>f.Id,StringComparer.Ordinal).ToList();published.ConfigChildren=[];published.ConfigIndexes=[];published.ConfigIndexColumns=[];
        published.ConfigPages=published.ConfigPages.Select(page=>new PageMetadata{Id=page.Id,ConfigTableId=page.ConfigTableId,YzProjectId=page.YzProjectId,YzAdminTypeId=page.YzAdminTypeId,Extra=multiProof.ContainsKey(page.Id)&&page.Extra.TryGetValue("type",out var kind)?new(){["type"]=kind.Clone()}:new()}).ToList();
        published.YzAdminTypes=published.YzAdminTypes.Select(admin=>new AdminTypeMetadata{Id=admin.Id,YzProjectId=admin.YzProjectId}).ToList();
        published.ConfigButtons=published.ConfigButtons.Where(button=>button.TryGetProperty("sql",out var path)&&path.ValueKind==JsonValueKind.String&&path.GetString() is "/admin/sys/system_maintenance/tables" or "/admin/sys/system_maintenance/backfill_join_columns").Select(button=>JsonSerializer.SerializeToElement(new{id=button.GetProperty("id").ToString(),sql=button.GetProperty("sql").GetString()})).ToList();
        // 固定记录集合的发布次序；不重排业务扩展数组，也不改变SortNo和原始输入。
        published.ConfigTables=published.ConfigTables.OrderBy(item=>item.Id,StringComparer.Ordinal).ToList();
        published.ConfigColumns=published.ConfigColumns.OrderBy(item=>item.Id,StringComparer.Ordinal).ToList();
        published.ConfigPages=published.ConfigPages.OrderBy(item=>item.Id,StringComparer.Ordinal).ToList();
        published.YzAdminTypes=published.YzAdminTypes.OrderBy(item=>item.Id,StringComparer.Ordinal).ToList();
        published.ConfigButtons=published.ConfigButtons.OrderBy(item=>item.GetProperty("id").ToString(),StringComparer.Ordinal).ToList();
        return published;
    }

    /// <summary>递归清理敏感键及嵌套JSON字符串；配置中的普通SQL定义只作为数据保留，不执行。</summary>
    internal static void Scrub(JsonNode node, string path, List<string> redactions)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
            {
                var childPath = path + "." + property.Key;
                if (IsSensitiveKey(property.Key)) { obj.Remove(property.Key); redactions.Add(childPath); }
                else if (property.Value is JsonObject or JsonArray) Scrub(property.Value, childPath, redactions);
                else if (property.Value is JsonValue value && value.TryGetValue<string>(out var content) && content.TrimStart() is [ '{' or '[', .. ])
                {
                    try
                    {
                        var nested = JsonNode.Parse(content);
                        if (nested is JsonObject or JsonArray)
                        {
                            // 仅实际脱敏时重写编码JSON；格式变化也会改变字符串默认值及用户原始声明。
                            var original=nested.DeepClone();Scrub(nested,childPath,redactions);
                            if(!JsonNode.DeepEquals(original,nested))obj[property.Key]=nested.ToJsonString(Json);
                        }
                    }
                    catch (JsonException) { /* 非JSON说明保持原文，不执行或猜测其内容。 */ }
                }
            }
        else if (node is JsonArray array) for (var i = 0; i < array.Count; i++) if (array[i] is { } item) Scrub(item, path + "[" + i + "]", redactions);
    }
    /// <summary>生成发布和窄范围守卫共用敏感字段名判定，不以可写或查询声明豁免凭据。</summary>
    internal static bool IsSensitiveKey(string value) => Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(value);

}
