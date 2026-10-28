using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Agent;

/// <summary>Agent只消费宿主固定发布的资源和身份范围，不能借客户端项目参数扩大签发域。</summary>
public interface IAgentHostResources : ISecurityResourceRegistry
{
    AgentHostCatalog AgentCatalog { get; }
}

/// <summary>能力目录实际使用的最小只读快照，不暴露可变元数据或数据库连接。</summary>
public sealed record AgentPublishedPage(string ProjectId, string Id, string Name, string TableName,
    bool RequiresParent, IReadOnlyList<string> RequiredCreateFields);

/// <summary>平台保持原平台身份规则；独立应用只允许自身发布后台，不产生平台管理员身份。</summary>
public sealed class AgentHostCatalog
{
    private readonly FrozenDictionary<string, AgentPublishedPage> pages;
    private readonly FrozenSet<string> adminTypes;
    private readonly bool platform;
    public string PendingProjectId { get; }
    public string ApprovalPermissionMessage => platform ? "批准Agent授权需要平台管理员权限。" : "批准Agent授权需要当前应用后台的超级角色。";

    /// <summary>只冻结已验证的发布页、必填规则和签发后台，不保存调用者可变对象。</summary>
    private AgentHostCatalog(MetadataCatalog issuer, IReadOnlyList<MetadataCatalog> catalogs, bool platform)
    {
        foreach (var catalog in catalogs) MetadataValidation.Validate(catalog);
        if (!long.TryParse(issuer.YzProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new InvalidOperationException("Agent签发项目必须为已发布的有效ID。");
        PendingProjectId = issuer.YzProjectId; this.platform = platform;
        adminTypes = issuer.YzAdminTypes.Where(admin => admin.YzProjectId == issuer.YzProjectId).Select(admin => admin.Id).ToFrozenSet(StringComparer.Ordinal);
        var definitions = catalogs.SelectMany(catalog => catalog.ConfigPages.Select(page =>
        {
            var table = catalog.ConfigTables.Single(table => table.Id == page.ConfigTableId);
            var required = catalog.ConfigPageColumns.Where(field => field.ConfigPageId == page.Id && field.IsEdit == 1 && field.IsRequired == 1)
                .Select(field => catalog.ConfigColumns.Single(column => column.Id == field.ConfigColumnId))
                .Where(column => column.DefaultValue.Length == 0).Select(column => JsonName(column.ColumnName)).Distinct(StringComparer.Ordinal).ToArray();
            return new AgentPublishedPage(catalog.YzProjectId, page.Id, page.Name, table.TableName,
                page.WhereSql.Contains("@parent", StringComparison.OrdinalIgnoreCase), Array.AsReadOnly(required));
        })).ToArray();
        if (definitions.Select(page => page.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new InvalidOperationException("Agent发布目录存在重复页面ID。");
        pages = definitions.ToFrozenDictionary(page => page.Id, StringComparer.Ordinal);
    }

    /// <summary>仅平台宿主使用；各项目页面仍按实际账号当前权限过滤。</summary>
    public static AgentHostCatalog ForPlatform(MetadataCatalog platform, MetadataCatalog demo) => new(platform, [platform, demo], true);
    /// <summary>由完整包的已核编译目录构造；不接受外部目录补充后台、页面或平台身份标志。</summary>
    public static AgentHostCatalog ForApplication(MetadataCatalog application) => new(application, [application], false);
    /// <summary>固定页面ID只能命中一份发布快照；未知页面不进入能力目录。</summary>
    public AgentPublishedPage? FindPage(string id) => pages.GetValueOrDefault(id);
    /// <summary>身份域来自实际账号/后台/项目链；迁出签发域后旧授权不能再换取凭据。</summary>
    public bool IsEligibleRealm(PrincipalRoleRealm realm) => platform ? realm.IsPlatformRealm
        : realm.ProjectId == PendingProjectId && adminTypes.Contains(realm.AdminTypeId);
    /// <summary>人工批准同时要求真实签发域与当前超级角色，不从工作区或客户端选项推断。</summary>
    public bool CanApprove(PrincipalRoleRealm realm) => platform ? realm.IsPlatformAdministrator : IsEligibleRealm(realm) && realm.IsSuper;
    /// <summary>沿用现有原字段JSON命名规则，冻结后的元数据变化不能修改已签发目录。</summary>
    private static string JsonName(string name) => Regex.Replace(name, "_([a-z])", match => match.Groups[1].Value.ToUpperInvariant());
}
