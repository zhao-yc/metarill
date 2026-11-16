using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Data.Tests;

public sealed partial class FixedConditionSqlTests
{
    /// <summary>固定常量入口必须保留SQL三值及原LIKE语义，不能把NULL等值改成IS NULL。</summary>
    [Fact]
    public void Constant_only_entry_preserves_sql_null_and_raw_like()
    {
        using var session=Session();var sql=Compile("name=NULL AND id IN(NULL,9007199254740993) AND name LIKE 'A_%'").ApplyConstants(session.Select<Row>(),session,"page").ToSql();
        Assert.Contains("=NULL",sql);Assert.DoesNotContain("IS NULL",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("IN (NULL,@page_0)",sql);Assert.DoesNotContain("9007199254740993",sql);Assert.DoesNotContain("A_%",sql);
    }
    /// <summary>账号及四种业务归属参数都来自已认证快照，高位ID精度和文本类型保持。</summary>
    [Fact]
    public void Account_conditions_bind_all_original_contexts_and_reject_missing_affiliations()
    {
        var types=new Dictionary<string,string>{{"id","int64"},{"merchant_id","string"},{"dealer_id","int64"},{"main_dealer_id","int64"},{"store_id","int64"}};
        var plan=FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(
            "id=@current_yz_manager_id AND merchant_id=@current_merchant_id AND dealer_id=@current_dealer_id AND main_dealer_id=@current_main_dealer_id AND store_id=@current_store_id",types.Keys),types);
        var actor=new Metarill.Core.Security.RuntimeActor(9007199254740993,"账号",true,"100","5"){MerchantId=9007199254740994,DealerId=8,MainDealerId=9,StoreId=10};
        using var session=Session();using var command=new MySqlConnector.MySqlCommand();
        var sql=plan.BindAccount(actor).BindConstants(command,session,"account");
        Assert.DoesNotContain("9007199254740993",sql);
        Assert.Equal(new object[]{9007199254740993L,"9007199254740994",8L,9L,10L},command.Parameters.Cast<System.Data.Common.DbParameter>().Select(item=>item.Value));
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));
        Assert.DoesNotContain("9007199254740994",System.Text.Json.JsonSerializer.Serialize(actor));
    }

    /// <summary>含变量的计划不能借无变量页面入口吞掉条件，失败前原查询保持不变。</summary>
    [Fact]
    public void Constant_only_entry_rejects_variables_before_mutating_query()
    {
        using var session=Session();var query=session.Select<Row>();var before=query.ToSql();Assert.Throws<InvalidOperationException>(()=>Compile("id=@actor").ApplyConstants(query,session,"page"));Assert.Equal(before,query.ToSql());
    }
    /// <summary>自建命令仅接规范谓词及驱动参数，字符串与高位ID不能进入SQL正文。</summary>
    [Fact]
    public void Constant_command_binding_preserves_values_and_sql_null()
    {
        using var session=Session();using var command=new MySqlConnector.MySqlCommand();
        var sql=Compile("name='A AND B,O''Neil' AND id IN(NULL,9007199254740993)").BindConstants(command,session,"file_page");
        Assert.Contains("NULL",sql);Assert.DoesNotContain("O'Neil",sql);Assert.DoesNotContain("9007199254740993",sql);
        Assert.Equal(2,command.Parameters.Count);Assert.Contains(command.Parameters.Cast<System.Data.Common.DbParameter>(),p=>Equals(p.Value,"A AND B,O'Neil"));Assert.Contains(command.Parameters.Cast<System.Data.Common.DbParameter>(),p=>Equals(p.Value,9007199254740993L));
    }
    /// <summary>变量或参数重名在修改命令前拒绝，不能吞掉条件或覆盖调用方身份参数。</summary>
    [Theory]
    [InlineData(true)][InlineData(false)]
    public void Constant_command_binding_rejects_variables_or_existing_parameter_without_mutation(bool variable)
    {
        using var session=Session();using var command=new MySqlConnector.MySqlCommand("SELECT 1");command.Parameters.AddWithValue("@PAGE_0",17L);
        if(variable)Assert.Throws<InvalidOperationException>(()=>Compile("id=@actor").BindConstants(command,session,"page"));
        else Assert.Throws<ArgumentException>(()=>Compile("id=9007199254740993").BindConstants(command,session,"page"));
        Assert.Equal("SELECT 1",command.CommandText);Assert.Equal(17L,Assert.Single(command.Parameters.Cast<System.Data.Common.DbParameter>()).Value);
    }
}
