using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Metarill.Runtime.Exports;

/// <summary>宿主注册此后台任务后，即使无人访问导出端点也会定期清理过期私有文件。</summary>
public sealed class ExportCleanupWorker(ExcelPageExportService exports, ILogger<ExportCleanupWorker> logger) : BackgroundService
{
    /// <summary>先清理崩溃遗留，再按分钟维护；失败可重试，不能终止整个应用。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await exports.CleanupExpiredAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning(exception, "过期导出文件清理失败，将在下一轮重试。"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
