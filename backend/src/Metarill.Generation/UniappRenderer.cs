using System.Text.Json;

namespace Metarill.Generation;

/// <summary>C#只负责展开原YZP模板；页面、布局与操作合同沿用原前端。</summary>
internal static class UniappRenderer
{
    /// <summary>原列表和详情共享同一元数据快照，尚未交付的后端控件仍写入能力缺口。</summary>
    public static IEnumerable<PlannedFile> Render(GenerationModel model)
    {
        var routes=new List<object>();var gaps=GenerationCapabilityAudit.Collect(model);var children=ChildrenCompilation.Compile(model);
        foreach(var table in model.Tables)
        foreach(var page in table.Pages)
        {
            var stem=table.Source.TableName;var list=$"pages/yzp/{stem}-list-{page.Source.Id}";var detail=$"pages/yzp/{stem}-detail-{page.Source.Id}";
            yield return new("frontend/"+list+".vue",OriginalUniappListRenderer.Render(model,table,page));
            yield return new("frontend/"+detail+".vue",OriginalUniappDetailRenderer.Render(model,table,page,children.Where(child=>child.Parent==page).ToArray()));
            routes.Add(new{path=list,style=new{navigationBarTitleText=page.Source.Name,navigationStyle="custom",enablePullDownRefresh=true}});
            routes.Add(new{path=detail,style=new{navigationBarTitleText=page.Source.Name,navigationStyle="custom"}});
        }
        // 固定可信模块才附带原自定义工作台路由；普通业务目录不能自报标记获得入口。
        routes.AddRange(TrustedSecurityWorkbenchRenderer.Routes(model));
        var subsets=new List<object>();
        subsets.AddRange(model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>MultiSelectCompilation.ForPage(model,table,page).Select(def=>new{controlKind="MultiSelectPage",tableId=table.Source.Id,columnId=def.Source.Source.Id,pageId=page.Source.Id,targetPageId=def.TargetPage.Source.Id,storageMode=table.Source.IsRongyu==0?"nonRedundant":"redundant",extraScopeSupported=false,renamePropagationSupported=false,nonRedundantSupported=table.Source.IsRongyu==0}))));
        subsets.AddRange(model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>CascadeTreeReadCompilation.ForPage(model,table,page).Where(plan=>NonRedundantSelfTreeSubset.Supports(model,table,page,plan.Source)).Select(plan=>(object)new{controlKind="CascadeTree",sourceMode="selfTree",tableId=table.Source.Id,columnId=plan.Source.Source.Id,pageId=page.Source.Id,targetPageId=plan.TargetPage.Source.Id,storageMode="nonRedundant",nonRedundantSupported=true,sameTableSupported=false,sourceParentSupported=false,derivedNameSupported=false,renamePropagationSupported=false}))));
        yield return new("generation-capabilities.json",JsonSerializer.Serialize(new{schemaVersion=1,unsupportedControls=gaps,supportedControlSubsets=subsets},Options)+"\n");
        yield return new("frontend/pages.generated.json",JsonSerializer.Serialize(new{pages=routes},Options)+"\n");
    }
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,NewLine="\n"};
}
