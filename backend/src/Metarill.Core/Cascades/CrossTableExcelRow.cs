using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
namespace Metarill.Core.Cascades;

/// <summary>整行级联先收集再合并，显式祖先及共享槽冲突不由Excel列顺序决定。</summary>
public sealed class CrossTableExcelRow(IReadOnlySet<string>? textKeys)
{
    public CrossTableExcelRow():this(null){}
    private readonly List<(string Header,Dictionary<string,JsonElement> Values,bool Clear)> cells=[];
    private readonly HashSet<string> auxiliaryTail=new(StringComparer.Ordinal);
    /// <summary>只接生成计划给出的ID槽；克隆单元格结果，避免JsonDocument释放或外部改写。</summary>
    public void Add(string header,IReadOnlyDictionary<string,JsonElement> values,bool clear) =>
        Add(header, values, clear, null);

    /// <summary>固定父级的祖先只约束同行主动路径，保留原空值和字段存在性。</summary>
    public void Add(string header, IReadOnlyDictionary<string,JsonElement> values, bool clear,
        IReadOnlyDictionary<string,JsonElement>? fixedPath)
    {
        if(string.IsNullOrWhiteSpace(header)||cells.Any(cell=>cell.Header==header))throw Invalid(header);
        var copy=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
        foreach(var pair in values)
        {
            if(string.IsNullOrWhiteSpace(pair.Key))throw Invalid(header);
            var key=Key(pair.Key,pair.Value,header);
            if(fixedPath is not null&&fixedPath.TryGetValue(pair.Key,out var fixedValue))
            {
                if(clear)continue;
                if(key!=Key(pair.Key,fixedValue,header))throw Invalid(header);
            }
            copy.Add(pair.Key,pair.Value.Clone());
        }
        cells.Add((header,copy,clear));
    }
    /// <summary>显式辅助列可表示部分路径，尾部空槽只补没有其它单元格声明的位置。</summary>
    public void AddAuxiliary(string header,IReadOnlyDictionary<string,JsonElement> values,bool clear,
        IReadOnlyDictionary<string,JsonElement>? fixedPath,IReadOnlyList<string> tail)
    {
        Add(header,values,clear,fixedPath);
        auxiliaryTail.UnionWith(tail);
    }

    /// <summary>非空路径必须彼此及显式ID一致；空单元格仅填未声明的槽，保持旧清空合同。</summary>
    public Dictionary<string,JsonElement> Merge(IReadOnlyDictionary<string,JsonElement> declared)
    {
        var merged=declared.ToDictionary(pair=>pair.Key,pair=>pair.Value.Clone(),StringComparer.Ordinal);
        foreach(var group in cells.SelectMany(cell=>cell.Values.Select(pair=>(cell.Header,cell.Clear,pair.Key,pair.Value))).GroupBy(value=>value.Key,StringComparer.Ordinal))
        {
            var active=group.Where(cell=>!cell.Clear).ToArray();
            if(active.Length==0){if(!merged.ContainsKey(group.Key))merged[group.Key]=JsonSerializer.SerializeToElement<object?>(null);continue;}
            var first=active[0];var key=Key(group.Key,first.Value,first.Header);
            foreach(var cell in active.Skip(1))if(Key(group.Key,cell.Value,cell.Header)!=key)throw Invalid(cell.Header);
            if(declared.TryGetValue(group.Key,out var explicitValue)&&Key(group.Key,explicitValue,first.Header)!=key)throw Invalid(first.Header);
            merged[group.Key]=first.Value;
        }
        foreach(var field in auxiliaryTail)merged.TryAdd(field,JsonSerializer.SerializeToElement<object?>(null));
        return merged;
    }
    /// <summary>仅比较ID含义；数字和字符串保持整数精度，null/空串均表示未选择。</summary>
    private string? Key(string slot,JsonElement value,string header)
    {
        if(value.ValueKind==JsonValueKind.Null)return null;
        var text=value.ValueKind==JsonValueKind.String?value.GetString():value.ValueKind==JsonValueKind.Number?value.GetRawText():throw Invalid(header);
        if(text=="")return null;
        if(textKeys?.Contains(slot)==true)return !string.IsNullOrWhiteSpace(text)?text:throw Invalid(header);
        if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(CultureInfo.InvariantCulture))throw Invalid(header);
        return text;
    }
    /// <summary>列名来自生成器固定表头，沿用原单元格错误封装。</summary>
    private static PageImportException Invalid(string header)=>PageImportException.Input(PageImportError.Relation,columnName:header);
}
