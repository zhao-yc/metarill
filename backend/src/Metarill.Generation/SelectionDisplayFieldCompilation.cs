using System.Text.Json;

namespace Metarill.Generation;

/// <summary>原列表和名称筛选共用表级显示属性归属；页级取值覆盖不能重命名表级辅助列。</summary>
internal static class SelectionDisplayFieldCompilation
{
    /// <summary>沿用原辅助名规则，只返回已声明字段；无名称辅助列时保留源ID属性。</summary>
    internal static ColumnModel Resolve(TableModel table,ColumnModel source)
    {
        if(source.Source.ConfigTableId!=table.Source.Id)throw new ArgumentException("显示字段必须属于当前表。");
        var display="name";
        if(source.Source.ControlKind is "List" or "SelectPage" or "MultiSelectPage")
        {
            try
            {
                using var definition=JsonDocument.Parse(source.Source.ControlDetail);
                if(definition.RootElement.TryGetProperty("nameField",out var field)&&field.ValueKind==JsonValueKind.String&&field.GetString() is {Length:>0} configured)display=configured;
            }
            catch(JsonException){/* 原列表空定义沿用name，能力编译器另行报告非法配置，不改变可见布局。 */}
        }
        if(display.Equals("id",StringComparison.OrdinalIgnoreCase))return source;
        var column=source.Source.ColumnName;
        var expected=column.EndsWith("_ids",StringComparison.Ordinal)?column[..^4]+"_"+(display.EndsWith('s')?display:display+"s"):column.EndsWith("_id",StringComparison.Ordinal)?column[..^3]+"_"+display:column;
        var declared=table.Columns.SingleOrDefault(candidate=>candidate.Source.ColumnName==expected);
        if(declared is not null)return declared;
        if(table.Source.IsRongyu!=0||source.Source.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase"))return source;
        // 非冗余自动名称只属于生成投影，不能为了显示创建数据库列或写回配置元数据。
        var metadata=new Metarill.Core.Metadata.ColumnMetadata{Id=source.Source.Id+"_display",ConfigTableId=table.Source.Id,ColumnName=expected,LogicalType="string",ColumnLength="max",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement(column[..^4]+"."+display)}};
        return new(metadata,GenerationModel.Pascal(expected),GenerationModel.Camel(expected),"string?",IsManagedMultiNames:true);
    }
}
