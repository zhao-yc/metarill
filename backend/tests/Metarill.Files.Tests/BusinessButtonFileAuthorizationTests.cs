using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Files;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>执行文件观察器实际授权入口，按钮来源不能借普通更新、导入、复制或删除旁路。</summary>
public sealed class BusinessButtonFileAuthorizationTests
{
    /// <summary>只有实际按钮被检查；普通动作和按钮来源不相互转成权限。</summary>
    [Theory][InlineData(null)][InlineData(RecordMutationOperation.Update)]
    public void Actual_button_updates_use_only_its_permission(RecordMutationOperation? operation)
    {
        var auth=new Authorization();var context=Context() with{BusinessButtonId="86001",AuthorizationOperation=operation};
        FileReferenceMutationObserver.RequireMutationPermission(context,auth);Assert.Equal("101:86001",auth.Button);Assert.Null(auth.Page);
        FileReferenceMutationObserver.RequireMutationPermission(Context(),auth);Assert.Equal("101:update",auth.Page);
    }
    /// <summary>来源形状必须是对既有对象的更新；无效形状在调用任何授权之前拒绝。</summary>
    [Theory][InlineData("blank")][InlineData("space")][InlineData("record")][InlineData("create")][InlineData("delete")]
    [InlineData("import")][InlineData("copy")][InlineData("unknown")]
    public void Invalid_button_states_never_reach_authorizer(string defect)
    {
        var context=Context() with{BusinessButtonId="86001"};context=defect switch{
            "blank"=>context with{BusinessButtonId=""},"space"=>context with{BusinessButtonId=" "},"record"=>context with{RecordId=0},
            "create"=>context with{IsCreate=true},"delete"=>context with{IsDelete=true},"import"=>context with{AuthorizationOperation=RecordMutationOperation.Import},
            "copy"=>context with{AuthorizationOperation=RecordMutationOperation.Copy},_=>context with{AuthorizationOperation=(RecordMutationOperation)999}};
        var auth=new Authorization();Assert.Throws<UnauthorizedAccessException>(()=>FileReferenceMutationObserver.RequireMutationPermission(context,auth));Assert.Null(auth.Button);Assert.Null(auth.Page);
    }
    /// <summary>旧宿主缺少按钮接口时拒绝，来源不序列化也不改变原构造合同。</summary>
    [Fact]
    public void Legacy_host_and_serialization_keep_existing_boundary()
    {
        var context=Context() with{BusinessButtonId="86001"};Assert.Throws<NotSupportedException>(()=>FileReferenceMutationObserver.RequireMutationPermission(context,new Legacy()));
        Assert.DoesNotContain("BusinessButtonId",JsonSerializer.Serialize(context with{Transaction=null!}));
        Assert.Contains(typeof(RecordMutationContext).GetConstructors(),ctor=>ctor.GetParameters().Length==8);
    }
    /// <summary>只测试授权分支，真实文件行和业务事务另由实库用例核验。</summary>
    private static RecordMutationContext Context()=>new("101","fixture",1,"54",new Dictionary<string,object?>(),false,null!);
    private sealed class Authorization:IRuntimeAuthorization
    {
        internal string? Button,Page;
        /// <summary>测试只提供已认证身份，不伪造实际权限聚合。</summary>
        public RuntimeActor RequireActor()=>new(8,"文件入口",false,"54","5");
        /// <summary>记录真实分支调用。</summary>
        public void RequirePagePermission(string pageId,string operation)=>Page=pageId+":"+operation;
        /// <summary>记录真实按钮分支调用。</summary>
        public void RequireButtonPermission(string pageId,string buttonId)=>Button=pageId+":"+buttonId;
    }
    private sealed class Legacy:IRuntimeAuthorization
    {
        /// <summary>旧身份不代表新增按钮权限。</summary>
        public RuntimeActor RequireActor()=>new(8,"旧宿主",true,"54","5");
        /// <summary>即使旧页权开放也不能代替按钮。</summary>
        public void RequirePagePermission(string pageId,string operation){}
    }
}
