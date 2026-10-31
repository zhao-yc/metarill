using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Runtime.Metadata;

public sealed record BatchColumnError(string Field,string Code,string Message);
public sealed class BatchColumnPreviewRow
{
    public int RowNumber {get;init;}
    public string? SourceLine {get;init;}
    public string ConfigTableId {get;init;}="";
    public string Name {get;set;}="";
    public string ColumnName {get;set;}="";
    public string ColumnType {get;set;}="";
    public string ColumnLength {get;set;}="";
    public string ControlKind {get;set;}="";
    public string ControlDetail {get;set;}="";
    public List<BatchColumnError> Errors {get;}=[];
    internal string JoinTable {get;set;}="";
    internal string LogicalType {get;set;}="string";
    internal long? LinkTableId {get;set;}
    internal int Precision {get;set;}=18;
    internal int Scale {get;set;}=2;
}
public sealed record BatchColumnPreview(bool Valid,int ErrorCount,IReadOnlyList<BatchColumnPreviewRow> Rows);
public sealed class BatchColumnValidationException(BatchColumnPreview preview):InvalidOperationException("字段配置未保存，请修正预检中的问题")
{public BatchColumnPreview Preview {get;}=preview;}

/// <summary>移植原两列文本与JSON白名单协议，只解析配置，不求值或执行SQL。</summary>
internal static class BatchColumnParser
{
    private static readonly HashSet<string> JsonFields=["name","columnName","columnType","columnLength","controlKind","controlDetail","joinTable"];
    /// <summary>保留原始行号；500行和1MB限制与原工作台一致。</summary>
    internal static List<BatchColumnPreviewRow> Parse(long tableId,string? content)
    {
        var rows=new List<BatchColumnPreviewRow>();
        BatchColumnPreviewRow Row(int number,string? source)=>new(){RowNumber=number,SourceLine=source,ConfigTableId=tableId.ToString(CultureInfo.InvariantCulture)};
        if(string.IsNullOrWhiteSpace(content)){var row=Row(1,content);Error(row,"content","REQUIRED","字段配置列表不能为空");rows.Add(row);return rows;}
        if(content.Length>1024*1024){var row=Row(1,"");Error(row,"content","TOO_LARGE","一次最多提交1MB字段配置文本");rows.Add(row);return rows;}
        if(content.TrimStart().StartsWith('['))
        {
            JsonDocument document;
            try{document=JsonDocument.Parse(content);}catch(JsonException){var row=Row(1,content);Error(row,"content","INVALID_JSON","字段配置不是合法JSON数组");rows.Add(row);return rows;}
            using(document)
            {
                var array=document.RootElement;
                if(array.GetArrayLength() is 0 or >500){var row=Row(1,"");Error(row,"content","INVALID_COUNT","一次需创建1至500个字段");rows.Add(row);return rows;}
                var index=0;
                foreach(var item in array.EnumerateArray())
                {
                    var row=Row(++index,item.GetRawText());rows.Add(row);
                    if(item.ValueKind!=JsonValueKind.Object){Error(row,"content","INVALID_FORMAT","每个数组条目必须是字段配置对象");continue;}
                    var seen=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var property in item.EnumerateObject())
                    {
                        if(!seen.Add(property.Name)||!JsonFields.Contains(property.Name)){Error(row,property.Name,"UNSUPPORTED_FIELD","不支持的批量配置参数："+property.Name);continue;}
                        if(property.Value.ValueKind!=JsonValueKind.String&&!(property.Name=="controlDetail"&&property.Value.ValueKind==JsonValueKind.Object))Error(row,property.Name,"INVALID_FORMAT","配置参数须为文本；控件明细也可使用JSON对象");
                    }
                    string Text(string key)=>item.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()!:"";
                    row.Name=Text("name");row.ColumnName=Text("columnName");row.ColumnType=Text("columnType");row.ColumnLength=Text("columnLength");row.ControlKind=Text("controlKind");row.JoinTable=Text("joinTable");
                    row.ControlDetail=item.TryGetProperty("controlDetail",out var detail)&&detail.ValueKind==JsonValueKind.Object?detail.GetRawText():Text("controlDetail");
                    Validate(row);
                }
            }
        }
        else
        {
            var lines=Regex.Split(content.Replace('\u00a0',' '),@"\r\n|\n|\r|\u0085|\u2028|\u2029");
            for(var index=0;index<lines.Length;index++)
            {
                var line=lines[index];if(string.IsNullOrWhiteSpace(line))continue;
                var row=Row(index+1,line);rows.Add(row);
                if(rows.Count>500){Error(row,"content","TOO_MANY_ROWS","一次最多创建500个字段");break;}
                var values=Regex.Split(line.Trim(),@"\s+");
                if(values.Length!=2){Error(row,"content","INVALID_FORMAT","每行必须填写字段名称和数据库字段名，以空格或制表符分隔");continue;}
                row.Name=values[0];row.ColumnName=values[1];ApplyTextDefaults(row);Validate(row);
            }
        }
        foreach(var group in rows.Where(row=>row.ColumnName.Length>0).GroupBy(row=>row.ColumnName,StringComparer.OrdinalIgnoreCase).Where(group=>group.Count()>1))
            foreach(var row in group)Error(row,"columnName","DUPLICATE_IN_BATCH","数据库字段名在本批次重复");
        return rows;
    }
    /// <summary>命名约定沿用布尔、日期、排序、计数和关联字段；关联ID统一为64位整数。</summary>
    private static void ApplyTextDefaults(BatchColumnPreviewRow row)
    {
        row.ColumnType="nvarchar";row.ColumnLength="50";
        if(row.ColumnName.StartsWith("is_",StringComparison.Ordinal)){row.ColumnType="int";row.ColumnLength="";row.ControlKind="CheckBox";row.ControlDetail="1,0";}
        if(row.ColumnName.EndsWith("_id",StringComparison.Ordinal)){row.ColumnType="bigint";row.ColumnLength="";row.ControlKind="List";row.JoinTable=row.ColumnName[..^3];}
        else if(row.ColumnName.EndsWith("_date",StringComparison.Ordinal)){row.ColumnType="datetime";row.ColumnLength="";row.ControlKind="date";}
        else if(row.ColumnName.EndsWith("_time",StringComparison.Ordinal)){row.ColumnType="datetime";row.ColumnLength="";row.ControlKind="date_time";}
        else if(row.ColumnName=="sort_no"||row.ColumnName.EndsWith("count",StringComparison.Ordinal)||row.ColumnName.EndsWith("status",StringComparison.Ordinal)){row.ColumnType="int";row.ColumnLength="";}
    }
    /// <summary>只接受当前C#生成器支持的类型，长度错误在预览中逐行返回。</summary>
    private static void Validate(BatchColumnPreviewRow row)
    {
        if(string.IsNullOrWhiteSpace(row.Name)||row.Name.Length>50)Error(row,"name","INVALID_NAME","字段名称不能为空且不能超过50字符");
        if(!Regex.IsMatch(row.ColumnName,"^[A-Za-z_][A-Za-z0-9_]{0,63}$"))Error(row,"columnName","INVALID_IDENTIFIER","数据库字段名须为不超过64字符的字母、数字、下划线标识符");
        row.LogicalType=row.ColumnType switch{"nvarchar" or "varchar" or "text" or "longtext"=>"string","int"=>"int32","bigint"=>"int64","decimal"=>"decimal","datetime"=>"datetime",_=>""};
        if(row.LogicalType.Length==0)Error(row,"columnType","INVALID_TYPE","当前C#生成器不支持此数据类型");
        if(row.ColumnType=="decimal")
        {
            var match=Regex.Match(row.ColumnLength,@"^([1-9][0-9]?)(?:,([0-9]{1,2}))?$");
            if(!match.Success)Error(row,"columnLength","INVALID_LENGTH","小数长度须为精度或精度,小数位");
            else{row.Precision=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);row.Scale=match.Groups[2].Success?int.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture):0;if(row.Precision>65||row.Scale>30||row.Scale>row.Precision)Error(row,"columnLength","INVALID_LENGTH","小数精度为1至65，小数位为0至30且不大于精度");}
        }
        else if(row.ColumnType is "nvarchar" or "varchar"&&row.ColumnLength.Length>0&&!row.ColumnLength.Equals("max",StringComparison.OrdinalIgnoreCase)&&(!int.TryParse(row.ColumnLength,NumberStyles.None,CultureInfo.InvariantCulture,out var size)||size is <1 or >65535))Error(row,"columnLength","INVALID_LENGTH","字符长度须为1至65535的整数或max");
        if(row.JoinTable.Length>0&&!Regex.IsMatch(row.JoinTable,"^[A-Za-z_][A-Za-z0-9_]{0,63}$"))Error(row,"joinTable","INVALID_IDENTIFIER","关联表名称无效");
        if(row.ControlKind=="List")row.LogicalType="int64";
    }
    /// <summary>稳定错误码用于原工作台行内定位，重复问题只展示一次。</summary>
    internal static void Error(BatchColumnPreviewRow row,string field,string code,string message)
    {if(!row.Errors.Any(error=>error.Field==field&&error.Message==message))row.Errors.Add(new(field,code,message));}
    /// <summary>全批次任一错误都会阻断保存，不跳过坏行。</summary>
    internal static BatchColumnPreview Preview(IReadOnlyList<BatchColumnPreviewRow> rows)=>new(rows.Count>0&&rows.All(row=>row.Errors.Count==0),rows.Sum(row=>row.Errors.Count),rows);
}
