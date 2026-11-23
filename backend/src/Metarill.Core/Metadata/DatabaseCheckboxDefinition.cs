using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>原数据库复选的候选配置；只解析结构，不接受请求SQL或修改发布元数据。</summary>
public sealed record DatabaseCheckboxDefinition(string TableId,string Table,string PageId,string WhereSql,string OrderBy,string LegacyTargetHint)
{
    /// <summary>旧空值/四段仍用name；新JSON名称仅声明标识符，目标用途由编译器证明。</summary>
    public string NameField { get; init; }="name";
    private static readonly DatabaseCheckboxDefinition Empty = new("","","","","","");

    /// <summary>空定义、旧目标字面值、四段及 JSON 格式共用解释；四段前两项不参与原候选查询。</summary>
    public static DatabaseCheckboxDefinition Parse(string? text)=>Empty.WithPageOverride(text);

    /// <summary>页面只覆盖已声明的键，缺省值继承表级配置，显式空值按原规则清空。</summary>
    public DatabaseCheckboxDefinition WithPageOverride(string? text)
    {
        text=text?.Trim()??"";
        if(text.Length==0)return this;
        if(!text.StartsWith('{'))
        {
            var parts=text.Split(';');
            if(parts.Length==4)return this with{WhereSql=parts[2].Trim(),OrderBy=parts[3].Trim()};
            if(parts.Length!=1)throw new InvalidOperationException("数据库复选旧配置必须为空、目标字面值或四段格式，不能忽略候选条件。");
            MetadataValidation.RequireIdentifier(text);return this with{LegacyTargetHint=text};
        }
        try
        {
            using var document=JsonDocument.Parse(text);var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=root.EnumerateObject().Count())throw new InvalidOperationException("数据库复选配置必须为无重复键对象。");
            var known=new[]{"tableId","table","pageId","nameField","whereSql","orderBy"};
            foreach(var field in root.EnumerateObject())
                if(!known.Contains(field.Name,StringComparer.Ordinal)&&field.Name is not("buttonText" or "placeholder" or "description" or "futureOption")||known.Any(name=>name.Equals(field.Name,StringComparison.OrdinalIgnoreCase)&&name!=field.Name))throw new InvalidOperationException("数据库复选配置包含未证明的查询或大小写映射，不能退化为宽候选。");
            string Read(string name,string inherited)
            {
                if(!root.TryGetProperty(name,out var value))return inherited;
                if(value.ValueKind==JsonValueKind.Null)return "";
                return value.ValueKind==JsonValueKind.String?value.GetString()!:value.ValueKind==JsonValueKind.Number&&name is "tableId" or "pageId"&&value.TryGetInt64(out var id)?id.ToString(System.Globalization.CultureInfo.InvariantCulture):throw new InvalidOperationException("数据库复选已知配置字段必须为文本。");
            }
            var label=Read("nameField",NameField);if(label.Length==0)label="name";MetadataValidation.RequireIdentifier(label);
            if(Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(label))throw new NotSupportedException("数据库复选名称不能读取凭据。");
            var table=Read("table",Table);if(table.Length>0)MetadataValidation.RequireIdentifier(table);
            return new(Read("tableId",TableId),table,Read("pageId",PageId),Read("whereSql",WhereSql),
                Read("orderBy",OrderBy),LegacyTargetHint){NameField=label};
        }
        catch(JsonException error){throw new InvalidOperationException("数据库复选配置不是有效JSON。",error);}
    }
    /// <summary>不同页面可以改变名称和排序，持久名称副本须沿用同一候选条件。</summary>
    public bool HasSameCondition(DatabaseCheckboxDefinition other,IEnumerable<string> fields)
    {
        if(WhereSql==other.WhereSql)return true;
        var allowed=fields.ToArray();
        var left=FixedConditionPlan.Compile(WhereSql,allowed).Terms.Select(term=>term.ToDefinition()).ToHashSet(StringComparer.Ordinal);
        return left.SetEquals(FixedConditionPlan.Compile(other.WhereSql,allowed).Terms.Select(term=>term.ToDefinition()));
    }

    /// <summary>普通账号字典的项目与发布页条件已执行，只保留控件独立追加的条件供候选和回填共用。</summary>
    public string AdditionalProjectConditions(string publishedWhere,IEnumerable<string> fields)
    {
        var allowed=fields.ToArray();
        var published=FixedConditionPlan.Compile(publishedWhere,allowed).Terms.Select(Identity).ToHashSet(StringComparer.Ordinal);
        var additional=FixedConditionPlan.Compile(WhereSql,allowed).Terms
            .Where(term=>!GlobalMultipleRelationTarget.IsProjectScope(term)&&!published.Contains(Identity(term))).ToArray();
        if(additional.Any(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
            MultiTargetParentScope.IsParameter(value.Text))))
            throw new InvalidOperationException("复选附加父范围必须来自实际字典页。");
        return string.Join(" AND ",additional.Select(term=>term.ToDefinition()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

        static string Identity(FixedConditionTerm term)=>term is {Comparison:FixedConditionComparison.Equal,
            Values:[{Kind:FixedConditionValueKind.Variable} value]}&&MultiTargetParentScope.IsParameter(value.Text)
            ?term.Field+"=@parentId":term.ToDefinition();
    }

    /// <summary>普通项目字典仅把实际表单标量和附加常量纳入共享名称范围。</summary>
    public string? AdditionalPhysicalFormConditions(MetadataCatalog catalog,TableMetadata target,PageMetadata page)
    {
        if(!WhereSql.Contains("@formParameters.",StringComparison.Ordinal)||
            MultiTargetParentScope.ProjectCheckboxPhysicalConditions(catalog,target,page) is null)
            return null;
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        var physical=storage.PhysicalColumns.Where(column=>GlobalPhysicalRelationValue.IsScalar(storage,target,column))
            .Select(column=>column.ColumnName).ToArray();
        var additional=AdditionalProjectConditions(page.WhereSql,physical);
        var condition=FixedConditionPlan.Compile(additional,physical);
        return condition.Variables.Count>0&&condition.Variables.All(variable=>variable.StartsWith("formParameters.",StringComparison.Ordinal))
            ?additional:null;
    }

    /// <summary>仅供固定读取编译，名称和候选范围各走自己的静态用途；旧目标提示仅在没有明确绑定时使用。</summary>
    public JsonElement ReadDefinition(ColumnMetadata source)
    {
        var table=Table;
        // 明确表或页面声明优先，旧字面值不得在数据层重新参与目标匹配。
        if(table.Length==0&&TableId.Length==0&&PageId.Length==0&&source.LinkConfigTableId.Length==0&&string.IsNullOrWhiteSpace(source.Extra.GetValueOrDefault("joinTable").ToString())&&string.IsNullOrWhiteSpace(source.Extra.GetValueOrDefault("linkConfigPageId").ToString()))table=LegacyTargetHint;
        return JsonSerializer.SerializeToElement(new{tableId=TableId,table,pageId=PageId,nameField=NameField,whereSql=WhereSql,orderBy=OrderBy});
    }
}
