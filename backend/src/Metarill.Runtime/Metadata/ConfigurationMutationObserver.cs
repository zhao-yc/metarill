using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

/// <summary>元数据对象固有联动：创建表补默认字段/页面，新增字段补各页字段，沿原事务执行。</summary>
public sealed partial class ConfigurationMutationObserver(SqlRowStore store,MetadataRegistry registry,IIdGenerator ids):IRecordMutationObserver
{
    public async Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default)
    {
        if(context.IsDelete){await DeleteOwnedMetadata(context,cancellationToken);return;}
        // 保留自定义页路径校验，将其与新增归属检查一起置于原事务及项目锁内。
        if(context.TableName is not ("config_table" or "config_column" or "config_page" or "config_page_column" or "config_custom_page"))return;
        if(store.Session.CurrentTransaction is null||store.Session.CurrentTransaction!=context.Transaction)throw new InvalidOperationException("配置联动必须加入同一事务。");
        if(!registry.Platform.ConfigTables.Any(t=>t.TableName==context.TableName))throw new InvalidOperationException("配置元模型缺少对应表定义。");
        var project=long.Parse(context.ProjectId);
        // 与菜单、字段和索引聚合共享项目锁，保存时不能依据过期引用作出归属判断。
        await LockProjectAsync(project,cancellationToken);
        if(context.TableName=="config_custom_page")
        {
            var custom=await OwnedRowAsync("config_custom_page",context.RecordId,project,cancellationToken);
            var path=SqlRowStore.Text(custom,"page_path").TrimStart('/');
            if(!System.Text.RegularExpressions.Regex.IsMatch(path,"^pages/[a-zA-Z0-9_/-]+$")||path.Contains(".."))throw new ArgumentException("自定义页面必须使用合法的内部路由。");
            await ValidateAdminTypeAsync(custom,project,true,cancellationToken);
            await ValidateMenusAsync("config_custom_page_id",custom,project,cancellationToken);
            return;
        }
        if(context.TableName=="config_page_column")
        {
            await ValidatePageColumnAsync(context,project,cancellationToken);
            return;
        }
        await ValidateAndNormalize(context,project,cancellationToken);
        if(context.TableName=="config_table")
        {
            var table=(await store.ReadAsync("config_table",new Dictionary<string,object?>{{"id",context.RecordId}},cancellationToken)).Single();
            // 默认系统列只属于本次新表创建，普通更新不能补齐有意省略或软删的历史定义。
            if(context.IsCreate)
            {
                var existing=await store.ReadAsync("config_column",new Dictionary<string,object?>{{"config_table_id",context.RecordId}},cancellationToken);
                var nextSort=existing.Count==0?0:existing.Max(row=>Convert.ToInt32(row.GetValueOrDefault("sort_no")??0));
                foreach(var (name,label,type) in new[]{("id","标识","int64"),("name","名称","string"),("yz_project_id","项目","int64"),("insert_time","创建时间","datetime"),("update_time","修改时间","datetime"),("insert_manager_id","创建账号","int64"),("update_manager_id","修改账号","int64"),("delete_no","删除时间","datetime")})
                {
                    if(existing.Any(c=>SqlRowStore.Text(c,"column_name")==name))continue;
                    // 系统字段同样使用已发布元模型的必填默认值，并在已有排序后顺序追加。
                    nextSort=checked(nextSort+10);
                    var values=new Dictionary<string,object?>{{"id",ids.NextId()},{"config_table_id",context.RecordId},{"config_table_name",SqlRowStore.Text(table,"name")},{"column_name",name},{"name",label},{"logical_type",type},{"column_type",type},{"column_length",type=="string"?"255":""},{"is_db_not_null",name=="id"?1:0},{"yz_project_id",project},{"sort_no",nextSort}};
                    ConfigurationColumnDefaults.Fill(values,registry.Platform);
                    await store.InsertAsync("config_column",values,cancellationToken);
                }
            }
            var pages=await store.ReadAsync("config_page",new Dictionary<string,object?>{{"config_table_id",context.RecordId}},cancellationToken);
            if(context.IsCreate||context.SubmittedFields.ContainsKey("table_name"))
                await SynchronizeTableNameAsync(table,pages,project,cancellationToken);
            long? initializedPage=null;
            if(context.IsCreate&&pages.Count==0)
            {
                var pageId=ids.NextId();var admin=table.GetValueOrDefault("yz_admin_type_id")??(await store.ReadAsync("yz_admin_type",new Dictionary<string,object?>{{"yz_project_id",project}},cancellationToken)).FirstOrDefault()?.GetValueOrDefault("id")??throw new InvalidOperationException("请先为当前项目配置后台类型。");
                await store.InsertAsync("config_page",new Dictionary<string,object?>{{"id",pageId},{"config_table_id",context.RecordId},{"config_table_table_name",SqlRowStore.Text(table,"table_name")},{"name",SqlRowStore.Text(table,"name")},{"yz_project_id",project},{"yz_admin_type_id",admin},{"page_size",20},{"is_new",1},{"is_save",1},{"is_update",1},{"is_delete",1},{"is_search",1},{"is_edit",1},{"where_sql","yz_project_id=@current_yz_project_id"}},cancellationToken);
                initializedPage=pageId;
            }
            await ValidateAndCreatePageFields(context.RecordId,project,false,cancellationToken);
            if(initializedPage is{} defaultPage)await InitializePageFieldsAsync(defaultPage,project,cancellationToken);
        }
        if(context.TableName=="config_column")
        {
            var column=(await store.ReadAsync("config_column",new Dictionary<string,object?>{{"id",context.RecordId}},cancellationToken)).Single();
            await ValidateAndCreatePageFields(SqlRowStore.Id(column,"config_table_id"),project,context.IsCreate,cancellationToken,newColumnId:context.IsCreate?context.RecordId:null);
        }
        if(context.TableName=="config_page")
        {
            var page=(await store.ReadAsync("config_page",new Dictionary<string,object?>{{"id",context.RecordId}},cancellationToken)).Single();
            // 普通新增页与复制页仅校验；从表初始化必须由明确的内部入口单独调用，不能复活源页遗漏字段。
            await ValidateAndCreatePageFields(SqlRowStore.Id(page,"config_table_id"),project,false,cancellationToken,pageId:context.RecordId);
        }
    }
    /// <summary>对象规则在生成写入的同一事务里校验；失败回滚刚写入的记录。</summary>
    private async Task ValidateAndNormalize(RecordMutationContext context,long project,CancellationToken token)
    {
        var row=await OwnedRowAsync(context.TableName,context.RecordId,project,token);
        if(context.TableName=="config_table")
        {
            var name=SqlRowStore.Text(row,"table_name");Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(name);
            if((await store.ReadAsync("config_table",new Dictionary<string,object?>{{"yz_project_id",project},{"table_name",name}},token)).Count!=1)throw new InvalidOperationException("同项目表名不能重复。");
            await ValidateAdminTypeAsync(row,project,false,token);
            return;
        }
        var tableId=SqlRowStore.Id(row,"config_table_id");
        var tables=await store.ReadAsync("config_table",new Dictionary<string,object?>{{"id",tableId},{"yz_project_id",project}},token);
        if(tables.Count!=1)throw new UnauthorizedAccessException("所属表不属于当前项目。");
        if(context.TableName=="config_page")
        {
            if(context.IsCreate||context.SubmittedFields.ContainsKey("config_table_id"))
                ConfigurationMaterializedValues.RequirePage(row,tables[0]);
            await ValidateAdminTypeAsync(row,project,true,token);
            await ValidatePageFieldsAsync(row,project,token);
            await ValidateMenusAsync("config_page_id",row,project,token);
            return;
        }
        var references=await ValidateColumnReferencesAsync(row,project,token);
        var column=SqlRowStore.Text(row,"column_name");Metarill.Core.Metadata.MetadataValidation.RequireIdentifier(column);
        if((await store.ReadAsync("config_column",new Dictionary<string,object?>{{"config_table_id",tableId},{"column_name",column}},token)).Count!=1)throw new InvalidOperationException("同表字段名不能重复。");
        var originalType=SqlRowStore.Text(row,"logical_type");
        var type=Metarill.Core.Metadata.MetadataColumnTypePolicy.Resolve(column,originalType,SqlRowStore.Text(row,"column_type"),context.IsCreate,
            context.SubmittedFields.ContainsKey("logical_type"),context.SubmittedFields.ContainsKey("column_type"));
        // 无关配置编辑不能从空物理类型重推逻辑类型，也不追加无效果的类型写入。
        if(!string.Equals(type,originalType,StringComparison.Ordinal))
            await store.UpdateAsync("config_column",context.RecordId,new Dictionary<string,object?>{{"logical_type",type}},token);
        await SynchronizeColumnValuesAsync(row,references,context.SubmittedFields.Keys.ToHashSet(StringComparer.Ordinal),token);
    }

    /// <summary>统一锁定项目，和菜单及索引工作台的聚合写入串行校验。</summary>
    private async Task LockProjectAsync(long project,CancellationToken token)
    {
        if((await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",project}},token)).Count!=1)
            throw new UnauthorizedAccessException("当前元数据项目不存在。");
    }
    /// <summary>关联对象必须活动且属于同一项目，不能凭合法ID跨项目迁移。</summary>
    private async Task<Dictionary<string,object?>> OwnedRowAsync(string table,long id,long project,CancellationToken token)
        => (await store.ReadForUpdateAsync(table,new Dictionary<string,object?>{{"id",id},{"yz_project_id",project}},token)).SingleOrDefault()
            ??throw new UnauthorizedAccessException("配置引用不存在或不属于当前项目。");
    /// <summary>页面必须有明确后台；表的可选后台一旦指定也必须属于当前项目。</summary>
    private async Task ValidateAdminTypeAsync(IReadOnlyDictionary<string,object?> row,long project,bool required,CancellationToken token)
    {
        if(row.GetValueOrDefault("yz_admin_type_id") is not{} admin)
        {
            if(required)throw new InvalidOperationException("页面必须选择当前项目的后台类型。");
            return;
        }
        await OwnedRowAsync("yz_admin_type",Convert.ToInt64(admin),project,token);
    }
    /// <summary>页面换表前检查全部既有字段，不能保留旧表引用或静默挪动页面字段。</summary>
    private async Task ValidatePageFieldsAsync(IReadOnlyDictionary<string,object?> page,long project,CancellationToken token)
    {
        var table=SqlRowStore.Id(page,"config_table_id");
        foreach(var field in await store.ReadForUpdateAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",SqlRowStore.Id(page)}},token))
        {
            RequireProject(field,project);
            var column=await OwnedRowAsync("config_column",SqlRowStore.Id(field,"config_column_id"),project,token);
            if(SqlRowStore.Id(column,"config_table_id")!=table)throw new InvalidOperationException("页面已有字段属于其他表，不能直接修改页面所属表。");
        }
    }
    /// <summary>页面换后台时不能留下无法导航的菜单；所有引用均必须同项目同后台。</summary>
    private async Task ValidateMenusAsync(string field,IReadOnlyDictionary<string,object?> page,long project,CancellationToken token)
    {
        foreach(var menu in await store.ReadForUpdateAsync("config_menu",new Dictionary<string,object?>{{field,SqlRowStore.Id(page)}},token))
        {
            RequireProject(menu,project);
            if(SqlRowStore.Id(menu,"yz_admin_type_id")!=SqlRowStore.Id(page,"yz_admin_type_id"))throw new InvalidOperationException("页面仍被其他后台的菜单引用，不能直接修改页面后台类型。");
        }
    }
    /// <summary>字段换表前核验页面与索引聚合，不能让原字段引用跨表或跨项目。</summary>
    private async Task<List<Dictionary<string,object?>>> ValidateColumnReferencesAsync(IReadOnlyDictionary<string,object?> column,long project,CancellationToken token)
    {
        var table=SqlRowStore.Id(column,"config_table_id");var id=SqlRowStore.Id(column);
        // 先只读发现全部异常归属，再锁自己的引用；不能先锁其他项目的损坏记录。
        var fields=await store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_column_id",id}},token);
        var indexes=await store.ReadAsync("config_index_column",new Dictionary<string,object?>{{"config_column_id",id}},token);
        foreach(var field in fields)
        {
            RequireProject(field,project);
            var page=(await store.ReadAsync("config_page",new Dictionary<string,object?>{{"id",SqlRowStore.Id(field,"config_page_id")},{"yz_project_id",project}},token)).SingleOrDefault();
            if(page is null||SqlRowStore.Id(page,"config_table_id")!=table)throw new InvalidOperationException("字段仍被原表页面引用，不能直接修改字段所属表。");
        }
        foreach(var field in indexes)
        {
            RequireProject(field,project);
            var index=(await store.ReadAsync("config_index",new Dictionary<string,object?>{{"id",SqlRowStore.Id(field,"config_index_id")},{"yz_project_id",project}},token)).SingleOrDefault();
            if(index is null||SqlRowStore.Id(index,"config_table_id")!=table)throw new InvalidOperationException("字段仍被原表索引引用，不能直接修改字段所属表。");
        }
        var locked=new List<Dictionary<string,object?>>();
        foreach(var discovered in fields.OrderBy(field=>SqlRowStore.Id(field)))
        {
            var field=await OwnedRowAsync("config_page_column",SqlRowStore.Id(discovered),project,token);
            if(SqlRowStore.Id(field,"config_column_id")!=id)throw new InvalidOperationException("页面字段关联已变化，请重试。");
            var page=await OwnedRowAsync("config_page",SqlRowStore.Id(field,"config_page_id"),project,token);
            if(SqlRowStore.Id(page,"config_table_id")!=table)throw new InvalidOperationException("字段仍被原表页面引用，不能直接修改字段所属表。");
            locked.Add(field);
        }
        foreach(var discovered in indexes.OrderBy(field=>SqlRowStore.Id(field)))
        {
            var field=await OwnedRowAsync("config_index_column",SqlRowStore.Id(discovered),project,token);
            if(SqlRowStore.Id(field,"config_column_id")!=id)throw new InvalidOperationException("索引字段关联已变化，请重试。");
            var index=await OwnedRowAsync("config_index",SqlRowStore.Id(field,"config_index_id"),project,token);
            if(SqlRowStore.Id(index,"config_table_id")!=table)throw new InvalidOperationException("字段仍被原表索引引用，不能直接修改字段所属表。");
        }
        return locked;
    }
    /// <summary>从属记录按真实归属校验，过滤查询不能掩盖已存在的跨项目引用。</summary>
    private static void RequireProject(IReadOnlyDictionary<string,object?> row,long project)
    {
        if(SqlRowStore.Text(row,"yz_project_id")!=project.ToString(System.Globalization.CultureInfo.InvariantCulture))throw new UnauthorizedAccessException("配置从属记录属于其他项目。");
    }
    /// <summary>删除配置只清理所属元数据；真实数据库删表删列须另行制定结构变更计划。</summary>
    private async Task DeleteOwnedMetadata(RecordMutationContext context,CancellationToken token)
    {
        if(context.TableName is not ("config_table" or "config_column" or "config_page" or "config_custom_page"))return;
        if(store.Session.CurrentTransaction is null||store.Session.CurrentTransaction!=context.Transaction)throw new InvalidOperationException("配置删除联动必须在同一事务内。");
        await LockProjectAsync(long.Parse(context.ProjectId),token);
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
    /// <summary>只供新表默认页与显式从表建页初始化；调用者必须已经在同一项目事务内创建目标页。</summary>
    internal async Task<IReadOnlyList<long>> InitializePageFieldsAsync(long pageId,long project,CancellationToken cancellationToken=default)
    {
        if(store.Session.CurrentTransaction is null)throw new InvalidOperationException("页面字段初始化必须加入创建事务。");
        await LockProjectAsync(project,cancellationToken);
        var page=await OwnedRowAsync("config_page",pageId,project,cancellationToken);
        var table=await OwnedRowAsync("config_table",SqlRowStore.Id(page,"config_table_id"),project,cancellationToken);
        return await ValidateAndCreatePageFields(SqlRowStore.Id(table),project,true,cancellationToken,pageId:pageId);
    }
    /// <summary>保留归属校验；新列仅创建自己的映射，普通页与Update不补历史洞；显式初始化固定show=true。</summary>
    private async Task<IReadOnlyList<long>> ValidateAndCreatePageFields(long tableId,long project,bool createMissing,CancellationToken cancellationToken,long? pageId=null,long? newColumnId=null)
    {
        // 页数必须是项目锁后的活动集合，不能沿用RR旧快照来决定第3页之后的显示默认值。
        var pages=await store.ReadForUpdateAsync("config_page",new Dictionary<string,object?>{{"config_table_id",tableId}},cancellationToken);
        var columns=await store.ReadForUpdateAsync("config_column",new Dictionary<string,object?>{{"config_table_id",tableId}},cancellationToken);
        var created=new List<long>();
        foreach(var column in columns)RequireProject(column,project);
        var columnIds=columns.Select(column=>SqlRowStore.Id(column)).ToHashSet();
        if(newColumnId is{} createdId&&(!createMissing||!columnIds.Contains(createdId)))throw new InvalidOperationException("新增页面字段缺少本次活动源列。");
        var columnsToCreate=newColumnId is{} id?columns.Where(column=>SqlRowStore.Id(column)==id):columns;
        foreach(var page in pages.Where(p=>pageId is null||SqlRowStore.Id(p)==pageId))
        {
            RequireProject(page,project);
            var existing=await store.ReadForUpdateAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",SqlRowStore.Id(page)}},cancellationToken);
            foreach(var field in existing)
            {
                RequireProject(field,project);
                if(!columnIds.Contains(SqlRowStore.Id(field,"config_column_id")))throw new InvalidOperationException("页面已有字段不属于当前表，不能自动补充配置。");
            }
            // 普通保存只校验；不能复活已软删映射，也不能把其它列自动加入现有页面。
            if(!createMissing)continue;
            foreach(var column in columnsToCreate)
            {
                if(existing.Any(f=>SqlRowStore.Text(f,"config_column_id")==SqlRowStore.Text(column,"id")))continue;
                var fieldId=ids.NextId();var values=ConfigurationPageFieldDefaults.BuildPageField(fieldId,page,column,project,newColumnId is null||pages.Count<=2);
                await store.InsertAsync("config_page_column",values,cancellationToken);
                created.Add(fieldId);
            }
        }
        return created;
    }
}
