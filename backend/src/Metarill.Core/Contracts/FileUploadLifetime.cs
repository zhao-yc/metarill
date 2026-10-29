using System.Globalization;
using Metarill.Core.Metadata;
namespace Metarill.Core.Contracts;

/// <summary>上传期限独立于展示用审计日历；新计划保存UTC截止值，旧计划继续解释原UTC创建值。</summary>
public static class FileUploadLifetime
{
    public const string DeadlineColumn = "upload_expires_at";
    /// <summary>按实际瞬时计算三十分钟，截到MySQL六位小数；返回UTC且与计划响应使用同一个值。</summary>
    public static DateTime NewDeadline(DateTimeOffset now)
    {
        var deadline=now.UtcDateTime.AddMinutes(30);
        return new DateTime(deadline.Ticks-deadline.Ticks%10,DateTimeKind.Utc);
    }
    /// <summary>新字段存在时只信明确截止值；只有旧记录空字段才沿用原UTC创建时间，坏值拒绝。</summary>
    public static void RequireActive(IReadOnlyDictionary<string,object?> row,DateTimeOffset now)
    {
        try
        {
            var explicitDeadline=row.GetValueOrDefault(DeadlineColumn);
            var deadline=explicitDeadline is not null and not DBNull?UtcValue(explicitDeadline):UtcValue(row.GetValueOrDefault("insert_time")).AddMinutes(30);
            if(now.UtcDateTime>deadline)throw new InvalidOperationException("上传计划已过期。");
        }
        catch(Exception error)when(error is FormatException or InvalidCastException or ArgumentException or OverflowException)
        {throw new InvalidOperationException("上传计划期限无效。",error);}
    }
    /// <summary>发布与更新前固定基础目录必须声明可空六位UTC期限，不允许缺字段或任意默认表达式。</summary>
    public static void ValidateMetadata(MetadataCatalog infrastructure)
    {
        var table=infrastructure.ConfigTables.SingleOrDefault(table=>table.TableName=="yz_file_library")??throw new InvalidOperationException("文件基础目录缺少文件库表。");
        var column=infrastructure.ConfigColumns.SingleOrDefault(column=>column.ConfigTableId==table.Id&&column.ColumnName==DeadlineColumn);
        var physical=column?.ColumnType??"";
        if(column is null||column.LogicalType!="datetime"||(physical.Length>0&&!physical.Equals("datetime",StringComparison.OrdinalIgnoreCase)&&!physical.Equals("datetime(6)",StringComparison.OrdinalIgnoreCase))||MetadataColumnTypePolicy.TemporalPrecision(column)!=6||column.IsDbNotNull!=0||!string.IsNullOrWhiteSpace(column.DefaultValue))
            throw new InvalidOperationException("文件基础目录缺少可空六位UTC上传期限："+DeadlineColumn);
    }
    /// <summary>库内无时区值在此专用字段明确代表UTC；显式偏移/UTC/Local值保留其瞬时，不用宿主日历推断。</summary>
    private static DateTime UtcValue(object? value)
    {
        if(value is DateTimeOffset offset)return offset.UtcDateTime;
        var date=value is DateTime time?time:value is string text?DateTime.Parse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind):throw new InvalidOperationException("上传计划期限无效。");
        return date.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(date,DateTimeKind.Utc):date.ToUniversalTime();
    }
}
