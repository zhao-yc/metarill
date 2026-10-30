using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>从原详情生成源头输出Vue并实际编译挂载；不把离线探针当作浏览器或后端兼容验收。</summary>
public sealed class OriginalUniappDetailTests
{
    /// <summary>原DOM、单表单校验、分组和每类原控件必须一并存在，不能只恢复类名。</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task Original_detail_preserves_dom_controls_rules_buttons_and_actual_vue_behavior(int buttonsPosition)
    {
        var catalog=Catalog();catalog.ConfigPages[0].Extra["detailPageButtonsPosition"]=JsonSerializer.SerializeToElement(buttonsPosition);
        var model=GenerationModel.Create(catalog,"Example.OriginalDetail");var table=model.Tables.Single();var page=table.Pages.Single();var detail=OriginalUniappDetailRenderer.Render(model,table,page,[]);
        Assert.Single(Regex.Matches(detail,"<uni-forms ").Cast<Match>());Assert.Contains("ref='entityForm'",detail);Assert.Contains(":rules=\"entity_rules\"",detail);Assert.Contains("this.$refs.entityForm.validate()",detail);
        Assert.Contains("class='detail-page'",detail);Assert.Contains("class=\"base-info\"",detail);Assert.Contains("class='section-view'",detail);Assert.Contains("--detail-grid-columns",detail);Assert.Contains("grid-column: span 2",detail);
        Assert.Contains("createDetailLayoutMixin",detail);Assert.Contains("v-show='activeDetailGroupKey ===",detail);Assert.Contains("revealDetailValidationError(err)",detail);Assert.Contains("class='bottom-buttons",detail);Assert.Contains(">关闭</button>",detail);
        Assert.Contains("<uni-data-checkbox",detail);Assert.Contains("<zxz-uni-data-select",detail);Assert.DoesNotContain("<yzp-relation-select",detail);Assert.DoesNotContain("<uni-data-select",detail);
        Assert.Contains("<uni-file-picker",detail);Assert.Contains("<yzp-file-library v-model=",detail);Assert.Contains("access-mode='PROTECTED'",detail);Assert.Contains("<yzp-cascade-tree",detail);Assert.Contains("cascadeTreeOptions",detail);Assert.Contains("<view class='rtf-review' v-html=",detail);
        Assert.Contains("parentId:that.parent_id,formParameters:uni.getStorageSync(that.formParametersName)",detail);Assert.Contains("data: that.entity",detail);Assert.DoesNotContain("buildPayload",detail);Assert.Contains("customerButtonHandler900",detail);Assert.Contains("showCustomerButtons",detail);Assert.DoesNotContain("##",detail);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-original-detail");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        try {await File.WriteAllTextAsync(Path.Combine(output,"detail.vue"),detail);await RunProbe(output);}
        finally {await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>损坏布局、未知控件/按钮或目标页缺失必须暴露，不能降级为另一套界面。</summary>
    [Theory]
    [InlineData("layoutVersion")] [InlineData("layoutKey")] [InlineData("layoutMissingGroup")] [InlineData("layoutHiddenGroup")] [InlineData("layoutMissingGroupNone")] [InlineData("control")] [InlineData("buttonAction")] [InlineData("buttonSave")]
    public void Original_detail_rejects_unknown_or_incomplete_configuration(string scenario)
    {
        var catalog=Catalog();var page=catalog.ConfigPages[0];
        if(scenario=="layoutVersion")page.Extra["detailLayout"]=JsonSerializer.SerializeToElement("{\"version\":2,\"mode\":\"tabs\",\"groups\":[]}");
        if(scenario=="layoutKey")page.Extra["detailLayout"]=JsonSerializer.SerializeToElement("{\"version\":1,\"mode\":\"tabs\",\"groups\":[{\"key\":\"same\",\"name\":\"甲\"},{\"key\":\"same\",\"name\":\"乙\"}]}");
        if(scenario is "layoutMissingGroup" or "layoutHiddenGroup" or "layoutMissingGroupNone")
        {
            catalog.ConfigPageColumns[0].Extra["detailGroupKey"]=JsonSerializer.SerializeToElement("missing_group");
            if(scenario=="layoutHiddenGroup"){catalog.ConfigPageColumns[0].IsEdit=0;catalog.ConfigPageColumns[0].Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}
            if(scenario=="layoutMissingGroupNone")page.Extra["detailLayout"]=JsonSerializer.SerializeToElement(page.Extra["detailLayout"].GetString()!.Replace("tabs","none",StringComparison.Ordinal));
        }
        if(scenario=="control")catalog.ConfigColumns.Single(column=>column.ColumnName=="notes").ControlKind="UnknownNewControl";
        if(scenario is "buttonAction" or "buttonSave")catalog.ConfigButtons[0]=JsonSerializer.SerializeToElement(new {id="900",name="未知按钮",configPageId=page.Id,place="Edit",visibleScheme="Normal",actionType=scenario=="buttonAction"?4:1,saveOption=scenario=="buttonSave"?"Invented":"NotSave"});
        var model=GenerationModel.Create(catalog,"Example.Reject");var table=model.Tables.Single();Assert.Throws<InvalidOperationException>(()=>OriginalUniappDetailRenderer.Render(model,table,table.Pages.Single(),[]));
    }
    /// <summary>保存与实际原模板渲染共同遵循64字符边界，不让非法配置进入发布。</summary>
    [Theory][InlineData(64,true)][InlineData(65,false)][InlineData(100,false)]
    public void Original_detail_group_key_length_matches_workbench(int length,bool accepted)
    {
        var catalog=Catalog();var key=new string('a',length);
        catalog.ConfigPages[0].Extra["detailLayout"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{version=1,mode="tabs",groups=new[]{new{key,name="基础信息"},new{key="other",name="更多内容"}}}));
        foreach(var field in catalog.ConfigPageColumns)
            if(field.Extra.TryGetValue("detailGroupKey",out var value)&&value.GetString()=="basic")field.Extra["detailGroupKey"]=JsonSerializer.SerializeToElement(key);
        var model=GenerationModel.Create(catalog,"Example.LayoutLength");var table=model.Tables.Single();var page=table.Pages.Single();
        if(accepted)Assert.Contains(key,OriginalUniappDetailRenderer.Render(model,table,page,[]));
        else Assert.Throws<InvalidOperationException>(()=>OriginalUniappDetailRenderer.Render(model,table,page,[]));
    }

    /// <summary>原子表结构继续在表单之外，保存前不挂载；原props/打开方式与刷新事件全部保留。</summary>
    [Fact]
    public async Task Original_detail_keeps_parent_child_lifecycle_and_original_component_contract()
    {
        var catalog=Catalog();var table=catalog.ConfigTables.Single();var clone=JsonSerializer.Deserialize<TableMetadata>(JsonSerializer.Serialize(table))!;clone.Id="child";clone.TableName="original_child";catalog.ConfigTables.Add(clone);
        var childPage=new PageMetadata{Id="302",ConfigTableId=clone.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId=catalog.YzAdminTypes.Single().Id,Name="原子表",WhereSql="yz_project_id=@current_yz_project_id AND parent_id=@parentId"};catalog.ConfigPages.Add(childPage);
        catalog.ConfigColumns.AddRange(new[]{new ColumnMetadata{Id="childid",ConfigTableId=clone.Id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new ColumnMetadata{Id="childproject",ConfigTableId=clone.Id,ColumnName="yz_project_id",LogicalType="int64",IsDbNotNull=1},new ColumnMetadata{Id="childparent",ConfigTableId=clone.Id,ColumnName="parent_id",LogicalType="int64",LinkConfigTableId=table.Id}});
        catalog.ConfigPageColumns.Add(new(){Id="childfield",ConfigPageId=childPage.Id,ConfigColumnId="childparent",IsEdit=1,IsShow=1});
        catalog.ConfigChildren.Add(JsonSerializer.SerializeToElement(new{id="88",parentConfigPageId=catalog.ConfigPages[0].Id,childConfigPageId=childPage.Id,childPageOpenType=2,sortNo=1,yzProjectId=catalog.YzProjectId}));catalog.ConfigPages[0].Extra["childPlace"]=JsonSerializer.SerializeToElement("Tab");
        var model=GenerationModel.Create(catalog,"Example.Child");var parent=model.Tables.Single(item=>item.Source.Id==table.Id);var children=ChildrenCompilation.Compile(model);var detail=OriginalUniappDetailRenderer.Render(model,parent,parent.Pages.Single(),children);
        Assert.Contains("v-if=\"originalChildrenReady\"",detail);Assert.Contains("class=\"yzp-child-content\"",detail);Assert.Contains(":parent_id='id'",detail);Assert.Contains(":parentReadOnlyCondition='readOnlyCondition'",detail);Assert.Contains(":parent_page_sn='page_sn'",detail);Assert.Contains(":child_page_open_type='2'",detail);Assert.Contains("this.$refs.ChildList88?.refreshData()",detail);Assert.True(detail.IndexOf("</uni-forms>",StringComparison.Ordinal)<detail.IndexOf("<ChildList88",StringComparison.Ordinal));
        await Probe(detail,"children");
    }
    /// <summary>选择页保留原弹层和props/events，内部多选仍使用已验证完整行回执。</summary>
    [Fact]
    public async Task Original_detail_selection_keeps_original_popup_and_complete_row_bridge()
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog("original_selection");
        var source=catalog.ConfigTables.Single(table=>table.Id=="2");
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        sourcePage.IsNew=1;sourcePage.IsSave=1;sourcePage.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==sourcePage.Id))field.IsEdit=1;
        var single=new ColumnMetadata{Id="28",ConfigTableId=source.Id,ColumnName="single_id",LogicalType="int64",LinkConfigTableId="3",ControlKind="SelectPage",ControlDetail="{\"pageId\":\"302\",\"nameField\":\"name\",\"buttonText\":\"选择单项\"}"};
        catalog.ConfigColumns.Add(single);catalog.ConfigColumns.Add(new(){Id="29",ConfigTableId=source.Id,ColumnName="single_name",LogicalType="string"});
        catalog.ConfigPageColumns.AddRange([new(){Id="2004",ConfigPageId=sourcePage.Id,ConfigColumnId="28",IsShow=1,IsEdit=1},new(){Id="2005",ConfigPageId=sourcePage.Id,ConfigColumnId="29",IsShow=1,IsEdit=1}]);
        var targetPage=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="301")))!;targetPage.Id="302";targetPage.Extra.Remove("type");catalog.ConfigPages.Add(targetPage);
        catalog.ConfigPageColumns.Add(new(){Id="3003",ConfigPageId="302",ConfigColumnId="32",IsShow=1});
        var model=GenerationModel.Create(catalog,"Example.OriginalSelection");var table=model.Tables.Single(table=>table.Source.Id==source.Id);var page=table.Pages.Single();
        var detail=OriginalUniappDetailRenderer.Render(model,table,page,[]);
        Assert.Contains("<yzp-popup ref=\"selectPageDialog25\"",detail);Assert.Contains("class=\"select-page-dialog-view\"",detail);Assert.Contains(":selectedIds=\"entity.targetIds\"",detail);Assert.Contains("@onPopSelect=\"onPopSelect25\"",detail);Assert.Contains(":parent_id=\"multiTargetParent('targetIds')\"",detail);Assert.Contains("<view class='select-page-input-row'>",detail);Assert.DoesNotContain("yzp-relation-select",detail);
        await Probe(detail,"selection");
    }
    /// <summary>真实项目主题与统一更新策略在原DOM内生效，旧IsSave不能覆盖明确isUpdate。</summary>
    [Fact]
    public void Original_detail_uses_project_color_and_unified_update_policy()
    {
        var catalog=Catalog();catalog.PrimaryColor="#137abc";catalog.ConfigPages[0].IsSave=0;catalog.ConfigPages[0].Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,"Example.Color");var table=model.Tables.Single();var detail=OriginalUniappDetailRenderer.Render(model,table,table.Pages.Single(),[]);
        Assert.Contains("primary-color=\"#137abc\"",detail);Assert.Contains("page_is_save:true",detail);Assert.DoesNotContain("#0ab3ad",detail);
    }
    /// <summary>原非冗余派生项只读，不产生输入/校验和自动保存，未修改后端能力门禁。</summary>
    [Fact]
    public void Original_detail_nonredundant_derived_field_keeps_original_display_only_branch()
    {
        var catalog=Catalog();catalog.ConfigTables[0].IsRongyu=0;var column=catalog.ConfigColumns.Single(item=>item.ColumnName=="notes");column.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);column.Extra["isAutoPostBack"]=JsonSerializer.SerializeToElement(1);var field=catalog.ConfigPageColumns.Single(item=>item.ConfigColumnId==column.Id);field.IsRequired=1;field.Extra["isAutoSaveAfterChange"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,"Example.OriginalDerived");var table=model.Tables.Single();var detail=OriginalUniappDetailRenderer.Render(model,table,table.Pages.Single(),[]);
        Assert.Contains("<text>{{readonlyDisplayText(entity.notes)}}</text>",detail);Assert.Contains("notesReadOnly() { return true; }",detail);Assert.DoesNotContain("v-model='entity.notes'",detail);Assert.DoesNotContain("'entity.notes'(newVal,oldVal)",detail);Assert.DoesNotContain("notes:{rules:",detail);
    }
    /// <summary>在真实临时文件内执行生成Vue，清理只覆盖本次随机目录。</summary>
    private static async Task Probe(string detail,string scenario)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-original-detail");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        try {await File.WriteAllTextAsync(Path.Combine(output,"detail.vue"),detail);await File.WriteAllTextAsync(Path.Combine(output,"scenario.txt"),scenario);await RunProbe(output);}
        finally {await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }

    /// <summary>覆盖原控件及两个详情分组，元数据由真实demo导出改写，不访问旧服务或数据库。</summary>
    private static MetadataCatalog Catalog([CallerFilePath]string file="")
    {
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(root,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var page=catalog.ConfigPages.Single();page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);page.Extra["cols"]=JsonSerializer.SerializeToElement(2);page.Extra["detailLayout"]=JsonSerializer.SerializeToElement("{\"version\":1,\"mode\":\"tabs\",\"groups\":[{\"key\":\"basic\",\"name\":\"基础信息\"},{\"key\":\"other\",\"name\":\"更多内容\"}]}");
        catalog.ConfigColumns.Single(column=>column.ColumnName=="notes").ControlKind="TextArea";catalog.ConfigColumns.Single(column=>column.ColumnName=="is_enabled").ControlKind="CheckBox";
        foreach(var field in catalog.ConfigPageColumns)field.Extra["detailGroupKey"]=JsonSerializer.SerializeToElement(field.ColumnName=="notes"?"other":"basic");
        catalog.ConfigColumns.Single(column=>column.ColumnName=="notes").Extra["colSpan"]=JsonSerializer.SerializeToElement(2);
        var controls=new[]{("constant","ConstList","甲|a,乙|b"),("dropdown","ConstDownList","甲|a,乙|b"),("rtf","RTF",""),("protected_files","FileLibrary","{\"accessMode\":\"PROTECTED\",\"maxCount\":2,\"maxSizeMb\":1,\"extensions\":[\"txt\"]}"),("image","Image","{}"),("images","MultiImage","{\"limit\":3,\"cropImages\":true}"),("upload","Upload","2"),("video","Video","1"),("base64","Base64Image",""),("network_image","IntenetImage",""),("relation_id","List",""),("multi_ids","MultiCheckBoxDatabase",""),("date_value","date",""),("date_time_value","date_time",""),("month_value","month",""),("time_value","time",""),("cascade_id","CascadeTree","{\"sourceMode\":\"selfTree\",\"selfTree\":{\"table\":\"demo_customer\",\"idField\":\"id\",\"nameField\":\"name\",\"parentField\":\"parent_id\"}}"),("custom","CustomControl","yzp-original-custom")};
        var index=500;foreach(var(name,kind,detail)in controls){var column=new ColumnMetadata{Id=(index++).ToString(),ConfigTableId=catalog.ConfigTables[0].Id,YzProjectId=catalog.YzProjectId,Name=name,ColumnName=name,LogicalType="string",ColumnLength="max",ControlKind=kind,ControlDetail=detail,SortNo=index};catalog.ConfigColumns.Add(column);var field=new PageColumnMetadata{Id="f"+index,ConfigPageId=page.Id,ConfigColumnId=column.Id,YzProjectId=catalog.YzProjectId,IsEdit=1,IsShow=1,SortNo=index};field.Extra["detailGroupKey"]=JsonSerializer.SerializeToElement("other");catalog.ConfigPageColumns.Add(field);}
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="900",name="原确认动作",configPageId=page.Id,place="Edit",visibleScheme="Normal",actionType=1,saveOption="NotSave",isNeedConfirm=1,confirmMsg="原确认提示",type=1}));
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="901",name="先保存再执行",configPageId=page.Id,place="EditAndGrid",visibleScheme="Normal",actionType=1,saveOption="Save",isNeedConfirm=0,type=0}));return catalog;
    }
    /// <summary>通过真实Vue3编译和挂载生成脚本，表单外部组件以可控协议模拟校验返回。</summary>
    private static async Task RunProbe(string output,[CallerFilePath]string file="")
    {
        var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!,"OriginalUniappDetailProbe.mjs"));start.ArgumentList.Add(output);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
