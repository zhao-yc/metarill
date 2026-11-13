using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Metarill.Runtime;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真正生成并编译非冗余double页面；离线读取/筛选证据不能记为实库CRUD。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantDoubleGenerationTests
{
    /// <summary>列表/详情/type2使用声明的double，不把大测量值转decimal或开放隐藏范围列。</summary>
    [Fact]
    public async Task Actual_generated_double_projection_filters_and_partial_protection_execute()
    {
        var catalog=Catalog();
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonredundant-double");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var generated=Path.Combine(directory,"generated");
        try
        {
            var generator=new CSharpGenerator();generator.Generate(catalog,generated,"Example.NonRedDouble");var model=GenerationModel.Create(catalog,"Example.NonRedDouble");var table=model.Tables.Single(value=>value.Source.Id=="1");
            var hand=Path.Combine(generated,"Services/MtNrdDoubleOrderService.cs");await File.AppendAllTextAsync(hand,"\n// 本轮double手写保护。\n");var bytes=await File.ReadAllBytesAsync(hand);generator.Generate(catalog,generated,model.Namespace);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
            await File.WriteAllTextAsync(Path.Combine(directory,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(model.Namespace+".Generated.Services.MtNrdDoubleOrderService",true)!;
            foreach(var page in table.Pages)
            {
                var plan=NonRedundantReadCompilation.ForPage(model,table,page);var type=assembly.GetType(model.Namespace+".Generated.VOs."+page.Name+"Vo",true)!;
                Assert.Equal(typeof(double?),type.GetProperty("Measurement")!.PropertyType);Assert.Equal(typeof(double?),type.GetProperty("CustomerAmount")!.PropertyType);Assert.Null(type.GetProperty("YzProjectId"));Assert.Null(type.GetProperty("GroupId"));
                var row=plan.Outputs.ToDictionary(field=>field.Storage,field=>field.Storage switch{"id"=>(object?)9007199254740993L,"customer_id"=>9007199254740995L,"status"=>1,"measurement"=>1.25e100,"customer_amount"=>-3.5e-200,"name"=>"订单","customer_name"=>"客户","customer_notes"=>"派生备注","private_payload"=>"原可读字段",_=>throw new InvalidOperationException("新增输出须明确验证："+field.Storage)},StringComparer.Ordinal);
                var project=type.GetMethod("FromReadRow",BindingFlags.Public|BindingFlags.Static)!;var vo=project.Invoke(null,[row]);var json=JsonSerializer.SerializeToElement(vo,type,JsonDefaults.Options);
                Assert.Equal(1.25e100,json.GetProperty("measurement").GetDouble());Assert.Equal(-3.5e-200,json.GetProperty("customerAmount").GetDouble());Assert.Equal("9007199254740993",json.GetProperty("id").GetString());
                row["measurement"]=null;Assert.Null(type.GetProperty("Measurement")!.GetValue(project.Invoke(null,[row])));row["measurement"]=double.NaN;Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>project.Invoke(null,[row])).InnerException);
                var filters=service.GetMethod("ReadFilters"+page.Source.Id,BindingFlags.NonPublic|BindingFlags.Static)!;var actor=new RuntimeActor(1,"double读取验收",false,"54","5");var request=new PageRequest{ParentId="71",Filters=new(){["measurement"]=JsonSerializer.Deserialize<JsonElement>("\"1.25e100\""),["customerAmount"]=JsonSerializer.Deserialize<JsonElement>("[-3.5e-200,0]")}};
                var actual=Assert.IsType<List<FixedReadFilter>>(filters.Invoke(null,[request,actor]));Assert.Equal(1.25e100,Assert.IsType<double>(Assert.Single(actual,value=>value.Field=="measurement").Value));var selected=Assert.IsAssignableFrom<IReadOnlyList<object?>>(Assert.Single(actual,value=>value.Field=="customer_amount").Value);Assert.Equal(-3.5e-200,Assert.IsType<double>(selected[0]));Assert.Equal(54L,Assert.Single(actual,value=>value.Field=="yz_project_id").Value);Assert.Equal(71L,Assert.Single(actual,value=>value.Field=="group_id").Value);
                request.Filters=new(){["measurement"]=JsonSerializer.Deserialize<JsonElement>("\"1e-999\"")};Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(()=>filters.Invoke(null,[request,actor])).InnerException);
            }
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>以同一double元数据导出真正Full包并严格构建，不以BackendOnly或引用原仓库替代。</summary>
    [Fact]
    public async Task Full_application_contains_current_double_reader_and_builds_independently()
    {
        var catalog=Catalog();foreach(var row in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))row.YzProjectId=catalog.YzProjectId;
        foreach(var page in catalog.ConfigPages.Where(page=>page.Id!="102"))catalog.ConfigMenus.Add(new(){Id="900"+page.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="double页面"+page.Id,Kind="GENERATED_PAGE",ConfigPageId=page.Id});
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonredundant-double-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var result=new ApplicationPackager().Create(catalog,output,"Example.NonRedDoublePackage",source);Assert.Equal(ApplicationPackageMode.FullApplication,result.Mode);Assert.True(result.FrontendIncluded);
            // 打包器对C#源已有明确LF规则；只规范CRLF/CR，任何其它内容差异仍拒绝。
            foreach(var file in new[]{"Queries/NonRedundantReads.cs","Queries/MySqlNonRedundantReader.cs"})
            {
                var expected=(await File.ReadAllTextAsync(Path.Combine(source,"backend/src/Metarill.Data",file))).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
                Assert.Equal(expected,await File.ReadAllTextAsync(Path.Combine(output,"src/Metarill.Data",file)));
            }
            var page=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/mt_nrd_double_order-list-101.vue"));Assert.Contains("measurement",page);Assert.Contains("customerAmount",page);
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--disable-build-servers","--verbosity","quiet"})build.ArgumentList.Add(argument);
            var evidence=Path.Combine(source,".local/nonredundant-double-package-builds",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);build.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;await NonRedundantApplicationFixture.RunAsync(build);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>同一源配置驱动实际生成与完整包，double统计维持既有独立边界。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrd_double");
        foreach(var field in catalog.ConfigColumns.Where(column=>column.Id is "17" or "26"))field.LogicalType="double";
        catalog.ConfigColumns.Add(new(){Id="900",ConfigTableId="1",ColumnName="measurement",Name="测量值",LogicalType="double"});
        catalog.ConfigPageColumns.Add(new(){Id="101900",ConfigPageId="101",ConfigColumnId="900",IsShow=1,IsEdit=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1),["isSort"]=JsonSerializer.SerializeToElement(1)}});
        var derived=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="17");derived.Extra["sumKind"]=JsonSerializer.SerializeToElement(0);derived.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        NonRedundantGeneratedFixture.AddSelectionPage(catalog);
        return catalog;
    }

}
