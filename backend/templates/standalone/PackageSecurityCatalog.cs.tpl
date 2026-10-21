using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace {{NAMESPACE}}.Host;

/// <summary>完整包目录只信任编译时绑定的脱敏UTF8/LF字节；manifest与调用者可变对象都不是权威。</summary>
internal sealed class VerifiedPackageSecurityCatalog
{
    private const string ExpectedSha256=PackageSecurityCatalogBinding.Sha256;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly byte[] frozen;
    private readonly MetadataCatalog catalog;
    private readonly IReadOnlyList<PagePermissionInheritanceLink> inheritance;
    internal object Generation {get;}=new();
    internal string ProjectId=>catalog.YzProjectId;
    internal string ProjectName=>catalog.YzProjectName;
    internal string Sha256=>ExpectedSha256;
    internal IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs {get;}

    /// <summary>先私有复制再验证固定摘要与文本编码；外部数组变化不能让核验字节与使用字节分叉。</summary>
    internal VerifiedPackageSecurityCatalog(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);frozen=bytes.ToArray();
        if(frozen.Length is 0 or >32*1024*1024||frozen.Contains((byte)'\r')||frozen.AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf})||Convert.ToHexStringLower(SHA256.HashData(frozen))!=ExpectedSha256)
            throw new InvalidOperationException("应用固定目录与编译摘要不一致，请重新同步完整依赖并构建。");
        try{_ = new UTF8Encoding(false,true).GetString(frozen);}
        catch(DecoderFallbackException){throw new InvalidOperationException("应用固定目录必须是规范UTF8/LF。");}
        catalog=Clone();MetadataValidation.Validate(catalog);
        if(!Positive(ProjectId)||catalog.YzAdminTypes.Any(admin=>!Positive(admin.Id)||admin.YzProjectId!=ProjectId))throw new InvalidOperationException("应用固定目录的项目或后台归属无效。");
        inheritance=PagePermissionInheritance.Compile(catalog);
        PublishedCatalogs=Array.AsReadOnly(new[]{ProjectResources(null)});
    }
    /// <summary>固定文件路径有大小上限，启动只读取一次；不读取自报摘要或热重载配置。</summary>
    internal static VerifiedPackageSecurityCatalog Load(string path)
    {
        var info=new FileInfo(path);
        if(!info.Exists||info.Length is 0 or >32*1024*1024)throw new InvalidOperationException("缺少有界的应用固定目录。");
        return new(File.ReadAllBytes(path));
    }
    /// <summary>完整JSON每次独立反序列化，未知合法键与revision内容保留，调用者修改不会污染权威。</summary>
    internal MetadataCatalog Clone()=>JsonSerializer.Deserialize<MetadataCatalog>(frozen,Json)??throw new InvalidOperationException("应用固定目录无效。");
    /// <summary>后台必须为本项目声明的规范长ID，不接受客户端补充身份范围。</summary>
    internal bool ContainsAdmin(string id)=>Positive(id)&&catalog.YzAdminTypes.Any(admin=>admin.Id==id&&admin.YzProjectId==ProjectId);
    /// <summary>仅从已验证固定摘要的私有模型读取后台账号匹配字段，新应用不需要手工补写安全库配置。</summary>
    internal string? ExactDataManageManagerField(string projectId,string adminTypeId)
    {
        if(projectId!=ProjectId||!ContainsAdmin(adminTypeId))return null;
        var admin=catalog.YzAdminTypes.Single(admin=>admin.Id==adminTypeId);
        if(!admin.Extra.TryGetValue("exactDataManageMatchField",out var value)||value.ValueKind==JsonValueKind.Null)return "";
        return value.ValueKind==JsonValueKind.String?value.GetString():throw new NotSupportedException("已发布后台匹配字段声明无效。");
    }
    /// <summary>Realm来自真实锁后身份；既不是客户端工作区，也不允许包外后台进入目录证明。</summary>
    internal void RequireRealm(long project,long admin)
    {
        if(project.ToString(CultureInfo.InvariantCulture)!=ProjectId||!ContainsAdmin(admin.ToString(CultureInfo.InvariantCulture)))throw new UnauthorizedAccessException("目录项目或后台不属于当前完整包。");
    }
    /// <summary>只返回不可改集合中的值对象；每次当前证明使用新的资源对象身份。</summary>
    internal SecurityResourceCatalog ProjectResources(string? admin)
    {
        var pages=catalog.ConfigPages.Where(page=>page.YzProjectId==ProjectId&&(admin is null||page.YzAdminTypeId==admin)).ToArray();
        var ids=pages.Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        return new(ProjectId,Array.AsReadOnly(catalog.ConfigTables.Select(table=>new SecurityTableResource(table.Id,table.TableName)).ToArray()),
            Array.AsReadOnly(pages.Select(page=>new SecurityPageResource(page.Id,page.ConfigTableId,page.YzProjectId,page.YzAdminTypeId,page.IsNew==1,PageUpdatePolicy.CanUpdate(page),page.IsDelete==1,
                page.IsExport==1||page.Extra.TryGetValue("exportMethod",out var export)&&export.ToString() is "list" or "edit",page.IsCopy==1,page.IsImport==1||page.Extra.TryGetValue("importMethod",out var import)&&!string.IsNullOrWhiteSpace(import.ToString()))).ToArray()),
            Array.AsReadOnly(catalog.ConfigMenus.Where(menu=>menu.YzProjectId==ProjectId&&(admin is null||menu.YzAdminTypeId==admin)).Select(menu=>new SecurityMenuResource(menu.Id,menu.YzProjectId,menu.YzAdminTypeId)).ToArray()),
            Array.AsReadOnly(catalog.ConfigButtons.Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("id",out _)&&button.TryGetProperty("configPageId",out var page)&&ids.Contains(page.ToString()))
                .Select(button=>new SecurityButtonResource(button.GetProperty("id").ToString(),button.GetProperty("configPageId").ToString())).ToArray()))
        {Inheritance=Array.AsReadOnly(inheritance.Where(link=>ids.Contains(link.SourcePageId)&&ids.Contains(link.TargetPageId)).ToArray())};
    }
    /// <summary>菜单引用只来自冻结完整目录，不按发布投影缺失推断无引用。</summary>
    internal bool HasMenus(string admin)=>catalog.ConfigMenus.Any(menu=>menu.YzProjectId==ProjectId&&menu.YzAdminTypeId==admin);
    /// <summary>标识保持规范十进制字符串，不隐式接受前导零或表达式。</summary>
    private static bool Positive(string id)=>long.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out var value)&&value>0&&id==value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>显式不可变包权威适配；不将PublishedCatalogs冒充平台实时目录，也不访问不存在的config表。</summary>
