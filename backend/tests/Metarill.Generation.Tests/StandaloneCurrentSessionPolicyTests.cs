using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际编译嵌入模板，仅执行政策函数；真实DatabaseSession使用禁止SQL的本机ADO替身。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class StandaloneCurrentSessionPolicyTests(CurrentSessionPolicyTemplateFixture template):IClassFixture<CurrentSessionPolicyTemplateFixture>
{
    /// <summary>必要Store缺失不构造政策；模板编译闭包只有Core/Data/Security，没有Runtime或OAuth。</summary>
    [Fact]
    public void Required_store_and_template_assembly_have_no_platform_or_agent_fallback()
    {
        Assert.Throws<ArgumentNullException>(()=>template.Create(null));
        Assert.DoesNotContain(template.References,name=>name is not null&&name.StartsWith("Metarill.",StringComparison.Ordinal)&&name is not("Metarill.Core" or "Metarill.Data" or "Metarill.Security"));
    }

    /// <summary>有实际活动事务也不支持Agent；任意grant值都不能产生虚假的锁后证明。</summary>
    [Theory]
    [InlineData(0L)][InlineData(1L)][InlineData(long.MaxValue)]
    public async Task Actual_transaction_always_rejects_agent_without_sql_or_oauth(long grant)
    {
        using var f=new SessionPolicyContext();var policy=template.Create(f.Store);
        await f.Session.InTransactionAsync(async()=>
        {
            var transaction=f.Session.CurrentTransaction!;Assert.NotNull(transaction.Connection);
            var error=await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>policy.ReadAgentGrantForUpdateAsync(f.Session,transaction,grant,CancellationToken.None));
            Assert.Equal("独立应用未启用Agent会话。",error.Message);return true;
        });Assert.Equal(1,f.Provider.Opens);Assert.Equal(0,f.Provider.SqlCalls);
    }

    /// <summary>null、错误Session和不属于当前调用链的事务都先按上下文错误拒绝。</summary>
    [Theory]
    [InlineData("null-session")][InlineData("null-transaction")][InlineData("other-session")][InlineData("old-transaction")]
    public async Task Invalid_contexts_reject_before_agent_decision_or_any_sql(string mode)
    {
        using var f=new SessionPolicyContext();using var other=new SessionPolicyContext();var policy=template.Create(f.Store);DbTransaction? previous=null;
        if(mode=="old-transaction")await f.Session.InTransactionAsync(()=>{previous=f.Session.CurrentTransaction;return Task.FromResult(true);});
        await f.Session.InTransactionAsync(async()=>
        {
            var transaction=mode=="null-transaction"?null:mode=="old-transaction"?previous:f.Session.CurrentTransaction;
            var session=mode=="null-session"?null:mode=="other-session"?other.Session:f.Session;
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>policy.ReadAgentGrantForUpdateAsync(session!,transaction!,1,CancellationToken.None));
            Assert.Equal("独立应用当前会话适配必须使用同一Session和实际事务。",error.Message);return true;
        });Assert.Equal(0,f.Provider.SqlCalls);Assert.Equal(0,other.Provider.SqlCalls);Assert.Equal(0,other.Provider.Opens);
    }

    /// <summary>只有对象不够；没有当前事务时不能凭另行构造的事务对象进入Agent决策。</summary>
    [Fact]
    public async Task No_current_transaction_is_rejected_without_opening_any_connection()
    {
        using var f=new SessionPolicyContext();var policy=template.Create(f.Store);await using var connection=f.Provider.CreateConnection(f.Session.Options);await using var transaction=connection.BeginTransaction();
        Assert.Null(f.Session.CurrentTransaction);Assert.NotNull(transaction.Connection);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>policy.ReadAgentGrantForUpdateAsync(f.Session,transaction,1,CancellationToken.None));Assert.Equal(0,f.Provider.Opens);Assert.Equal(0,f.Provider.SqlCalls);
    }

    /// <summary>ADO负例返回缺连接的当前事务：两个引用都相等，仍必须因连接缺失拒绝。</summary>
    [Fact]
    public async Task Current_transaction_without_connection_is_rejected_without_sql()
    {
        using var f=new SessionPolicyContext(disconnected:true);var policy=template.Create(f.Store);
        await f.Session.InTransactionAsync(async()=>
        {
            var transaction=f.Session.CurrentTransaction!;Assert.Null(transaction.Connection);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>policy.ReadAgentGrantForUpdateAsync(f.Session,transaction,1,CancellationToken.None));return true;
        });Assert.Equal(0,f.Provider.SqlCalls);
    }

    /// <summary>有效上下文的取消仍按取消返回，不能产生grant证明或执行额外查询。</summary>
    [Fact]
    public async Task Cancelled_valid_transaction_never_returns_a_proof_or_runs_sql()
    {
        using var f=new SessionPolicyContext();var policy=template.Create(f.Store);
        await f.Session.InTransactionAsync(async()=>
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>policy.ReadAgentGrantForUpdateAsync(f.Session,f.Session.CurrentTransaction!,1,new CancellationToken(true)));return true;
        });Assert.Equal(0,f.Provider.SqlCalls);
    }
}

