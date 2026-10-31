using Metarill.Core.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Menus;

public sealed class MenuSaveRequest
{
    public string ProjectId {get;set;}="";
    public string AdminTypeId {get;set;}="";
    public string Revision {get;set;}="";
    public string Id {get;set;}="";
    public string Kind {get;set;}="DIRECTORY";
    public string? ParentId {get;set;}
    public string Name {get;set;}="";
    public string? Icon {get;set;}
    public int SortNo {get;set;}
    public bool Hidden {get;set;}
    public string? TargetPageId {get;set;}
    public bool ShowCount {get;set;}
    public string? VisibleScheme {get;set;}
}
/// <summary>菜单业务协调页面归属、树校验、版本冲突和同库事务，不属于表级 CRUD。</summary>
public sealed class MenuWorkbenchBusiness(SqlRowStore store,MetadataRepository metadata,HttpRuntimeAuthorization authorization,IIdGenerator ids)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,SemaphoreSlim> Gates=new();
    public async Task<object> SnapshotAsync(string projectId,string? adminTypeId,CancellationToken cancellationToken=default)
    {
        authorization.RequireAdministrator();
        var catalog=await metadata.LoadProjectAsync(projectId,cancellationToken);
        var admin=string.IsNullOrEmpty(adminTypeId)?catalog.YzAdminTypes.FirstOrDefault()?.Id??"":adminTypeId;
        if(!catalog.YzAdminTypes.Any(a=>a.Id==admin))throw new InvalidOperationException("后台类型不存在。");
        Validate(catalog);
        var menus=catalog.ConfigMenus.Where(m=>m.YzAdminTypeId==admin).ToArray();
        object[] Build(string? parent,string? root)=>(menus.Where(m=>string.IsNullOrEmpty(m.ParentMenuId)?parent is null:m.ParentMenuId==parent).OrderBy(m=>m.SortNo).ThenBy(m=>m.Id,StringComparer.Ordinal).Select(m=>(object)new
        {m.Id,parentId=m.ParentMenuId,firstMenuId=root??m.Id,m.Kind,m.Name,m.SortNo,icon=m.IconName,hidden=m.IsHidden==1,targetPageId=m.ConfigPageId??m.ConfigCustomPageId??"",showCount=m.IsShowCount==1,m.VisibleScheme,children=Build(m.Id,root??m.Id)})).ToArray();
        return new {projectId,adminTypeId=admin,adminTypes=catalog.YzAdminTypes,revision=Revision(catalog),isV8=true,canWrite=true,capabilities=new{first=new{create=true,update=true,delete=true},second=new{create=true,update=true,delete=true}},tree=Build(null,null),targets=catalog.ConfigPages.Where(p=>p.YzAdminTypeId==admin).Select(p=>(object)new{id=p.Id,kind="GENERATED_PAGE",name=p.Name,route=Route(catalog,p),showCountSupported=true}).Concat(catalog.ConfigCustomPages.Where(p=>p.YzAdminTypeId==admin).Select(p=>(object)new{id=p.Id,kind="CUSTOM_PAGE",name=p.Name,route=CustomRoute(p),showCountSupported=false}))};
    }
    public async Task<object> SaveAsync(MenuSaveRequest request,bool deleting=false,CancellationToken cancellationToken=default)
    {
        var actor=authorization.RequireAdministrator();var gate=Gates.GetOrAdd(request.ProjectId,_=>new SemaphoreSlim(1,1));await gate.WaitAsync(cancellationToken);
        try
        {
            await store.Session.InTransactionAsync(async()=>
            {
                // 锁项目行，让同项目菜单的不同进程写入也串行；随后重读并比较快照。
                await store.ReadForUpdateAsync("yz_project",new Dictionary<string,object?>{{"id",long.Parse(request.ProjectId)}},cancellationToken);
                var catalog=await metadata.LoadProjectAsync(request.ProjectId,cancellationToken);
                if(request.Revision!=Revision(catalog))throw new InvalidOperationException("YZP_CONFLICT：菜单已变化，请刷新后重试。");
                var current=string.IsNullOrEmpty(request.Id)?null:catalog.ConfigMenus.SingleOrDefault(m=>m.Id==request.Id)??throw new InvalidOperationException("菜单不存在。");
                if(current is not null&&current.YzAdminTypeId!=request.AdminTypeId)throw new InvalidOperationException("不能跨后台修改菜单。");
                if(deleting)
                {
                    if(current is null)throw new InvalidOperationException("菜单不存在。");
                    if(catalog.ConfigMenus.Any(m=>m.ParentMenuId==current.Id))throw new InvalidOperationException("请先处理子菜单。");
                    await store.UpdateAsync("config_menu",long.Parse(current.Id),new Dictionary<string,object?>{{"delete_no",BusinessCalendar.Now},{"update_manager_id",actor.AccountId}},cancellationToken);
                    return true;
                }
                if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>50)throw new InvalidOperationException("菜单名称需要1到50个字符。");
                var kind=request.Kind=="FIRST_MENU"?"DIRECTORY":request.Kind;
                if(kind is not ("DIRECTORY" or "GENERATED_PAGE" or "CUSTOM_PAGE"))throw new InvalidOperationException("菜单类型无效。");
                var node=new MenuMetadata {Id=current?.Id??ids.NextId().ToString(),YzProjectId=request.ProjectId,YzAdminTypeId=request.AdminTypeId,Name=request.Name.Trim(),ParentMenuId=request.Kind=="FIRST_MENU"?null:request.ParentId,Kind=kind,ConfigPageId=kind=="GENERATED_PAGE"?request.TargetPageId:null,ConfigCustomPageId=kind=="CUSTOM_PAGE"?request.TargetPageId:null,SortNo=request.SortNo,IconName=request.Icon,IsHidden=request.Hidden?1:0,IsShowCount=request.ShowCount?1:0,Extra=[]};
                catalog.ConfigMenus.RemoveAll(m=>m.Id==node.Id);catalog.ConfigMenus.Add(node);Validate(catalog);
                var values=new Dictionary<string,object?>{{"id",long.Parse(node.Id)},{"yz_project_id",long.Parse(node.YzProjectId)},{"yz_admin_type_id",long.Parse(node.YzAdminTypeId)},{"name",node.Name},{"parent_menu_id",string.IsNullOrEmpty(node.ParentMenuId)?null:long.Parse(node.ParentMenuId)},{"kind",node.Kind},{"config_page_id",string.IsNullOrEmpty(node.ConfigPageId)?null:long.Parse(node.ConfigPageId)},{"config_custom_page_id",string.IsNullOrEmpty(node.ConfigCustomPageId)?null:long.Parse(node.ConfigCustomPageId)},{"sort_no",node.SortNo},{"icon_name",node.IconName},{"is_hidden",node.IsHidden},{"is_show_count",node.IsShowCount},{"visible_scheme",request.VisibleScheme},{"update_time",BusinessCalendar.Now},{"update_manager_id",actor.AccountId}};
                if(current is null){values["insert_time"]=BusinessCalendar.Now;values["insert_manager_id"]=actor.AccountId;await store.InsertAsync("config_menu",values,cancellationToken);}else await store.UpdateAsync("config_menu",long.Parse(node.Id),values,cancellationToken);
                return true;
            },cancellationToken);
            return await SnapshotAsync(request.ProjectId,request.AdminTypeId,cancellationToken);
        }
        finally{gate.Release();}
    }
    /// <summary>提供旧菜单组件实际读取的展示字段及刷新上下文，导航可见性仍独立于页面操作授权。</summary>
    public async Task<object[]> NavigationAsync(string projectId,CancellationToken cancellationToken=default)
    {
        var actor=authorization.RequireActor();var catalog=await metadata.LoadProjectAsync(projectId,cancellationToken);Validate(catalog);
        var menus=catalog.ConfigMenus.Where(m=>m.IsHidden!=1).ToArray();
        object[] Build(string? parent)=>menus.Where(m=>string.IsNullOrEmpty(m.ParentMenuId)?parent is null:m.ParentMenuId==parent).OrderBy(m=>m.SortNo).ThenBy(m=>m.Id,StringComparer.Ordinal).Where(m=>actor.IsAdministrator||m.Kind=="DIRECTORY"||authorization.RequireIdentity().MenuIds.Contains(m.Id)).Select(m=>(object)new
        {id=m.Id,menu_id=m.Id,m.Name,text=m.Name,icon=m.IconName,projectId=m.YzProjectId,adminTypeId=m.YzAdminTypeId,nodeType=m.Kind,isShowCount=m.IsShowCount,target=Target(catalog,m),children=Build(m.Id)}).ToArray();
        return Build(null);
    }
    /// <summary>只把当前目录中登记的页面映射为内部前端路由，自定义页不携带计数接口。</summary>
    private static object? Target(MetadataCatalog catalog,MenuMetadata menu)
    {
        if(menu.ConfigPageId is{} id&&catalog.ConfigPages.FirstOrDefault(p=>p.Id==id)is{} page)
        {
            var table=catalog.ConfigTables.Single(t=>t.Id==page.ConfigTableId);
            return new{route=Route(catalog,page),tableName=table.TableName,pageId=page.Id,tableId=page.ConfigTableId,countEndpoint="/admin/auto/"+table.TableName+"/"+page.Id+"/count"};
        }
        if(menu.ConfigCustomPageId is{} custom&&catalog.ConfigCustomPages.FirstOrDefault(p=>p.Id==custom)is{} target)return new{route=CustomRoute(target),pageId=target.Id};
        return null;
    }
    private static string CustomRoute(CustomPageMetadata page)
    {
        var path=page.PagePath.TrimStart('/');
        if(!System.Text.RegularExpressions.Regex.IsMatch(path,"^pages/[a-zA-Z0-9_/-]+$")||path.Contains(".."))throw new InvalidOperationException("自定义页面路由无效。");
        return "/"+path;
    }
    private bool CanRead(string pageId){try{authorization.RequirePagePermission(pageId,"list");return true;}catch(UnauthorizedAccessException){return false;}}
    public static string Route(MetadataCatalog catalog,PageMetadata page)=>"/pages/yzp/"+catalog.ConfigTables.Single(t=>t.Id==page.ConfigTableId).TableName+"-list-"+page.Id;
    public static string Revision(MetadataCatalog catalog)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{menus=catalog.ConfigMenus.OrderBy(m=>m.Id,StringComparer.Ordinal),pages=catalog.ConfigPages.OrderBy(p=>p.Id,StringComparer.Ordinal)},JsonDefaults.Options)))).ToLowerInvariant();
    public static void Validate(MetadataCatalog catalog)
    {
        if(catalog.ConfigMenus.Count>5000)throw new InvalidOperationException("菜单数量超过服务端上限。");
        var nodes=catalog.ConfigMenus.ToDictionary(m=>m.Id);
        foreach(var node in nodes.Values)
        {
            if(node.YzProjectId!=catalog.YzProjectId||!catalog.YzAdminTypes.Any(a=>a.Id==node.YzAdminTypeId))throw new InvalidOperationException("菜单后台归属无效。");
            if(node.ConfigPageId is{}pageId&&pageId.Length>0&&!catalog.ConfigPages.Any(p=>p.Id==pageId&&p.YzAdminTypeId==node.YzAdminTypeId))throw new InvalidOperationException("目标页面不属于当前后台。");
            if(node.Kind=="GENERATED_PAGE"&&string.IsNullOrEmpty(node.ConfigPageId)||node.Kind=="CUSTOM_PAGE"&&string.IsNullOrEmpty(node.ConfigCustomPageId))throw new InvalidOperationException("导航菜单必须选择目标页面。");
            if(node.ConfigCustomPageId is{} custom&&custom.Length>0&&!catalog.ConfigCustomPages.Any(p=>p.Id==custom&&p.YzAdminTypeId==node.YzAdminTypeId))throw new InvalidOperationException("自定义页面不属于当前后台。");
            if(node.IsShowCount==1&&node.Kind!="GENERATED_PAGE")throw new InvalidOperationException("只有标准页面支持数量角标。");
            var seen=new HashSet<string>{node.Id};var parent=node.ParentMenuId;
            while(!string.IsNullOrEmpty(parent))
            {
                if(seen.Count>32)throw new InvalidOperationException("菜单层级不能超过32层。");
                if(!seen.Add(parent))throw new InvalidOperationException("菜单父级不能成环。");
                if(!nodes.TryGetValue(parent,out var ancestor)||ancestor.YzAdminTypeId!=node.YzAdminTypeId||ancestor.YzProjectId!=node.YzProjectId||ancestor.Kind!="DIRECTORY")throw new InvalidOperationException("父菜单归属或类型无效。");
                parent=ancestor.ParentMenuId;
            }
        }
    }
}
