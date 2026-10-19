using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Files;

public sealed record FileLibraryOptions
{
    public string RootDirectory { get; init; } = ".local/files";
    public long MaxFileBytes { get; init; } = 100L * 1024 * 1024;
    public IReadOnlySet<string> AllowedExtensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".txt", ".csv", ".xlsx", ".xls", ".docx", ".doc", ".pptx", ".ppt", ".zip", ".mp3", ".mp4" };
}
public sealed record FileUploadRequest(string OriginalName, string? MimeType, long SizeBytes, string? ConfigColumnId = null, long? FolderId = null, string? Sha256 = null);
public sealed record LocalUploadPlan(string ProviderCode, string UploadMode, string UploadUrl, IReadOnlyDictionary<string, string> Headers, IReadOnlyDictionary<string, string> FormData, DateTimeOffset ExpireAt);
public sealed record FileUploadPlan(long FileId, LocalUploadPlan Plan);
public sealed record FileDescriptor(long FileId, string OriginalName, string MimeType, long SizeBytes, string AccessMode, string Status, string? Url, string Mode, long? FolderId, string? Sha256);
public sealed record FileContentHandle(Stream Stream, string ContentType, string Name, long Length, string Sha256);
public sealed record FileDeleteResult(long FileId, bool StorageRemoved);
public sealed record FileFolder(long Id, string Name, long? ParentFolderId);

/// <summary>文件安全规则不接受客户端对象键、磁盘路径或远端 URL。</summary>
public static partial class FileValidation
{
    [GeneratedRegex("^[a-f0-9]{32}\\.blob$", RegexOptions.CultureInvariant)] private static partial Regex StorageKeyPattern();
    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex DigestPattern();

    /// <summary>文件名仅用于显示；拒绝路径、控制字符和平台保留符号。</summary>
    public static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != name.Trim() || name is "." or ".." ||
            name.IndexOfAny(['/', '\\', ':', '<', '>', '|', '?', '*']) >= 0 || name.Any(char.IsControl))
            throw new InvalidOperationException("文件名无效，不允许路径或控制字符。");
        return name;
    }

    /// <summary>只把服务端随机键映射到固定根目录，不信任数据库中的任意路径。</summary>
    public static string StoragePath(string rootDirectory, string storageKey)
    {
        if (!StorageKeyPattern().IsMatch(storageKey)) throw new InvalidOperationException("存储键无效。");
        var root = Path.GetFullPath(rootDirectory);
        var path = Path.GetFullPath(Path.Combine(root, storageKey));
        if (Path.GetDirectoryName(path) != root) throw new InvalidOperationException("存储路径超出文件根目录。");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("文件对象不能是符号链接。");
        return path;
    }

    /// <summary>摘要只允许完整 SHA-256 十六进制，不接受客户端摘要作为已核验结果。</summary>
    public static string? Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        if (!DigestPattern().IsMatch(digest)) throw new InvalidOperationException("SHA-256 摘要格式无效。");
        return digest.ToLowerInvariant();
    }

    /// <summary>响应 MIME 来自服务端扩展名映射，上传者不能把普通文件声明成 HTML。</summary>
    public static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".webp" => "image/webp",
        ".pdf" => "application/pdf", ".txt" => "text/plain", ".csv" => "text/csv", ".mp3" => "audio/mpeg", ".mp4" => "video/mp4",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xls" => "application/vnd.ms-excel",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".doc" => "application/msword",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation", ".ppt" => "application/vnd.ms-powerpoint",
        ".zip" => "application/zip", _ => "application/octet-stream"
    };

    /// <summary>解析旧控件的 ID 数组或对象数组；URL 和其他字段永远不能成为存储定位。</summary>
    public static IReadOnlyList<long> ParseFileIds(object? value)
    {
        if (value is null || value is string empty && string.IsNullOrWhiteSpace(empty)) return [];
        JsonElement json;
        try { json = value is JsonElement element ? element : value is string text ? JsonSerializer.Deserialize<JsonElement>(text) : JsonSerializer.SerializeToElement(value); }
        catch (JsonException exception) { throw new InvalidOperationException("文件字段必须是合法 JSON 数组。", exception); }
        if (json.ValueKind == JsonValueKind.Null) return [];
        if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() > 100) throw new InvalidOperationException("文件字段必须是最多 100 项的数组。");
        var result = new List<long>();
        foreach (var item in json.EnumerateArray())
        {
            var idValue = item;
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (!item.TryGetProperty("fileId", out idValue) || idValue.ValueKind == JsonValueKind.Null || string.IsNullOrWhiteSpace(idValue.ToString()))
                    if (!item.TryGetProperty("id", out idValue)) throw new InvalidOperationException("文件对象缺少 fileId。");
            }
            if (idValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || !long.TryParse(idValue.ToString(), out var id) || id <= 0)
                throw new InvalidOperationException("文件标识必须是正整数，不能使用 URL。");
            if (!result.Contains(id)) result.Add(id);
        }
        return result;
    }

    /// <summary>控件只允许已知访问模式、大小和扩展名规则，未知配置明确失败。</summary>
    internal static FileControlPolicy Policy(ColumnMetadata? column, FileLibraryOptions options)
    {
        if (options.MaxFileBytes is <= 0 or > 100L * 1024 * 1024) throw new InvalidOperationException("服务端文件大小上限必须在100MiB以内。");
        var access = "PROTECTED"; var maximum = options.MaxFileBytes; var count = 9;
        IReadOnlySet<string> extensions = options.AllowedExtensions;
        if (column is not null && !string.IsNullOrWhiteSpace(column.ControlDetail))
        {
            using var detail = JsonDocument.Parse(column.ControlDetail);
            if (detail.RootElement.TryGetProperty("accessMode", out var mode)) access = mode.GetString() ?? "PROTECTED";
            if (detail.RootElement.TryGetProperty("maxSizeMb", out var size) && size.ValueKind != JsonValueKind.Null)
            {
                var mb = size.GetInt32();
                if (mb is <= 0 or > 10240) throw new InvalidOperationException("控件大小上限无效。");
                maximum = Math.Min(maximum, mb * 1024L * 1024L);
            }
            if (detail.RootElement.TryGetProperty("maxCount", out var limit) && limit.ValueKind != JsonValueKind.Null) count = limit.GetInt32();
            if (detail.RootElement.TryGetProperty("extensions", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
                extensions = list.EnumerateArray().Select(x => "." + (x.GetString() ?? "").TrimStart('.').ToLowerInvariant()).Where(options.AllowedExtensions.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        if (access is not ("PUBLIC" or "PROTECTED") || count is < 1 or > 100) throw new InvalidOperationException("文件控件访问模式或数量上限无效。");
        return new FileControlPolicy(access, maximum, count, extensions);
    }
}
internal sealed record FileControlPolicy(string AccessMode, long MaximumBytes, int MaxCount, IReadOnlySet<string> Extensions);
