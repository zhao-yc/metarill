using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record TrustedSecurityModuleBinding(string ModuleId, string ModuleSha256, string ProjectId, string ManagementAdminTypeId);
internal sealed record SecurityModuleDependency(string Code, string PageId, string ColumnId, string Reference);

/// <summary>仅由固定工厂签发的双模型；每次读取均为独立副本，业务JSON标记不能构造可信上下文。</summary>
internal sealed class SecurityManagementModelSet
{
    private readonly MetadataCatalog physical, published;
    internal TrustedSecurityModuleBinding Binding { get; }
    internal IReadOnlyList<SecurityModuleDependency> PendingAdapters { get; }
    internal MetadataCatalog Physical => SecurityManagementModuleFactory.Clone(physical);
    internal MetadataCatalog Published => SecurityManagementModuleFactory.Clone(published);
    // 构造器不公开；工厂签发记录才是后续受控模板入口的来源证明，Binding本身不是授权标记。
    internal SecurityManagementModelSet(MetadataCatalog physical, MetadataCatalog published, TrustedSecurityModuleBinding binding, IReadOnlyList<SecurityModuleDependency> pending)
    {
        this.physical = SecurityManagementModuleFactory.Clone(physical); this.published = SecurityManagementModuleFactory.Clone(published);
        Binding = binding; PendingAdapters = Array.AsReadOnly(pending.ToArray());
    }
}

/// <summary>固定源码模块的专用组合边界；不放宽普通MergePhysical，不接任意文件、版本或自报模块标记。</summary>
internal static class SecurityManagementModuleFactory
{
    private const string Prefix = "Metarill.Generation.SecurityManagement.V1";
    private const string ModuleId = "security-management-v1";
    private const string SourceProject = "1608097412978577409", SourceAdmin = "1762777514515656705";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SecurityManagementModelSet, object> Issued = new();
    private static readonly Dictionary<string, string> Tables = new(StringComparer.Ordinal)
    { ["6031906809708544"]="yz_manager", ["1692532858499969026"]="yz_role", ["1696799308939239425"]="yz_admin_type" };
    private static readonly Dictionary<string, string> Pages = new(StringComparer.Ordinal)
    { ["6031906822946816"]="6031906809708544", ["1692532858529329154"]="1692532858499969026", ["1696799309006348290"]="1696799308939239425" };

