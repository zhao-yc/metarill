using System.Reflection;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Compatibility;
using Xunit;
namespace Metarill.Metadata.Tests;

/// <summary>执行实际工作台审计方法；不把内存行为验证当成SQL事务验收。</summary>
public sealed class PlatformAuditCalendarTests
{
    /// <summary>实际公共写入辅助方法覆盖伪造审计、保留普通字段并使用独立宿主时钟对照。</summary>
    [Theory][InlineData(typeof(MetadataWorkbenchBusiness))][InlineData(typeof(IndexWorkbenchBusiness))]
    public void Workbench_update_audits_use_host_calendar(Type type)
    {
        var actor=new RuntimeActor(8,"审计账号",true,"7","9");var row=new Dictionary<string,object?>{{"id",9007199254740993L},{"name","保持"},{"update_time",new DateTime(2000,1,1)},{"update_manager_id",999L}};var before=DateTime.Now;
        var result=type.GetMethod("Audit",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[row,actor]);Assert.Same(row,result);Calendar(row["update_time"],before);Assert.Equal(8L,row["update_manager_id"]);Assert.Equal(9007199254740993L,row["id"]);Assert.Equal("保持",row["name"]);
    }
    /// <summary>原配置复制等入口的新增公共值保持ID和审计身份，日历无额外时区Kind。</summary>
    [Fact]
    public void Original_configuration_new_values_use_calendar_and_current_actor()
    {
        var actor=new RuntimeActor(8,"审计账号",true,"7","9");var before=DateTime.Now;
        var values=(Dictionary<string,object?>)typeof(ConfigurationActionsBusiness).GetMethod("NewValues",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[9007199254740993L,actor])!;
        Calendar(values["insert_time"],before);Calendar(values["update_time"],before);Assert.Equal(8L,values["insert_manager_id"]);Assert.Equal(7L,values["yz_project_id"]);Assert.Equal(9007199254740993L,values["id"]);Assert.Null(values["delete_no"]);
    }
    /// <summary>预期直接取系统日历，避免产品时钟校验自身。</summary>
    private static void Calendar(object? value,DateTime before){var actual=Assert.IsType<DateTime>(value);Assert.Equal(DateTimeKind.Unspecified,actual.Kind);Assert.InRange(actual,before,DateTime.Now);}
}
