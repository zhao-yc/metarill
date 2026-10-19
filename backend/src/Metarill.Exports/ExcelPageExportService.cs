using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Core.Security;

namespace Metarill.Runtime.Exports;

/// <summary>将已受页面权限和数据范围约束的投影写为 Excel，下载票据绑定真实账号及当前项目，原页面可显式签发短时下载凭证。</summary>
public sealed partial class ExcelPageExportService : IPageExportService
{
    private sealed record Ticket(long AccountId, string ProjectId, string Path, string Name, DateTimeOffset ExpiresAt, DateTimeOffset? DownloadExpiresAt);
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly ExportOptions _options;
    private readonly TimeProvider _clock;
    private readonly string _root;
    private readonly SemaphoreSlim _writers;
    private readonly object _sync = new();
    private readonly Dictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activePaths = new(StringComparer.Ordinal);
    private int _reservations;
    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex TicketPattern();
    [GeneratedRegex("^[a-f0-9]{32}\\.xlsx(?:\\.part)?$", RegexOptions.CultureInvariant)] private static partial Regex ObjectPattern();

    /// <summary>校验私有目录与资源限额，时间提供器便于确定性验证过期行为。</summary>
    public ExcelPageExportService(ExportOptions options, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TimeToLive <= TimeSpan.Zero || options.TimeToLive > TimeSpan.FromDays(1) || options.MaxRows is < 1 or > 1_048_575 ||
            options.MaxColumns is < 1 or > 16_384 || options.MaxCells < 1 || options.MaxCellCharacters is < 1 or > 32_767 ||
            options.MaxTotalTextCharacters < 1 || options.MaxFileBytes < 1 || options.MaxActiveTickets < 1 || options.MaxConcurrentExports is < 1 or > 32)
            throw new InvalidOperationException("导出有效期或资源限制配置无效。");
        _options = options; _clock = clock ?? TimeProvider.System; _root = Path.GetFullPath(options.RootDirectory);
        _writers = new SemaphoreSlim(options.MaxConcurrentExports, options.MaxConcurrentExports);
    }

    /// <summary>输入只含生成层开放的列投影，服务不接受客户端 SQL、路径或模板文件。</summary>
    public Task<string> CreateAsync(PageExportRequest request, RuntimeActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateCoreAsync(actor, (_, _) => Task.FromResult(request), false, cancellationToken);
    }

    /// <summary>先限制并发与票据额度，再把服务端行数上限传给数据库读取委托。</summary>
    public Task<string> CreateAsync(RuntimeActor actor, Func<int, CancellationToken, Task<PageExportRequest>> prepare, CancellationToken cancellationToken = default)
        => CreateCoreAsync(actor, prepare, false, cancellationToken);

    /// <summary>仅固定页面的已授权导出入口调用；无头凭证为256位随机值，最多两分钟且不超文件有效期。</summary>
    public Task<string> CreateDownloadAsync(RuntimeActor actor, Func<int, CancellationToken, Task<PageExportRequest>> prepare, CancellationToken cancellationToken = default)
        => CreateCoreAsync(actor, prepare, true, cancellationToken);

    /// <summary>兼容入口和延迟读取入口共享唯一资源门禁，失败或取消均释放预留额度。</summary>
    private async Task<string> CreateCoreAsync(RuntimeActor actor, Func<int, CancellationToken, Task<PageExportRequest>> prepare, bool downloadGrant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare); ValidateActor(actor);
        cancellationToken.ThrowIfCancellationRequested();
        await _writers.WaitAsync(cancellationToken);
        string? temporary = null; string? destination = null; var reserved = false;
        try
        {
            await CleanupExpiredAsync(cancellationToken);
            lock (_sync)
            {
                if (_tickets.Count + _reservations >= _options.MaxActiveTickets) throw new InvalidOperationException("当前导出文件数量达到上限，请等待过期清理。");
                _reservations++; reserved = true;
            }
            // 读取必须发生在已取得并发槽和预留票据之后，不能先全量物化再校验。
            var request = await prepare(_options.MaxRows, cancellationToken);
            ArgumentNullException.ThrowIfNull(request); ValidateDimensions(request);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRoot();
            var key = Guid.NewGuid().ToString("N") + ".xlsx";
            destination = Path.Combine(_root, key); temporary = destination + ".part";
            lock (_sync) { _activePaths.Add(destination); _activePaths.Add(temporary); }
            var title = SafeTitle(request.Title);
            // 每个任务独立工作簿，不共享 ClosedXML 对象；同步保存过程通过受限流观察取消。
            using (var workbook = BuildWorkbook(request, title, cancellationToken))
            {
                await using var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous);
                using var limited = new LimitedWriteStream(file, _options.MaxFileBytes, cancellationToken);
                workbook.SaveAs(limited, new SaveOptions { EvaluateFormulasBeforeSaving = false, GenerateCalculationChain = false });
                await file.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination); temporary = null;
            var exportId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            lock (_sync)
            {
                var now = _clock.GetUtcNow(); var expiresAt = now.Add(_options.TimeToLive);
                var grantExpiresAt = now.AddMinutes(2);
                _tickets.Add(exportId, new Ticket(actor.AccountId, actor.ProjectId, destination, title + ".xlsx", expiresAt, downloadGrant ? (grantExpiresAt < expiresAt ? grantExpiresAt : expiresAt) : null));
                _activePaths.Remove(destination); _activePaths.Remove(destination + ".part");
            }
            destination = null;
            return exportId;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("导出文件写入失败，请检查私有存储。", exception); }
        finally
        {
            if (temporary is not null) TryDelete(temporary);
            if (destination is not null) TryDelete(destination);
            lock (_sync)
            {
                if (temporary is not null) _activePaths.Remove(temporary);
                if (destination is not null) { _activePaths.Remove(destination); _activePaths.Remove(destination + ".part"); }
                if (reserved) _reservations--;
            }
            _writers.Release();
        }
    }

    /// <summary>票据可在有效期内重试下载；管理员也不能跨账号或跨项目使用他人票据。</summary>
    public Task<ExportContentHandle> OpenAsync(string exportId, RuntimeActor actor, CancellationToken cancellationToken = default)
    {
        ValidateActor(actor); return OpenCoreAsync(exportId, actor, false, cancellationToken);
    }

    /// <summary>原 openUrl 无认证头，只凭已签发的两分钟 bearer 凭证读取该文件；不伪造账号或项目身份。</summary>
    public Task<ExportContentHandle> OpenDownloadAsync(string exportId, CancellationToken cancellationToken = default)
        => OpenCoreAsync(exportId, null, true, cancellationToken);

    /// <summary>带头账号约束与短时无头凭证共用对象检查，任何拒绝均不泄露票据状态或物理路径。</summary>
    private async Task<ExportContentHandle> OpenCoreAsync(string exportId, RuntimeActor? actor, bool downloadGrant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(exportId) || !TicketPattern().IsMatch(exportId)) throw Denied();
        await CleanupExpiredAsync(cancellationToken);
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            if (!_tickets.TryGetValue(exportId, out var ticket) || ticket.ExpiresAt <= now) throw Denied();
            if (downloadGrant ? ticket.DownloadExpiresAt is null || ticket.DownloadExpiresAt <= now : actor is null || ticket.AccountId != actor.AccountId || ticket.ProjectId != actor.ProjectId) throw Denied();
            try
            {
                if (File.GetAttributes(ticket.Path).HasFlag(FileAttributes.ReparsePoint)) throw Denied();
                var stream = new FileStream(ticket.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return new ExportContentHandle(stream, ticket.Name, ExcelContentType, stream.Length, downloadGrant ? ticket.DownloadExpiresAt!.Value : ticket.ExpiresAt);
            }
            catch (FileNotFoundException) { _tickets.Remove(exportId); throw Denied(); }
            catch (DirectoryNotFoundException) { _tickets.Remove(exportId); throw Denied(); }
            catch (IOException exception) { throw new InvalidOperationException("导出文件当前无法读取。", exception); }
            catch (UnauthorizedAccessException exception) { throw new InvalidOperationException("导出文件当前无法读取。", exception); }
        }
    }

    /// <summary>过期立即失去下载权限；同时清理本实例文件及崩溃后留下的过期随机对象。</summary>
    public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var now = _clock.GetUtcNow(); var paths = new HashSet<string>(StringComparer.Ordinal);
        lock (_sync)
        {
            foreach (var pair in _tickets.Where(t => t.Value.ExpiresAt <= now).ToArray()) { paths.Add(pair.Value.Path); _tickets.Remove(pair.Key); }
            if (Directory.Exists(_root))
            {
                EnsureRoot();
                foreach (var path in Directory.EnumerateFiles(_root))
                    if (ObjectPattern().IsMatch(Path.GetFileName(path)) && !_activePaths.Contains(path) && !_tickets.Values.Any(t => t.Path == path) && File.GetLastWriteTimeUtc(path) <= now.UtcDateTime.Subtract(_options.TimeToLive)) paths.Add(path);
            }
        }
        var removed = 0;
        foreach (var path in paths) { cancellationToken.ThrowIfCancellationRequested(); if (TryDelete(path)) removed++; }
        return Task.FromResult(removed);
    }

    /// <summary>拒绝尺寸不一致的行，不能默默错位或丢弃超限数据。</summary>
    private void ValidateDimensions(PageExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PageId) || request.PageId.Length > 100 || request.Title?.Length > 1000 || request.Headers is null || request.Rows is null ||
            request.Headers.Count < 1 || request.Headers.Count > _options.MaxColumns || request.Rows.Count > _options.MaxRows ||
            (long)(request.Rows.Count + 1) * request.Headers.Count > _options.MaxCells)
            throw new InvalidOperationException("导出页面、行列数量或单元格总量超过限制。");
        foreach (var row in request.Rows) if (row is null || row.Count != request.Headers.Count) throw new InvalidOperationException("导出行与表头列数不一致。");
    }

    /// <summary>类型明确写入，字符串从不赋给公式API；避免自动列宽测量依赖服务器字体。</summary>
    private XLWorkbook BuildWorkbook(PageExportRequest request, string title, CancellationToken token)
    {
        var workbook = new XLWorkbook();
        workbook.Properties.Author = "Metarill"; workbook.Properties.LastModifiedBy = "Metarill";
        try
        {
            var worksheet = workbook.Worksheets.Add(TrimRunes(title, 31).Trim('\''));
            long characters = 0;
            for (var column = 0; column < request.Headers.Count; column++) ExportCellWriter.Write(worksheet.Cell(1, column + 1), request.Headers[column], _options, ref characters);
            worksheet.Row(1).Style.Font.Bold = true; worksheet.SheetView.FreezeRows(1);
            for (var row = 0; row < request.Rows.Count; row++)
            {
                token.ThrowIfCancellationRequested();
                for (var column = 0; column < request.Headers.Count; column++) ExportCellWriter.Write(worksheet.Cell(row + 2, column + 1), request.Rows[row][column], _options, ref characters);
            }
            worksheet.Columns(1, request.Headers.Count).Width = 22;
            return workbook;
        }
        catch { workbook.Dispose(); throw; }
    }

    /// <summary>标题只作为下载显示名及工作表名，移除路径/控制字符并避免截断代理字符。</summary>
    private static string SafeTitle(string? title)
    {
        var builder = new StringBuilder();
        foreach (var rune in (title ?? "").EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator || rune.IsAscii && "<>:\"/\\|?*[]".Contains((char)rune.Value)) builder.Append('_');
            else builder.Append(rune.ToString());
        }
        var result = TrimRunes(builder.ToString().Trim(' ', '.', '\''), 100);
        if (result.Length == 0) result = "导出数据";
        if (Regex.IsMatch(result, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9]|History)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) result = "导出_" + result;
        return result;
    }

    /// <summary>按 UTF-16 上限截取完整 Unicode 字符，Excel 工作表最多31单位。</summary>
    private static string TrimRunes(string value, int maximum)
    {
        var builder = new StringBuilder(); foreach (var rune in value.EnumerateRunes()) { if (builder.Length + rune.Utf16SequenceLength > maximum) break; builder.Append(rune.ToString()); } return builder.ToString();
    }
    /// <summary>仅创建私有根目录，不允许根本身为符号链接。</summary>
    private void EnsureRoot() { Directory.CreateDirectory(_root); if (File.GetAttributes(_root).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("导出目录不能是符号链接。"); }
    /// <summary>只删除实现内部生成的路径，清理失败保留给下次维护重试。</summary>
    private static bool TryDelete(string path) { try { File.Delete(path); return true; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }
    /// <summary>拒绝没有真实账号或项目上下文的后台调用。</summary>
    private static void ValidateActor(RuntimeActor actor) { if (actor is null || actor.AccountId <= 0 || string.IsNullOrWhiteSpace(actor.ProjectId) || actor.ProjectId.Length > 100) throw Denied(); }
    /// <summary>不存在、过期与越权保持一致，避免票据状态探测。</summary>
    private static UnauthorizedAccessException Denied() => new("导出文件不存在、已过期或无权下载。");
}
