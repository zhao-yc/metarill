using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
namespace Metarill.Core.Cascades;

/// <summary>整行级联先收集再合并，显式祖先及共享槽冲突不由Excel列顺序决定。</summary>
public sealed class CrossTableExcelRow
{
    private readonly List<(string Header,Dictionary<string,JsonElement> Values,bool Clear)> cells=[];
    /// <summary>只接生成计划给出的ID槽；克隆单元格结果，避免JsonDocument释放或外部改写。</summary>
    public void Add(string header,IReadOnlyDictionary<string,JsonElement> values,bool clear)
    {
        if(string.IsNullOrWhiteSpace(header)||cells.Any(cell=>cell.Header==header))throw Invalid(header);
        var copy=values.ToDictionary(pair=>pair.Key,pair=>pair.Value.Clone(),StringComparer.Ordinal);
        foreach(var pair in copy){if(string.IsNullOrWhiteSpace(pair.Key))throw Invalid(header);_=Key(pair.Value,header);}
        cells.Add((header,copy,clear));
    }
    /// <summary>非空路径必须彼此及显式ID一致；空单元格仅填未声明的槽，保持旧清空合同。</summary>
    public Dictionary<string,JsonElement> Merge(IReadOnlyDictionary<string,JsonElement> declared)
    {
        var merged=declared.ToDictionary(pair=>pair.Key,pair=>pair.Value.Clone(),StringComparer.Ordinal);
        foreach(var group in cells.SelectMany(cell=>cell.Values.Select(pair=>(cell.Header,cell.Clear,pair.Key,pair.Value))).GroupBy(value=>value.Key,StringComparer.Ordinal))
        {
            var active=group.Where(cell=>!cell.Clear).ToArray();
            if(active.Length==0){if(!merged.ContainsKey(group.Key))merged[group.Key]=JsonSerializer.SerializeToElement<object?>(null);continue;}
            var first=active[0];var key=Key(first.Value,first.Header);
            foreach(var cell in active.Skip(1))if(Key(cell.Value,cell.Header)!=key)throw Invalid(cell.Header);
            if(declared.TryGetValue(group.Key,out var explicitValue)&&Key(explicitValue,first.Header)!=key)throw Invalid(first.Header);
            merged[group.Key]=first.Value;
        }
        return merged;
    }
    /// <summary>仅比较ID含义；数字和字符串保持整数精度，null/空串均表示未选择。</summary>
    private static string? Key(JsonElement value,string header)
    {
        if(value.ValueKind==JsonValueKind.Null)return null;
        var text=value.ValueKind==JsonValueKind.String?value.GetString():value.ValueKind==JsonValueKind.Number?value.GetRawText():throw Invalid(header);
        if(text=="")return null;
        if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(CultureInfo.InvariantCulture))throw Invalid(header);
        return text;
    }
    /// <summary>列名来自生成器固定表头，沿用原单元格错误封装。</summary>
    private static PageImportException Invalid(string header)=>PageImportException.Input(PageImportError.Relation,columnName:header);
}
