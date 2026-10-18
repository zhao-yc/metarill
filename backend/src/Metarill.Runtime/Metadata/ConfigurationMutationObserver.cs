using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

/// <summary>元数据对象固有联动：创建表补默认字段/页面，新增字段补各页字段，沿原事务执行。</summary>
public sealed class ConfigurationMutationObserver(SqlRowStore store,MetadataRegistry registry,IIdGenerator ids):IRecordMutationObserver
{
    public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default)
    {
        if(context.IsDelete)return;
        if(context.TableName is not ("config_table" or "config_column" or "config_page"))return;
        if(store.Session.CurrentTransaction!=context.Transaction)throw new InvalidOperationException("配置联动必须加入同一事务。");
        var project=long.Parse(context.ProjectId);
        if(context.TableName=="config_table")
        {
            var table=(await store.ReadAsync("config_table",new Dictionary<string,object?>{{"id",context.RecordId}},cancellationToken)).Single();
            var existing=await store.ReadAsync("config_column",new Dictionary<string,object?>{{"config_table_id",context.RecordId}},cancellationToken);
            foreach(var (name,label,type) in new[]{("id","标识","int64"),("name","名称","string"),("yz_project_id","项目","int64"),("insert_time","创建时间","datetime"),("update_time","修改时间","datetime"),("insert_manager_id","创建账号","int64"),("update_manager_id","修改账号","int64"),("delete_no","删除时间","datetime")})
            {
                if(existing.Any(c=>SqlRowStore.Text(c,"column_name")==name))continue;
                await store.InsertAsync("config_column",new Dictionary<string,object?>{{"id",ids.NextId()},{"config_table_id",context.RecordId},{"column_name",name},{"name",label},{"logical_type",type},{"column_type",type},{"column_length",type=="string"?"255":""},{"is_db_not_null",name=="id"?1:0},{"yz_project_id",project},{"sort_no",existing.Count*10}},cancellationToken);
            }
            var pages=await store.ReadAsync("config_page",new Dictionary<string,object?>{{"config_table_id",context.RecordId}},cancellationToken);
            if(pages.Count==0)
            {
                var pageId=ids.NextId();var admin=table.GetValueOrDefault("yz_admin_type_id")??long.Parse(registry.Platform.YzAdminTypes[0].Id);
                await store.InsertAsync("config_page",new Dictionary<string,object?>{{"id",pageId},{"config_table_id",context.RecordId},{"config_table_table_name",SqlRowStore.Text(table,"table_name")},{"name",SqlRowStore.Text(table,"name")},{"yz_project_id",project},{"yz_admin_type_id",admin},{"page_size",20},{"is_new",1},{"is_save",1},{"is_update",1},{"is_delete",1},{"is_search",1},{"where_sql","yz_project_id=@current_yz_project_id"}},cancellationToken);
            }
            await EnsurePageFields(context.RecordId,project,cancellationToken);
        }
        if(context.TableName=="config_column")
        {
            var column=(await store.ReadAsync("config_column",new Dictionary<string,object?>{{"id",context.RecordId}},cancellationToken)).Single();
            await EnsurePageFields(SqlRowStore.Id(column,"config_table_id"),project,cancellationToken);
        }
        if(context.TableName=="config_page")
        {
            var page=(await store.ReadAsync("config_page",new Dictionary<string,object?>{{"id",context.RecordId}},cancellationToken)).Single();
            await EnsurePageFields(SqlRowStore.Id(page,"config_table_id"),project,cancellationToken);
        }
    }
    private async Task EnsurePageFields(long tableId,long project,CancellationToken cancellationToken)
    {
        var pages=await store.ReadAsync("config_page",new Dictionary<string,object?>{{"config_table_id",tableId}},cancellationToken);
        var columns=await store.ReadAsync("config_column",new Dictionary<string,object?>{{"config_table_id",tableId}},cancellationToken);
        foreach(var page in pages)
        {
            var existing=await store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",SqlRowStore.Id(page)}},cancellationToken);
            foreach(var column in columns)
            {
                if(existing.Any(f=>SqlRowStore.Text(f,"config_column_id")==SqlRowStore.Text(column,"id")))continue;
                var name=SqlRowStore.Text(column,"column_name");var writable=name is not ("id" or "yz_project_id" or "delete_no")&&!name.StartsWith("insert_")&&!name.StartsWith("update_");
                await store.InsertAsync("config_page_column",new Dictionary<string,object?>{{"id",ids.NextId()},{"config_page_id",SqlRowStore.Id(page)},{"config_column_id",SqlRowStore.Id(column)},{"column_name",name},{"name",SqlRowStore.Text(column,"name")},{"yz_project_id",project},{"sort_no",column.GetValueOrDefault("sort_no")??0},{"is_edit",writable?1:0},{"is_show",1},{"is_in_edit",writable?1:0},{"is_in_grid",1},{"is_read_only",writable?0:1}},cancellationToken);
            }
        }
    }
}
