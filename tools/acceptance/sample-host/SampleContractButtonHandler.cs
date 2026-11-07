using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.GlobalSample.Business;

/// <summary>原页面三个按钮的手写适配；只把详情原值转成既有摘要，不用服务端最新值自动通过冲突检查。</summary>
public sealed class SampleContractButtonHandler(string buttonId,SampleContractAgentBusiness business,MetadataCatalog metadata):IOriginalBusinessButtonHandler
{
    private readonly int amountScale=metadata.ConfigColumns.Single(column=>column.ConfigTableId==metadata.ConfigTables.Single(table=>table.TableName=="sample_contract").Id&&column.ColumnName=="amount").Scale;
    public string ButtonId {get;}=buttonId;
    public string Path=>"/admin/custom/sample-contract-agent/"+(ButtonId switch{"86000"=>"preview","86001"=>"complete","86002"=>"rollback",_=>throw new NotSupportedException("样板按钮未登记。")});
    /// <summary>仍调用同一个手写业务及生成Service，原R数据、409冲突、404缺失和对象规则保持统一。</summary>
    public async Task<IActionResult> ExecuteAsync(OriginalBusinessButtonRequest request,CancellationToken cancellationToken)
    {
        var id=long.Parse(request.EntityId!,CultureInfo.InvariantCulture);
        try
        {
            if(ButtonId=="86000")return new ObjectResult(R<SampleContractPreview>.Success(await business.PreviewAsync(id,cancellationToken)));
            var result=await business.CompleteAsync(id,ExpectedDigest(id,request.Entity),ButtonId=="86002",cancellationToken);
            return new ObjectResult(R<SampleContractCompletion>.Success(result));
        }
        catch(SampleContractConflictException error){return new ObjectResult(R<object>.Error(error.Message,409)){StatusCode=409};}
        catch(SampleContractMissingException error){return new ObjectResult(R<object>.Error(error.Message,404)){StatusCode=404};}
    }
    /// <summary>显式摘要可沿用；普通详情正文按原摘要字段精确计算，列表缺原值则要求打开详情。</summary>
    private string ExpectedDigest(long id,JsonElement? value)
    {
        if(value is not{} entity||entity.ValueKind!=JsonValueKind.Object)throw MissingOriginal();
        if(entity.EnumerateObject().GroupBy(item=>item.Name,StringComparer.OrdinalIgnoreCase).Any(group=>group.Count()!=1))throw MissingOriginal();
        if(entity.TryGetProperty("expectedDigest",out var explicitDigest))return explicitDigest.ValueKind==JsonValueKind.String?explicitDigest.GetString()!:throw MissingOriginal();
        if(!entity.TryGetProperty("id",out _)||!entity.TryGetProperty("name",out var name)||!entity.TryGetProperty("amount",out var amount)||!entity.TryGetProperty("remarks",out var remarks))throw MissingOriginal();
        decimal? number=null;
        if(amount.ValueKind!=JsonValueKind.Null)
        {
            var text=amount.ValueKind==JsonValueKind.String?amount.GetString():amount.ValueKind==JsonValueKind.Number?amount.GetRawText():null;
            if(text is null||amountScale is<0 or>28||!ExactNumericValue.TryDecimal(text,out var parsed))throw MissingOriginal();
            // JSON浏览器往返会丢掉尾零；只按发布字段恢复小数位，超出字段精度的原值不能四舍五入后冒充有效摘要。
            if(!ExactNumericValue.TryDecimal(parsed.ToString("F"+amountScale,CultureInfo.InvariantCulture),out var fixedScale)||parsed!=fixedScale)throw MissingOriginal();
            number=fixedScale;
        }
        return SampleContractDigest.Calculate(id,Text(name),number,Text(remarks));
        // 原值仅用于乐观并发摘要，不传入Mapper或作为项目/角色/数据范围。
        static string? Text(JsonElement field)=>field.ValueKind==JsonValueKind.Null?null:field.ValueKind==JsonValueKind.String?field.GetString():throw MissingOriginal();
    }
    /// <summary>缺详情原值不能补读新记录并自行构造有效摘要。</summary>
    private static SampleContractConflictException MissingOriginal()=>new("缺少完整详情原值，请重新打开详情后执行。");
}
