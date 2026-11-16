using System.Globalization;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Generation;

internal static partial class BackendRenderer
{
    /// <summary>原页面新建初始值由服务端在提交值后填写，再进入默认审计、父归属、钩子和关系校验。</summary>
    private static string PageInitialization(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var column in table.Columns)
            if(PageInitialValue(model,table,page,column) is {} value)
                result.AppendLine($"        entity.{column.Property} = {value}; submitted.Add({Literal(column.Source.ColumnName)});");
        return result.ToString();
    }

    /// <summary>按原页面声明解析新建初始值，普通字段默认值仍按原有省略语义处理。</summary>
    internal static string? PageInitialValue(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        var field=model.Catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==column.Source.Id);
        if(field is null||!field.Extra.TryGetValue("initValue",out var definition)||definition.ValueKind==JsonValueKind.Null)return null;
        var raw=definition.ValueKind switch
        {
            JsonValueKind.String=>definition.GetString()!,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False=>definition.GetRawText(),
            _=>throw new InvalidOperationException("页面初始值必须为标量。")
        };
        if(string.IsNullOrWhiteSpace(raw))return null;
        // 项目和父键由Audit/真实父范围负责，页面初始化不能覆盖这两个来源。
        if(column.Source.ColumnName=="yz_project_id"&&raw=="current_yz_project_id")return null;
        if(page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==column.Source.Id))
            throw new InvalidOperationException("页面父键不能另配新建初始值："+column.Source.ColumnName);
        if(column.IsSystem||column.IsJsonArray||column.IsManagedCascadeName||column.Source.IsVirtualColumn!=0||
            GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")||!NonRedundantReadCompilation.Physical(model,table,column)||
            ManagerCredentialCompilation.IsCredential(table,column)||ExactDataManageValues.IsSensitiveField(column.Source.ColumnName)||
            NonRedundantCrossParentCompilation.IsSlot(model,table,column))
            throw new InvalidOperationException("页面初始值需要独立的非凭据物理字段："+column.Source.ColumnName);

        if(raw=="current_yz_project_id")return ScopeValue(column,"actor.ProjectId");
        if(AccountConditionVariables.IsSupported(raw))
            return ScopeValue(column,"global::Metarill.Core.Security.AccountConditionVariables.Require(actor, "+Literal(raw)+")");
        if(raw=="today")
            return column.Type=="DateTime?"?"BusinessCalendar.Now.Date":throw new InvalidOperationException("today初始值需要日期或时间字段。");

        // 数字、布尔和日期先在生成期校验，坏配置不延迟到用户保存时才报错。
        return column.Type switch
        {
            "bool?" when raw is "0" or "1"=>raw=="1"?"true":"false",
            "bool?" when bool.TryParse(raw,out var flag)=>flag?"true":"false",
            "int?" when int.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out var integer)=>integer.ToString(CultureInfo.InvariantCulture),
            "long?" when long.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out var longInteger)=>longInteger.ToString(CultureInfo.InvariantCulture)+"L",
            "DateTime?" when DateTime.TryParseExact(raw,["yyyy-MM-dd","yyyy-MM-dd HH:mm:ss.FFFFFFF","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)
                =>$"new DateTime({date.Ticks}L, DateTimeKind.Unspecified)",
            "decimal?" when Metarill.Core.Contracts.ExactNumericValue.TryDecimal(raw,out var number)
                =>number.ToString(CultureInfo.InvariantCulture)+"m",
            "string?" or "double?" or "TimeSpan?"=>DefaultExpression(column,raw),
            _=>throw new InvalidOperationException("页面初始值与物理字段类型不一致："+column.Source.ColumnName)
        };
    }
}
