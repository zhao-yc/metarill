using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Security.Tests;

/// <summary>编译真实元数据关系，不创建库表或模拟授权决策。</summary>
public sealed class PagePermissionInheritanceTests
{
    /// <summary>原父子边与列/页控件引用都被保留，重复关系只产生一次，输入模型保持。</summary>
    [Fact]
    public void Compiles_both_relation_kinds_without_mutating_metadata()
    {
        var catalog=Catalog();catalog.ConfigChildren=[JsonSerializer.SerializeToElement(new{parentConfigPageId="100",childConfigPageId="200",yzProjectId="1"}),JsonSerializer.SerializeToElement(new{parent_config_page_id="100",child_config_page_id="200",yz_project_id="1"})];
        var before=JsonSerializer.Serialize(catalog);var links=PagePermissionInheritance.Compile(catalog);
        Assert.Equal(2,links.Count);Assert.Contains(new("100","200",PagePermissionInheritanceKind.Child),links);Assert.Contains(new("100","200",PagePermissionInheritanceKind.Selection),links);
        Assert.Equal(before,JsonSerializer.Serialize(catalog));Assert.True(Assert.IsAssignableFrom<IList<PagePermissionInheritanceLink>>(links).IsReadOnly);
    }
    /// <summary>页面覆盖只替换非空值；对象覆盖和原明确关联使用同一生成语义。</summary>
    [Theory]
    [InlineData("{\"pageId\":\"300\"}","300")][InlineData("{\"pageId\":\"\"}","200")][InlineData("{}","200")]
    public void Effective_selection_uses_the_page_override(string overlay,string target)
    {
        var catalog=Catalog();catalog.ConfigPageColumns[0].Extra["controlDetail"]=JsonSerializer.SerializeToElement(overlay);
        Assert.Equal(target,Assert.Single(PagePermissionInheritance.Compile(catalog)).TargetPageId);
        catalog.ConfigPageColumns[0].Extra["controlDetail"]=JsonDocument.Parse(overlay).RootElement.Clone();Assert.Equal(target,Assert.Single(PagePermissionInheritance.Compile(catalog)).TargetPageId);
        catalog.ConfigColumns[0].ControlDetail="{}";catalog.ConfigPageColumns[0].Extra.Clear();catalog.ConfigColumns[0].Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("300");Assert.Equal("300",Assert.Single(PagePermissionInheritance.Compile(catalog)).TargetPageId);
    }
    /// <summary>跨后台、跨项目、软删除、缺失/歧义来源及损坏配置均不能授予目标查询。</summary>
    [Theory]
    [InlineData("foreign-admin")][InlineData("foreign-project")][InlineData("deleted-page")][InlineData("deleted-field")]
    [InlineData("wrong-table")][InlineData("wrong-kind")][InlineData("missing-page")][InlineData("duplicate-field")]
    [InlineData("bad-json")][InlineData("duplicate-key")][InlineData("object-id")][InlineData("bad-known-key")]
    public void Unproven_selection_does_not_compile_a_permission_edge(string scenario)
    {
        var catalog=Catalog();
        switch(scenario)
        {
            case "foreign-admin":catalog.ConfigPages[1].YzAdminTypeId="20";break;
            case "foreign-project":catalog.ConfigPages[1].YzProjectId="2";break;
            case "deleted-page":catalog.ConfigPages[1].Extra["deleteNo"]=JsonSerializer.SerializeToElement("deleted");break;
            case "deleted-field":catalog.ConfigPageColumns[0].Extra["deleteNo"]=JsonSerializer.SerializeToElement("deleted");break;
            case "wrong-table":catalog.ConfigColumns[0].ConfigTableId="2";break;
            case "wrong-kind":catalog.ConfigColumns[0].ControlKind="List";break;
            case "missing-page":catalog.ConfigPages.RemoveAt(1);break;
            case "duplicate-field":catalog.ConfigPageColumns.Add(new(){Id="12",ConfigPageId="100",ConfigColumnId="11"});break;
            case "bad-json":catalog.ConfigColumns[0].ControlDetail="[";break;
            case "duplicate-key":catalog.ConfigColumns[0].ControlDetail="{\"pageId\":\"200\",\"pageId\":\"300\"}";break;
            case "object-id":catalog.ConfigColumns[0].ControlDetail="{\"pageId\":{}}";break;
            case "bad-known-key":catalog.ConfigColumns[0].ControlDetail="{\"pageId\":\"200\",\"nameField\":{}}";break;
        }
        Assert.Empty(PagePermissionInheritance.Compile(catalog));
    }
    /// <summary>父子关系只认可真实相同后台页面，不能从whereSql或未知JSON推断。</summary>
    [Theory]
    [InlineData("foreign-admin")][InlineData("foreign-owner")][InlineData("deleted")][InlineData("missing")][InlineData("duplicate-key")][InlineData("conflicting-alias")]
    public void Unproven_child_relation_is_ignored(string scenario)
    {
        var catalog=Catalog();catalog.ConfigPageColumns.Clear();var child=new Dictionary<string,object?>{{"parentConfigPageId","100"},{"childConfigPageId","200"},{"yzProjectId","1"}};
        switch(scenario)
        {
            case "foreign-admin":catalog.ConfigPages[1].YzAdminTypeId="20";break;
            case "foreign-owner":child["yzProjectId"]="2";break;
            case "deleted":child["deleteNo"]="deleted";break;
            case "missing":child["childConfigPageId"]="missing";break;
            case "conflicting-alias":child["child_config_page_id"]="300";break;
        }
        catalog.ConfigChildren=[scenario=="duplicate-key"?JsonDocument.Parse("{\"parentConfigPageId\":\"100\",\"childConfigPageId\":\"200\",\"childConfigPageId\":\"300\"}").RootElement.Clone():JsonSerializer.SerializeToElement(child)];
        Assert.Empty(PagePermissionInheritance.Compile(catalog));catalog.ConfigChildren.Clear();catalog.ConfigPages[1].WhereSql="id=@parent_id";Assert.Empty(PagePermissionInheritance.Compile(catalog));
    }
    /// <summary>最小目录中的实际表/页/字段保持彼此归属，独立页面未依赖菜单。</summary>
    private static MetadataCatalog Catalog()=>new()
    {
        YzProjectId="1",ConfigTables=[new(){Id="1",TableName="records",YzProjectId="1"},new(){Id="2",TableName="targets",YzProjectId="1"}],
        ConfigPages=[new(){Id="100",ConfigTableId="1",YzAdminTypeId="10",YzProjectId="1"},new(){Id="200",ConfigTableId="2",YzAdminTypeId="10",YzProjectId="1"},new(){Id="300",ConfigTableId="2",YzAdminTypeId="10",YzProjectId="1"}],
        ConfigColumns=[new(){Id="11",ConfigTableId="1",ColumnName="target_id",ControlKind="SelectPage",ControlDetail="{\"pageId\":\"200\"}"}],ConfigPageColumns=[new(){Id="111",ConfigPageId="100",ConfigColumnId="11"}]
    };
}
