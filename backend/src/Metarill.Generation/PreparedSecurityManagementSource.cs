using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

/// <summary>从持久化脱敏业务来源重新签发的管理上下文；不是完整包或源码更新授权。</summary>
internal sealed record PreparedSecurityManagementSource(SecurityManagementModelSet Module,string RootNamespace,string ApplicationMetadataSha256)
{
    internal const string ApplicationPath="backend/metadata/application-source.json";
    internal const string SourceMarker=".metarill-management-source.json";
    private const string ProjectPath="backend/metadata/project.json",GenerationPath="backend/generated/.metarill-generation.json";
    internal static IReadOnlyList<string> OwnedMetadataPaths {get;}=Array.AsReadOnly(new[]{ApplicationPath,ProjectPath,GenerationPath,"backend/metadata/files.json","backend/metadata/security.json"});
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    /// <summary>磁盘声明只用于归属核验；管理模型始终由当前工厂按原业务输入重新构造，不相信自报可信标志。</summary>
    internal static PreparedSecurityManagementSource Read(string packageDirectory)
    {
        try{return ReadCore(Path.GetFullPath(packageDirectory));}
        catch(Exception error) when(error is JsonException or KeyNotFoundException or FormatException or ArgumentException or IOException or InvalidOperationException)
        {throw new InvalidOperationException("管理准备包业务来源不完整或格式错误，请重新准备。",error);}
    }

    /// <summary>只读取固定元数据路径；手写代码和开发者宿主不在本来源校验范围，更新器须另验完整归属。</summary>
    private static PreparedSecurityManagementSource ReadCore(string root)
    {
        var sourceMarker=CSharpGenerator.SafePath(root,SourceMarker);var preparationMarker=CSharpGenerator.SafePath(root,".metarill-management-preparation.json");
        var source=File.Exists(sourceMarker);if(source&&File.Exists(preparationMarker))throw Invalid();
        using var marker=ReadDocument(source?sourceMarker:preparationMarker);var header=marker.RootElement;
        if(header.GetProperty("schemaVersion").GetInt32()!=1||Text(header,"profile")!=(source?"management-source-v1":"management-preparation-v1")
            ||!source&&header.GetProperty("readyForPublication").GetBoolean())throw Invalid();
        var rootNamespace=Text(header,"rootNamespace");CSharpGenerator.ValidateNamespace(rootNamespace);
        var files=header.GetProperty("files");
        byte[] ReadOwned(string relative)
        {
            var entries=files.EnumerateArray().Where(entry=>Text(entry,"path")==relative).ToArray();
            if(entries.Length!=1)throw Invalid();
            var bytes=File.ReadAllBytes(CSharpGenerator.SafePath(root,relative));
            if(Text(entries[0],"sha256")!=Hash(bytes))throw Invalid();
            return bytes;
        }
        var applicationBytes=ReadOwned(ApplicationPath);var applicationHash=Hash(applicationBytes);
        if(Text(header,"applicationMetadataSha256")!=applicationHash)throw Invalid();
        using var applicationDocument=ReadDocument(applicationBytes);
        var application=applicationDocument.RootElement.Deserialize<MetadataCatalog>(Json)??throw Invalid();
        // 再脱敏必须幂等，重算文件摘要也不能把新凭据夹进可恢复的业务快照。
        if(!CSharpGenerator.CanonicalMetadata(application).AsSpan().SequenceEqual(CSharpGenerator.CanonicalMetadata(GenerationMetadataPublisher.Publish(application).Metadata)))throw Invalid();
        if(application.YzProjectId!=Text(header,"projectId"))throw Invalid();
        var module=SecurityManagementModuleFactory.Compose(application,Text(header,"managementAdminTypeId"));
        if(module.Binding.ModuleSha256!=Text(header,"moduleSha256"))throw Invalid();
        var expected=SecurityManagementModuleFactory.RequirePublishedForGeneration(module);var expectedHash=Hash(CSharpGenerator.CanonicalMetadata(expected));
        var projectBytes=ReadOwned(ProjectPath);using var projectDocument=ReadDocument(projectBytes);
        var project=projectDocument.RootElement.Deserialize<MetadataCatalog>(Json)??throw Invalid();
        if(Text(header,"metadataSha256")!=expectedHash||Text(header,"securityCatalogSha256")!=Hash(projectBytes)
            ||!CSharpGenerator.CanonicalMetadata(expected).AsSpan().SequenceEqual(CSharpGenerator.CanonicalMetadata(project)))throw Invalid();
        using var generation=ReadDocument(ReadOwned(GenerationPath));var ownership=generation.RootElement;
        if(ownership.GetProperty("version").GetInt32()!=1||Text(ownership,"projectId")!=application.YzProjectId
            ||Text(ownership,"rootNamespace")!=rootNamespace||Text(ownership,"metadataSha256")!=expectedHash)throw Invalid();
        // 此处只固定来源完整性；原准备阶段的基础图门禁必须再次验证实际包文件。
        _=ReadOwned("backend/metadata/files.json");_=ReadOwned("backend/metadata/security.json");
        SecurityManagementApplicationPreparer.RequireMatchingFoundations(module,root);
        return new(module,rootNamespace,applicationHash);
    }
    /// <summary>归属摘要采用实际文件字节，目录结构比较另用生成器统一的规范元数据。</summary>
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    /// <summary>字符串声明不接受空值、数字或隐式类型转换。</summary>
    private static string Text(JsonElement value,string name)=>value.GetProperty(name).GetString() is {Length:>0} text?text:throw Invalid();
    /// <summary>文件路径固定由调用方给出，JSON内容不提供任意读取路径。</summary>
    private static JsonDocument ReadDocument(string path)=>ReadDocument(File.ReadAllBytes(path));
    /// <summary>忽略大小写的重复属性会产生多种解释，来源与归属均在反序列化前拒绝。</summary>
    private static JsonDocument ReadDocument(byte[] bytes)
    {
        var document=JsonDocument.Parse(bytes);
        try{GenerationMetadataJson.RequireUnique(document.RootElement);return document;}catch{document.Dispose();throw;}
    }
    /// <summary>错误不回显原业务字段或凭据，旧准备包缺少快照时要求重新准备。</summary>
    private static InvalidOperationException Invalid()=>new("管理准备包业务来源或固定模块归属不一致，请重新准备。");
}
