using Metarill.Core.Metadata;

namespace Metarill.Runtime.Security;

public static partial class PrincipalRolePolicy
{
    private sealed record InheritedDecisions(HashSet<string> Exact,HashSet<string> FileQueries,HashSet<string> FileExact,HashSet<string> InheritedAll);
    /// <summary>真实父子与选择引用使用直接授权来源，禁止递归继承；文件不接受选择引用来源。</summary>
    private static InheritedDecisions ApplyInheritance(IReadOnlyDictionary<string,SecurityPageResource> pages,
        IReadOnlyDictionary<string,SecurityPageResource> published,IEnumerable<SecurityResourceCatalog> catalogs,SecurityResourceCatalog? current,
        HashSet<string> permissions,HashSet<string> scoped,HashSet<string> ordinary,bool super)
    {
        var fixedLinks=catalogs.SelectMany(catalog=>catalog.Inheritance).ToHashSet();
        var links=(current is null?fixedLinks:current.Inheritance.Where(fixedLinks.Contains)).Where(link=>
            pages.TryGetValue(link.SourcePageId,out var source)&&pages.TryGetValue(link.TargetPageId,out var target)&&source.AdminTypeId==target.AdminTypeId&&source.ProjectId==target.ProjectId)
            .Distinct().ToLookup(link=>link.TargetPageId,StringComparer.Ordinal);
        var enabled=pages.ToDictionary(pair=>pair.Key,pair=>PageActions(pair.Value).Intersect(PageActions(published[pair.Key]),StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),StringComparer.Ordinal);
        var inheritedAll=new HashSet<string>(StringComparer.Ordinal);
        var exact=new HashSet<string>(StringComparer.Ordinal);var fileQueries=new HashSet<string>(StringComparer.Ordinal);var fileExact=new HashSet<string>(StringComparer.Ordinal);
        foreach(var page in pages.Values)
        foreach(var action in enabled[page.Id])
        {
            var sources=new HashSet<string>(StringComparer.Ordinal){page.Id};var fileSources=new HashSet<string>(StringComparer.Ordinal){page.Id};
            foreach(var link in links[page.Id])
            {
                if(!Available(link.SourcePageId,action))continue;
                if(link.Kind==PagePermissionInheritanceKind.Child){sources.Add(link.SourcePageId);fileSources.Add(link.SourcePageId);}
                else if(link.Kind==PagePermissionInheritanceKind.Selection&&action=="is_query")sources.Add(link.SourcePageId);
            }
            var general=Decision(sources,action);var key=page.Id+":"+action;
            // 原直接is_all保持原响应集合形状；只有继承真正补上缺少的动作时才新增权限键。
            if(general.Allowed&&!Granted(page.Id,action,scoped)&&!Granted(page.Id,action,ordinary)&&!super)
            {
                // 继承is_all只服务复制/导入，不能变成绕过父页动作开关的通用is_all。
                if(action=="is_all")inheritedAll.Add(key);else permissions.Add(key);
            }
            if(general.Exact)exact.Add(key);
            if(action!="is_query")continue;
            var file=Decision(fileSources,action);if(file.Allowed)fileQueries.Add(key);if(file.Exact)fileExact.Add(key);
        }
        return new(exact,fileQueries,fileExact,inheritedAll);
        // 当前与已发布开关必须同时开放来源动作，不能由在线新开关借未发布动作授权。
        bool Available(string id,string action)=>enabled[id].Contains(action);
        // 只读未修改的直接来源集合；继承得到的权限不再成为其它继承的源。
        (bool Allowed,bool Exact) Decision(IEnumerable<string> sources,string action)
        {
            if(super)return (true,false);
            var restricted=sources.Any(source=>Granted(source,action,scoped));var unrestricted=sources.Any(source=>Granted(source,action,ordinary));
            return (restricted||unrestricted,restricted&&!unrestricted);
        }
    }
    /// <summary>同页同动作或原is_all才是有效来源，其它动作不能取消精确限制。</summary>
    private static bool Granted(string page,string action,IReadOnlySet<string> values)=>values.Contains(page+":"+action)||values.Contains(page+":is_all");
}
