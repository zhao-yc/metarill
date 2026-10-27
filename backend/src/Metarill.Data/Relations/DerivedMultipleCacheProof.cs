using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;

/// <summary>派生多选缓存按原固定目标页、名称和辅助列证明，不开放任意数组或动态查询。</summary>
internal static class DerivedMultipleCacheProof
{
    /// <summary>数据层独立核对生成器声明；派生值仍须由单值匹配编译器取得终点证明。</summary>
    internal static void Require(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign,TableMetadata target,ColumnMetadata value,ColumnMetadata cache)
    {
        if(foreign.ControlKind!="MultiSelectPage"||foreign.LogicalType!="string"||!foreign.ColumnName.EndsWith("_ids",StringComparison.Ordinal))throw Invalid();using var doc=JsonDocument.Parse(foreign.ControlDetail);var root=doc.RootElement;if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=root.EnumerateObject().Count())throw Invalid();
        string Text(string key,string fallback="")=>!root.TryGetProperty(key,out var v)||v.ValueKind==JsonValueKind.Null?fallback:v.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(v.GetString())?fallback:v.GetString()!:throw Invalid();
        var pageId=Text("pageId",foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString());var page=catalog.ConfigPages.SingleOrDefault(p=>p.Id==pageId&&p.ConfigTableId==target.Id);if(page is null||page.Extra.GetValueOrDefault("type").ToString()!="2"||Text("nameField","name")!=value.ColumnName||Text("whereSql").Length>0||new[]{"sql","where"}.Any(k=>root.TryGetProperty(k,out _))||Text("tableId") is{Length:>0} tableId&&tableId!=target.Id||Text("table") is{Length:>0} tableName&&tableName!=target.TableName)throw Invalid();
        if(!catalog.ConfigPageColumns.Any(f=>f.ConfigPageId==page.Id&&f.ConfigColumnId==value.Id)||page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId||foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString() is{Length:>0} link&&link!=page.Id)throw Invalid();
        var stem=foreign.ColumnName[..^4];if(cache.ColumnName!=stem+"_"+(value.ColumnName.EndsWith('s')?value.ColumnName:value.ColumnName+"s"))throw Invalid();var read=NonRedundantReads.CompileMatch(catalog,target.Id,value.ColumnName);if(read.OutputTypes[value.ColumnName]!="string")throw Invalid();
    }
    /// <summary>异常不包含目录原文或任何连接配置。</summary>
    private static NotSupportedException Invalid()=>new("派生多选缓存缺少固定目标页、单值名称或辅助列证明。");
}
