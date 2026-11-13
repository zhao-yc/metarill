using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定目录声明不能因虚拟列先过滤而绕过完整应用门禁，也不能借用其它页的覆盖配置。</summary>
public sealed class FixedCatalogOverlayAuditTests
{
    private const string Declaration="{\"catalog\":\"metarill-csharp-templates/v1\",\"nameField\":\"name\"}";

    /// <summary>使用实际Full导出证明在输出前拒绝；不是只检查审计字符串或人为破坏清单。</summary>
    [Theory]
    [InlineData("",Declaration)]
    [InlineData("","{\"catalog\":\"unknown\",\"nameField\":\"name\"}")]
    [InlineData("","{\"catalog\":")]
    [InlineData("","{\"catalog\":\"metarill-csharp-templates/v1\",\"nameField\":\"name\",\"catalog\":\"unknown\"}")]
    [InlineData(Declaration,"{\"catalog\":\"unknown\",\"nameField\":\"name\"}")]
    [InlineData(Declaration,"")]
    public async Task Virtual_source_or_overlay_catalog_is_rejected_before_full_output(string sourceDetail,string overlay)
    {
        var catalog=TemplateVersionCatalogTests.Catalog();var column=VersionColumn(catalog);var field=VersionField(catalog);
        column.IsVirtualColumn=1;column.ControlDetail=sourceDetail;field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(overlay);
        var model=GenerationModel.Create(catalog,"Example.CatalogGap");var page=model.Tables.Single().Pages.Single();
        Assert.DoesNotContain(model.Tables.Single().Columns,item=>item.Source.Id==column.Id);
        Assert.DoesNotContain(page.Readable.Concat(page.Writable),item=>item.Source.Id==column.Id);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-fixed-overlay");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var error=Record.Exception(()=>new ApplicationPackager().Create(catalog,directory,"Example.CatalogGap",SourceRoot()));
            // 重复JSON键已由发布器提前拒绝；其它虚拟声明必须由目录缺口门禁明确拒绝。
            if(error is ArgumentException)
            {
                Assert.NotEqual(overlay.IndexOf("\"catalog\"",StringComparison.Ordinal),overlay.LastIndexOf("\"catalog\"",StringComparison.Ordinal));
                Assert.Contains("catalog",error.Message);
            }
            else
            {
                var rejected=Assert.IsType<InvalidOperationException>(error);
                Assert.Contains("固定版本目录",rejected.Message);Assert.Contains(column.ColumnName,rejected.Message);
            }
            Assert.False(Directory.Exists(directory));
            var gap=Assert.Single(GenerationCapabilityAudit.Collect(model));Assert.Equal(page.Source.Id,gap.PageId);Assert.Equal(column.ColumnName,gap.Column);Assert.Equal("detail",gap.Usage);
            Assert.Contains("非联接物理版本字段",gap.Reason);
            Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>声明只归属实际raw页字段；同表未配置该字段的页面不能继承另一页声明。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Raw_catalog_audit_uses_the_exact_page_column(bool sourceDeclaration)
    {
        var catalog=TemplateVersionCatalogTests.Catalog();var column=VersionColumn(catalog);var field=VersionField(catalog);
        column.IsVirtualColumn=1;column.ControlDetail=sourceDeclaration?Declaration:"";
        field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(sourceDeclaration?"":Declaration);
        var other=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single()))!;other.Id="999";other.Name="无版本字段页";catalog.ConfigPages.Add(other);
        var model=GenerationModel.Create(catalog,"Example.PageBound");var gap=Assert.Single(GenerationCapabilityAudit.Collect(model));
        Assert.Equal(field.ConfigPageId,gap.PageId);Assert.DoesNotContain(GenerationCapabilityAudit.Collect(model),item=>item.PageId==other.Id);
        catalog.ConfigPageColumns.Remove(field);Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.NoUsage")));
    }

    /// <summary>相邻物理字段仍采用原严格目录合同；页覆盖不能替代缺失源声明或偷偷改成未知目录。</summary>
    [Theory]
    [InlineData(Declaration,Declaration,true)]
    [InlineData("",Declaration,false)]
    [InlineData(Declaration,"{\"catalog\":\"unknown\",\"nameField\":\"name\"}",false)]
    public void Physical_catalog_keeps_the_existing_strict_resolution(string sourceDetail,string overlay,bool supported)
    {
        var catalog=TemplateVersionCatalogTests.Catalog();VersionColumn(catalog).ControlDetail=sourceDetail;VersionField(catalog).Extra["controlDetail"]=JsonSerializer.SerializeToElement(overlay);
        var model=GenerationModel.Create(catalog,"Example.Physical");var table=model.Tables.Single();var page=table.Pages.Single();
        var result=FixedOptionCatalogCompilation.Resolve(model,page,table.Columns.Single(column=>column.Source.Id==TemplateVersionCatalog.VersionColumnId));
        Assert.True(result.Declared);Assert.Equal(supported,result.Definition is not null);
        if(supported){Assert.Empty(GenerationCapabilityAudit.Collect(model));new FrontendPackager().Validate(catalog);}
        else{Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Column=="yz_yzp_version_id");Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));}
    }

    /// <summary>夹具来自当前平台的固定版本列，不另造目标表或放宽列ID证明。</summary>
    private static ColumnMetadata VersionColumn(MetadataCatalog catalog)=>catalog.ConfigColumns.Single(column=>column.Id==TemplateVersionCatalog.VersionColumnId);
    private static PageColumnMetadata VersionField(MetadataCatalog catalog)=>catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId==TemplateVersionCatalog.VersionColumnId);
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
