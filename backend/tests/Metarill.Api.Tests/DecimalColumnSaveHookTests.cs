using System.Reflection;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Platform.Generated.Entities;
using Metarill.Platform.Generated.Mappers;
using Metarill.Platform.Generated.Services;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>验证实际平台服务在生成的映射调用及响应投影前同步原小数定义。</summary>
public sealed class DecimalColumnSaveHookTests
{
    [Fact]
    public void Original_config_column_save_updates_bound_fields_before_mapping()
    {
        using var session = new DatabaseSession(new DatabaseOptions
        {
            ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Port=1;Database=offline"
        });
        var service = new ConfigColumnService(session, new ConfigColumnMapper(session), new SnowflakeIdGenerator(331), []);
        var entity = new ConfigColumn
        {
            ColumnName = "amount", ColumnType = "decimal", ColumnLength = "19, 4",
            LogicalType = "string", Precision = 18, Scale = 2
        };
        var submitted = new HashSet<string>(StringComparer.Ordinal) { "column_type", "column_length", "logical_type" };
        var beforeSave = Assert.IsAssignableFrom<MethodInfo>(typeof(ConfigColumnService)
            .GetMethod("BeforeSave", BindingFlags.Instance | BindingFlags.NonPublic));

        beforeSave.Invoke(service, [entity, new RuntimeActor(1, "配置测试", true, "1", ""), false, submitted]);

        Assert.Equal("19, 4", entity.ColumnLength);
        Assert.Equal("decimal", entity.LogicalType);
        Assert.Equal(19, entity.Precision);
        Assert.Equal(4, entity.Scale);
        Assert.Contains("precision", submitted);
        Assert.Contains("scale", submitted);
    }
}