/// <summary>独立classlibrary编译实际嵌入模板，不改完整包/Program或伪称模板已下发。</summary>
public sealed class CurrentSessionPolicyTemplateFixture:IAsyncLifetime
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"metarill-session-policy-template",Guid.NewGuid().ToString("N"));private Type type=null!;
    public IReadOnlyList<string?> References{get;private set;}=[];
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);var source=SourceRoot();File.Copy(Path.Combine(source,"global.json"),Path.Combine(root,"global.json"));
        var assembly=typeof(CSharpGenerator).Assembly;var resource=assembly.GetManifestResourceNames().Single(name=>name.EndsWith(".SessionPolicy.cs.tpl",StringComparison.Ordinal));using var reader=new StreamReader(assembly.GetManifestResourceStream(resource)!);
        File.WriteAllText(Path.Combine(root,"SessionPolicy.cs"),(await reader.ReadToEndAsync()).Replace("{{NAMESPACE}}","Example.CurrentPolicy"));
        var assemblyName="Metarill.SessionPolicy.Probe."+Guid.NewGuid().ToString("N");
        new XDocument(new XElement("Project",new XAttribute("Sdk","Microsoft.NET.Sdk"),new XElement("PropertyGroup",new XElement("TargetFramework","net10.0"),new XElement("Nullable","enable"),new XElement("ImplicitUsings","enable"),new XElement("AssemblyName",assemblyName)),new XElement("ItemGroup",new XElement("ProjectReference",new XAttribute("Include",Path.Combine(source,"backend/src/Metarill.Security/Metarill.Security.csproj")))))).Save(Path.Combine(root,"Probe.csproj"));
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var value in new[]{"build","Probe.csproj","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})start.ArgumentList.Add(value);
        using(await SharedSourceBuildGate.Shared.EnterAsync()){using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));try{await process.WaitForExitAsync(deadline.Token);}catch(Exception interrupted){await SharedSourceBuildGate.StopOwnProcessAsync(process,output,error,interrupted);throw;}Assert.True(process.ExitCode==0,await output+await error);}
        var compiled=AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(root,"bin/Debug/net10.0",assemblyName+".dll"))));References=compiled.GetReferencedAssemblies().Select(value=>value.Name).ToArray();type=compiled.GetType("Example.CurrentPolicy.Host.ApplicationSecurityCurrentSessionPolicy",true)!;
    }
    /// <summary>直接执行实际模板构造器，去除反射包装后保留真实拒绝类型。</summary>
    internal ISecurityCurrentSessionPolicy Create(SqlRowStore? store)
    {
        try{return(ISecurityCurrentSessionPolicy)Activator.CreateInstance(type,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,[store],CultureInfo.InvariantCulture)!;}
        catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    public Task DisposeAsync()=>TestDirectoryCleanup.DeleteAsync(root,Path.Combine(Path.GetTempPath(),"metarill-session-policy-template"));
}
