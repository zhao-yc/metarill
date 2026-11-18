using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Generation;

/// <summary>原列表和名称筛选共用表级显示属性归属；页级取值覆盖不能重命名表级辅助列。</summary>
internal static class SelectionDisplayFieldCompilation
{
    /// <summary>沿原控件配置读取名称字段，空原定义继续采用 name。</summary>
    private static string NameField(TableModel table,ColumnModel source)
    {
        if(DatabaseCheckboxCompilation.Generic(table,source))return DatabaseCheckboxDefinition.Parse(source.Source.ControlDetail).NameField;
        if(source.Source.ControlKind is "List" or "SelectPage" or "MultiSelectPage")
        {
            try
            {
                using var definition=JsonDocument.Parse(source.Source.ControlDetail);
                if(definition.RootElement.TryGetProperty("nameField",out var field)&&field.ValueKind==JsonValueKind.String&&field.GetString() is {Length:>0} configured)return configured;
            }
            catch(JsonException){/* 能力编译器另行报告非法配置。 */}
        }
        return "name";
    }

    /// <summary>沿用原辅助名规则，显式字段优先。非冗余数组可合成原只读名称。</summary>
    internal static ColumnModel Resolve(TableModel table,ColumnModel source)
    {
        if(source.Source.ConfigTableId!=table.Source.Id)throw new ArgumentException("显示字段必须属于当前表。");
        var display=NameField(table,source);
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

    /// <summary>原单值关系的自动名称只出现在读取投影，显式列优先。</summary>
    internal static ColumnModel? AutomaticSingle(TableModel table,ColumnModel source,ColumnModel name)
    {
        if(!SupportsAutomaticSingle(table,source)||name.IsId||name.IsJsonArray||
            name.Type!="string?"&&!DerivedRelationNamesCompilation.IsTypedLabel(name)||
            ExactDataManageValues.IsSensitiveField(name.Source.ColumnName))return null;
        var stem=source.Source.ColumnName[..^3];
        var alias=stem+"_"+name.Source.ColumnName;
        if(table.Columns.Any(column=>column.Source.ColumnName.Equals(alias,StringComparison.OrdinalIgnoreCase)))return null;
        MetadataValidation.RequireIdentifier(alias);
        var metadata=new ColumnMetadata{Id=source.Source.Id+"_display",ConfigTableId=table.Source.Id,ColumnName=alias,
            LogicalType=name.Source.LogicalType,Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                ["columnFullName"]=JsonSerializer.SerializeToElement(stem+"."+name.Source.ColumnName)}};
        return new(metadata,GenerationModel.Pascal(alias),GenerationModel.Camel(alias),name.Type);
    }

    /// <summary>原页面只绑定当前读取页已开放的自动名称。</summary>
    internal static ColumnModel? AutomaticSingle(GenerationModel model,TableModel table,PageModel page,ColumnModel source,string? display=null)
    {
        if(!SupportsAutomaticSingle(table,source)||!page.Readable.Any(column=>column.Source.Id==source.Source.Id))return null;
        var nameField=NameField(table,source);
        if(display is not null&&display!=nameField)return null;
        var target=ForeignValidationRenderer.Target(model,source);
        var name=target?.Columns.SingleOrDefault(column=>column.Source.ColumnName==nameField);
        return name is null?null:AutomaticSingle(table,source,name);
    }

    /// <summary>只有非冗余的普通单值外键使用自动名称。</summary>
    private static bool SupportsAutomaticSingle(TableModel table,ColumnModel source)=>
        table.Source.IsRongyu==0&&source.Source.ConfigTableId==table.Source.Id&&source.Source.ControlKind is "List" or "SelectPage"&&
        source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal);
}
