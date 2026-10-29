using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class DerivedRuleRecordGenerationTests
{
    /// <summary>实际固定读取器先取ID再分批补名，命令使用同一RR事务；传输快照不是MySQL隔离实测。</summary>
    [Fact]
    public async Task Actual_multi_name_rule_batches_share_snapshot_and_capture_json_array_values()
    {
        var catalog=NonRedundantMultiSelectionTests.Catalog();var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("targetNames");page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,"Example.DerivedMultiRule");var table=model.Tables.Single(table=>table.Source.Id=="2");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-rule");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var serviceType=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;
            var plan=(NonRedundantReadPlan)serviceType.GetField("RuleRecordPlan201",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;var transport=new Transport(plan.OutputTypes){ProjectId=catalog.YzProjectId};
            transport.OnRootRead=()=>transport.TargetNames=["稍后甲","稍后乙"];
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"},null,transport);
            var actor=new RuntimeActor(8,"多值规则",false,catalog.YzProjectId,page.YzAdminTypeId);var authorization=new Authorization(actor,"201");var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,session)!;
            var service=Activator.CreateInstance(serviceType,[session,mapper,new SnowflakeIdGenerator(948),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            async Task<IGeneratedRuleRecord> Read()
            {var task=(Task)serviceType.GetMethod("ReadAgentRecord201Async")!.Invoke(service,[Transport.Id,"71","update",actor,authorization,CancellationToken.None])!;await task;return Assert.IsAssignableFrom<IGeneratedRuleRecord>(task.GetType().GetProperty("Result")!.GetValue(task));}
            var record=await Read();Assert.True(record.TryGetLoadedRuleValue("target_names",out var value));var names=Assert.IsType<JsonElement>(value);Assert.Equal(new[]{"快照甲","快照乙"},names.EnumerateArray().Select(item=>item.GetString()));
            Assert.Equal(2,transport.Commands.Count);Assert.Same(transport.Commands[0].Transaction,transport.Commands[1].Transaction);Assert.All(transport.Commands,command=>{Assert.Equal(System.Data.IsolationLevel.RepeatableRead,command.Isolation);Assert.DoesNotContain("COUNT(",command.Sql);Assert.DoesNotContain("FOR SHARE",command.Sql);});
            Assert.Contains("metarill_multi_input_",transport.Commands[0].Sql);Assert.StartsWith("SELECT multi_value.",transport.Commands[1].Sql);Assert.Equal(1,transport.Commits);Assert.Null(session.CurrentTransaction);
            transport.TargetIds="[]";transport.Commands.Clear();var empty=await Read();Assert.True(empty.TryGetLoadedRuleValue("target_names",out var emptyValue));Assert.Equal(0,Assert.IsType<JsonElement>(emptyValue).GetArrayLength());Assert.Single(transport.Commands);
            Assert.True(Metarill.Core.Metadata.ReadOnlyConditionRule.Compile("targetNames").IsReadOnly(new Dictionary<string,object?>{{"targetNames",emptyValue}}));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
