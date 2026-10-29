using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>执行实际期限合同与发布元数据，明确不代表MySQL写入或完整上传验收。</summary>
public sealed class FileUploadLifetimeTests
{
    /// <summary>实际截止瞬时与审计日历互不依赖，精确边界沿用旧实现大于截止才拒绝。</summary>
    [Theory][InlineData(-12)][InlineData(0)][InlineData(8)][InlineData(14)]
    public void Explicit_deadline_ignores_calendar_offset_and_has_exact_boundary(int offsetHours)
    {
        var now=new DateTimeOffset(2026,10,29,12,0,0,TimeSpan.FromHours(offsetHours)).AddTicks(7);var expires=FileUploadLifetime.NewDeadline(now);
        Assert.Equal(DateTimeKind.Utc,expires.Kind);Assert.Equal(0,expires.Ticks%10);Assert.InRange((now.UtcDateTime.AddMinutes(30)-expires).Ticks,0L,9L);
        var row=new Dictionary<string,object?>{{"insert_time",new DateTime(2000,1,1)},{FileUploadLifetime.DeadlineColumn,DateTime.SpecifyKind(expires,DateTimeKind.Unspecified)}};
        FileUploadLifetime.RequireActive(row,now);FileUploadLifetime.RequireActive(row,new DateTimeOffset(expires));Assert.Throws<InvalidOperationException>(()=>FileUploadLifetime.RequireActive(row,new DateTimeOffset(expires).AddTicks(1)));
    }
    /// <summary>新增字段为空的旧计划沿用UTC创建时间；不把原UTC值当成新的宿主日历。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Legacy_plans_keep_original_utc_creation_interpretation(bool includeNull)
    {
        var now=new DateTimeOffset(2026,10,29,12,0,0,TimeSpan.Zero);
        foreach(var created in new object[]{DateTime.SpecifyKind(now.UtcDateTime.AddMinutes(-29),DateTimeKind.Unspecified),now.AddMinutes(-29),"2026-10-29 11:31:00",now.AddMinutes(-29).LocalDateTime})
        {
            var row=new Dictionary<string,object?>{{"insert_time",created}};if(includeNull)row[FileUploadLifetime.DeadlineColumn]=null;
            FileUploadLifetime.RequireActive(row,now);Assert.Throws<InvalidOperationException>(()=>FileUploadLifetime.RequireActive(row,now.AddMinutes(2)));
        }
    }
    /// <summary>坏的显式期限不能降级使用合法旧创建值，未知类型也不能变成不限期。</summary>
    [Fact]
    public void Corrupt_explicit_or_legacy_values_are_rejected_without_fallback()
    {
        var now=DateTimeOffset.UtcNow;
        foreach(var value in new object[]{"not-a-date","",42,new object()})
        {
            Assert.Throws<InvalidOperationException>(()=>FileUploadLifetime.RequireActive(new Dictionary<string,object?>{{FileUploadLifetime.DeadlineColumn,value},{"insert_time",now}},now));
            Assert.Throws<InvalidOperationException>(()=>FileUploadLifetime.RequireActive(new Dictionary<string,object?>{{"insert_time",value}},now));
        }
        Assert.Throws<InvalidOperationException>(()=>FileUploadLifetime.RequireActive(new Dictionary<string,object?>(),now));
    }
    /// <summary>元数据唯一来源直接编译MySQL方言；不手写DDL或仅检查JSON文字。</summary>
    [Fact]
    public void Published_nullable_deadline_compiles_without_database_access()
    {
        var catalog=Catalog();FileUploadLifetime.ValidateMetadata(catalog);var schema=MySqlSchemaPlanner.Compile(catalog,"offline");
        var column=Assert.Single(schema.Tables.Single(table=>table.Name=="yz_file_library").Columns,column=>column.Name==FileUploadLifetime.DeadlineColumn);Assert.Equal("datetime(6)",column.StoreType);
        var previous=schema with{Tables=schema.Tables.Select(table=>table with{Columns=table.Columns.Where(item=>item.Name!=FileUploadLifetime.DeadlineColumn).ToArray()}).ToArray()};var change=Assert.Single(MySqlSchemaPlanner.Compare(previous,schema));Assert.Equal("addColumn",change.Kind);Assert.Equal("yz_file_library",change.Table);Assert.Contains("`upload_expires_at` datetime(6) NULL",change.Sql);
        Assert.Empty(catalog.ConfigPageColumns);Assert.Empty(catalog.ConfigPages);
    }
    /// <summary>缺列、不可空、错误类型、精度或默认均在发布前拒绝。</summary>
    [Theory][InlineData("missing")][InlineData("required")][InlineData("type")][InlineData("precision")][InlineData("default")][InlineData("physical")][InlineData("time")][InlineData("malformed")]
    public void Incomplete_infrastructure_is_rejected(string change)
    {
        var catalog=Catalog();var column=catalog.ConfigColumns.Single(column=>column.ColumnName==FileUploadLifetime.DeadlineColumn);
        if(change=="missing")catalog.ConfigColumns.Remove(column);if(change=="required")column.IsDbNotNull=1;if(change=="type")column.LogicalType="string";if(change=="precision")column.ColumnType="datetime(0)";if(change=="default")column.DefaultValue="now()";if(change=="physical")column.ColumnType="nvarchar";if(change=="time")column.ColumnType="time(6)";if(change=="malformed")column.ColumnType="datetime(6) NULL";
        Assert.Contains(FileUploadLifetime.DeadlineColumn,Assert.Throws<InvalidOperationException>(()=>FileUploadLifetime.ValidateMetadata(catalog)).Message);
    }
    /// <summary>读取真实基础设施元数据，不复制近似模型到测试。</summary>
    private static MetadataCatalog Catalog([CallerFilePath]string source="")=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!,"../../metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
