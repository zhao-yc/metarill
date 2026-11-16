using System.Globalization;

namespace Metarill.Core.Security;

/// <summary>原页面及候选条件可使用的账号身份值。只从认证快照读取，不接受客户端同名变量。</summary>
public static class AccountConditionVariables
{
    public static bool IsSupported(string variable) => variable.ToLowerInvariant() is
        "current_yz_manager_id" or "current_merchant_id" or "current_dealer_id" or "current_main_dealer_id" or "current_store_id";

    /// <summary>按原 WhereSql 约束限定商户和门店字段；账号、经销商及上级经销商可声明业务关联列。</summary>
    public static bool IsPageField(string variable, string field) => variable.ToLowerInvariant() switch
    {
        "current_merchant_id" => field == "merchant_id",
        "current_store_id" => field is "store_id" or "id",
        _ => IsSupported(variable)
    };

    /// <summary>返回精确十进制ID，由目标列类型决定文本或数字参数；缺失归属不能借管理员身份绕过。</summary>
    public static string Require(RuntimeActor actor, string variable)
    {
        if(actor.AccountId<=0)throw new UnauthorizedAccessException("登录身份无效。");
        long? value = variable.ToLowerInvariant() switch
        {
            "current_yz_manager_id" => actor.AccountId,
            "current_merchant_id" => actor.MerchantId,
            "current_dealer_id" => actor.DealerId,
            "current_main_dealer_id" => actor.MainDealerId,
            "current_store_id" => actor.StoreId,
            _ => throw new ArgumentException("账号条件变量未发布。", nameof(variable))
        };
        if (value is null or <= 0) throw new UnauthorizedAccessException("当前账号缺少页面条件所需的归属。");
        return value.Value.ToString(CultureInfo.InvariantCulture);
    }
}
