using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>原配置页已有后台ID，名称只属于固定读取投影，不新增冗余物理字段。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed partial class FixedConfigurationAdminNameTests
{
    internal const string TableId="6191080026669056",TablePage="6191083005411328",PageTable="6191173858623488";
    internal const string RootPage="6191175925956608",ChildPage="1868973685063311361",SelectionPage="1869016706289577986";
    /// <summary>直接读取当前发布元数据，不用手造另一份关系图替代原页面。</summary>
    internal static Metarill.Core.Metadata.MetadataCatalog Catalog([CallerFilePath]string source="")=>JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../metadata/platform.json"))),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>四个原页面都必须有只读名称，子页仍保留自己的父范围。</summary>
    [Theory]
    [InlineData(TablePage)] [InlineData(RootPage)] [InlineData(ChildPage)] [InlineData(SelectionPage)]
    public void Original_configuration_admin_alias_is_generated_without_new_storage(string id)
    {
        var catalog=Catalog();var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.ConfigurationAdmin");var table=model.Tables.Single(t=>t.Pages.Any(p=>p.Source.Id==id));var page=table.Pages.Single(p=>p.Source.Id==id);
        var definition=FixedRoleAdminNameCompilation.ForPage(model,table,page);Assert.NotNull(definition);
        var output=BackendRenderer.Render(model).ToDictionary(f=>f.Path,f=>f.Content);var vo=output["VOs/"+page.Name+"Vo.g.cs"];
        Assert.Contains("YzAdminTypeName",vo);Assert.DoesNotContain("YzAdminTypeName",output["Entities/"+table.Name+".g.cs"]);Assert.DoesNotContain("YzAdminTypeName",output["DTOs/"+page.Name+"Requests.g.cs"]);
        Assert.Contains("yzAdminTypeName",output["Controllers/"+page.Name+"Controller.g.cs"]);
        if(id==ChildPage)Assert.Contains(page.Scopes,s=>s.Source=="parent"&&s.Column.Source.ColumnName=="config_table_id");
        if(id==TablePage)Assert.Contains("""case "yzAdminTypeName":""",output["Services/"+table.Name+"Service.g.cs"]);
        if(id==RootPage)Assert.Contains("""case "yzAdminTypeId":""",output["Services/"+table.Name+"Service.g.cs"]);
        Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }
}
