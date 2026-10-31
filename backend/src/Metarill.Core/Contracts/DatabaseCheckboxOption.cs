namespace Metarill.Core.Contracts;

/// <summary>原数据库复选沿用id/name/value/text四字段，标识始终为精确字符串。</summary>
public sealed record DatabaseCheckboxOption(string Id,string? Name,string Value,string? Text);
