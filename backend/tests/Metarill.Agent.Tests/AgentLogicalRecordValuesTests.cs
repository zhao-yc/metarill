using System.Text.Json.Serialization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FreeSql.DataAnnotations;
using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using Xunit;
namespace Metarill.Agent.Tests;

/// <summary>规则消费者必须区分原始物理实体和服务端装载的逻辑值；本类只验证消费合同，不冒充SQL或身份验收。</summary>
public sealed class AgentLogicalRecordValuesTests
{
    private static readonly IReadOnlyDictionary<string,string> Names=new Dictionary<string,string>{{"countryId","country_id"}};
    /// <summary>装载逻辑值优先于物理空位；已装载null也不能回落成实体值。</summary>
    [Theory][InlineData("9007199254740993")][InlineData(null)]
    public void Logical_proof_takes_precedence_over_physical_property(string? logical)
    {
        var record=new Record{Loaded=true,Logical=logical};var values=Read(record);Assert.Equal(logical,values["countryId"]);Assert.Equal(0,record.GetterCalls);
    }
    /// <summary>字段虽有物理列，缺少所需逻辑证明时仍须拒绝；不能借默认0把当前记录变为可写。</summary>
    [Fact]
    public void Missing_required_logical_proof_never_falls_back_to_physical_value()
    {
        var record=new Record();Assert.Throws<NotSupportedException>(()=>Read(record));Assert.Equal(0,record.GetterCalls);
    }
    /// <summary>没有逻辑装载要求的普通物理字段保持原属性读取合同。</summary>
    [Fact]
    public void Ordinary_physical_field_preserves_existing_getter_contract()
    {
        var record=new Record{Required=false};var values=Read(record);Assert.Equal("0",values["countryId"]);Assert.Equal(1,record.GetterCalls);
    }
    /// <summary>通过反射调用实际内部消费者，保持产品封装；解包原异常，不复制消费算法。</summary>
    private static IReadOnlyDictionary<string,object?> Read(Record record)
    {
        var method=typeof(AgentCapabilityCatalog).Assembly.GetType("Metarill.Runtime.Agent.AgentRecordValues",true)!.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!;
        try{return (IReadOnlyDictionary<string,object?>)method.Invoke(null,[record,typeof(Record),Names,new[]{"countryId"}])!;}
        catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private sealed class Record:IGeneratedRuleRecord
    {
        internal bool Required=true,Loaded;internal string? Logical;internal int GetterCalls;
        [Column(Name="country_id")][JsonPropertyName("countryId")]
        public string CountryId{get{GetterCalls++;return "0";}}
        public bool RequiresLoadedRuleValue(string physicalColumn)=>Required&&physicalColumn=="country_id";
        public bool TryGetLoadedRuleValue(string physicalColumn,out object? value){value=Logical;return Loaded&&physicalColumn=="country_id";}
    }
}