internal sealed class ImmutablePackageSecurityResources(SqlRowStore store,VerifiedPackageSecurityCatalog catalog):ISecurityCurrentResources,IRolePermissionCatalog
{
    private sealed record Proof(DatabaseSession Session,DbTransaction Transaction,DbConnection Connection,object Generation,long Project,long Admin,string Sha256);
    private readonly ConditionalWeakTable<SecurityResourceCatalog,Proof> loaded=new();
    /// <summary>普通工作台读取同一完整冻结代的独立副本，无数据库或发布目录回退。</summary>
    public Task<MetadataCatalog> ReadAsync(DatabaseSession session,string projectId,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();RequireSession(session);
        if(projectId!=catalog.ProjectId)throw new UnauthorizedAccessException("目录项目不属于当前完整包。");
        return Task.FromResult(catalog.Clone());
    }
    /// <summary>聚合预锁后的同一事务才可签发目录证明；这里只证明不变目录，不替代身份及授权行当前读。</summary>
    public Task<SecurityResourceCatalog> ReadForUpdateAsync(DatabaseSession session,DbTransaction transaction,long projectId,long adminTypeId,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();var connection=RequireCurrent(session,transaction);catalog.RequireRealm(projectId,adminTypeId);
        var resources=catalog.ProjectResources(adminTypeId.ToString(CultureInfo.InvariantCulture));
        loaded.Add(resources,new(session,transaction,connection,catalog.Generation,projectId,adminTypeId,catalog.Sha256));return Task.FromResult(resources);
    }
    /// <summary>同一适配、对象身份、连接事务和目录世代缺一不可；完整revision不能由瘦资源对象重建。</summary>
    public MetadataCatalog RequireLoaded(DatabaseSession session,DbTransaction transaction,SecurityResourceCatalog resources)
    {
        var connection=RequireCurrent(session,transaction);
        if(!loaded.TryGetValue(resources,out var proof)||!ReferenceEquals(proof.Session,session)||!ReferenceEquals(proof.Transaction,transaction)||!ReferenceEquals(proof.Connection,connection)||!ReferenceEquals(proof.Generation,catalog.Generation)||proof.Sha256!=catalog.Sha256)
            throw new InvalidOperationException("缺少本事务同一固定目录证明。");
        catalog.RequireRealm(proof.Project,proof.Admin);return catalog.Clone();
    }
    /// <summary>固定菜单引用仍核实际事务与后台；没有config存储也绝不能把未知状态解释成无引用。</summary>
    public Task<bool> HasAdminMenuReferencesForUpdateAsync(DatabaseSession session,DbTransaction transaction,long adminTypeId,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();RequireCurrent(session,transaction);catalog.RequireRealm(long.Parse(catalog.ProjectId,CultureInfo.InvariantCulture),adminTypeId);
        return Task.FromResult(catalog.HasMenus(adminTypeId.ToString(CultureInfo.InvariantCulture)));
    }
    /// <summary>适配器和调用者必须持有同一个实际Session，不连接其它数据库兜底。</summary>
    private void RequireSession(DatabaseSession session){if(!ReferenceEquals(store.Session,session))throw new InvalidOperationException("固定目录必须使用同一数据库Session。");}
    /// <summary>连接必须实际打开，不能用仅有事务对象或已结束事务伪造准备状态。</summary>
    private DbConnection RequireCurrent(DatabaseSession session,DbTransaction transaction)
    {
        RequireSession(session);
        if(transaction is null||!ReferenceEquals(session.CurrentTransaction,transaction)||transaction.Connection is not{State:ConnectionState.Open} connection)throw new InvalidOperationException("固定目录必须使用同一当前连接及事务。");
        return connection;
    }
}

/// <summary>两目录接口必须映射同一个Scoped适配；本阶段不注册Reader、Guard、工作台或控制器。</summary>
internal static class ApplicationSecurityCatalogRegistration
{
    internal static void Add(IServiceCollection services,VerifiedPackageSecurityCatalog catalog)
    {
        services.AddSingleton(catalog);services.AddScoped<ImmutablePackageSecurityResources>();
        services.AddScoped<ISecurityCurrentResources>(provider=>provider.GetRequiredService<ImmutablePackageSecurityResources>());
        services.AddScoped<IRolePermissionCatalog>(provider=>provider.GetRequiredService<ImmutablePackageSecurityResources>());
    }
}
