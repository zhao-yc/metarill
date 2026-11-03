using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Data;
using MySqlConnector;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class ProjectFileIntegrationFactAttribute : FactAttribute
{
    /// <summary>只为跨库文件用例选择专属租约，其他测试仍使用原固定测试库入口。</summary>
    public ProjectFileIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProjectFileFreshLease.SettingsVariable)))
            Skip = new MetadataIntegrationFactAttribute().Skip;
    }
}

internal sealed record ProjectFileFreshLease(string RunId, string Hostname, string ServerUuid, DatabaseOptions Platform, DatabaseOptions Business)
{
    internal const string SettingsVariable = "METARILL_PROJECT_FILE_NATIVE_SETTINGS";

    /// <summary>创建会话前验证专属租约、实际连接参数和两个精确库名。</summary>
    internal static ProjectFileFreshLease Parse(JsonElement config)
    {
        var run = config.GetProperty("RunId").GetString() ?? "";
        if (!Regex.IsMatch(run, "^[a-f0-9]{16}$") ||
            config.GetProperty("Purpose").GetString() != "project-files-native-v1" ||
            config.GetProperty("AuthorizationStatus").GetString() != "approved" ||
            config.GetProperty("LeaseStatus").GetString() != "running")
            throw new InvalidOperationException("跨库文件验收缺少本轮有效租约。");
        var instance = config.GetProperty("InstancePin");
        var host = instance.GetProperty("hostname").GetString() ?? "";
        var uuid = instance.GetProperty("serverUuid").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(host) || !Guid.TryParseExact(uuid, "D", out _))
            throw new InvalidOperationException("跨库文件验收缺少实例指纹。");
        return new(run, host, uuid, Database("Database", "platform"), Database("BusinessDatabase", "business"));

        DatabaseOptions Database(string property, string suffix)
        {
            var expected = $"metarill_fresh_{run}_{suffix}";
            var options = config.GetProperty(property).Deserialize<DatabaseOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (options?.Provider != "mysql" || options.ExpectedDatabase != expected)
                throw new InvalidOperationException("跨库文件验收只允许本轮两个独占 MySQL 库。");
            var connection = new MySqlConnectionStringBuilder(options.ConnectionString);
            if (connection.Server is not ("deve-server.yinzon.com" or "192.168.31.200") || connection.Port != 3306 ||
                connection.Database != expected || connection.UserID != "mtf_" + run)
                throw new InvalidOperationException("跨库文件验收只允许 Server-200 本轮临时账号与目标库。");
            return options;
        }
    }
}
