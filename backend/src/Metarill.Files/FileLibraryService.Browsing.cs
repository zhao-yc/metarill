using System.Data.Common;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;
namespace Metarill.Runtime.Files;

public sealed partial class FileLibraryService
{
    /// <summary>原文件库按已发布字段的访问级别浏览，字段只定位当前项目，不能构造文件读取授权。</summary>
    public Task<IReadOnlyList<FileDescriptor>> ListAccessibleAsync(string? configColumnId,string? keyword,long? folderId,CancellationToken token=default)
    {
        var column=BrowseColumn(configColumnId,authorization.RequireActor(),required:true);
        return ListVisibleCoreAsync(column,keyword,folderId,false,token);
    }
    /// <summary>完整项目目录树由前端按parentId组装；带控件时数量复用同一可见文件规则。</summary>
    public async Task<IReadOnlyList<FileFolder>> ListFolderTreeAsync(string? configColumnId=null,CancellationToken token=default)
    {
        var actor=authorization.RequireActor();var column=BrowseColumn(configColumnId,actor,required:false);
        var rows=await store.ReadAsync(Folders,new Dictionary<string,object?>{{"yz_project_id",Project(actor)}},token);
        var files=await FileCandidatesAsync(column is null?null:FileValidation.Policy(column,options).AccessMode,null,null,true,token);
        var visible=column is null?files:await VisibleRowsAsync(files,actor,token);
        var counts=visible.Where(row=>NullableId(row,"folder_id") is not null).GroupBy(row=>SqlRowStore.Id(row,"folder_id")).ToDictionary(group=>group.Key,group=>(long)group.Count());
        return rows.OrderBy(row=>row.GetValueOrDefault("insert_time") as DateTime? ?? DateTime.MinValue).ThenBy(row=>SqlRowStore.Id(row))
            .Select(row=>new FileFolder(SqlRowStore.Id(row),SqlRowStore.Text(row,"name"),NullableId(row,"parent_folder_id")){FileCount=counts.GetValueOrDefault(SqlRowStore.Id(row))}).ToArray();
    }
    /// <summary>新建文件夹可按原字段定位上下文；省略字段沿当前真实项目，不引入文件编辑权门槛。</summary>
    public Task<FileFolder> CreateLibraryFolderAsync(string name,long? parentId,string? configColumnId,CancellationToken token=default)
    {
        BrowseColumn(configColumnId,authorization.RequireActor(),required:false);
        return CreateFolderAsync(name,parentId,token);
    }
    /// <summary>原HTTP重命名返回已修改的安全描述；既有Task扩展入口保持，结果仍在同一实际事务取得。</summary>
    public Task<FileDescriptor> RenameWithResultAsync(long id,string name,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        await RenameAsync(id,name,token);return Describe(await RequiredAsync(Files,id,false,token),allowProtectedDraft:true);
    },token);
    /// <summary>原目录重命名返回修改后的节点，读取自身未提交值，不在提交后另开竞态查询。</summary>
    public Task<FileFolder> RenameFolderWithResultAsync(long id,string name,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        await RenameFolderAsync(id,name,token);return await FolderResultAsync(id,token);
    },token);
    /// <summary>原目录移动返回新parentId，保留已有无返回值Service调用。</summary>
    public Task<FileFolder> MoveFolderWithResultAsync(long id,long? parentId,CancellationToken token=default)=>store.Session.InTransactionAsync(async()=>
    {
        await MoveFolderAsync(id,parentId,token);return await FolderResultAsync(id,token);
    },token);
    /// <summary>操作已取得项目范围锁，结果与项目目录元数据的原计数规则一致。</summary>
    private async Task<FileFolder> FolderResultAsync(long id,CancellationToken token)
    {
        var row=await RequiredAsync(Folders,id,false,token);var actor=authorization.RequireActor();RequireProject(row,actor);
        var files=await store.ReadAsync(Files,new Dictionary<string,object?>{{"yz_project_id",Project(actor)},{"folder_id",id}},token);
        return new(SqlRowStore.Id(row),SqlRowStore.Text(row,"name"),NullableId(row,"parent_folder_id")){FileCount=files.LongCount(file=>SqlRowStore.Text(file,"status")!="deleted")};
    }
    /// <summary>兼容Service入口同样按真实读取过滤；HTTP文件列表始终要求原configColumnId。</summary>
    private async Task<IReadOnlyList<FileDescriptor>> ListVisibleCoreAsync(ColumnMetadata? column,string? keyword,long? folderId,bool allFolders,CancellationToken token)
    {
        var actor=authorization.RequireActor();var candidates=await FileCandidatesAsync(column is null?null:FileValidation.Policy(column,options).AccessMode,keyword,folderId,allFolders,token);
        return(await VisibleRowsAsync(candidates,actor,token)).Select(row=>Describe(row,allowProtectedDraft:true)).ToArray();
    }
    /// <summary>仅忽略明确拒权，数据库/连接/取消故障保持错误，不能伪装为空目录。</summary>
    private async Task<List<Dictionary<string,object?>>> VisibleRowsAsync(List<Dictionary<string,object?>> rows,RuntimeActor actor,CancellationToken token)
    {
        var visible=new List<Dictionary<string,object?>>();
        foreach(var row in rows)
        {
            try{await RequireReadableAsync(row,actor,token);visible.Add(row);}
            catch(Exception denied)when(denied is UnauthorizedAccessException or SecurityException){ }
        }
        return visible;
    }
    /// <summary>控件必须来自本项目已发布的真实物理文件列，不能借其它项目字段改变模式。</summary>
    private ColumnMetadata? BrowseColumn(string? id,RuntimeActor actor,bool required)
    {
        if(string.IsNullOrWhiteSpace(id))return required?throw new ArgumentException("字段不是文件库控件。"):null;
        foreach(var catalog in registry.Catalogs.Where(catalog=>catalog.YzProjectId==actor.ProjectId))
        foreach(var table in catalog.ConfigTables)
        {
            var column=registry.PhysicalColumns(catalog,table).SingleOrDefault(column=>column.Id==id&&column.ControlKind=="FileLibrary");
            if(column is not null)return column;
        }
        throw new UnauthorizedAccessException("文件库字段不属于当前项目发布模型。");
    }
    /// <summary>只读取固定文件描述及授权列；搜索按原分词/字符有序LIKE跨目录，值全部参数化。</summary>
    private async Task<List<Dictionary<string,object?>>> FileCandidatesAsync(string? mode,string? keyword,long? folderId,bool allFolders,CancellationToken token)
    {
        var actor=authorization.RequireActor();var provider=store.Session.Provider;var transaction=store.Session.CurrentTransaction;
        await using var owned=transaction is null?provider.CreateConnection(store.Session.Options):null;
        var connection=transaction?.Connection??owned??throw new InvalidOperationException("文件浏览事务连接不可用。");if(owned is not null)await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.Transaction=transaction;
        var names=new[]{"id","name","content_type","size","access_mode","status","folder_id","sha256","owner_id","yz_project_id","insert_time"};
        var sql="SELECT "+string.Join(",",names.Select(provider.QuoteIdentifier))+" FROM "+provider.QuoteIdentifier(Files)+" WHERE "+provider.QuoteIdentifier("yz_project_id")+"="+Bind("browse_project",Project(actor))+" AND "+provider.QuoteIdentifier("delete_no")+" IS NULL AND "+provider.QuoteIdentifier("status")+"<>"+Bind("deleted_status","deleted");
        if(mode is not null)sql+=" AND "+provider.QuoteIdentifier("access_mode")+"="+Bind("browse_mode",mode);
        var words=string.IsNullOrWhiteSpace(keyword)?Array.Empty<string>():Regex.Split(keyword.Trim(),@"\s+");
        if(words.Length==0&&!allFolders)sql+=" AND "+provider.QuoteIdentifier("folder_id")+(folderId is null?" IS NULL":"="+Bind("browse_folder",folderId.Value));
        for(var index=0;index<words.Length;index++)sql+=" AND "+provider.QuoteIdentifier("name")+" LIKE "+Bind("search_"+index,"%"+string.Join("%",words[index].EnumerateRunes().Select(rune=>rune.ToString()))+"%");
        command.CommandText=sql+" ORDER BY "+provider.QuoteIdentifier("insert_time")+" DESC,"+provider.QuoteIdentifier("id")+" DESC";
        await using var reader=await command.ExecuteReaderAsync(token);var rows=new List<Dictionary<string,object?>>();
        while(await reader.ReadAsync(token)){var row=new Dictionary<string,object?>();for(var index=0;index<reader.FieldCount;index++)row.Add(reader.GetName(index),await reader.IsDBNullAsync(index,token)?null:reader.GetValue(index));rows.Add(row);}return rows;
        /// <summary>固定参数名和值分开传递，关键词不进入SQL语法。</summary>
        string Bind(string name,object value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value;command.Parameters.Add(parameter);return provider.ParameterPlaceholder(name);}
    }
    /// <summary>文件夹是项目共享展示元数据，所有者只记录创建者，不作为独立授权边界。</summary>
    private static void RequireProject(IReadOnlyDictionary<string,object?> row,RuntimeActor actor)
    {
        if(SqlRowStore.Text(row,"yz_project_id")!=actor.ProjectId)throw new UnauthorizedAccessException("文件或目录不属于当前项目。");
    }
    /// <summary>展示名不参与磁盘定位，只按原规则修剪、检查空值和长度。</summary>
    private static string DisplayName(string? name,int maximum)
    {
        var value=name?.Trim()??"";if(value.Length==0||value.Length>maximum)throw new InvalidOperationException("名称不能为空或超过允许长度。");return value;
    }
    /// <summary>范围锁已持有，同事务按数据库排序规则检查项目同级重名，排除自身及墓碑。</summary>
    private async Task RequireSiblingNameAsync(RuntimeActor actor,long? parentId,string name,long? exclude,CancellationToken token)
    {
        var matches=await store.ReadForUpdateAsync(Folders,new Dictionary<string,object?>{{"yz_project_id",Project(actor)},{"parent_folder_id",parentId},{"name",name}},token);
        if(matches.Any(row=>SqlRowStore.Id(row)!=exclude))throw new InvalidOperationException("同级目录已存在相同名称。");
    }
    /// <summary>删除文件仍限定上传者或当前超级身份；读取权不能变成物理删除权。</summary>
    private void RequireDeleteOwner(IReadOnlyDictionary<string,object?> row,RuntimeActor actor)
    {
        RequireProject(row,actor);if(!actor.IsAdministrator&&!authorization.IsCurrentRealmSuper()&&SqlRowStore.Id(row,"owner_id")!=actor.AccountId)throw new UnauthorizedAccessException("无权删除该文件。");
    }
}
