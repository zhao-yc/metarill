using System.Text.Json;

namespace Metarill.Generation;

/// <summary>C#只负责展开原YZP模板；页面、布局与操作合同沿用原前端。</summary>
internal static class UniappRenderer
{
    /// <summary>原列表和详情共享同一元数据快照，尚未交付的后端控件仍写入能力缺口。</summary>
    public static IEnumerable<PlannedFile> Render(GenerationModel model)
    {
        var routes=new List<object>();var gaps=new List<object>();var children=ChildrenCompilation.Compile(model);
        foreach(var table in model.Tables)
        foreach(var page in table.Pages)
        {
            foreach(var column in page.Readable)
            {
                var result=ControlRenderer.Render(model,table,page,column);
                if(result.UnsupportedReason is not null)gaps.Add(new{pageId=page.Source.Id,column=column.Source.ColumnName,usage="detail",reason=result.UnsupportedReason});
            }
            foreach(var column in page.Searchable)
            {
                var result=ControlRenderer.RenderFilter(model,page,column);
                if(result.UnsupportedReason is not null)gaps.Add(new{pageId=page.Source.Id,column=column.Source.ColumnName,usage="filter",reason=result.UnsupportedReason});
            }
            var stem=table.Source.TableName;var list=$"pages/yzp/{stem}-list-{page.Source.Id}";var detail=$"pages/yzp/{stem}-detail-{page.Source.Id}";
            yield return new("frontend/"+list+".vue",OriginalUniappListRenderer.Render(model,table,page));
            yield return new("frontend/"+detail+".vue",OriginalUniappDetailRenderer.Render(model,table,page,children.Where(child=>child.Parent==page).ToArray()));
            routes.Add(new{path=list,style=new{navigationBarTitleText=page.Source.Name,navigationStyle="custom",enablePullDownRefresh=true}});
            routes.Add(new{path=detail,style=new{navigationBarTitleText=page.Source.Name,navigationStyle="custom"}});
        }
        var subsets=model.Tables.SelectMany(table=>table.Pages.SelectMany(page=>MultiSelectCompilation.ForPage(model,table,page).Select(def=>new{controlKind="MultiSelectPage",tableId=table.Source.Id,columnId=def.Source.Source.Id,pageId=page.Source.Id,targetPageId=def.TargetPage.Source.Id,storageMode="redundant",extraScopeSupported=false,renamePropagationSupported=false,nonRedundantSupported=false})));
        yield return new("generation-capabilities.json",JsonSerializer.Serialize(new{schemaVersion=1,unsupportedControls=gaps,supportedControlSubsets=subsets},Options)+"\n");
        yield return new("frontend/pages.generated.json",JsonSerializer.Serialize(new{pages=routes},Options)+"\n");
    }
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,NewLine="\n"};
}
