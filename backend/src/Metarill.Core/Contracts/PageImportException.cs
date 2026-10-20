namespace Metarill.Core.Contracts;

/// <summary>导入错误只使用固定分类，不能用任意异常消息充当可展示业务提示。</summary>
public enum PageImportError { RowsArray, RowObject, DuplicateHeader, UnknownHeader, Number, Boolean, DateTime, Array, Constant, Relation }

/// <summary>明确区分可修正的单元格输入与内部执行失败；内部异常链只供宿主日志使用。</summary>
public sealed class PageImportException : Exception
{
    public bool IsInputError { get; }
    public int? RowNumber { get; }
    public string? ColumnName { get; }
    public PageImportError? Code { get; }

    private PageImportException(bool input, int? rowNumber, string? columnName, PageImportError? code, Exception? inner)
        : base(MessageFor(input, rowNumber, columnName, code), inner)
        => (IsInputError, RowNumber, ColumnName, Code) = (input, rowNumber, columnName, code);

    /// <summary>列名只能来自生成器已发布标题；未知或重复的Excel标题强制不回显客户端文本。</summary>
    public static PageImportException Input(PageImportError code, int? rowNumber = null, string? columnName = null, Exception? inner = null)
    {
        if (!Enum.IsDefined(code)) throw new ArgumentOutOfRangeException(nameof(code));
        if (rowNumber is <= 0) throw new ArgumentOutOfRangeException(nameof(rowNumber));
        if (code is PageImportError.RowsArray or PageImportError.RowObject or PageImportError.DuplicateHeader or PageImportError.UnknownHeader) columnName = null;
        return new(true, rowNumber, columnName, code, inner);
    }

    /// <summary>内部错误仅记录安全行上下文并保留原异常；宿主不得把此消息作为输入提示。</summary>
    public static PageImportException Execution(int rowNumber, Exception inner)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowNumber); ArgumentNullException.ThrowIfNull(inner);
        return new(false, rowNumber, null, null, inner);
    }

    /// <summary>外层事务补充从1开始的数据行号；已有合法行号不可被另一层覆盖。</summary>
    public PageImportException AtRow(int rowNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowNumber);
        if (!IsInputError) throw new InvalidOperationException("只能为导入输入错误补充行号。");
        return RowNumber.HasValue ? this : Input(Code!.Value, rowNumber, ColumnName, InnerException);
    }

    /// <summary>错误原因全部为固定中文，不拼接原值、SQL、路径或InnerException.Message。</summary>
    private static string MessageFor(bool input, int? row, string? column, PageImportError? code)
    {
        if (!input) return $"导入第{row}行执行失败。";
        var prefix = row is {} number ? $"第{number}行" : "导入数据";
        if (column is not null) prefix += $"，列「{column}」";
        var reason = code switch
        {
            PageImportError.RowsArray => "必须提交Excel解析后的行数组。", PageImportError.RowObject => "每行必须是标题与单元格值组成的对象。",
            PageImportError.DuplicateHeader => "导入标题重复，请核对模板。", PageImportError.UnknownHeader => "存在未开放的导入列，请核对模板。",
            PageImportError.Number => "应填写有效数字。", PageImportError.Boolean => "应填写有效布尔选项。", PageImportError.DateTime => "应填写有效日期或时间。",
            PageImportError.Array => "应填写有效数组。", PageImportError.Constant => "应填写已配置的选项。", PageImportError.Relation => "关联名称无匹配或存在重名，请修正后导入。",
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
        return prefix + "：" + reason;
    }
}
