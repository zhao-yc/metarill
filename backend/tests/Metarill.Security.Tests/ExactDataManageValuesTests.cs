using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Security.Tests;

/// <summary>窄匹配快照、逻辑类型和固定动作边界；不执行SQL，不冒充记录过滤验收。</summary>
public sealed class ExactDataManageValuesTests
{
    /// <summary>长ID不经double；数组null不匹配空列，重复值不扩大集合，nullable实体类型保持。</summary>
    [Fact]
    public void Long_precision_scalar_and_json_array_are_preserved()
    {
        Assert.Equal(new long[]{9007199254740993},Required("9007199254740993").Read<long>());
        Assert.Equal(new long?[]{9007199254740993,7},Required("[\"9007199254740993\",null,7,7]").Read<long?>());
        Assert.Equal(new long[]{9007199254740993},Required(9007199254740993L).Read<long>());
        Assert.Equal(new[]{"原值"},Required("原值").Read<string>());
    }
    /// <summary>原空值、空数组和仅空白值均产生空集合，不变成未限制。</summary>
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("  ")][InlineData("[]")][InlineData("[null]")]
    public void Missing_values_are_required_but_match_nothing(string? value)
    {
        var scope=Required(value);Assert.True(scope.IsRequired);Assert.Empty(scope.Read<long>());
    }
    /// <summary>损坏JSON、对象、嵌套数组、溢出或浮点ID明确拒绝，错误不回显账号原值。</summary>
    [Theory]
    [InlineData("[private-value")][InlineData("[{}]")][InlineData("[[1]]")]
    [InlineData("9223372036854775808")][InlineData("1.5")]
    public void Invalid_identity_values_never_become_unrestricted(string value)
    {
        var error=Assert.Throws<ArgumentException>(()=>Required(value).Read<long>());Assert.DoesNotContain(value,error.Message);
    }
    /// <summary>逻辑类型为整数、小数、布尔、日期和文本分别严格转换，文本不会被当SQL。</summary>
    [Fact]
    public void Scalar_logical_types_are_explicit_and_nonfinite_numbers_are_rejected()
    {
        Assert.Equal(new[]{-7,12},Required("[-7,\"12\"]").Read<int>());
        Assert.Equal(new[]{12.50m},Required("12.50").Read<decimal>());
        Assert.Equal(new[]{true,false},Required("[1,0,true,false]").Read<bool>());
        Assert.Equal(new DateTime(2026,10,21,1,2,3,DateTimeKind.Utc),Assert.Single(Required("2026-10-21T01:02:03Z").Read<DateTime>()));
        Assert.Equal(new object?[]{"x' OR 1=1 --"},Required("x' OR 1=1 --").ReadLogical("varchar"));
        Assert.Throws<ArgumentException>(()=>Required("Infinity").Read<double>());
        Assert.Throws<ArgumentException>(()=>Required("2147483648").Read<int>());
        Assert.Throws<NotSupportedException>(()=>Required(1).ReadLogical("json"));
    }
    /// <summary>仅规范字段名的大小写与下划线，不解释SQL、括号或属性路径。</summary>
    [Theory]
    [InlineData("real_name","real_name")][InlineData("realName","real_name")]
    [InlineData("RealName","real_name")][InlineData("dealerID","dealer_id")][InlineData(" ","")]
    public void Configured_field_aliases_are_canonical(string input,string expected)=>Assert.Equal(expected,ExactDataManageValues.Field(input));
    /// <summary>字段字符串不能携带SQL或多段对象路径。</summary>
    [Theory]
    [InlineData("name OR 1=1")][InlineData("x.y")][InlineData("`name`")][InlineData("name()")]
    public void Configured_field_expressions_are_rejected(string value)=>Assert.Throws<ArgumentException>(()=>ExactDataManageValues.Field(value));
    /// <summary>只捕获已认证后台选择的一项值，复制后不依赖账号字典，密码字段不进入快照。</summary>
    [Fact]
    public void Capture_is_narrow_detached_and_hides_values_from_json()
    {
        var account=new Dictionary<string,object?>{{"real_name","[\"甲\",\"乙\"]"},{"password","不得快照"}};
        var match=ExactDataManageIdentity.Capture(new Dictionary<string,object?>{{"exact_data_manage_match_field","realName"}},account);
        account["real_name"]="后来修改";Assert.Equal("real_name",match.Field);Assert.Equal(new[]{"甲","乙"},new ExactDataManageValues(true,match.Value).Read<string>());
        var secret=ExactDataManageIdentity.Capture(new Dictionary<string,object?>{{"exact_data_manage_match_field","password"}},account);Assert.Null(secret.Value);Assert.Empty(secret.Field);
        Assert.Equal("{}",JsonSerializer.Serialize(new ExactDataManageValues(true,match.Value)));
        var identity=Identity() with{ExactDataManageMatchField=match.Field,ExactDataManageMatchValue=match.Value};
        var json=JsonSerializer.Serialize(identity,new JsonSerializerOptions(JsonSerializerDefaults.Web));Assert.DoesNotContain("exactDataManage",json);Assert.DoesNotContain("甲",json);
    }
    /// <summary>查询、导出与修改分别消费各自真实动作决策，不能用查询的普通授权取消导出范围。</summary>
    [Fact]
    public void Each_operation_resolves_its_own_decision_and_checks_the_claim_binding()
    {
        var identity=Identity() with{ExactDataManagePermissions=new HashSet<string>{"100:is_export","100:is_update"},ExactDataManageMatchField="real_name",ExactDataManageMatchValue=JsonSerializer.SerializeToElement("甲")};
        Assert.False(ExactDataManageIdentity.Resolve(identity,"100","detail","realName").IsRequired);
        Assert.Equal(new[]{"甲"},ExactDataManageIdentity.Resolve(identity,"100","export","realName").Read<string>());
        Assert.True(ExactDataManageIdentity.Resolve(identity,"100","update","realName").IsRequired);
        Assert.Empty(ExactDataManageIdentity.Resolve(identity,"100","export","").Read<string>());
        Assert.Throws<NotSupportedException>(()=>ExactDataManageIdentity.Resolve(identity,"100","export","mobile"));
        Assert.Throws<NotSupportedException>(()=>ExactDataManageIdentity.Resolve(identity with{PrincipalProjectId="2"},"100","export","realName"));
        Assert.Throws<UnauthorizedAccessException>(()=>ExactDataManageIdentity.Resolve(identity,"100","arbitrary","realName"));
    }
    /// <summary>旧授权器可以继续处理未配置范围的页面，不能因缺省方法替配置范围声明无限制。</summary>
    [Fact]
    public void Legacy_authorization_does_not_claim_scope_support()
    {
        IRuntimeAuthorization authorization=new LegacyAuthorization();Assert.Throws<NotSupportedException>(()=>authorization.ResolveExactDataManage("100","detail","realName"));
    }
    /// <summary>发布计划优先于数据库中的旧/空声明，新应用无需另写配置，账号数据仍只取固定一项。</summary>
    [Fact]
    public void Published_manager_field_selects_the_claim_without_database_configuration_step()
    {
        var captured=ExactDataManageIdentity.Capture(new Dictionary<string,object?>{{"exact_data_manage_match_field","password"}},new Dictionary<string,object?>{{"real_name","范围甲"},{"password","不应捕获"}},"realName");
        Assert.Equal("real_name",captured.Field);Assert.Equal(new[]{"范围甲"},new ExactDataManageValues(true,captured.Value).Read<string>());
        var disabled=ExactDataManageIdentity.Capture(new Dictionary<string,object?>{{"exact_data_manage_match_field","real_name"}},new Dictionary<string,object?>{{"real_name","范围甲"}},"");Assert.Empty(disabled.Field);Assert.Null(disabled.Value);
    }
    /// <summary>原4参数身份构造保持；测试明确建立同一真实项目和后台上下文。</summary>
    private static AuthenticatedIdentity Identity()=>new(new RuntimeActor(1,"账号",false,"1","10"),new HashSet<string>(),2,null){PrincipalProjectId="1",PrincipalAdminTypeId="10"};
    /// <summary>模拟来自真实账号的已脱离数据读取器值，不接收HTTP输入。</summary>
    private static ExactDataManageValues Required(object? value)=>new(true,JsonSerializer.SerializeToElement(value));
    /// <summary>仅实现原接口的旧宿主，用于检查默认范围方法明确失败。</summary>
    private sealed class LegacyAuthorization:IRuntimeAuthorization
    {
        /// <summary>仅原接口身份，不提供任何新范围能力。</summary>
        public RuntimeActor RequireActor()=>new(1,"账号",false,"1","10");
        /// <summary>本例只检查新增默认方法，不模拟实际页面策略。</summary>
        public void RequirePagePermission(string pageId,string operation){}
    }
}
