using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

public sealed partial class MetadataRepository
{
    /// <summary>只在已预锁项目/后台的当前事务读取安全目录；不读取连接、字段明细或整项目配置。</summary>
    internal async Task<MetadataCatalog> LoadRealmForUpdateAsync(long projectId,long adminId,CancellationToken token)
    {
        if(store.Session.CurrentTransaction is null)throw new InvalidOperationException("安全目录当前读必须加入写入事务。");
        var catalogs=new[]{registry.Platform,registry.Demo}.Where(catalog=>catalog.YzProjectId==projectId.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var tableIds=catalogs.SelectMany(catalog=>catalog.ConfigTables).Select(table=>long.Parse(table.Id,System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
        var project=new Dictionary<string,object?>{{"yz_project_id",projectId}};
        var realm=new Dictionary<string,object?>{{"yz_project_id",projectId},{"yz_admin_type_id",adminId}};
        var tables=await store.ReadForUpdateByIdsAsync("config_table","id",tableIds,project,token);
        var pages=await store.ReadForUpdateAsync("config_page",realm,token);
        var menus=await store.ReadForUpdateAsync("config_menu",realm,token);
        if(pages.Count>1000||menus.Count>1000)throw new InvalidOperationException("安全目录超过1000项，请拆分后台配置后重试。");
        var pageIds=pages.Select(row=>SqlRowStore.Id(row)).Distinct().ToArray();
        var buttons=await store.ReadForUpdateByIdsAsync("config_button","config_page_id",pageIds,project,token);
        if(buttons.Count>5000)throw new InvalidOperationException("安全按钮目录超过5000项，请缩小后台配置后重试。");
        return new(){YzProjectId=projectId.ToString(System.Globalization.CultureInfo.InvariantCulture),ConfigTables=tables.Select(DecodeRow<TableMetadata>).ToList(),
            ConfigPages=pages.Select(DecodeRow<PageMetadata>).ToList(),ConfigMenus=menus.Select(DecodeRow<MenuMetadata>).ToList(),ConfigButtons=buttons.Select(DecodeRow<JsonElement>).ToList()};
    }

    /// <summary>当前读与普通元数据加载使用相同物理别名和页面开关解释，避免授权分叉。</summary>
    private static T DecodeRow<T>(Dictionary<string,object?> row)
    {
            var clean=row.Where(p=>p.Value is not null).ToDictionary(p=>MetadataRegistry.Camel(p.Key),p=>p.Value);
            var record=JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(clean,JsonDefaults.Options),JsonDefaults.Options)!;
            // 物理配置沿用UI别名，加载时同步Core规范，避免显示开关与生成行为分离。
            if(record is PageMetadata page)page.IsSave=MetadataWorkbenchRules.StoredFlag(row.GetValueOrDefault("is_edit"))==1&&MetadataWorkbenchRules.StoredFlag(row.GetValueOrDefault("is_update"))==1?1:0;
            if(record is PageColumnMetadata field)
            {
                if(row.TryGetValue("is_in_edit",out var edit))field.IsEdit=MetadataWorkbenchRules.StoredFlag(edit);
                if(row.TryGetValue("is_in_grid",out var show))field.IsShow=MetadataWorkbenchRules.StoredFlag(show);
                if(row.TryGetValue("is_must",out var required))field.IsRequired=MetadataWorkbenchRules.StoredFlag(required);
                foreach(var(alias,physical)in MetadataWorkbenchRules.PageFlags)
                    if(row.TryGetValue(physical,out var value))field.Extra[alias]=JsonSerializer.SerializeToElement(MetadataWorkbenchRules.StoredFlag(value));
            }
            return record;
    }
}
