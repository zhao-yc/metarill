using Metarill.Core.Contracts;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

/// <summary>仅修正首期开发库中的内置种子；先完整备份，不修改账号、角色、文件或业务记录。</summary>
public sealed class DevelopmentSeedSynchronizer(SqlRowStore store,MetadataRegistry registry)
{
    /// <summary>开发种子固定标识后，可显式重建包内元数据；普通初始化仍只补缺失记录。</summary>
    public async Task<string> SynchronizeAsync(string backupRoot,CancellationToken token=default)
    {
        var paths=new List<string>();foreach(var catalog in new[]{registry.Platform,registry.Demo})paths.Add(await SynchronizeCatalogAsync(catalog,backupRoot,token));return string.Join("；",paths);
    }
    private async Task<string> SynchronizeCatalogAsync(MetadataCatalog catalog,string backupRoot,CancellationToken token)
    {
        if(store.Session.Options.ExpectedDatabase is not ("metarill_dev" or "metarill_test"))throw new InvalidOperationException("种子校正只允许本项目指定开发库和测试库。");
        var source=new Dictionary<string,object>{
            ["config_table"]=catalog.ConfigTables,["config_column"]=catalog.ConfigColumns,["config_page"]=catalog.ConfigPages,
            ["config_page_column"]=catalog.ConfigPageColumns,["yz_admin_type"]=catalog.YzAdminTypes,["config_menu"]=catalog.ConfigMenus,
            ["config_custom_page"]=catalog.ConfigCustomPages,["config_button"]=catalog.ConfigButtons,["config_child"]=catalog.ConfigChildren,
            ["config_index"]=catalog.ConfigIndexes,["config_index_column"]=catalog.ConfigIndexColumns};
        var before=new Dictionary<string,List<Dictionary<string,object?>>>();
        // 备份真实读取内容并落盘成功之后才进入写事务，文件名由服务端生成。
        foreach(var(table,records)in source)
        {
            var expected=JsonSerializer.SerializeToElement(records,JsonDefaults.Options).EnumerateArray().Select(r=>r.GetProperty("id").ToString()).ToHashSet(StringComparer.Ordinal);
            before[table]=(await store.ReadIncludingDeletedAsync(table,cancellationToken:token)).Where(r=>SqlRowStore.Text(r,"yz_project_id")==catalog.YzProjectId||expected.Contains(SqlRowStore.Text(r,"id"))).ToList();
        }
        Directory.CreateDirectory(backupRoot);
        var backup=Path.Combine(backupRoot,store.Session.Options.ExpectedDatabase+"-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N")+".json");
        await File.WriteAllTextAsync(backup,JsonSerializer.Serialize(before,JsonDefaults.Options),token);
        await store.Session.InTransactionAsync(async()=>
        {
            foreach(var(table,records)in source)
            {
                var expected=new HashSet<long>();
                var physical=registry.Platform.ConfigTables.Single(t=>t.TableName==table);
                foreach(var record in JsonSerializer.SerializeToElement(records,JsonDefaults.Options).EnumerateArray())
                {
                    var values=record.EnumerateObject().ToDictionary(p=>MetadataRegistry.Snake(p.Name),p=>(object?)p.Value.Clone());
                    var id=long.Parse(values["id"]!.ToString()!);expected.Add(id);
                    if(table=="config_page_column")
                    {
                        values["is_in_edit"]=record.GetProperty("isEdit").GetInt32();
                        values["is_in_grid"]=record.GetProperty("isShow").GetInt32();
                        values["is_must"]=record.GetProperty("isRequired").GetInt32();
                    }
                    foreach(var column in registry.Platform.ConfigColumns.Where(c=>c.ConfigTableId==physical.Id&&!values.ContainsKey(c.ColumnName)))
                        values[column.ColumnName]=!string.IsNullOrEmpty(column.DefaultValue)?column.DefaultValue:column.IsDbNotNull==1&&column.LogicalType is "int32" or "int64" or "decimal"?0:null;
                    values["delete_no"]=null;values["update_time"]=BusinessCalendar.Now;
                    if(await store.UpdateAsync(table,id,values,token)==0)await store.InsertAsync(table,values,token);
                }
                foreach(var old in before[table].Where(r=>r.GetValueOrDefault("delete_no") is null&&!expected.Contains(SqlRowStore.Id(r))))
                    await store.UpdateAsync(table,SqlRowStore.Id(old),new Dictionary<string,object?>{{"delete_no",BusinessCalendar.Now}},token);
            }
            return true;
        },token);
        return backup;
    }
}