    /// <summary>将三页绑定到一个已发布后台；输出只是准备模型，仍须通过原Full/Runtime/Custom门禁。</summary>
    internal static SecurityManagementModelSet Compose(MetadataCatalog application, string publishedManagementAdminTypeId)
    {
        ArgumentNullException.ThrowIfNull(application);
        application = Clone(application);
        MetadataValidation.Validate(application);
        if (!Id(application.YzProjectId) || !Id(publishedManagementAdminTypeId)) throw Rejected();
        ValidateOwners(application);
        var selected = application.YzAdminTypes.Where(admin => admin.Id == publishedManagementAdminTypeId && admin.YzProjectId == application.YzProjectId).ToArray();
        if (selected.Length != 1) throw Rejected();
        var (asset, sha) = Load();
        ValidateAsset(asset);
        // 物理保留对象与三页布局ID按全类别核对；同名或同ID业务声明不能获得特权。
        var reserved = Records(asset.Security).Concat(Records(asset.Files)).Concat(Records(asset.Management).Where(row => row.Kind != "admin"))
            .Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        var names = asset.Security.ConfigTables.Concat(asset.Files.ConfigTables).Select(table => table.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Records(application).Any(row => reserved.Contains(row.Id)) || application.ConfigTables.Any(table => names.Contains(table.TableName))) throw Rejected();
        var physical = MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(application, Bind(asset.Files,application,selected[0])), Bind(asset.Security,application,selected[0]));
        // 基础物理对象无页面；先重新绑定项目，再以专用路径附加固定显示声明，通用合成器依旧拒绝同名安全CRUD。
        var management = Bind(asset.Management, application, selected[0]);
        var published = Clone(physical);
        foreach (var table in management.ConfigTables) published.ConfigTables[published.ConfigTables.FindIndex(item => item.Id == table.Id)] = table;
        foreach (var column in management.ConfigColumns) published.ConfigColumns[published.ConfigColumns.FindIndex(item => item.Id == column.Id)] = column;
        published.ConfigPages.AddRange(management.ConfigPages); published.ConfigPageColumns.AddRange(management.ConfigPageColumns);
        published.ConfigMenus.AddRange(management.ConfigMenus); published.ConfigCustomPages.AddRange(management.ConfigCustomPages);
        published.ConfigButtons.AddRange(management.ConfigButtons);
        ValidateOwners(physical); ValidateOwners(published);
        ValidateGraph(published);
        // 用原严格物理合成逐项复验显示声明未改变任何安全结构，不以控件配置覆盖物理事实。
        var noPages = Clone(published); noPages.ConfigPages.Clear(); noPages.ConfigPageColumns.Clear();
        _ = MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(noPages, asset.Files), asset.Security);
        var result = new SecurityManagementModelSet(physical, published, new(ModuleId, sha, application.YzProjectId, selected[0].Id), asset.PendingAdapters);
        Issued.Add(result, new object());
        return result;
    }

    /// <summary>后续可信模板入口须接工厂上下文而非反序列化Binding；本片不注册任何发布入口。</summary>
    internal static void RequireIssued(SecurityManagementModelSet models)
    {
        if (!Issued.TryGetValue(models, out _)) throw Rejected();
    }

    /// <summary>生成器只消费工厂冻结快照；伪造同形Binding或构造结果不在签发表，调用方可变副本不能替换它。</summary>
    internal static MetadataCatalog RequirePublishedForGeneration(SecurityManagementModelSet models)
    {
        ArgumentNullException.ThrowIfNull(models);
        RequireIssued(models);
        return models.Published;
    }

    /// <summary>摘要绑定固定嵌入字节，业务输入不能提供另一份模块或预期摘要。</summary>
    private static (Asset Asset, string Sha) Load()
    {
        var assembly = typeof(SecurityManagementModuleFactory).Assembly;
        using var content = assembly.GetManifestResourceStream(Prefix + ".json") ?? throw Rejected();
        using var checksum = assembly.GetManifestResourceStream(Prefix + ".sha256") ?? throw Rejected();
        using var buffer = new MemoryStream(); content.CopyTo(buffer);
        if (buffer.Length > 512 * 1024) throw Rejected();
        using var reader = new StreamReader(checksum);
        var expected = reader.ReadToEnd().Trim(); var bytes = buffer.ToArray();
        if (expected.Length != 64 || expected.Any(character => !char.IsAsciiHexDigitLower(character)) || Convert.ToHexStringLower(SHA256.HashData(bytes)) != expected) throw Rejected();
        var options = new JsonSerializerOptions(Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        return (JsonSerializer.Deserialize<Asset>(bytes, options) ?? throw Rejected(), expected);
    }

    /// <summary>固定资产的物理、布局、后台、引用均闭合后才允许绑定，不能凭schemaVersion自动信任新模块。</summary>
    private static void ValidateAsset(Asset asset)
    {
        if (asset.SchemaVersion != 1 || asset.ModuleId != ModuleId || asset.Sources.Count != 3 || !new[]{"platformSha256","securitySha256","filesSha256"}.All(key => asset.Sources.TryGetValue(key,out var sha) && sha.Length==64 && sha.All(char.IsAsciiHexDigitLower))) throw Rejected();
        var catalog = asset.Management;
        if (catalog.YzProjectId != SourceProject || catalog.YzAdminTypes.Count != 1 || catalog.YzAdminTypes[0].Id != SourceAdmin
            || catalog.ConfigTables.Count != 3 || catalog.ConfigColumns.Count != 69 || catalog.ConfigPages.Count != 3 || catalog.ConfigPageColumns.Count != 62
            || catalog.ConfigTables.Any(table => !Tables.TryGetValue(table.Id,out var name) || table.TableName != name)
            || catalog.ConfigPages.Any(page => !Pages.TryGetValue(page.Id,out var table) || page.ConfigTableId != table || page.YzAdminTypeId != SourceAdmin)
            || catalog.ConfigMenus.Count != 6 || catalog.ConfigCustomPages.Count != 1 || catalog.ConfigButtons.Count != 1 || catalog.ConfigChildren.Count != 0
            || catalog.ConfigIndexes.Count != 0 || catalog.ConfigIndexColumns.Count != 0) throw Rejected();
        ValidateOwners(catalog); ValidateGraph(catalog);
        if (asset.Security.ConfigTables.Count != 6 || asset.Files.ConfigTables.Count != 3 || asset.Security.ConfigPages.Count != 0 || asset.Files.ConfigPages.Count != 0
            || catalog.ConfigCustomPages[0].Id != "2062363000000000002" || catalog.ConfigCustomPages[0].PagePath != "pages/main/set_role_permission"
            || Text(catalog.ConfigButtons[0],"id") != "1696805746482847746" || Text(catalog.ConfigButtons[0],"sql") != "/admin/custom/index/init_admin"
            || !asset.PendingAdapters.Select(item=>item.Code).SequenceEqual(new[]{"admin-project-options","role-permission-workbench","initialize-account","security-write-runtime"})) throw Rejected();
        var physical = MetadataCatalogComposition.MergePhysical(asset.Files, asset.Security);
        var rich = Clone(catalog); rich.ConfigPages.Clear(); rich.ConfigPageColumns.Clear();
        // 布局资产不重复索引；校验三表显示声明时用安全物理源的完整原索引闭包，不能把缺索引当作新定义。
        rich.ConfigIndexes = asset.Security.ConfigIndexes.Where(index=>Tables.ContainsKey(Text(index,"configTableId"))).Select(index=>index.Clone()).ToList();
        var indexIds=rich.ConfigIndexes.Select(index=>Text(index,"id")).ToHashSet(StringComparer.Ordinal);
        rich.ConfigIndexColumns=asset.Security.ConfigIndexColumns.Where(field=>indexIds.Contains(Text(field,"configIndexId"))).Select(field=>field.Clone()).ToList();
        _ = MetadataCatalogComposition.MergePhysical(rich, asset.Security);
        if (physical.ConfigTables.Count != 9) throw Rejected();
    }

    /// <summary>原完整显示声明仅绑定已核实的归属字段，不改稳定ID、控件、SQL文本或页面布局。</summary>
    private static MetadataCatalog Bind(MetadataCatalog source, MetadataCatalog application, AdminTypeMetadata selected)
    {
        var node = JsonSerializer.SerializeToNode(source,Json)!;
        void Visit(JsonNode? value)
        {
            if(value is JsonObject item)
            {
                foreach(var key in item.Select(pair=>pair.Key).ToArray())
                {
                    if(key=="yzProjectId") item[key]=application.YzProjectId;
                    else if(key=="yzProjectName") item[key]=application.YzProjectName;
                    else if(key=="yzAdminTypeId") item[key]=selected.Id;
                    else if(key=="yzAdminTypeName") item[key]=selected.Name;
                    else Visit(item[key]);
                }
            }
            else if(value is JsonArray array) foreach(var child in array) Visit(child);
        }
        Visit(node);
        var result=node.Deserialize<MetadataCatalog>(Json)!;
        result.YzProjectId=application.YzProjectId; result.YzProjectName=application.YzProjectName; result.PrimaryColor=application.PrimaryColor;
        // 纯物理源没有归属声明，附加到应用后也必须明确同一项目，索引引用仍原样。
        foreach(var record in TypedRecords(result)) record.YzProjectId=application.YzProjectId;
        return result;
    }

    /// <summary>最终整图包含普通业务目录；所有页、字段、菜单和按钮必须同项目同已发布后台且引用闭合。</summary>
    private static void ValidateGraph(MetadataCatalog catalog)
    {
        MetadataValidation.Validate(catalog);
        var admins=catalog.YzAdminTypes.ToDictionary(item=>item.Id,StringComparer.Ordinal);
        var pages=catalog.ConfigPages.ToDictionary(item=>item.Id,StringComparer.Ordinal);
        var columns=catalog.ConfigColumns.ToDictionary(item=>item.Id,StringComparer.Ordinal);
        var custom=catalog.ConfigCustomPages.ToDictionary(item=>item.Id,StringComparer.Ordinal);
        var menus=catalog.ConfigMenus.ToDictionary(item=>item.Id,StringComparer.Ordinal);
        foreach(var page in pages.Values) if(!admins.ContainsKey(page.YzAdminTypeId)) throw Rejected();
        foreach(var field in catalog.ConfigPageColumns) if(columns[field.ConfigColumnId].ConfigTableId!=pages[field.ConfigPageId].ConfigTableId) throw Rejected();
        foreach(var page in custom.Values) if(!admins.ContainsKey(page.YzAdminTypeId)) throw Rejected();
        foreach(var menu in menus.Values)
        {
            if(!admins.ContainsKey(menu.YzAdminTypeId)) throw Rejected();
            if(menu.Kind=="GENERATED_PAGE" && (!pages.TryGetValue(menu.ConfigPageId??"",out var page)||page.YzAdminTypeId!=menu.YzAdminTypeId)) throw Rejected();
            if(menu.Kind=="CUSTOM_PAGE" && (!custom.TryGetValue(menu.ConfigCustomPageId??"",out var customPage)||customPage.YzAdminTypeId!=menu.YzAdminTypeId||menu.NavigateUrl!=customPage.PagePath)) throw Rejected();
            var seen=new HashSet<string>(StringComparer.Ordinal); var current=menu;
            while(!string.IsNullOrEmpty(current.ParentMenuId))
            {
                if(!seen.Add(current.Id)||!menus.TryGetValue(current.ParentMenuId,out var parent)||parent.YzAdminTypeId!=menu.YzAdminTypeId) throw Rejected();
                current=parent;
            }
        }
        foreach(var button in catalog.ConfigButtons)
        {
            if(!pages.TryGetValue(Text(button,"configPageId"),out var page)) throw Rejected();
            if(button.TryGetProperty("yzAdminTypeId",out var admin) && admin.ToString()!=page.YzAdminTypeId) throw Rejected();
        }
    }

    /// <summary>跨类别ID先查重，兼容字典扩展不能借用固定ID；不信元数据中的任意module标记。</summary>
    private static void ValidateOwners(MetadataCatalog catalog)
    {
        var all=Records(catalog).ToArray();
        if(all.Any(row=>!Id(row.Id))||all.Select(row=>row.Id).Distinct(StringComparer.Ordinal).Count()!=all.Length) throw Rejected();
        if(TypedRecords(catalog).Any(record=>record.YzProjectId!=catalog.YzProjectId)) throw Rejected();
        foreach(var raw in catalog.ConfigButtons.Concat(catalog.ConfigChildren).Concat(catalog.ConfigIndexes).Concat(catalog.ConfigIndexColumns))
            if(raw.TryGetProperty("yzProjectId",out var project) && project.GetString()!=catalog.YzProjectId) throw Rejected();
    }
    /// <summary>枚举有明确项目归属的全部类型记录，不把后台模板追加为新后台。</summary>
    private static IEnumerable<MetadataRecord> TypedRecords(MetadataCatalog catalog) => catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns).Concat(catalog.YzAdminTypes).Concat(catalog.ConfigMenus).Concat(catalog.ConfigCustomPages);
    /// <summary>跨类型汇总物理和页面等ID，避免分集合验证漏掉保留ID借用。</summary>
    private static IEnumerable<(string Id,string Kind)> Records(MetadataCatalog catalog) => TypedRecords(catalog).Select(record=>(record.Id,record is AdminTypeMetadata?"admin":"record"))
        .Concat(catalog.ConfigButtons.Concat(catalog.ConfigChildren).Concat(catalog.ConfigIndexes).Concat(catalog.ConfigIndexColumns).Select(record=>(Text(record,"id"),"raw")));
    /// <summary>字符串ID必须为规范正整数，避免长ID精度损失和前导零别名。</summary>
    private static bool Id(string value) => long.TryParse(value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var number)&&number>0&&number.ToString(System.Globalization.CultureInfo.InvariantCulture)==value;
    /// <summary>固定元数据原始对象只读取明确键，不解释SQL或动态表达式。</summary>
    private static string Text(JsonElement value,string key)=>value.TryGetProperty(key,out var property)?property.ToString():"";
    /// <summary>所有输出和基础快照深拷贝，防止共享可变元数据串入后续生成。</summary>
    internal static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,Json),Json)!;
    /// <summary>错误不回显元数据值、连接或任何凭据。</summary>
    private static InvalidOperationException Rejected()=>new("固定安全管理模块来源、标识或归属不符；尚未执行生成、初始化或发布。");
    private sealed record Asset(int SchemaVersion,string ModuleId,Dictionary<string,string> Sources,MetadataCatalog Files,MetadataCatalog Security,MetadataCatalog Management,List<SecurityModuleDependency> PendingAdapters);
}
