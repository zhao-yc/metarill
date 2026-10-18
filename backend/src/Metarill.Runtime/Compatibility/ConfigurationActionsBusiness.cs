using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Menus;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Compatibility;

public sealed record ConfigurationActionReceipt(string Action, string PageId, bool Created, string Message,
    IReadOnlyDictionary<string,string>? FieldIds = null, string? MenuId = null, string? AdminTypeId = null, string? Revision = null);

/// <summary>页面复制、创建及快捷菜单是配置领域事务；不执行数据库视图SQL，不修改任何角色授权。</summary>
public sealed class ConfigurationActionsBusiness(SqlRowStore store, MetadataRegistry registry, MetadataRepository metadata,
    HttpRuntimeAuthorization authorization, IIdGenerator ids, ConfigurationMutationObserver observer)
{
    public const string DefaultMenuDirectoryName = "页面";
    private sealed record PageScope(Dictionary<string,object?> Page, Dictionary<string,object?> Table, List<Dictionary<string,object?>> Columns);

    /// <summary>只复制源页出向的主子关系，子页仍指原目标；入向关系和权限记录保持原状。</summary>
    public Task<ConfigurationActionReceipt> CopyPageAsync(string sourceId, CancellationToken cancellationToken = default) => InProjectAsync(async actor =>
    {
        var scope = await LockPageAsync(sourceId, actor, cancellationToken);
        await ValidatePageReferences(scope.Page, scope.Table, actor, cancellationToken);
        var source = scope.Page; var pageId = ids.NextId(); var name = CopyName(SqlRowStore.Text(source,"name"));
        var fields = await LockedRows("config_page_column","config_page_id",SqlRowStore.Id(source),actor,cancellationToken);
        var buttons = await LockedRows("config_button","config_page_id",SqlRowStore.Id(source),actor,cancellationToken);
        var children = await LockedRows("config_child","parent_config_page_id",SqlRowStore.Id(source),actor,cancellationToken);
        var columnIds = scope.Columns.Select(row=>SqlRowStore.Id(row)).ToHashSet();
        var groups = LayoutGroups(source);
        var seen = new HashSet<long>();
        // 先验证全部引用，再插入任何复制记录，字段和布局不能在失败时留下半份副本。
        foreach(var field in fields)
        {
            var columnId = SqlRowStore.Id(field,"config_column_id");
            if(!columnIds.Contains(columnId)||!seen.Add(columnId))throw new InvalidOperationException("页面字段必须唯一引用当前表字段。");
            var key = SqlRowStore.Text(field,"detail_group_key");
            if(key.Length>0&&!groups.Contains(key))throw new InvalidOperationException("页面字段引用了不存在的详情布局分组。");
            await ValidateFieldReferences(field,scope.Table,actor,cancellationToken);
        }
        foreach(var button in buttons)
            if(SqlRowStore.Id(button,"config_table_id") is var tableId && tableId!=0 && tableId!=SqlRowStore.Id(scope.Table))throw new InvalidOperationException("按钮引用了其他配置表。");
        foreach(var child in children)
        {
            var target=await Owned("config_page",SqlRowStore.Id(child,"child_config_page_id"),actor,true,cancellationToken);
            await Owned("config_table",SqlRowStore.Id(target,"config_table_id"),actor,true,cancellationToken);
            await RequireAdminType(target,actor,cancellationToken);
        }
        var copy = CopyValues(source,pageId,actor); copy["name"]=name;
        await store.InsertAsync("config_page",copy,cancellationToken);
        var mapping = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var field in fields)
        {
            var newId=ids.NextId(); mapping.Add(SqlRowStore.Text(field,"id"),newId.ToString(CultureInfo.InvariantCulture));
            var values=CopyValues(field,newId,actor); values["config_page_id"]=pageId; values["config_page_name"]=name;
            await store.InsertAsync("config_page_column",values,cancellationToken);
        }
        foreach(var button in buttons)
        {
            var values=CopyValues(button,ids.NextId(),actor);values["config_page_id"]=pageId;values["config_page_name"]=name;
            await store.InsertAsync("config_button",values,cancellationToken);
        }
        foreach(var child in children)
        {
            var values=CopyValues(child,ids.NextId(),actor);values["parent_config_page_id"]=pageId;values["parent_config_page_name"]=name;
            await store.InsertAsync("config_child",values,cancellationToken);
        }
        // 已复制字段先插入，再复用新页联动补缺失列；观察器不会重复已有字段。
        await PageCreated(pageId,copy,actor,cancellationToken,mapping.Values.Select(Parse).ToHashSet());
        return new("copyPage",pageId.ToString(CultureInfo.InvariantCulture),true,"已复制页面及其字段、按钮和出向子页关系；未复制角色授权。",mapping);
    },cancellationToken);

    /// <summary>沿用表创建页面规则，仅增加页面元数据与字段，不创建数据库视图或执行DDL。</summary>
    public Task<ConfigurationActionReceipt> CreatePageAsync(string tableId, CancellationToken cancellationToken = default) => InProjectAsync(async actor =>
    {
        var table=await Owned("config_table",Parse(tableId),actor,true,cancellationToken);
        await Columns(table,actor,cancellationToken);
        var admin=await RequireAdminType(table,actor,cancellationToken);MetadataValidation.RequireIdentifier(SqlRowStore.Text(table,"table_name"));
        var id=ids.NextId(); var values=NewValues(id,actor);
        foreach(var column in PhysicalColumns("config_page").Where(column=>column.ColumnName.StartsWith("is_",StringComparison.Ordinal))) values[column.ColumnName]=0;
        foreach(var flag in new[]{"is_edit","is_new","is_save","is_delete","is_search","is_update","is_update_edit_columns_only"})values[flag]=1;
        values["name"]=RequireName(SqlRowStore.Text(table,"name"));values["config_table_id"]=SqlRowStore.Id(table);
        values["config_table_name"]=SqlRowStore.Text(table,"name");values["config_table_table_name"]=SqlRowStore.Text(table,"table_name");
        values["yz_admin_type_id"]=SqlRowStore.Id(admin);values["where_sql"]="yz_project_id=@current_yz_project_id";
        values["type"]=0;values["export_method"]="";values["child_place"]="OneWithMany";values["mobile_list_page_style_type"]=2;values["cols"]=2;values["page_size"]=20;
        values["detail_layout"]="{\"version\":1,\"mode\":\"none\",\"groups\":[]}";
        await store.InsertAsync("config_page",values,cancellationToken);
        await PageCreated(id,values,actor,cancellationToken);
        return new("createPage",id.ToString(CultureInfo.InvariantCulture),true,"已创建页面配置及字段，未修改数据库结构。");
    },cancellationToken);

    /// <summary>快捷入口使用同后台根目录“页面”；全树规则及版本与菜单工作台共用，重复调用不新增菜单。</summary>
    public Task<ConfigurationActionReceipt> CreateMenuAsync(string pageId, CancellationToken cancellationToken = default) => InProjectAsync(async actor =>
    {
        var scope=await LockPageAsync(pageId,actor,cancellationToken);
        var admin=await RequireAdminType(scope.Page,actor,cancellationToken);var adminId=SqlRowStore.Text(admin,"id");
        if(SqlRowStore.Text(scope.Page,"where_sql").Contains("@parent_id",StringComparison.OrdinalIgnoreCase)||SqlRowStore.Id(scope.Page,"parent_config_page_id")!=0)
            throw new InvalidOperationException("子表页面不能创建独立菜单入口。");
        var catalog=await metadata.LoadProjectAsync(actor.ProjectId,cancellationToken);
        MenuWorkbenchBusiness.Validate(catalog);var before=MenuWorkbenchBusiness.Revision(catalog);
        var roots=catalog.ConfigMenus.Where(menu=>menu.YzAdminTypeId==adminId&&menu.Kind=="DIRECTORY"&&string.IsNullOrEmpty(menu.ParentMenuId)&&menu.Name==DefaultMenuDirectoryName).ToArray();
        if(roots.Length>1)throw new InvalidOperationException("当前后台存在多个“页面”根目录，请先在菜单工作台消除歧义。");
        var existing=catalog.ConfigMenus.Where(menu=>menu.YzAdminTypeId==adminId&&menu.Kind=="GENERATED_PAGE"&&menu.ConfigPageId==pageId).OrderBy(menu=>menu.Id,StringComparer.Ordinal).FirstOrDefault();
        if(existing is not null)return new("createMenu",pageId,false,"菜单已存在："+existing.Name,MenuId:existing.Id,AdminTypeId:adminId,Revision:before);
        var created=new List<MenuMetadata>();
        var directory=roots.SingleOrDefault();
        if(directory is null)
        {
            directory=new(){Id=ids.NextId().ToString(CultureInfo.InvariantCulture),Name=DefaultMenuDirectoryName,YzProjectId=actor.ProjectId,YzAdminTypeId=adminId,Kind="DIRECTORY",SortNo=NextSort(catalog.ConfigMenus.Where(menu=>menu.YzAdminTypeId==adminId&&string.IsNullOrEmpty(menu.ParentMenuId)))};
            catalog.ConfigMenus.Add(directory);created.Add(directory);
        }
        var node=new MenuMetadata{Id=ids.NextId().ToString(CultureInfo.InvariantCulture),Name=RequireName(SqlRowStore.Text(scope.Page,"name")),YzProjectId=actor.ProjectId,YzAdminTypeId=adminId,Kind="GENERATED_PAGE",ParentMenuId=directory.Id,ConfigPageId=pageId,SortNo=NextSort(catalog.ConfigMenus.Where(menu=>menu.ParentMenuId==directory.Id))};
        catalog.ConfigMenus.Add(node);created.Add(node);
        // 不嵌套调用Save，避免项目行锁→进程gate与Save的gate→项目行锁形成反向等待。
        MenuWorkbenchBusiness.Validate(catalog);
        foreach(var menu in created)
        {
            var values=NewValues(Parse(menu.Id),actor);values["name"]=menu.Name;values["yz_admin_type_id"]=Parse(adminId);values["kind"]=menu.Kind;
            values["parent_menu_id"]=menu.ParentMenuId is null?null:Parse(menu.ParentMenuId);values["config_page_id"]=menu.ConfigPageId is null?null:Parse(menu.ConfigPageId);
            values["sort_no"]=menu.SortNo;values["is_hidden"]=0;values["is_show_count"]=0;values["visible_scheme"]="Normal";
            await store.InsertAsync("config_menu",values,cancellationToken);
        }
        var saved=await metadata.LoadProjectAsync(actor.ProjectId,cancellationToken);MenuWorkbenchBusiness.Validate(saved);
        return new("createMenu",pageId,true,"菜单已创建于当前后台的“页面”根目录。",MenuId:node.Id,AdminTypeId:adminId,Revision:MenuWorkbenchBusiness.Revision(saved));
    },cancellationToken);

    /// <summary>所有配置动作先锁真实当前项目，再锁表/页聚合，新增记录共享同一数据库事务。</summary>
    private Task<ConfigurationActionReceipt> InProjectAsync(Func<RuntimeActor,Task<ConfigurationActionReceipt>> action,CancellationToken cancellationToken)
    {
        var actor=authorization.RequireAdministrator();var project=Parse(actor.ProjectId);
        return store.Session.InTransactionAsync(async()=>
        {
            if((await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",project}},cancellationToken)).Count!=1)throw new UnauthorizedAccessException("当前项目不存在。");
            return await action(actor);
        },cancellationToken);
    }
    /// <summary>项目锁建立后重新查归属，再按表→页顺序取得当前读锁。</summary>
    private async Task<PageScope> LockPageAsync(string pageId,RuntimeActor actor,CancellationToken cancellationToken)
    {
        var page=await Owned("config_page",Parse(pageId),actor,false,cancellationToken);
        var table=await Owned("config_table",SqlRowStore.Id(page,"config_table_id"),actor,true,cancellationToken);
        page=await Owned("config_page",Parse(pageId),actor,true,cancellationToken);
        if(SqlRowStore.Id(page,"config_table_id")!=SqlRowStore.Id(table))throw new InvalidOperationException("页面所属表已变化，请重试。");
        return new(page,table,await Columns(table,actor,cancellationToken));
    }
    /// <summary>所属列不能混入其他项目；同表字段引用由后续复制验证逐条核对。</summary>
    private Task<List<Dictionary<string,object?>>> Columns(Dictionary<string,object?> table,RuntimeActor actor,CancellationToken cancellationToken)=>LockedRows("config_column","config_table_id",SqlRowStore.Id(table),actor,cancellationToken);
    /// <summary>聚合从属记录全部当前读，发现跨项目污染立即拒绝。</summary>
    private async Task<List<Dictionary<string,object?>>> LockedRows(string table,string field,long id,RuntimeActor actor,CancellationToken cancellationToken)
    {
        var rows=await store.ReadForUpdateAsync(table,new Dictionary<string,object?>{{field,id}},cancellationToken);
        if(rows.Any(row=>SqlRowStore.Text(row,"yz_project_id")!=actor.ProjectId))throw new UnauthorizedAccessException("从属配置存在跨项目引用。");
        return rows;
    }
    /// <summary>每个明确关联ID同时匹配真实项目，逻辑删除记录不参与复制。</summary>
    private async Task<Dictionary<string,object?>> Owned(string table,long id,RuntimeActor actor,bool locking,CancellationToken cancellationToken)
    {
        if(id<=0)throw new InvalidOperationException("配置关联标识无效。");
        var filter=new Dictionary<string,object?>{{"id",id},{"yz_project_id",Parse(actor.ProjectId)}};
        var rows=locking?await store.ReadForUpdateAsync(table,filter,cancellationToken):await store.ReadAsync(table,filter,cancellationToken);
        return rows.SingleOrDefault()??throw new UnauthorizedAccessException("配置对象不存在或不属于当前项目。");
    }
    /// <summary>页面或表直接归属的后台必须是当前项目内的有效记录。</summary>
    private Task<Dictionary<string,object?>> RequireAdminType(Dictionary<string,object?> row,RuntimeActor actor,CancellationToken cancellationToken)=>Owned("yz_admin_type",SqlRowStore.Id(row,"yz_admin_type_id"),actor,true,cancellationToken);
    /// <summary>页面自身的后台和父级指针也必须是当前项目内的有效配置。</summary>
    private async Task ValidatePageReferences(Dictionary<string,object?> page,Dictionary<string,object?> table,RuntimeActor actor,CancellationToken cancellationToken)
    {
        await RequireAdminType(page,actor,cancellationToken);
        if(SqlRowStore.Id(page,"parent_config_table_id") is var parentTable&&parentTable!=0)await Owned("config_table",parentTable,actor,true,cancellationToken);
        if(SqlRowStore.Id(page,"parent_config_page_id") is var parentPage&&parentPage!=0)
        {
            var parent=await Owned("config_page",parentPage,actor,true,cancellationToken);
            await Owned("config_table",SqlRowStore.Id(parent,"config_table_id"),actor,true,cancellationToken);
            if(SqlRowStore.Id(page,"parent_config_table_id")!=0&&SqlRowStore.Id(parent,"config_table_id")!=SqlRowStore.Id(page,"parent_config_table_id"))throw new InvalidOperationException("页面父表与父页归属不一致。");
        }
    }
    /// <summary>派生/联接配置只保留项目内有效引用；主字段和合并字段必须属于当前表。</summary>
    private async Task ValidateFieldReferences(Dictionary<string,object?> field,Dictionary<string,object?> table,RuntimeActor actor,CancellationToken cancellationToken)
    {
        if(SqlRowStore.Id(field,"link_config_table_id") is var linkTable&&linkTable!=0)await Owned("config_table",linkTable,actor,true,cancellationToken);
        if(SqlRowStore.Id(field,"link_config_page_id") is var linkPage&&linkPage!=0)
        {
            var linked=await Owned("config_page",linkPage,actor,true,cancellationToken);
            await Owned("config_table",SqlRowStore.Id(linked,"config_table_id"),actor,true,cancellationToken);
            if(SqlRowStore.Id(field,"link_config_table_id")!=0&&SqlRowStore.Id(linked,"config_table_id")!=SqlRowStore.Id(field,"link_config_table_id"))throw new InvalidOperationException("页面字段联接表与联接页不一致。");
        }
        if(SqlRowStore.Id(field,"combine_super_column_id") is var combine&&combine!=0)
        {
            var column=await Owned("config_column",combine,actor,true,cancellationToken);
            if(SqlRowStore.Id(column,"config_table_id")!=SqlRowStore.Id(table))throw new InvalidOperationException("合并字段必须属于当前配置表。");
        }
    }
    /// <summary>布局仅含分组定义；字段新ID单独映射，分组key保持不变。</summary>
    private static HashSet<string> LayoutGroups(Dictionary<string,object?> page)
    {
        var raw=SqlRowStore.Text(page,"detail_layout");if(string.IsNullOrWhiteSpace(raw))return new(StringComparer.Ordinal);
        using var json=JsonDocument.Parse(raw);return MetadataWorkbenchRules.Layout(json.RootElement).Groups.Select(group=>group.Key).ToHashSet(StringComparer.Ordinal);
    }
    /// <summary>新页复用配置Service观察器，同一事务补齐当前页字段且跳过已有引用。</summary>
    private async Task PageCreated(long id,Dictionary<string,object?> values,RuntimeActor actor,CancellationToken cancellationToken,HashSet<long>? preservedFields=null)
    {
        await observer.BeforeCommitAsync(new RecordMutationContext("6191175925956608","config_page",id,actor.ProjectId,values,false,store.Session.CurrentTransaction!,true),cancellationToken);
        var columns=(await store.ReadAsync("config_column",new Dictionary<string,object?>{{"config_table_id",values["config_table_id"]}},cancellationToken)).ToDictionary(row=>SqlRowStore.Id(row));
        foreach(var field in await store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",id}},cancellationToken))
        {
            if(preservedFields?.Contains(SqlRowStore.Id(field))==true)continue;
            var column=columns[SqlRowStore.Id(field,"config_column_id")];var defaults=NewValues(SqlRowStore.Id(field),actor);
            foreach(var flag in new[]{"is_important","is_in_filter","is_auto_focus","sum_kind","is_must","is_print","is_auto_save_after_change"})defaults[flag]=0;
            defaults["default_value"]="";
            if(SqlRowStore.Text(column,"column_name")=="sort_no"){defaults["default_value"]="99";defaults["is_must"]=1;}
            if(SqlRowStore.Id(column,"is_join_column")==1)defaults["is_read_only"]=1;
            await store.UpdateAsync("config_page_column",SqlRowStore.Id(field),defaults,cancellationToken);
        }
    }
    /// <summary>仅使用发布物理字段定义补默认值，不接受动态客户端列名。</summary>
    private IReadOnlyList<ColumnMetadata> PhysicalColumns(string table)
    {var definition=registry.Platform.ConfigTables.Single(row=>row.TableName==table);return registry.Platform.ConfigColumns.Where(column=>column.ConfigTableId==definition.Id&&column.IsVirtualColumn==0).ToArray();}
    /// <summary>复制业务配置值；主键、导入批次、逻辑删除和所有审计字段由当前服务端重建。</summary>
    private Dictionary<string,object?> CopyValues(IReadOnlyDictionary<string,object?> source,long id,RuntimeActor actor)
    {
        var result=source.Where(pair=>pair.Key!="id"&&pair.Key!="delete_no"&&pair.Key!="import_no"&&!pair.Key.StartsWith("insert_",StringComparison.Ordinal)&&!pair.Key.StartsWith("update_",StringComparison.Ordinal)).ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
        foreach(var pair in NewValues(id,actor))result[pair.Key]=pair.Value;
        return result;
    }
    /// <summary>新记录的归属、审计和删除状态只能来自当前身份与服务器时钟。</summary>
    private static Dictionary<string,object?> NewValues(long id,RuntimeActor actor)=>new(){["id"]=id,["yz_project_id"]=Parse(actor.ProjectId),["delete_no"]=null,["insert_time"]=DateTime.UtcNow,["update_time"]=DateTime.UtcNow,["insert_manager_id"]=actor.AccountId,["update_manager_id"]=actor.AccountId,["insert_user_name"]=actor.AccountName,["update_user_name"]=actor.AccountName};
    /// <summary>解析明确正整数主键。</summary>
    private static long Parse(string value)=>long.Parse(CompatibilityButtonPolicy.RequireId(value),CultureInfo.InvariantCulture);
    /// <summary>沿用菜单和配置页面名称长度边界。</summary>
    private static string RequireName(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=50?value:throw new InvalidOperationException("配置名称需要1到50个字符。");
    /// <summary>复制后缀预留长度，截取时不拆开Unicode字符。</summary>
    private static string CopyName(string source)
    {
        var text=new System.Text.StringBuilder();
        foreach(var rune in RequireName(source).EnumerateRunes()){if(text.Length+rune.Utf16SequenceLength>47)break;text.Append(rune.ToString());}
        return text+"_复制";
    }
    /// <summary>新节点追加到同级末尾，整数溢出时阻断创建。</summary>
    private static int NextSort(IEnumerable<MenuMetadata> siblings)=>checked(siblings.Select(menu=>menu.SortNo).DefaultIfEmpty(0).Max()+10);
}
