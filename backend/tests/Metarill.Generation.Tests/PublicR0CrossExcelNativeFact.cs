using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>只发现公开用途的专属设置；缺少设置明确跳过，不以通用实库配置启用。</summary>
public sealed class PublicR0CrossExcelNativeFactAttribute:FactAttribute
{
    /// <summary>发现阶段只检查路径存在，运行时仍须完整核验租约。</summary>
    public PublicR0CrossExcelNativeFactAttribute(bool overwrite=false)
    {
        var key=overwrite?PublicR0CrossExcelNativeLease.OverwriteEnvironmentKey:PublicR0CrossExcelNativeLease.EnvironmentKey;
        if(!File.Exists(Environment.GetEnvironmentVariable(key)))Skip="公开R0完整包尚无本轮独占新库授权，未运行正式Web／MySQL。";
    }
}
