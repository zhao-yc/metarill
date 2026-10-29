using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
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
    bool RequiresParent, IReadOnlyList<string> RequiredCreateFields)
{
    public string TableId { get; init; } = "";
    public bool IsFunctionTable { get; init; }
    public bool CanUpdate { get; init; }
    public string ReadOnlyCondition { get; init; } = "";
    public bool HasOwnedActionSource { get; init; }
    internal IReadOnlyDictionary<string,string>? RecordNames {get;init;}
}

/// <summary>按钮路径、确认与可见规则来自发布快照，不能被调用方元数据或请求覆盖。</summary>
public sealed record AgentPublishedButton(string Id, AgentPublishedPage Page, string Path, bool ConfirmationRequired,
    string ConfirmationMessage, string Visibility)
{
    internal ButtonVisibilityRule? Rule {get;init;}
    internal ReadOnlyConditionRule? ReadOnlyRule {get;init;}
    internal IReadOnlyDictionary<string,string> RecordNames {get;init;} = FrozenDictionary<string,string>.Empty;
}

/// <summary>平台保持原平台身份规则；独立应用只允许自身发布后台，不产生平台管理员身份。</summary>
public sealed class AgentHostCatalog
{
    private readonly FrozenDictionary<string, AgentPublishedPage> pages;
    private readonly FrozenSet<string> adminTypes;
    private readonly FrozenDictionary<string, AgentPublishedButton> buttons;
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
                page.WhereSql.Contains("@parent", StringComparison.OrdinalIgnoreCase), Array.AsReadOnly(required))
            {
                TableId = table.Id, IsFunctionTable = table.IsFunctionTable == 1, RecordNames=CompileRecordNames(catalog,table.Id),
                CanUpdate = PageUpdatePolicy.CanUpdate(page),
                ReadOnlyCondition = page.Extra.GetValueOrDefault("readOnlyCondition").ToString(),
                HasOwnedActionSource = table.YzProjectId == catalog.YzProjectId && page.YzProjectId == catalog.YzProjectId
            };
        })).ToArray();
        if (definitions.Select(page => page.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new InvalidOperationException("Agent发布目录存在重复页面ID。");
        pages = definitions.ToFrozenDictionary(page => page.Id, StringComparer.Ordinal);
        // 无效/重复业务声明只从业务目录排除，不影响既有标准页面目录。
        var published = catalogs.SelectMany(catalog => catalog.ConfigButtons.Select(button => PublishButton(catalog, button)))
            .OfType<AgentPublishedButton>().ToArray();
        var duplicateIds = catalogs.SelectMany(catalog => catalog.ConfigButtons).Where(button => button.ValueKind == JsonValueKind.Object)
            .Select(button => Text(button, "id")).GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() != 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        buttons = published.Where(button => !duplicateIds.Contains(button.Id)).ToFrozenDictionary(button => button.Id, StringComparer.Ordinal);
    }

    /// <summary>仅平台宿主使用；各项目页面仍按实际账号当前权限过滤。</summary>
    public static AgentHostCatalog ForPlatform(MetadataCatalog platform, MetadataCatalog demo) => new(platform, [platform, demo], true);
    /// <summary>由完整包的已核编译目录构造；不接受外部目录补充后台、页面或平台身份标志。</summary>
    public static AgentHostCatalog ForApplication(MetadataCatalog application) => new(application, [application], false);
    /// <summary>固定页面ID只能命中一份发布快照；未知页面不进入能力目录。</summary>
    public AgentPublishedPage? FindPage(string id) => pages.GetValueOrDefault(id);
    /// <summary>只返回固定且唯一的业务按钮，未知或归属冲突不自动猜测。</summary>
    public AgentPublishedButton? FindButton(string id) => buttons.GetValueOrDefault(id);
    /// <summary>原业务按钮路径必须是本应用绝对路径，确认不能由动作声明反向修改。</summary>
    private AgentPublishedButton? PublishButton(MetadataCatalog catalog, JsonElement button)
    {
        if (button.ValueKind != JsonValueKind.Object || Text(button, "actionType") != "1") return null;
        var id = Text(button, "id"); var page = FindPage(Text(button, "configPageId")); var path = Text(button, "sql");
        if (string.IsNullOrWhiteSpace(id) || page is null || !page.HasOwnedActionSource || page.ProjectId != catalog.YzProjectId
            || Text(button, "yzProjectId") is var project && project.Length != 0 && project != page.ProjectId
            || Text(button, "configTableId") is var table && table.Length != 0 && table != page.TableId || !IsApplicationPath(path)) return null;
        var visibility = Text(button, "visibleScheme");
        // 可写按钮沿原Java受限语法发布；不支持的条件仍拒绝，普通按钮不额外套页面只读规则。
        if (visibility is not ("Normal" or "AlwaysVisible" or "VisibleWhenWritable")
            || visibility == "VisibleWhenWritable" && !page.CanUpdate) return null;
        ButtonVisibilityRule rule;ReadOnlyConditionRule? readOnly=null;
        try
        {
            rule=ButtonVisibilityRule.Compile(Text(button,"visibleRuleJson"));
            if(visibility=="VisibleWhenWritable"&&!page.IsFunctionTable)readOnly=ReadOnlyConditionRule.Compile(page.ReadOnlyCondition);
        }
        catch(ArgumentException){return null;}
        var names=page.RecordNames;if(names is null)return null;
        if(readOnly is not null&&readOnly.Fields.Any(field=>!names.ContainsKey(field.Split('.')[0])))return null;
        return new(id, page, path, Text(button, "isNeedConfirm") == "1" || Text(button, "isNeedConfirm").Equals("true", StringComparison.OrdinalIgnoreCase), Text(button, "confirmMsg"), visibility){Rule=rule,ReadOnlyRule=readOnly,RecordNames=names.ToFrozenDictionary(StringComparer.Ordinal)};
    }
    /// <summary>页面和按钮共用冻结的物理/JSON别名，只按真实元数据声明绑定，不读客户端字段定义。</summary>
    private static IReadOnlyDictionary<string,string>? CompileRecordNames(MetadataCatalog catalog,string tableId)
    {
        var names=new Dictionary<string,string>(StringComparer.Ordinal){["id"]="id"};
        foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId==tableId))
            foreach(var name in new[]{column.ColumnName,JsonName(column.ColumnName),GeneratedRecordJsonName(column.ColumnName)})
            {if(names.TryGetValue(name,out var previous)&&previous!=column.ColumnName)return null;names[name]=column.ColumnName;}
        return names.ToFrozenDictionary(StringComparer.Ordinal);
    }
    /// <summary>按钮字段同时兼容原命名与生成器Pascal/Camel规则；最终属性按真实Column.Name绑定。</summary>
    private static string GeneratedRecordJsonName(string name)
    {
        var value=string.Concat(name.Split('_',StringSplitOptions.RemoveEmptyEntries).Select(part=>char.ToUpperInvariant(part[0])+part[1..]));
        return value.Length==0?"":char.ToLowerInvariant(value[0])+value[1..];
    }
    /// <summary>严格匹配本应用路径，不接收外链、查询片段或向上路径。</summary>
    internal static bool IsApplicationPath(string path) => path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal)
        && !path.Contains('\\') && !path.Contains('?') && !path.Contains('#') && !path.Contains("..", StringComparison.Ordinal) && !path.Any(char.IsControl);
    /// <summary>兼容原按钮字符串/数字配置，缺失与null保留为空。</summary>
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? field.ToString() : "";
    /// <summary>身份域来自实际账号/后台/项目链；迁出签发域后旧授权不能再换取凭据。</summary>
    public bool IsEligibleRealm(PrincipalRoleRealm realm) => platform ? realm.IsPlatformRealm
        : OwnsIssuerRealm(realm.ProjectId,realm.AdminTypeId);
    /// <summary>只匹配固定签发项目与发布后台，不把该匹配结果当作平台管理员身份。</summary>
    public bool OwnsIssuerRealm(string projectId,string adminTypeId)=>projectId==PendingProjectId&&adminTypes.Contains(adminTypeId);
    /// <summary>人工批准同时要求真实签发域与当前超级角色，不从工作区或客户端选项推断。</summary>
    public bool CanApprove(PrincipalRoleRealm realm) => platform ? realm.IsPlatformAdministrator : IsEligibleRealm(realm) && realm.IsSuper;
    /// <summary>沿用现有原字段JSON命名规则，冻结后的元数据变化不能修改已签发目录。</summary>
    private static string JsonName(string name) => Regex.Replace(name, "_([a-z])", match => match.Groups[1].Value.ToUpperInvariant());
}
