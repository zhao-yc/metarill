using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;

namespace Metarill.Generation.Tests;

/// <summary>数值实表验收使用真正公开Full包，普通与非冗余页来自同一元数据及生成链。</summary>
internal static class DoublePersistenceNativePreparation
{
    internal const string Root = ".local/double-persistence-native";
    internal const string RootNamespace = "Metarill.DoublePersistenceNative";
    internal const string ProductCommit = "f5ed1551a4837285401f95550c53595644eca087";
    internal const string Prefix = "mt_double_native";
    internal const ulong UpperBits = 0x0410000000000000, LowerBits = 0x040fffffffffffff;
    internal const string UpperText = "4.1045368012983762E-289", LowerText = "4.1045368012983758E-289";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };

    /// <summary>沿用已有三表关系与真实父范围，只增加声明的double字段及普通导入／导出／统计开关。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog = NonRedundantGeneratedFixture.Catalog(Prefix);
        catalog.YzProjectName = "MySQL数值持久化验收";
        catalog.YzAdminTypes[0].Name = "数值后台";
        foreach (var row in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns)) row.YzProjectId = catalog.YzProjectId;
        foreach (var page in catalog.ConfigPages)
        {
            page.IsNew = 1; page.IsSave = 1; page.IsDelete = 1; page.IsCopy = 1;
            page.Name = "数值页" + page.Id;
            catalog.ConfigMenus.Add(new() { Id = "900" + page.Id, YzProjectId = "54", YzAdminTypeId = "5", Name = page.Name, Kind = "GENERATED_PAGE", ConfigPageId = page.Id });
        }
        foreach (var id in new[] { "17", "26" }) catalog.ConfigColumns.Single(column => column.Id == id).LogicalType = "double";
        catalog.ConfigColumns.Single(column => column.Id == "26").DefaultValue = UpperText;
        Add("900", "1", "measurement", "测量值", UpperText, "101", true);
        Add("901", "1", "measurement_previous", "相邻值", LowerText, "101", false);
        Add("902", "2", "previous_amount", "相邻金额", LowerText, "201", false);
        Add("903", "2", "exact_amount", "精确金额", "", "201", false, "decimal");
        var exact = catalog.ConfigColumns.Single(column => column.Id == "903"); exact.Precision = 30; exact.Scale = 9;
        foreach (var id in new[] { "101", "201" })
        {
            var page = catalog.ConfigPages.Single(value => value.Id == id); page.IsImport = 1; page.IsExport = 1; page.Extra["isSum"] = JsonSerializer.SerializeToElement(1);
        }
        foreach (var id in new[] { "17", "26" })
        {
            var field = catalog.ConfigPageColumns.Single(value => value.ConfigColumnId == id);
            field.Extra["isSearch"] = JsonSerializer.SerializeToElement(1); field.Extra["sumKind"] = JsonSerializer.SerializeToElement(1);
        }
        catalog.ConfigColumns.Single(column => column.Id == "14").Name = "记录名";
        return catalog;

        // 字段由正式逻辑类型规划器处理，不写业务实体、表DDL或自定义查询。
        void Add(string id, string table, string name, string label, string defaultValue, string page, bool sum, string type = "double")
        {
            catalog.ConfigColumns.Add(new() { Id = id, YzProjectId = "54", ConfigTableId = table, ColumnName = name, Name = label, LogicalType = type, DefaultValue = defaultValue });
            catalog.ConfigPageColumns.Add(new() { Id = page + id, YzProjectId = "54", ConfigPageId = page, ConfigColumnId = id, IsShow = 1, IsEdit = 1,
                Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1), ["isSort"] = JsonSerializer.SerializeToElement(1), ["sumKind"] = JsonSerializer.SerializeToElement(sum ? 1 : 0) } });
        }
    }

    /// <summary>真实首行事务内写入可见后拒绝第二行，记录位型；不替换Service、身份或传输。</summary>
    internal static string Handwritten(string table, string valueProperty)
    {
        var name = GenerationModel.Pascal(table);
        return $$"""
using FreeSql;
using Metarill.Core.Security;
namespace {{RootNamespace}}.Generated.Services;
public partial class {{name}}Service
{
    /// <summary>本轮合成故障只能在首行已经真实执行DML且加入同一事务后触发。</summary>
    partial void BeforeSave({{RootNamespace}}.Generated.Entities.{{name}} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        if(entity.Name!="数值后置拒绝")return;
        var prior=mapper.Query().Where(row=>row.Name=="数值回滚首行").ToList();
        if(prior.Count!=1||session.CurrentTransaction is null||prior[0].{{valueProperty}} is not {} value)throw new InvalidOperationException("未观察到本轮首行数值DML或当前事务。");
        var path=Environment.GetEnvironmentVariable("METARILL_DOUBLE_PERSISTENCE_HOOK_RECEIPT")??throw new InvalidOperationException("缺少数值事务回执位置。");
        File.AppendAllText(path,System.Text.Json.JsonSerializer.Serialize(new{table={{JsonSerializer.Serialize(table)}},recordId=prior[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture),bits=BitConverter.DoubleToUInt64Bits(value).ToString("X16",System.Globalization.CultureInfo.InvariantCulture),insideTransaction=true})+"\n");
        throw new InvalidOperationException("本轮数值合成后置拒绝。");
    }
}
""" + "\n";
    }

    /// <summary>公开Full装包后只改首次创建的两份手写partial，两轮正规再生成必须保护它们。</summary>
    internal static void Create(string sourceRoot, string output)
    {
        var catalog = Catalog();
        var result = new ApplicationPackager().Create(catalog, output, RootNamespace, sourceRoot);
        if (result.Mode != ApplicationPackageMode.FullApplication || !result.FrontendIncluded) throw new InvalidOperationException("数值验收必须是真正完整应用。");
        var generated = CSharpGenerator.SafePath(output, "backend/generated");
        var hands = Hands();
        foreach (var pair in hands) File.WriteAllText(CSharpGenerator.SafePath(output, pair.Key), pair.Value);
        for (var round = 0; round < 2; round++)
        {
            new CSharpGenerator().Generate(catalog, generated, RootNamespace);
            if (hands.Any(pair => File.ReadAllText(CSharpGenerator.SafePath(output, pair.Key)) != pair.Value)) throw new InvalidOperationException("数值验收手写partial被覆盖。");
        }
    }

    /// <summary>复用现有不跟随链接的全文件捕获；正式包按本模型与专属手写规则再次验证。</summary>
    internal static GlobalCachedPackageProof Capture(string package) => GlobalCachedSourceNativePreparation.Capture(package);

    /// <summary>关键身份、完整文件集合、元数据和受管来源同时核对，不能重新捕获收编额外代码。</summary>
    internal static MetadataCatalog Verify(GlobalCachedPackageProof proof, bool runtime = false)
    {
        if (!Path.IsPathFullyQualified(proof.PackageRoot) || Path.TrimEndingDirectorySeparator(Path.GetFullPath(proof.PackageRoot)) != proof.PackageRoot) throw new InvalidOperationException("数值固定包根不规范。");
        const string journal = "backend/Application.Host/.local/schema-history.jsonl";
        if (proof.Files.ContainsKey(journal)) throw new InvalidOperationException("数值准备包不能含运行状态。");
        var actual = Capture(proof.PackageRoot); var paths = actual.Files.Keys.ToHashSet(StringComparer.Ordinal); if (runtime) paths.Remove(journal);
        if (!paths.SetEquals(proof.Files.Keys) || proof.Files.Any(pair => !actual.Files.TryGetValue(pair.Key, out var hash) || hash != pair.Value)) throw new InvalidOperationException("数值固定包文件漂移。");
        if (actual.ManifestSha256 != proof.ManifestSha256 || actual.AssemblySha256 != proof.AssemblySha256 || actual.MetadataSha256 != proof.MetadataSha256) throw new InvalidOperationException("数值固定包摘要漂移。");
        using var document = JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot, ".metarill-package.json")));
        var marker = document.RootElement;
        if (marker.GetProperty("packageMode").GetString() != "FullApplication" || marker.GetProperty("authenticationProfile").GetString() != "web-database-v1" || marker.GetProperty("rootNamespace").GetString() != RootNamespace) throw new InvalidOperationException("数值验收不能替换为开发或准备宿主。");
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot, "backend/metadata/project.json")), Json)!;
        if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(catalog), JsonSerializer.SerializeToNode(Catalog()))) throw new InvalidOperationException("数值固定模型漂移。");
        if (marker.GetProperty("metadataSha256").GetString() != Convert.ToHexStringLower(SHA256.HashData(CSharpGenerator.CanonicalMetadata(catalog))) || marker.GetProperty("securityCatalogSha256").GetString() != proof.MetadataSha256) throw new InvalidOperationException("数值模型与安全绑定不符。");
        var hands = Hands();
        var declared = marker.GetProperty("files").EnumerateArray().ToDictionary(file => file.GetProperty("path").GetString()!, file => file.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
        var projects = declared.Keys.Where(path => path.EndsWith(".csproj", StringComparison.Ordinal)).Select(path => path[..path.LastIndexOf('/')]).ToArray();
        foreach (var path in proof.Files.Keys)
            if (!declared.ContainsKey(path) && path != ".metarill-package.json" && !projects.Any(project => path.StartsWith(project + "/bin/", StringComparison.Ordinal) || path.StartsWith(project + "/obj/", StringComparison.Ordinal) || path == project + "/packages.lock.json")) throw new InvalidOperationException("数值固定包混入额外来源。");
        foreach (var pair in declared)
            if (!proof.Files.TryGetValue(pair.Key, out var hash) || !hands.ContainsKey(pair.Key) && hash != pair.Value) throw new InvalidOperationException("数值受管文件与清单不符。");
        foreach (var pair in hands)
            if (File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot, pair.Key)).Replace("\r\n", "\n", StringComparison.Ordinal) != pair.Value) throw new InvalidOperationException("数值手写规则漂移。");
        return catalog;
    }

    /// <summary>固定包不启动，执行副本只允许新增原宿主结构审计。</summary>
    internal static GlobalCachedPackageProof CopyForExecution(GlobalCachedPackageProof proof, string destination)
    {
        Verify(proof); var root = Path.GetFullPath(destination);
        if (Directory.Exists(root) || File.Exists(root)) throw new InvalidOperationException("数值执行副本已存在。");
        _ = CSharpGenerator.SafePath(root, ".metarill-execution-boundary"); Directory.CreateDirectory(root);
        foreach (var pair in proof.Files) { var target = CSharpGenerator.SafePath(root, pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(CSharpGenerator.SafePath(proof.PackageRoot, pair.Key), target); }
        var copied = proof with { PackageRoot = root }; Verify(copied); return copied;
    }

    /// <summary>只允许两份明确的首次创建业务扩展，与元数据表名及字段一一对应。</summary>
    private static IReadOnlyDictionary<string, string> Hands() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["backend/generated/Services/" + GenerationModel.Pascal(Prefix + "_party") + "Service.cs"] = Handwritten(Prefix + "_party", "Amount"),
        ["backend/generated/Services/" + GenerationModel.Pascal(Prefix + "_order") + "Service.cs"] = Handwritten(Prefix + "_order", "Measurement")
    };
}