using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

public sealed class MetadataRepository(SqlRowStore store, MetadataRegistry registry)
{
    /// <summary>种子只补缺失记录，重复初始化不能覆盖用户已经编辑的元数据。</summary>
    public async Task SeedAsync(MetadataCatalog catalog,CancellationToken cancellationToken=default)
    {
        await store.Session.InTransactionAsync(async () =>
        {
            await SeedRows("config_table",catalog.ConfigTables,cancellationToken);
            await SeedRows("config_column",catalog.ConfigColumns,cancellationToken);
            await SeedRows("config_page",catalog.ConfigPages,cancellationToken);
            await SeedRows("config_page_column",catalog.ConfigPageColumns,cancellationToken);
            await SeedRows("yz_admin_type",catalog.YzAdminTypes,cancellationToken);
            await SeedRows("config_menu",catalog.ConfigMenus,cancellationToken);
            await SeedRows("config_custom_page",catalog.ConfigCustomPages,cancellationToken);
            await SeedRows("config_button",catalog.ConfigButtons,cancellationToken);
            await SeedRows("config_child",catalog.ConfigChildren,cancellationToken);
            await SeedRows("config_index",catalog.ConfigIndexes,cancellationToken);
            await SeedRows("config_index_column",catalog.ConfigIndexColumns,cancellationToken);
            return true;
        },cancellationToken);
    }
    private async Task SeedRows<T>(string table,IEnumerable<T> records,CancellationToken cancellationToken)
    {
        var existing=(await store.ReadIncludingDeletedAsync(table,cancellationToken:cancellationToken)).Select(r=>SqlRowStore.Id(r)).ToHashSet();
        foreach(var record in records)
        {
            var json=JsonSerializer.SerializeToElement(record,JsonDefaults.Options);
            var values=json.EnumerateObject().ToDictionary(p=>MetadataRegistry.Snake(p.Name),p=>(object?)p.Value.Clone());
            if(!values.TryGetValue("id",out var idValue)||!long.TryParse(idValue?.ToString(),out var id))throw new InvalidOperationException("种子记录缺少合法标识。");
            if(existing.Contains(id))continue;
            if(record is PageMetadata page){values.TryAdd("is_edit",1);values.TryAdd("is_update",page.IsSave);}
            if(record is PageColumnMetadata field){values["is_in_edit"]=field.IsEdit;values["is_in_grid"]=field.IsShow;values["is_must"]=field.IsRequired;}
            values.TryAdd("insert_time",DateTime.UtcNow);values.TryAdd("update_time",DateTime.UtcNow);
            var physical=registry.Platform.ConfigTables.Single(t=>t.TableName==table);
            foreach(var required in registry.Platform.ConfigColumns.Where(c=>c.ConfigTableId==physical.Id&&c.IsDbNotNull==1&&!values.ContainsKey(c.ColumnName)))
                if(required.LogicalType is "int32" or "int64" or "decimal")values[required.ColumnName]=0;
                else if(required.LogicalType=="bool")values[required.ColumnName]=false;
            await store.InsertAsync(table,values,cancellationToken);
        }
    }
    public async Task<MetadataCatalog> LoadProjectAsync(string projectId,CancellationToken cancellationToken=default)
    {
        if(!long.TryParse(projectId,out var project))throw new InvalidOperationException("项目标识无效。");
        var filters=new Dictionary<string,object?>{{"yz_project_id",project}};
        async Task<List<T>> Read<T>(string table)=> (await store.ReadAsync(table,filters,cancellationToken)).Select(row=>
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
        }).ToList();
        // 项目名与主色必须来自同一项目的实际记录，不能把业务项目统一生成为默认名称/平台主题。
        var projectRow=(await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",project}},cancellationToken)).SingleOrDefault();
        var projectName=projectRow is null?"":SqlRowStore.Text(projectRow,"name");
        var primaryColor=projectRow is null?"":SqlRowStore.Text(projectRow,"primary_color");
        var result=new MetadataCatalog
        {
            YzProjectId=projectId,YzProjectName=string.IsNullOrWhiteSpace(projectName)?projectId==registry.Platform.YzProjectId?"Metarill":"业务项目":projectName,
            PrimaryColor=string.IsNullOrWhiteSpace(primaryColor)?"#0ab3ad":primaryColor,
            ConfigTables=await Read<TableMetadata>("config_table"),ConfigColumns=await Read<ColumnMetadata>("config_column"),
            ConfigPages=await Read<PageMetadata>("config_page"),ConfigPageColumns=await Read<PageColumnMetadata>("config_page_column"),
            YzAdminTypes=await Read<AdminTypeMetadata>("yz_admin_type"),ConfigMenus=await Read<MenuMetadata>("config_menu"),
            ConfigCustomPages=await Read<CustomPageMetadata>("config_custom_page"),
            ConfigButtons=await Read<JsonElement>("config_button"),ConfigChildren=await Read<JsonElement>("config_child"),
            ConfigIndexes=await Read<JsonElement>("config_index"),ConfigIndexColumns=await Read<JsonElement>("config_index_column")
        };
        foreach(var custom in result.ConfigCustomPages)if(string.IsNullOrEmpty(custom.YzAdminTypeId))custom.YzAdminTypeId=result.YzAdminTypes.FirstOrDefault()?.Id??"";
        MetadataValidation.Validate(result);return result;
    }
}
