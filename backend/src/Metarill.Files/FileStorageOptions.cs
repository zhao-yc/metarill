using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Runtime.Files;

/// <summary>存储供应商只由部署配置选择；原对象携带固定位置摘要，切换配置不能重解释旧文件。</summary>
public sealed record FileStorageOptions
{
    public string DefaultProfile { get; init; } = "local";
    public Dictionary<string, S3FileStorageProfile> S3Profiles { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>S3兼容连接的私有配置；日志字符串始终不输出凭据。</summary>
public sealed record S3FileStorageProfile
{
    public string Endpoint { get; init; } = "";
    public string Region { get; init; } = "us-east-1";
    public string Bucket { get; init; } = "";
    public string Prefix { get; init; } = "metarill";
    public string AccessKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public string? SessionToken { get; init; }
    public bool ForcePathStyle { get; init; } = true;
    public bool AllowHttp { get; init; }

    /// <summary>不让记录类型的默认格式化把密钥写入异常或诊断。</summary>
    public override string ToString() => "S3FileStorageProfile { 私有部署配置 }";

    /// <summary>复制并验证固定端点/桶/前缀；对象定位中的任何字符串均不能成为连接配置。</summary>
    internal S3FileStorageProfile Snapshot()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" && !(AllowHttp && endpoint.Scheme == "http") ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath != "/" || endpoint.Host.Length == 0 ||
            string.IsNullOrEmpty(Region) || !Regex.IsMatch(Region, "^[a-z0-9-]{1,64}$", RegexOptions.CultureInvariant) ||
            string.IsNullOrEmpty(Bucket) || !Regex.IsMatch(Bucket, "^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant) ||
            Bucket.Contains("..", StringComparison.Ordinal) ||
            Prefix is null || Prefix.Length > 128 || Prefix.Length > 0 && Prefix.Split('/').Any(part => !Regex.IsMatch(part, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)) ||
            string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey) ||
            AccessKey.Length > 256 || SecretKey.Length > 512 || AccessKey.Any(char.IsControl) || SecretKey.Any(char.IsControl) ||
            SessionToken is not null && (SessionToken.Length > 8192 || SessionToken.Any(char.IsControl)))
            throw new InvalidOperationException("S3私有配置无效，请核对端点、区域、桶、前缀与凭据。");
        return this with { Endpoint = endpoint.GetLeftPart(UriPartial.Authority) };
    }

    /// <summary>摘要不含可轮换凭据；修改对象位置或寻址方式必须发布新配置别名。</summary>
    internal string LocationId() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { Endpoint, Region, Bucket, Prefix, ForcePathStyle }))))[..32];
}
