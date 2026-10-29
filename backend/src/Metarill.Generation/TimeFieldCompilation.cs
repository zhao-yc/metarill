using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>时间控件与原生TIME使用显式字段元数据分流，避免仅按CLR类型误限原生时长。</summary>
internal static class TimeFieldCompilation
{
    /// <summary>原time选择器为日内时刻，普通文本/反查原生TIME保持总小时合同。</summary>
    internal static bool IsClock(ColumnModel? column)=>column?.Source.ControlKind=="time";
    /// <summary>返回明确框架合同名，实体同名时也不能遮蔽。</summary>
    internal static string Value(ColumnModel? column)=>"global::Metarill.Core.Contracts."+(IsClock(column)?"ClockTimeValue":"SqlTimeValue");
    /// <summary>字段DTO/VO使用自己的转换器，不注册到全局持续时间选项。</summary>
    internal static string Converter(ColumnModel? column)=>"global::Metarill.Core.Contracts."+(IsClock(column)?"ClockTimeJsonConverter":"SqlTimeJsonConverter");
    /// <summary>只为含物理TIME列的模型生成一个批量回读方法，不影响既有无TIME模型。</summary>
    internal static string PersistedValues(GenerationModel model,TableModel table)
    {
        var columns=table.Columns.Where(column=>column.Type=="TimeSpan?"&&NonRedundantReadCompilation.Physical(model,table,column)).ToArray();
        if(columns.Length==0)return "";
        var result=new StringBuilder().AppendLine("    /// <summary>同一写事务读取数据库精度后的值，再执行写后规则及生成回执。</summary>")
            .AppendLine($"    private async Task RefreshPersistedTimesAsync({table.EntityType} entity,CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine($"        var values=await session.Provider.ReadPersistedTimeValuesAsync(session.CurrentTransaction??throw new InvalidOperationException(\"TIME回读缺少写事务。\"),{JsonSerializer.Serialize(table.Source.TableName)},entity.Id,[{string.Join(",",columns.Select(column=>JsonSerializer.Serialize(column.Source.ColumnName)))}],cancellationToken);")
            .AppendLine($"        if(values.Count!={columns.Length})throw new InvalidOperationException(\"TIME回读列数不符。\");");
        for(var i=0;i<columns.Length;i++)
        {
            var column=columns[i];result.AppendLine($"        entity.{column.Property}=values[{i}];");
            if(IsClock(column))result.AppendLine($"        if(entity.{column.Property} is {{ }} clock{column.Property}&&!global::Metarill.Core.Contracts.ClockTimeValue.IsClock(clock{column.Property}))throw new InvalidOperationException(\"数据库精度后的时间不在日内范围，写入已回滚。\");");
        }
        return result.AppendLine("    }").ToString();
    }
    /// <summary>仅在实际发生写入后回读；更新省略保持既有列存在性。</summary>
    internal static string AfterWrite(GenerationModel model,TableModel table,bool updating=false)
        =>table.Columns.Any(column=>column.Type=="TimeSpan?"&&NonRedundantReadCompilation.Physical(model,table,column))?"        "+(updating?"if(submitted.Count>0)":"")+"await RefreshPersistedTimesAsync(entity,cancellationToken);\n":"";
}
