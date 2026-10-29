using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>真实平台自举元数据的结构闭环及来源反例；只编译内存结构，不连接数据库或执行DDL。</summary>
public sealed class PlatformPhysicalModelTests
{
    /// <summary>平台完整图与原自举物理图一致，全部原页/字段和输入保持；输出可改而不会污染下一次预览。</summary>
    [Fact]
    public void Actual_platform_keeps_its_physical_fingerprint_and_original_pages_without_mutating_inputs()
    {
        var f=new Models();var before=Text(f.Registry.Platform);var files=Text(f.Files);var security=Text(f.Security);
        var result=f.Service.Compose(f.Registry.Platform);
        Assert.Equal(Fingerprint(f.Registry.Platform),Fingerprint(result));
        Assert.Equal(Text(f.Registry.Platform.ConfigPages),Text(result.ConfigPages));Assert.Equal(Text(f.Registry.Platform.ConfigPageColumns),Text(result.ConfigPageColumns));
        Assert.Equal(before,Text(f.Registry.Platform));Assert.Equal(files,Text(f.Files));Assert.Equal(security,Text(f.Security));
        result.ConfigColumns[0].ColumnName="changed";result.ConfigPages.Clear();
        Assert.Equal(Fingerprint(f.Registry.Platform),Fingerprint(f.Service.Compose(f.Registry.Platform)));
    }
    /// <summary>没有显式服务端平台目录的普通合成器仍拒绝同形平台JSON，项目ID不能伪造来源。</summary>
    [Fact]
    public void Ordinary_composition_cannot_open_security_pages_by_copying_platform_project_id()
    {
        var f=new Models();Assert.Throws<InvalidOperationException>(()=>new ProjectPhysicalModels(f.Files,f.Security).Compose(Copy(f.Registry.Platform)));
        var ordinary=f.Service.Compose(f.Registry.Demo);
        var expected=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(f.Registry.Demo,f.Files),f.Security);
        expected=MetadataCatalogComposition.MergePhysical(expected,AgentFoundationCatalog.ForApplication(f.Registry.Demo));
        Assert.Equal(Fingerprint(expected),Fingerprint(ordinary));
    }
    /// <summary>只允许实际发布的同表/同项目/同后台页面；布局标题可变，但新页面及跨归属声明不能借原标识进入。</summary>
    [Theory]
    [InlineData("new-page")][InlineData("wrong-table")][InlineData("wrong-project")][InlineData("wrong-admin")][InlineData("different-project")]
    public void Altered_security_page_binding_is_rejected(string kind)
    {
        var f=new Models();var input=Copy(f.Registry.Platform);var page=input.ConfigPages.Single(item=>item.Id=="6031906822946816");
        switch(kind)
        {
            case "new-page":page.Id="999999999999999981";input.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="6031906822946816");break;
            case "wrong-table":page.ConfigTableId="1692532858499969026";break;
            case "wrong-project":page.YzProjectId="999999999999999982";break;
            case "wrong-admin":page.YzAdminTypeId="999999999999999983";break;
            case "different-project":input.YzProjectId="999999999999999984";break;
        }
        Assert.Throws<InvalidOperationException>(()=>f.Service.Compose(input));
    }
    /// <summary>平台页面投影不能掩盖保留表、字段、索引或布局引用的真实冲突，原严格物理校验继续生效。</summary>
    [Theory]
    [InlineData("table")][InlineData("column")][InlineData("index")][InlineData("field-reference")]
    public void Physical_conflicts_and_invalid_page_fields_are_never_hidden(string kind)
    {
        var f=new Models();var input=Copy(f.Registry.Platform);const string manager="6031906809708544";
        switch(kind)
        {
            case "table":input.ConfigTables.Single(table=>table.Id==manager).TableName="forged_manager";break;
            case "column":input.ConfigColumns.First(column=>column.ConfigTableId==manager&&column.ColumnName=="name").ColumnLength="499";break;
            case "index":
                var position=input.ConfigIndexes.FindIndex(index=>index.GetProperty("configTableId").GetString()==manager);
                var node=JsonNode.Parse(input.ConfigIndexes[position].GetRawText())!;node["indexName"]="forged_index";input.ConfigIndexes[position]=JsonSerializer.SerializeToElement(node);break;
            case "field-reference":input.ConfigPageColumns.First(field=>field.ConfigPageId=="6031906822946816").ConfigColumnId="999999999999999985";break;
        }
        Assert.Throws<InvalidOperationException>(()=>f.Service.Compose(input));
    }
    /// <summary>服务端目录与基础设施在装配时取快照，调用者后改可变对象不扩权或改变DDL。</summary>
    [Fact]
    public void Composition_authority_is_a_snapshot_and_layout_changes_do_not_change_physical_schema()
    {
        var f=new Models();var input=Copy(f.Registry.Platform);var before=Fingerprint(input);
        input.ConfigPages.Single(page=>page.Id=="6031906822946816").Name="账号页面新标题";
        Assert.Equal(before,Fingerprint(f.Service.Compose(input)));
        f.Files.ConfigColumns[0].ColumnName="changed_after_registration";
        f.Security.ConfigColumns[0].ColumnName="changed_after_registration";
        f.Registry.Platform.ConfigPages.Single(page=>page.Id=="6031906822946816").Id="999999999999999986";
        Assert.Equal(before,Fingerprint(f.Service.Compose(input)));
        input.ConfigPages.Single(page=>page.Id=="6031906822946816").Id="999999999999999986";
        input.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="6031906822946816");
        Assert.Throws<InvalidOperationException>(()=>f.Service.Compose(input));
    }
    /// <summary>所有模型均读取当前仓库已发布元数据，不用缩减的手造平台图代替自举。</summary>
    private sealed class Models
    {
        internal MetadataRegistry Registry {get;}
        internal MetadataCatalog Files {get;}internal MetadataCatalog Security {get;}internal ProjectPhysicalModels Service {get;}
        internal Models([CallerFilePath]string source="")
        {
            var root=new DirectoryInfo(Path.GetDirectoryName(source)!);
            while(!File.Exists(Path.Combine(root.FullName,"global.json")))root=root.Parent??throw new InvalidOperationException("找不到测试仓库。");
            var metadata=Path.Combine(root.FullName,"backend/metadata");Registry=new(Path.Combine(metadata,"platform.json"),Path.Combine(metadata,"demo.json"));
            Files=MetadataRegistry.Load(Path.Combine(metadata,"files.json"));Security=MetadataRegistry.Load(Path.Combine(metadata,"security.json"));Service=new(Files,Security,Registry);
        }
    }
    /// <summary>指纹仅比较实际MySQL表/字段/索引，不把页面布局当作数据库结构。</summary>
    private static string Fingerprint(MetadataCatalog model)=>MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(model,"offline"));
    /// <summary>深拷贝用于输入不变性和伪造反例。</summary>
    private static T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(Text(value))!;
    /// <summary>稳定序列化整个输入，检查模型合成未修改原元数据。</summary>
    private static string Text<T>(T value)=>JsonSerializer.Serialize(value);
}
