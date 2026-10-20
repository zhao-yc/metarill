using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Security;

/// <summary>平台只将既有发布目录或同事务当前目录投影成共享决策实际消费的固定字段。</summary>
internal static class PlatformSecurityResourceProjection
{
    /// <summary>保留原页面开关和按钮缺省解释，不把字段、SQL及连接配置交给共享认证。</summary>
    internal static SecurityResourceCatalog Create(MetadataCatalog catalog)=>new(catalog.YzProjectId,
        catalog.ConfigTables.Select(table=>new SecurityTableResource(table.Id,table.TableName)).ToArray(),
        catalog.ConfigPages.Select(page=>new SecurityPageResource(page.Id,page.ConfigTableId,page.YzProjectId,page.YzAdminTypeId,
            page.IsNew==1,PageUpdatePolicy.CanUpdate(page),page.IsDelete==1,
            page.IsExport==1||page.Extra.TryGetValue("exportMethod",out var method)&&method.ValueKind==JsonValueKind.String&&method.GetString() is "list" or "edit")).ToArray(),
        catalog.ConfigMenus.Select(menu=>new SecurityMenuResource(menu.Id,menu.YzProjectId,menu.YzAdminTypeId)).ToArray(),
        catalog.ConfigButtons.Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("id",out _))
            .Select(button=>new SecurityButtonResource(button.GetProperty("id").ToString(),button.TryGetProperty("configPageId",out var page)?page.ToString():"")).ToArray());
}
