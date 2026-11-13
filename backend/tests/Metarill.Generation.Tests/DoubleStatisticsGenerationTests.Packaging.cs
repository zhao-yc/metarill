using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class DoubleStatisticsGenerationTests
{
    /// <summary>普通与派生统计从正式完整包自身源码独立构建，不能仅引用仓库的Core/Data通过。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Double_statistics_full_packages_include_the_current_runtime_and_build_independently(bool nonredundant)
    {
        var catalog=nonredundant?NonRedundantGeneratedFixture.Catalog("mt_double_statistics_package"):OriginalStatisticsFixture.Catalog();
        foreach(var column in catalog.ConfigColumns.Where(column=>nonredundant?column.Id is "17" or "26":column.ColumnName=="amount"))column.LogicalType="double";
        foreach(var row in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))row.YzProjectId=catalog.YzProjectId;
        foreach(var page in catalog.ConfigPages.Where(page=>page.Id!="102"))catalog.ConfigMenus.Add(new(){Id="910"+page.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId=page.YzAdminTypeId,Name="统计页面"+page.Id,Kind="GENERATED_PAGE",ConfigPageId=page.Id});
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-double-statistics-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var root=NonRedundantApplicationFixture.Root();
        try
        {
            var package=new ApplicationPackager().Create(catalog,output,"Example.DoubleStatisticsPackage",root);Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);Assert.True(package.FrontendIncluded);
            foreach(var file in new[]{"Metarill.Core/Contracts/LegacyStatisticsMath.cs","Metarill.Data/Queries/MySqlNonRedundantReader.cs","Metarill.Exports/FloatingWorkbookWriter.cs","Metarill.Exports/ExcelPageExportService.cs"})
            {
                var expected=(await File.ReadAllTextAsync(Path.Combine(root,"backend/src",file))).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');Assert.Equal(expected,await File.ReadAllTextAsync(Path.Combine(output,"src",file)));
            }
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--disable-build-servers","--verbosity","quiet"})build.ArgumentList.Add(argument);
            var evidence=Path.Combine(root,".local/double-statistics-package-builds",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);build.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
            await NonRedundantApplicationFixture.RunAsync(build);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
