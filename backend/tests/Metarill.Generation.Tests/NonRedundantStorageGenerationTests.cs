using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>读取尚未实现时必须在写文件前拒绝，不以物理列或空回显伪装非冗余已支持。</summary>
public sealed class NonRedundantStorageGenerationTests
{
    /// <summary>新输出不留stage；既有输出及手写文件均不因能力拒绝被改写。</summary>
    [Fact]
    public void Missing_nonredundant_read_plan_rejects_before_any_generation_write()
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-storage",Guid.NewGuid().ToString("N"));var catalog=Catalog();
        try
        {
            Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.Nonred"));Assert.False(Directory.Exists(root));
            Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"developer.cs"),"// 手写文件保持。\n");var before=File.ReadAllBytes(Path.Combine(root,"developer.cs"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.Nonred"));Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"developer.cs")));Assert.Single(Directory.GetFiles(root));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    /// <summary>完整包同样保留明确能力缺口，不因前端能显示只读项就绕过读取实现。</summary>
    [Fact]
    public void Full_frontend_reports_the_missing_derived_read_before_packaging()
    {
        var catalog=Catalog();var model=GenerationModel.Create(catalog,"Example.Nonred");Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Usage=="runtime"&&gap.Reason.Contains("非冗余派生字段",StringComparison.Ordinal));var error=Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));Assert.Contains("非冗余派生字段",error.Message);
    }
    /// <summary>未声明派生项的普通非冗余业务列依旧生成，不扩大到一律拒绝表模式。</summary>
    [Fact]
    public void Pure_physical_nonredundant_table_keeps_existing_generation()
    {
        var catalog=Catalog();catalog.ConfigColumns.RemoveAll(column=>column.Id=="13");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="13");var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-storage",Guid.NewGuid().ToString("N"));try{Assert.NotEmpty(new CSharpGenerator().Generate(catalog,root,"Example.Nonred").GeneratedFiles);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    /// <summary>未配置辅助列也可能需要自动关系名称读取，不能将它误当作已支持的纯物理CRUD。</summary>
    [Fact]
    public void Nonredundant_relation_without_explicit_display_column_still_requires_read_plan()
    {
        var catalog=Catalog();var column=catalog.ConfigColumns.Single(item=>item.Id=="13");column.Extra.Clear();column.ColumnName="customer_id";column.LogicalType="int64";column.ControlKind="List";var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-storage",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.Nonred"));Assert.False(Directory.Exists(root));
    }
    /// <summary>只保留已声明列及原页面，不构造可伪造的可信模块标记。</summary>
    private static MetadataCatalog Catalog()=>new()
    {
        YzProjectId="1",ConfigTables=[new(){Id="2",TableName="orders",YzProjectId="1",IsRongyu=0}],YzAdminTypes=[new(){Id="3",YzProjectId="1"}],ConfigPages=[new(){Id="4",ConfigTableId="2",YzAdminTypeId="3",YzProjectId="1"}],
        ConfigColumns=[new(){Id="11",ConfigTableId="2",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="12",ConfigTableId="2",ColumnName="name",LogicalType="string"},new(){Id="13",ConfigTableId="2",ColumnName="customer_notes",LogicalType="string",Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement("customer.notes")}}}],
        ConfigPageColumns=[new(){Id="21",ConfigPageId="4",ConfigColumnId="11"},new(){Id="22",ConfigPageId="4",ConfigColumnId="12"},new(){Id="23",ConfigPageId="4",ConfigColumnId="13"}]
    };
}
