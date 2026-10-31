using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Agent;

/// <summary>Agent物理模型只来自生成时固定契约；每次返回独立副本，不读取平台或业务扩展标志。</summary>
public static partial class AgentFoundationCatalog
{
    /// <summary>仅绑定应用根项目，固定字段与索引不能被业务JSON替换；结构仍走宿主显式计划。</summary>
    public static MetadataCatalog ForApplication(MetadataCatalog application)
    {
        ArgumentNullException.ThrowIfNull(application); MetadataValidation.Validate(application);
        if (!long.TryParse(application.YzProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0
            || application.YzProjectId != id.ToString(CultureInfo.InvariantCulture)) throw new InvalidOperationException("Agent基础模型要求规范应用项目ID。");
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(Definition, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if (application.BackendTarget != catalog.BackendTarget || application.SchemaVersion != catalog.SchemaVersion || application.DatabaseProvider != catalog.DatabaseProvider)
            throw new InvalidOperationException("Agent基础模型与应用版本、后端或数据库不一致。");
        catalog.YzProjectId = application.YzProjectId; catalog.YzProjectName = application.YzProjectName;
        return catalog;
    }
}
