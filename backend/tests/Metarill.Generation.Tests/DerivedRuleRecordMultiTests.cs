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
    [Theory]
    [InlineData("plain", false)]
    [InlineData("multi", false)]
    [InlineData("multi", true)]
    [InlineData("checkbox", false)]
    [InlineData("checkbox", true)]
    public async Task Actual_multi_name_rule_batches_share_snapshot_and_capture_json_array_values(string scenario, bool button)
    {
        var catalog=NonRedundantMultiSelectionTests.Catalog();
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");
        page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("targetNames");
        page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);
        const long storeId=9007199254741993;
        if(scenario!="plain")
        {
            var target=catalog.ConfigTables.Single(table=>table.Id=="3");
            target.IsRongyu=scenario=="multi"?0:1;
            var defaultPage=catalog.ConfigPages.Single(candidate=>candidate.Id=="301");
            var targetPage=new Metarill.Core.Metadata.PageMetadata
            {
                Id="302",ConfigTableId="3",YzProjectId=catalog.YzProjectId,YzAdminTypeId=defaultPage.YzAdminTypeId,
                WhereSql=defaultPage.WhereSql+" AND store_id=@current_store_id AND status=41"
            };
            targetPage.Extra["type"]=JsonSerializer.SerializeToElement(2);
            catalog.ConfigPages.Add(targetPage);
            catalog.ConfigPageColumns.Add(new(){Id="30201",ConfigPageId="302",ConfigColumnId="32"});
            var relationField=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25");
            relationField.Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"pageId\":\"302\",\"nameField\":\"name\"}");
            catalog.ConfigColumns.AddRange([
                new(){Id="38",ConfigTableId="3",ColumnName="store_id",LogicalType="int64"},
                new(){Id="39",ConfigTableId="3",ColumnName="status",LogicalType="int32"}
            ]);
            if(scenario=="checkbox")
            {
                var relation=catalog.ConfigColumns.Single(column=>column.Id=="25");
                relation.ControlKind="MultiCheckBoxDatabase";
                relation.ControlDetail="{\"tableId\":\"3\",\"pageId\":\"301\",\"nameField\":\"name\"}";
            }
        }
        if(button)
        {
            // 原中央入口的无父页面经本行已声明外键取得字典父级，避免离线传输替代 FreeSql 父页验证。
            page.WhereSql="yz_project_id=@current_yz_project_id";
            catalog.ConfigPageColumns.Add(new(){Id="20126",ConfigPageId="201",ConfigColumnId="26",IsShow=1,IsEdit=1});
            catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="86000",configPageId=page.Id,
                yzProjectId=catalog.YzProjectId,actionType=1,name="按当前记录执行",sql="/admin/custom/derived-rule/probe",visibleScheme="VisibleWhenWritable"}));
        }
        var model=GenerationModel.Create(catalog,"Example.DerivedMultiRule");var table=model.Tables.Single(table=>table.Source.Id=="2");
        if(scenario!="plain")
        {
            // 普通页面已能限定同一来源及目标页，规则读取必须携带完全相同的固定绑定。
            var generatedPage=table.Pages.Single(candidate=>candidate.Source.Id==page.Id);
            var scope=Assert.Single(NonRedundantReadCompilation.ProjectMultiTargets(model,table,generatedPage));
            Assert.Equal("302",scope.TargetPageId);
            Assert.True(scope.ApplyPageScope);
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-rule");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var serviceType=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;
            var plan=(NonRedundantReadPlan)serviceType.GetField("RuleRecordPlan201",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;var transport=new Transport(plan.OutputTypes){ProjectId=catalog.YzProjectId};
            transport.OnRootRead=()=>transport.TargetNames=["稍后甲","稍后乙"];
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"},null,transport);
            var actor=new RuntimeActor(8,"多值规则",false,catalog.YzProjectId,page.YzAdminTypeId){StoreId=scenario=="plain"?null:storeId};
            var authorization=new Authorization(actor,"201");
            var mapper=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Mappers."+table.Name+"Mapper",true)!,session)!;
            var service=Activator.CreateInstance(serviceType,[session,mapper,new SnowflakeIdGenerator(948),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),authorization])!;
            async Task<IGeneratedRuleRecord> Read()
            {
                var task=button
                    ?(Task)serviceType.GetMethod("ReadButtonRecord201Async")!.Invoke(service,[Transport.Id,"71","86000",actor,CancellationToken.None])!
                    :(Task)serviceType.GetMethod("ReadAgentRecord201Async")!.Invoke(service,[Transport.Id,"71","update",actor,authorization,CancellationToken.None])!;
                await task;
                return Assert.IsAssignableFrom<IGeneratedRuleRecord>(task.GetType().GetProperty("Result")!.GetValue(task));
            }
            var record=await Read();Assert.True(record.TryGetLoadedRuleValue("target_names",out var value));var names=Assert.IsType<JsonElement>(value);Assert.Equal(new[]{"快照甲","快照乙"},names.EnumerateArray().Select(item=>item.GetString()));
            Assert.Equal(2,transport.Commands.Count);Assert.Same(transport.Commands[0].Transaction,transport.Commands[1].Transaction);Assert.All(transport.Commands,command=>{Assert.Equal(System.Data.IsolationLevel.RepeatableRead,command.Isolation);Assert.DoesNotContain("COUNT(",command.Sql);Assert.DoesNotContain("FOR SHARE",command.Sql);});
            Assert.Contains("metarill_multi_input_",transport.Commands[0].Sql);Assert.StartsWith("SELECT multi_value.",transport.Commands[1].Sql);Assert.Equal(1,transport.Commits);Assert.Null(session.CurrentTransaction);
            var labels=transport.Commands[1];
            Assert.Contains("multi_value.`parent_id`",labels.Sql);
            Assert.Contains(labels.Values,value=>Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)=="71");
            Assert.Contains("multi_value.`delete_no`",labels.Sql);
            if(scenario!="plain")
            {
                Assert.Contains("multi_value.`store_id`",labels.Sql);
                Assert.Contains("multi_value.`status`",labels.Sql);
                Assert.Contains(labels.Values,value=>value is long key&&key==storeId);
                Assert.Contains(labels.Values,value=>value is int status&&status==41||value is long number&&number==41);
                Assert.DoesNotContain(storeId.ToString(),labels.Sql);
            }
            transport.TargetIds="[]";transport.Commands.Clear();var empty=await Read();Assert.True(empty.TryGetLoadedRuleValue("target_names",out var emptyValue));Assert.Equal(0,Assert.IsType<JsonElement>(emptyValue).GetArrayLength());Assert.Single(transport.Commands);
            Assert.True(Metarill.Core.Metadata.ReadOnlyConditionRule.Compile("targetNames").IsReadOnly(new Dictionary<string,object?>{{"targetNames",emptyValue}}));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
