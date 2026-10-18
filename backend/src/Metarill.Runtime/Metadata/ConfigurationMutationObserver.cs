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
        if(context.IsDelete){await DeleteOwnedMetadata(context,cancellationToken);return;}
        if(context.TableName=="config_custom_page")
        {
            var custom=(await store.ReadAsync("config_custom_page",new Dictionary<string,object?>{{"id",context.RecordId}},cancellationToken)).Single();
            var path=SqlRowStore.Text(custom,"page_path").TrimStart('/');
            if(!System.Text.RegularExpressions.Regex.IsMatch(path,"^pages/[a-zA-Z0-9_/-]+$")||path.Contains(".."))throw new ArgumentException("自定义页面必须使用合法的内部路由。");
            return;
        }
        if(context.TableName is not ("config_table" or "config_column" or "config_page"))return;
        if(store.Session.CurrentTransaction!=context.Transaction)throw new InvalidOperationException("配置联动必须加入同一事务。");
        if(!registry.Platform.ConfigTables.Any(t=>t.TableName==context.TableName))throw new InvalidOperationException("配置元模型缺少对应表定义。");
        var project=long.Parse(context.ProjectId);
        await ValidateAndNormalize(context,project,cancellationToken);
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
                var pageId=ids.NextId();var admin=table.GetValueOrDefault("yz_admin_type_id")??(await store.ReadAsync("yz_admin_type",new Dictionary<string,object?>{{"yz_project_id",project}},cancellationToken)).FirstOrDefault()?.GetValueOrDefault("id")??throw new InvalidOperationException("请先为当前项目配置后台类型。");
                await store.InsertAsync("config_page",new Dictionary<string,object?>{{"id",pageId},{"config_table_id",context.RecordId},{"config_table_table_name",SqlRowStore.Text(table,"table_name")},{"name",SqlRowStore.Text(table,"name")},{"yz_project_id",project},{"yz_admin_type_id",admin},{"page_size",20},{"is_new",1},{"is_save",1},{"is_update",1},{"is_delete",1},{"is_search",1},{"is_edit",1},{"where_sql","yz_project_id=@current_yz_project_id"}},cancellationToken);
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
            await EnsurePageFields(SqlRowStore.Id(page,"config_table_id"),project,cancellationToken,context.RecordId);
        }
    }
    /// <summary>对象规则在生成写入的同一事务里校验；失败回滚刚写入的记录。</summary>
    private async Task ValidateAndNormalize(RecordMutationContext context,long project,CancellationToken token)
    {
        var row=(await store.ReadAsync(context.TableName,new Dictionary<string,object?>{{"id",context.RecordId}},token)).Single();
        if(context.TableName=="config_table")
        {
            var name=SqlRowStore.Text(row,"table_name");Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(name);
            if((await store.ReadAsync("config_table",new Dictionary<string,object?>{{"yz_project_id",project},{"table_name",name}},token)).Count!=1)throw new InvalidOperationException("同项目表名不能重复。");
            if(row.GetValueOrDefault("yz_admin_type_id") is{} admin&&(await store.ReadAsync("yz_admin_type",new Dictionary<string,object?>{{"id",admin},{"yz_project_id",project}},token)).Count!=1)throw new UnauthorizedAccessException("后台类型不属于当前项目。");
            return;
        }
        var tableId=SqlRowStore.Id(row,"config_table_id");
        if((await store.ReadAsync("config_table",new Dictionary<string,object?>{{"id",tableId},{"yz_project_id",project}},token)).Count!=1)throw new UnauthorizedAccessException("所属表不属于当前项目。");
        if(context.TableName!="config_column")return;
        var column=SqlRowStore.Text(row,"column_name");Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(column);
        if((await store.ReadAsync("config_column",new Dictionary<string,object?>{{"config_table_id",tableId},{"column_name",column}},token)).Count!=1)throw new InvalidOperationException("同表字段名不能重复。");
        var type=SqlRowStore.Text(row,"logical_type");
        if(!context.SubmittedFields.ContainsKey("logical_type")||string.IsNullOrWhiteSpace(type))
        {
            var physical=SqlRowStore.Text(row,"column_type").ToLowerInvariant();
            type=physical switch
            {
                "bigint" or "int64"=>"int64","int" or "integer" or "tinyint" or "smallint" or "int32"=>"int32",
                "decimal" or "numeric" or "double" or "float"=>"decimal","date"=>"date","time"=>"time","datetime" or "timestamp"=>"datetime",
                "bool" or "boolean"=>"bool","json"=>"json","blob" or "binary"=>"binary",_=>"string"
            };
        }
        if(column=="id"||column.EndsWith("_id",StringComparison.Ordinal))type="int64";
        if(column=="sort_no")type="int32";
        if(type is not ("int64" or "int32" or "string" or "decimal" or "date" or "time" or "datetime" or "bool" or "json" or "binary"))throw new ArgumentException("字段逻辑类型无效。");
        await store.UpdateAsync("config_column",context.RecordId,new Dictionary<string,object?>{{"logical_type",type}},token);
    }

    /// <summary>删除配置只清理所属元数据；真实数据库删表删列须另行制定结构变更计划。</summary>
    private async Task DeleteOwnedMetadata(RecordMutationContext context,CancellationToken token)
    {
        if(context.TableName is not ("config_table" or "config_column" or "config_page" or "config_custom_page"))return;
        if(store.Session.CurrentTransaction!=context.Transaction)throw new InvalidOperationException("配置删除联动必须在同一事务内。");
        async Task DeleteRows(string table,string field,long id)
        {
            foreach(var row in await store.ReadAsync(table,new Dictionary<string,object?>{{field,id},{"yz_project_id",long.Parse(context.ProjectId)}},token))
                await store.UpdateAsync(table,SqlRowStore.Id(row),new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}},token);
        }
        if(context.TableName=="config_custom_page")await DeleteRows("config_menu","config_custom_page_id",context.RecordId);
        if(context.TableName=="config_column"){await DeleteRows("config_page_column","config_column_id",context.RecordId);await DeleteRows("config_index_column","config_column_id",context.RecordId);}
        var pageIds=context.TableName=="config_page"?new[]{context.RecordId}:context.TableName=="config_table"?(await store.ReadAsync("config_page",new Dictionary<string,object?>{{"config_table_id",context.RecordId},{"yz_project_id",long.Parse(context.ProjectId)}},token)).Select(r=>SqlRowStore.Id(r)).ToArray():[];
        foreach(var pageId in pageIds)
        {
            await DeleteRows("config_page_column","config_page_id",pageId);await DeleteRows("config_button","config_page_id",pageId);
            await DeleteRows("config_menu","config_page_id",pageId);await DeleteRows("config_child","parent_config_page_id",pageId);await DeleteRows("config_child","child_config_page_id",pageId);
        }
        if(context.TableName=="config_table")
        {
            await DeleteRows("config_page","config_table_id",context.RecordId);await DeleteRows("config_column","config_table_id",context.RecordId);
            foreach(var index in await store.ReadAsync("config_index",new Dictionary<string,object?>{{"config_table_id",context.RecordId}},token))await DeleteRows("config_index_column","config_index_id",SqlRowStore.Id(index));
            await DeleteRows("config_index","config_table_id",context.RecordId);
        }
    }
    /// <summary>表或字段变更更新全部所属页，单页变更仅补该页，避免意外污染其他页面配置。</summary>
    private async Task EnsurePageFields(long tableId,long project,CancellationToken cancellationToken,long? pageId=null)
    {
        var pages=await store.ReadAsync("config_page",new Dictionary<string,object?>{{"config_table_id",tableId}},cancellationToken);
        var columns=await store.ReadAsync("config_column",new Dictionary<string,object?>{{"config_table_id",tableId}},cancellationToken);
        foreach(var page in pages.Where(p=>pageId is null||SqlRowStore.Id(p)==pageId))
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
