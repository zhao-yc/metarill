using System.Globalization;
using System.Text.Json.Serialization;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>平台导出的离线请求只含单个动作与已发布元数据，不含路径、命令或任何写入授权。</summary>
public sealed record GenerationUpdateRequest(string Type, int SchemaVersion, string Action, string ProjectId,
    string MetadataSha256, MetadataCatalog Metadata, string RequestSha256,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] GenerationRuntimeParts? RuntimeParts=null);
public sealed record GenerationTargetIdentity(string ProjectId, string RootNamespace, string DatabaseProvider);
public sealed record GenerationRequestPreparation(string Status, string RequestSha256, string CandidateDirectory, string PlanPath, GenerationUpdatePlan Plan);

public sealed partial class LocalGenerationUpdater
{
    /// <summary>只读验证三层归属并返回本机绑定所需身份，不返回本机路径或数据库连接。</summary>
    public GenerationTargetIdentity InspectTarget(string targetDirectory)
    {
        var target = Root(targetDirectory); RejectPending(target); var package = ReadPackage(target, false);
        return new(package.ProjectId, package.Package.RootNamespace, package.Metadata.DatabaseProvider);
    }

    /// <summary>固定版本与单动作；元数据复制脱敏后计算摘要，不能把原始平台配置直接交给浏览器保存。</summary>
    public static GenerationUpdateRequest CreateRequest(MetadataCatalog catalog, string action, GenerationRuntimeParts? runtimeParts=null)
    {
        if (!Scopes.Contains(action, StringComparer.Ordinal)) throw new InvalidOperationException("离线生成动作无效。");
        ValidateRuntimeParts(runtimeParts is null?1:2,[action],runtimeParts);
        var published = GenerationMetadataPublisher.Publish(catalog).Metadata;
        MetadataValidation.Validate(published);
        new FrontendPackager().Validate(published);
        var request = new GenerationUpdateRequest("metarillUpdateRequest", runtimeParts is null?1:2, action, published.YzProjectId, CanonicalHash(published), published, "",runtimeParts);
        request = request with { RequestSha256 = Hash(Encode(request)) };
        ValidateRequest(request);
        return request;
    }

    /// <summary>有界读取严格离线协议；未知字段、重复键和篡改后的内容均不能成为生成输入。</summary>
    public static GenerationUpdateRequest ReadRequest(string path)
    {
        var full = Path.GetFullPath(path);
        ValidateRelative(Path.GetFileName(full));
        CSharpGenerator.SafePath(Path.GetDirectoryName(full)!, Path.GetFileName(full));
        var info = new FileInfo(full);
        if (!info.Exists || info.Length > 4 * 1024 * 1024) throw new InvalidOperationException("离线请求不存在或超过4MiB限制。");
        var request = Decode<GenerationUpdateRequest>(File.ReadAllBytes(full));
        ValidateRequest(request);
        return request;
    }

    /// <summary>只在明确本机目录准备候选与预览文件，绝不调用Apply、DDL、安装或服务启动。</summary>
    public GenerationRequestPreparation PrepareRequest(string requestPath, string targetDirectory, string candidateDirectory, string sourceRoot, string planPath)
    {
        var request = ReadRequest(requestPath);
        var target = Root(targetDirectory);
        var source = Root(sourceRoot);
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidateDirectory));
        var output = Path.GetFullPath(planPath);
        var input = Path.GetFullPath(requestPath);
        if (candidate.Equals(Path.GetPathRoot(candidate), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("候选目录不能是磁盘根目录。");
        CSharpGenerator.SafePath(candidate, PackageMarker);
        ValidateRelative(Path.GetFileName(output));
        CSharpGenerator.SafePath(Path.GetDirectoryName(output)!, Path.GetFileName(output));
        foreach (var (left, right) in new[] { (target, candidate), (target, source), (candidate, source) })
            if (IsWithin(left, right) || IsWithin(right, left)) throw new InvalidOperationException("目标、候选和可信源码根目录必须互不包含。");
        if (new[] { target, candidate, source }.Any(directory => IsWithin(directory, output) || IsWithin(directory, input)) || output.Equals(input, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请求与计划必须位于目标、候选及源码目录之外，且不能互相覆盖。");
        if (File.Exists(output) || Directory.Exists(output)) throw new InvalidOperationException("预览计划已存在，不能覆盖。");
        if (!Directory.Exists(Path.GetDirectoryName(output))) throw new InvalidOperationException("预览计划父目录不存在。");
        RejectPending(target);
        var before = ReadPackage(target, false);
        if (before.ProjectId != request.ProjectId || before.Metadata.DatabaseProvider != request.Metadata.DatabaseProvider)
            throw new InvalidOperationException("离线请求与目标的项目或数据库品牌不一致。");
        // 命名空间只能来自已验证的目标三层归属，平台请求无权用默认值改绑现有工程。
        if(before.Management is not null)new ApplicationPackager().CreateManagement(request.Metadata,candidate,before.Package.RootNamespace,source,before.Management.Module.Binding.ManagementAdminTypeId);
        else new ApplicationPackager().Create(request.Metadata, candidate, before.Package.RootNamespace, source);
        try
        {
            var plan = Preview(target, candidate, [request.Action],request.RuntimeParts);
            WritePlan(plan, output);
            return new(plan.CanApply ? "preview-ready" : "dependencies-required", request.RequestSha256, candidate, output, plan);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("离线准备未完成；目标未应用。候选保留于：" + candidate + "；计划位置：" + output + "。原因：" + error.Message, error);
        }
    }

    /// <summary>摘要是漂移检测，不是签名；只接收开发者审阅过的已发布请求。</summary>
    public static void ValidateRequest(GenerationUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRuntimeParts(request.SchemaVersion,[request.Action],request.RuntimeParts);
        if (request.Type != "metarillUpdateRequest" || request.SchemaVersion is not (1 or 2) || !Scopes.Contains(request.Action, StringComparer.Ordinal)
            || request.Metadata is null || !long.TryParse(request.ProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 || id.ToString(CultureInfo.InvariantCulture) != request.ProjectId
            || request.Metadata.YzProjectId != request.ProjectId || !ValidHash(request.MetadataSha256) || !ValidHash(request.RequestSha256)) throw new InvalidOperationException("离线请求版本、动作或项目身份无效。");
        MetadataValidation.Validate(request.Metadata);
        if (CanonicalHash(request.Metadata) != request.MetadataSha256 || CanonicalHash(GenerationMetadataPublisher.Publish(request.Metadata).Metadata) != request.MetadataSha256
            || Hash(Encode(request with { RequestSha256 = "" })) != request.RequestSha256) throw new InvalidOperationException("离线请求摘要不一致或含未发布的敏感配置。");
        if (Encode(request).Length > 4 * 1024 * 1024) throw new InvalidOperationException("离线请求超过4MiB限制。");
        new FrontendPackager().Validate(request.Metadata);
    }
}
