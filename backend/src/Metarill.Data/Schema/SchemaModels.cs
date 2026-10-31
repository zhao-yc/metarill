using System.Text.Json.Serialization;

namespace Metarill.Data.Schema;

public sealed record ColumnSchema(string Name, string StoreType, bool Nullable, bool PrimaryKey = false, string? DefaultSql = null, bool Generated = false);
/// <summary>索引方向逐列绑定；null沿用全ASC，旧快照JSON及摘要不因新增能力改变。</summary>
public sealed record IndexSchema(string Name, bool Unique, IReadOnlyList<string> Columns)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Directions { get; init; }
}
public sealed record TableSchema(string Name, IReadOnlyList<ColumnSchema> Columns, string? SnapshotDefinition = null, IReadOnlyList<IndexSchema>? Indexes = null);
public sealed record DatabaseSchema(string Database, IReadOnlyList<TableSchema> Tables);
/// <summary>适配器只读反查结果及其结构指纹，不包含可执行变更计划。</summary>
public sealed record SchemaInspectionSnapshot(string Provider, DatabaseSchema Schema, string Fingerprint);
public sealed record SchemaChange(string Kind, string Table, string? Column, string Sql);
public sealed record SchemaChangePlan(string Id, string Provider, string Database, string BeforeFingerprint, string DesiredFingerprint, IReadOnlyList<SchemaChange> Changes);
public sealed record SchemaApplyResult(string PlanId, string Database, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, bool Succeeded, int AppliedStatements, string? Error);

/// <summary>MySQL DDL 会隐式提交；异常必须保留已应用语句数量，不能声称整体回滚。</summary>
public sealed class SchemaApplyException(SchemaApplyResult result, Exception inner) : Exception("结构执行失败；请检查记录并重新预览，已执行 DDL 可能已经生效。", inner)
{
    public SchemaApplyResult Result { get; } = result;
}
