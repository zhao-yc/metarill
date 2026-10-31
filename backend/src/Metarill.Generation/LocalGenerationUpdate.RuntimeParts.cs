using System.Text.Json.Serialization;

namespace Metarill.Generation;

/// <summary>原设备前后端开关的固定快照；两字段必须明确出现，不能从缺省值猜测另一端授权。</summary>
public sealed record GenerationRuntimeParts([property:JsonRequired] bool Backend,[property:JsonRequired] bool Frontend);

public sealed partial class LocalGenerationUpdater
{
    /// <summary>只读协议能力来自实际更新器，不从用户配置声称分端支持。</summary>
    public static object ProtocolCapabilities()=>new{schemaVersion=1,requestSchemaVersions=new[]{1,2},planSchemaVersions=new[]{1,2},runtimePartsProtocol=2};

    /// <summary>无范围保持原v1字节/行为；分端必须v2且仅单个运行时动作，旧CLI会按版本拒绝。</summary>
    private static void ValidateRuntimeParts(int version,IReadOnlyList<string> actions,GenerationRuntimeParts? parts)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if(parts is null)
        {
            if(version!=1)throw new InvalidOperationException("分端运行时协议缺少明确范围。");
            return;
        }
        if(version!=2||(!parts.Backend&&!parts.Frontend)||actions.Count!=1||actions[0]!=Runtime)
            throw new InvalidOperationException("分端范围只允许v2单个运行时同步动作，且至少选择一端。");
    }

    /// <summary>完整应用的前端根归前端，src/后端宿主/根构建文件归后端；不从客户端接收路径清单。</summary>
    private static bool RuntimePartAllows(GenerationRuntimeParts parts,string path)
        =>path.StartsWith("frontend/",StringComparison.Ordinal)?parts.Frontend:parts.Backend;

    /// <summary>只有实际归属清单能作跨端记账，不能用manifest标签伪装另一端源码。</summary>
    private static bool IsRuntimeManifest(string path)=>path is PackageMarker or GenerationMarker or FrontendMarker||path==PreparedSecurityManagementSource.SourceMarker;

    /// <summary>应用、回执查询与恢复共用范围校验，不能重算摘要后借计划写入未选端或其他动作。</summary>
    private static void ValidateScopedPlan(GenerationUpdatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateRuntimeParts(plan.SchemaVersion,plan.Actions,plan.RuntimeParts);
        if(plan.RuntimeParts is null)return;
        if(plan.Files is null||plan.Files.Any(file=>file is null||file.Action!="preserve"&&(file.Scope=="manifest"?!IsRuntimeManifest(file.Path):file.Scope!=Runtime||!RuntimePartAllows(plan.RuntimeParts,file.Path))))
            throw new InvalidOperationException("分端计划包含未选择的源码范围。");
    }
}
