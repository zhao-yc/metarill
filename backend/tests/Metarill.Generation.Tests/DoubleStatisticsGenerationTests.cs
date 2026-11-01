using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>普通与派生double统计必须保留浮点，不经decimal或改变原统计外形。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed partial class DoubleStatisticsGenerationTests
{
    /// <summary>实际生成入口接收已声明的double求和；隐藏或非数值字段仍沿原门槛。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Declared_double_sum_is_generated_for_physical_and_nonredundant_pages(bool nonredundant)
    {
        var catalog=nonredundant?NonRedundantGeneratedFixture.Catalog("mt_double_statistics"):OriginalStatisticsFixture.Catalog();
        foreach(var column in catalog.ConfigColumns.Where(column=>nonredundant?column.Id is "17" or "26":column.ColumnName=="amount"))column.LogicalType="double";
        var model=GenerationModel.Create(catalog,"Example.DoubleStatistics");
        var table=model.Tables.Single(table=>table.Pages.Any(page=>page.Source.Id==(nonredundant?"101":"301")));var page=table.Pages.Single(page=>page.Source.Id==(nonredundant?"101":"301"));
        Assert.Contains(OriginalListStatisticsCompilation.ForPage(model,table,page)!,item=>item.Kind==1&&item.Column.Type=="double?");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-double-statistics");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,model.Namespace);
            var source=File.ReadAllText(Path.Combine(output,"Services",table.Name+"Service.g.cs"));
            Assert.Contains("LegacyStatisticsMath.SumDouble",source);
            Assert.Contains(nonredundant?"DoubleSums":"FloatingStatistics.SumAsync",source);
            await File.WriteAllTextAsync(Path.Combine(output,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(output);
            if(!nonredundant)await ExerciseOrdinaryStatistics(assembly);
        }
        finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
}
