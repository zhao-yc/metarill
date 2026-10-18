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
        var existing=(await store.ReadAsync(table,cancellationToken:cancellationToken)).Select(r=>SqlRowStore.Id(r)).ToHashSet();
        foreach(var record in records)
        {
            var json=JsonSerializer.SerializeToElement(record,JsonDefaults.Options);
            var values=json.EnumerateObject().ToDictionary(p=>MetadataRegistry.Snake(p.Name),p=>(object?)p.Value.Clone());
            if(!values.TryGetValue("id",out var idValue)||!long.TryParse(idValue?.ToString(),out var id))throw new InvalidOperationException("种子记录缺少合法标识。");
            if(existing.Contains(id))continue;
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
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(clean,JsonDefaults.Options),JsonDefaults.Options)!;
        }).ToList();
        var result=new MetadataCatalog
        {
            YzProjectId=projectId,YzProjectName=projectId==registry.Platform.YzProjectId?"Metarill":"业务项目",
            ConfigTables=await Read<TableMetadata>("config_table"),ConfigColumns=await Read<ColumnMetadata>("config_column"),
            ConfigPages=await Read<PageMetadata>("config_page"),ConfigPageColumns=await Read<PageColumnMetadata>("config_page_column"),
            YzAdminTypes=await Read<AdminTypeMetadata>("yz_admin_type"),ConfigMenus=await Read<MenuMetadata>("config_menu"),
            ConfigCustomPages=(await Read<CustomPageMetadata>("config_custom_page")).Select(p=>{p.YzAdminTypeId=registry.Platform.YzAdminTypes.FirstOrDefault()?.Id??"";return p;}).ToList(),
            ConfigButtons=await Read<JsonElement>("config_button"),ConfigChildren=await Read<JsonElement>("config_child"),
            ConfigIndexes=await Read<JsonElement>("config_index"),ConfigIndexColumns=await Read<JsonElement>("config_index_column")
        };
        MetadataValidation.Validate(result);return result;
    }
}
