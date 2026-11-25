using Metarill.Core.Contracts;
namespace Metarill.Core.Cascades;

/// <summary>固定跨表链的Excel无损路径；部分选择保留尾空层，不把数字名称当作ID。</summary>
public static class CrossTableExcelPath
{
    /// <summary>原完整路径外观保持，部分路径补到固定层数；不可无损表达的名称明确拒绝。</summary>
    public static string? Format(CrossTablePath? path,int levelCount,string joiner)=>Format(path,levelCount,joiner,null);

    /// <summary>按每层设定验证数字或文本键，再以原名称格式输出。</summary>
    public static string? Format(CrossTablePath? path,int levelCount,string joiner,IReadOnlyList<bool>? textLevels)
    {
        RequireShape(levelCount,joiner,textLevels);
        if(path is null||path.Path.Count==0)return null;
        RequirePath(path,levelCount,joiner,textLevels);
        return string.Join(joiner,path.Path.Select(node=>node.Name).Concat(Enumerable.Repeat("",levelCount-path.Path.Count)));
    }

    /// <summary>完整/补空路径按根绑定；旧短写检查每个可能连续层段，只接受唯一实际路径。</summary>
    public static Task<CrossTablePath> ResolveAsync(string text,int levelCount,string joiner,
        Func<int,string,long?,bool,CancellationToken,Task<IReadOnlyList<CascadeTreeNode>>> find,
        Func<int,long,CancellationToken,Task<CrossTablePath>> readPath,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(find);ArgumentNullException.ThrowIfNull(readPath);
        return ResolveByKeyAsync(text,levelCount,joiner,null,
            async (level,name,parent,bound,ct)=>(await find(level,name,parent is null?null:CrossTableReader.Long(parent),bound,ct)).Select(CrossTableReader.Adapt).ToArray(),
            (level,key,ct)=>readPath(level,CrossTableReader.Long(key),ct),token);
    }

    /// <summary>混合业务键仍按名称反查，原名、父键和逐层键均参与无损验证。</summary>
    public static async Task<CrossTablePath> ResolveByKeyAsync(string text,int levelCount,string joiner,IReadOnlyList<bool>? textLevels,
        Func<int,string,string?,bool,CancellationToken,Task<IReadOnlyList<TextCascadeTreeNode>>> find,
        Func<int,string,CancellationToken,Task<CrossTablePath>> readPath,CancellationToken token=default)
    {
        RequireShape(levelCount,joiner,textLevels);ArgumentNullException.ThrowIfNull(find);ArgumentNullException.ThrowIfNull(readPath);
        token.ThrowIfCancellationRequested();
        var parts=text.Split(joiner,StringSplitOptions.None);
        if(parts.Length==0||parts.Length>levelCount||parts.Any(part=>part.Length>0&&!ValidName(part,joiner)))throw Invalid();
        var explicitDepth=parts.Length==levelCount;
        var count=parts.Length;
        if(explicitDepth)while(count>0&&parts[count-1].Length==0)count--;
        if(count==0||parts.Take(count).Any(string.IsNullOrEmpty)||!explicitDepth&&parts.Any(string.IsNullOrEmpty))throw Invalid();
        CrossTablePath? selected=null;var matchedIds=new string[count];
        for(var offset=0;offset<=(explicitDepth?0:levelCount-count);offset++)
        {
            string? parent=null;var matched=true;
            for(var index=0;index<count;index++)
            {
                token.ThrowIfCancellationRequested();
                var level=offset+index;var bindParent=level==0||index>0;
                var rows=await find(level,parts[index],parent,bindParent,token);
                if(rows.Count>1)throw Invalid();
                if(rows.Count==0){matched=false;break;}
                var row=rows[0];
                if(!CrossTableReader.ValidKey(row.Key,textLevels?[level]==true)||row.Name!=parts[index]||index>0&&row.ParentKey!=parent)throw Invalid();
                matchedIds[index]=row.Key;parent=row.Key;
            }
            if(!matched)continue;
            var candidate=await readPath(offset+count-1,parent!,token);
            RequirePath(candidate,levelCount,joiner,textLevels);
            if(candidate.Path.Count!=offset+count||candidate.Path[^1].Id!=parent||
                Enumerable.Range(0,count).Any(index=>candidate.Path[offset+index].Name!=parts[index]||candidate.Path[offset+index].Id!=matchedIds[index]))throw Invalid();
            if(selected is not null)throw Invalid();
            // 不保留调用方可变列表；不同表同数字ID合法，身份还包含固定层号。
            selected=new(Array.AsReadOnly(candidate.Path.ToArray()),candidate.DisplayText);
        }
        return selected??throw Invalid();
    }
    /// <summary>固定层数及分隔符只来自生成计划，不从Excel请求决定。</summary>
    private static void RequireShape(int count,string joiner,IReadOnlyList<bool>? textLevels)
    {
        if(count<2||string.IsNullOrEmpty(joiner)||textLevels is not null&&textLevels.Count!=count)throw new ArgumentException("跨表Excel固定层数或分隔符无效。");
    }
    /// <summary>空洞、坏主键、重复层键或不可表达名称不能被格式化成另一个选择。</summary>
    private static void RequirePath(CrossTablePath path,int count,string joiner,IReadOnlyList<bool>? textLevels)
    {
        if(path.Path.Count is 0||path.Path.Count>count||path.Path.Where((node,index)=>!CrossTableReader.ValidKey(node.Id,textLevels?[index]==true)||!ValidName(node.Name,joiner)||string.IsNullOrWhiteSpace(node.LevelKey)).Any()||
            path.Path.Select(node=>node.LevelKey).Distinct(StringComparer.Ordinal).Count()!=path.Path.Count)throw Invalid();
    }
    /// <summary>名称不做静默修剪或分隔符转义，无法无损表达时明确拒绝。</summary>
    private static bool ValidName(string name,string joiner)=>!string.IsNullOrWhiteSpace(name)&&name==name.Trim()&&!name.Contains(joiner,StringComparison.Ordinal);
    /// <summary>统一返回单元格关联错误，不回显内部查询细节。</summary>
    private static PageImportException Invalid()=>PageImportException.Input(PageImportError.Relation);
}
