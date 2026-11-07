namespace Metarill.Generation.Tests;

/// <summary>测试宿主退出后，等待 Windows 短暂占用释放；持久失败仍使测试失败。</summary>
internal static class TestDirectoryCleanup
{
    /// <summary>只删除指定测试根下的随机目录，限时等待文件释放，不扩大清理范围。</summary>
    public static async Task DeleteAsync(string directory, string expectedRoot)
    {
        var target = Path.GetFullPath(directory);
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedRoot)) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
            throw new InvalidOperationException("拒绝清理当前测试随机目录之外的路径。");
        for (var attempt = 0; Directory.Exists(target); attempt++)
        {
            try { Directory.Delete(target, true); return; }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && attempt < 30)
            { await Task.Delay(100); }
        }
    }
}
