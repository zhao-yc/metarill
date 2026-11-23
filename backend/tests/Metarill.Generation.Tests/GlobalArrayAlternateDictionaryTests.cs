using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>原多选与数据库复选的实时名称沿实际候选页绑定字典。</summary>
public sealed class GlobalArrayAlternateDictionaryTests
{
    [Theory]
    [InlineData(0,"int64")]
    [InlineData(1,"int64")]
    [InlineData(0,"string")]
    [InlineData(1,"string")]
    public async Task Array_labels_accept_the_named_dictionary_page(int mode,string keyType)
    {
        var catalog=GlobalMultipleDerivedTargetGenerationTests.Catalog(mode,false,keyType);
        var source=catalog.ConfigTables.Single(table=>table.Id=="3");
        var target=catalog.ConfigTables.Single(table=>table.Id=="1");
        catalog.ConfigPages.Single(page=>page.Id=="20").WhereSql="status=17";
        catalog.ConfigPages.Add(new(){Id="21",ConfigTableId="2",YzProjectId="100",YzAdminTypeId="9",WhereSql="status=3"});
        catalog.ConfigPageColumns.Add(new(){Id="2122",ConfigPageId="21",ConfigColumnId="22",YzProjectId="100",IsShow=1,IsEdit=1});
        catalog.ConfigPageColumns.Single(column=>column.Id=="1013").Extra["controlDetail"]=
            JsonSerializer.SerializeToElement("""{"pageId":"21"}""");
        catalog.ConfigPages.Add(new(){Id="11",ConfigTableId="1",YzProjectId="100",YzAdminTypeId="9",WhereSql="status=1"});
        catalog.ConfigPageColumns.Add(new(){Id="1113",ConfigPageId="11",ConfigColumnId="13",YzProjectId="100",IsShow=1,IsEdit=1});

        foreach(var foreign in catalog.ConfigColumns.Where(column=>column.Id is "34" or "38"))
        {
            var proof=mode==1
                ?GlobalMultipleRelationTarget.RequireCached(catalog,source,foreign,target)
                :GlobalMultipleRelationTarget.Require(catalog,source,foreign,target);
            Assert.Equal("10",proof.Page.Id);
            Assert.Equal("global_target_name",proof.Name.ColumnName);
        }
        const string rootNamespace="Example.GlobalArrayDictionary";
        var model=GenerationModel.Create(catalog,rootNamespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-array-dictionary");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),rootNamespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(rootNamespace+".Generated.Services.CachedConsumerService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Import30Async"));
            Assert.NotNull(service.GetMethod("Export30Async"));
            const System.Reflection.BindingFlags instance=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            const System.Reflection.BindingFlags plans=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            var fields=new[]{"global_source_global_target_names","selected_global_source_global_target_names"};
            if(mode==0)
            {
                AssertScope(ReadPlan("ReadPlan30"),fields);
                AssertScope(ReadPlan("ExportReadPlan30"),fields);
            }
            else
            {
                var maintenance=assembly.GetType(rootNamespace+".Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null)!;
                var tables=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans",instance)!.GetValue(maintenance));
                var table=tables[source.Id]!;
                var read=table.GetType().GetProperty("Read")!.GetValue(table)!;
                AssertScope(read,fields);
                var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",instance)!.GetValue(read));
                foreach(var field in fields)
                    Assert.Contains(reverse.Cast<object>(),item=>
                        Equals("global_target",item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                        Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(
                            item.GetType().GetProperty("Outputs")!.GetValue(item)).TryGetValue(field,out var columns)&&columns.Contains("status"));
            }

            object ReadPlan(string name)
            {
                var plan=service.GetField(name,plans)!.GetValue(null)!;
                return plan.GetType().GetProperty("MySql",instance)!.GetValue(plan)!;
            }

            void AssertScope(object read,IReadOnlyList<string> names)
            {
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    read.GetType().GetProperty("Constants",instance)!.GetValue(read));
                Assert.Contains(3,constants.Values);
                Assert.DoesNotContain(17,constants.Values);
                foreach(var name in names)
                {
                    var sql=Assert.IsType<string>(read.GetType().GetMethod("Expression",instance)!.Invoke(read,[name]));
                    Assert.Contains(constants,item=>Equals(3,item.Value)&&sql.Contains(item.Key,StringComparison.Ordinal));
                }
            }
        }
        finally
        {
            if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }
}
