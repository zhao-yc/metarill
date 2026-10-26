using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Cascades;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

internal sealed record SharedCrossTableGroup(IReadOnlyList<CrossTableWriteModel> Plans,SharedCrossTableCoordinator Coordinator)
{
    internal string Key=>Plans.Select(p=>p.Read.Source.Source.Id).Order(StringComparer.Ordinal).First();
    internal IReadOnlyList<ColumnModel> Columns=>Plans.SelectMany(p=>p.Levels).SelectMany(l=>new[]{l.Id,l.Name}).DistinctBy(c=>c.Source.Id).ToArray();
}
internal sealed record SharedCrossTablePage(IReadOnlyList<SharedCrossTableGroup> Groups,IReadOnlyDictionary<string,string> Gaps,IReadOnlySet<string> Members);
/// <summary>从全部固定控件建立共享槽位组，坏同伴不能使另一个控件单独发布。</summary>
internal static class SharedCrossTableCompilation
{
    private static readonly ConditionalWeakTable<GenerationModel,ConcurrentDictionary<(string Table,string Page),SharedCrossTablePage>> Cache=new();
    /// <summary>普通单控件直接返回空；共享编译不反向调用写入证明，避免递归放行。</summary>
    internal static SharedCrossTablePage ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.Columns.Count(c=>c.Source.ControlKind=="CascadeTree")<2)return new([],new Dictionary<string,string>(),new HashSet<string>(StringComparer.Ordinal));
        return Cache.GetValue(model,_=>new()).GetOrAdd((table.Source.Id,page.Source.Id),_=>Compile(model,table,page));
    }
    /// <summary>仅存在共享结构的控件才返回组；失败原因按所有参与者保持。</summary>
    internal static SharedCrossTableGroup? ForSource(GenerationModel model,TableModel table,PageModel page,string source)=>ForPage(model,table,page).Groups.SingleOrDefault(g=>g.Plans.Any(p=>p.Read.Source.Source.Id==source));
    /// <summary>物理缓存只登记一次；相同槽的其它控件仍必须有完整同范围证明。</summary>
    internal static bool Equivalent(GenerationModel model,RelationPropagationBinding left,RelationPropagationBinding right)
    {
        if(left.Multiple||right.Multiple||left.CrossTableColumnId is null||right.CrossTableColumnId is null||left.Source.Source.Id!=right.Source.Source.Id||left.Foreign.Source.Id!=right.Foreign.Source.Id||left.Cache.Source.Id!=right.Cache.Source.Id||left.Target.Source.Id!=right.Target.Source.Id||left.Value.Source.Id!=right.Value.Source.Id||left.CrossTablePageId!=right.CrossTablePageId)return false;
        if(left.CrossTableColumnId==right.CrossTableColumnId)return left.CrossTableLevelKey==right.CrossTableLevelKey;
        return left.Source.Pages.Any(page=>ForPage(model,left.Source,page).Groups.Any(g=>g.Plans.Any(p=>p.Read.Source.Source.Id==left.CrossTableColumnId&&p.Levels.Any(l=>l.Read.Key==left.CrossTableLevelKey&&l.Id.Source.Id==left.Foreign.Source.Id))&&g.Plans.Any(p=>p.Read.Source.Source.Id==right.CrossTableColumnId&&p.Levels.Any(l=>l.Read.Key==right.CrossTableLevelKey&&l.Id.Source.Id==right.Foreign.Source.Id))));
    }
    /// <summary>共享公共前缀必须属于同一目标、读取页、父键和名字，不能合并权限范围。</summary>
    private static SharedCrossTablePage Compile(GenerationModel model,TableModel table,PageModel page)
    {
        var controls=table.Columns.Where(c=>c.Source.ControlKind=="CascadeTree").ToArray();var raw=controls.ToDictionary(c=>c.Source.Id,c=>Prefixes(model,page,c),StringComparer.Ordinal);var remaining=controls.Where(c=>raw[c.Source.Id].Count>0).ToList();var groups=new List<SharedCrossTableGroup>();var gaps=new Dictionary<string,string>(StringComparer.Ordinal);var members=new HashSet<string>(StringComparer.Ordinal);
        while(remaining.Count>0)
        {
            var component=new List<ColumnModel>{remaining[0]};remaining.RemoveAt(0);bool changed;do{changed=false;foreach(var c in remaining.ToArray())if(component.Any(other=>raw[c.Source.Id].Overlaps(raw[other.Source.Id]))){component.Add(c);remaining.Remove(c);changed=true;}}while(changed);if(component.Count<2)continue;members.UnionWith(component.Select(c=>c.Source.Id));
            try
            {
                if(table.Source.IsRongyu!=1||table.IsGlobal)throw new NotSupportedException("共享级联非冗余聚合存储／读取尚未接线，当前要求普通R1来源。");
                var storage=MetadataStoragePolicy.ForTable(model.Catalog,table.Source);var ids=component.Select(c=>c.Source.Id).ToHashSet(StringComparer.Ordinal);var plans=new List<CrossTableWriteModel>();var branches=new List<SharedCrossTableBranch>();
                foreach(var control in component.OrderBy(c=>c.Source.Id,StringComparer.Ordinal))
                {
                    var read=CrossTableReadCompilation.Resolve(model,table,page,control);if(read.Definition is not{} definition)throw new InvalidOperationException(read.Reason??"共享同伴缺少完整读取链。");
                    if(definition.Levels.Any(l=>l.Target.IsGlobal||l.Target.Source.Id==table.Source.Id))throw new NotSupportedException("共享级联的全局／来源自参与路径尚需独立聚合证明。");
                    var levels=new List<CrossTableWriteLevel>();var slots=new List<SharedCrossTableSlot>();
                    foreach(var level in definition.Levels)
                    {
                        var id=table.Columns.SingleOrDefault(c=>c.Source.ColumnName==level.Prefix+"_id");var name=table.Columns.SingleOrDefault(c=>c.Source.ColumnName==level.Prefix+"_name");
                        if(id is null||name is null||id.IsSystem||name.IsSystem||id.Type is not("string?" or "long?")||name.Type!="string?"||id.IsJsonArray||name.IsJsonArray||id.Source.IsVirtualColumn!=0||name.Source.IsVirtualColumn!=0||!storage.IsPhysical(id.Source)||!storage.IsPhysical(name.Source)||GenerationModel.Enabled(id.Source.Extra,"isJoinColumn"))throw new InvalidOperationException("共享级联要求明确的物理标量ID和缓存。");
                        if(id.Source.Id!=control.Source.Id&&id.Source.ControlKind.Length>0&&!(id.Source.ControlKind=="CascadeTree"&&ids.Contains(id.Source.Id)))throw new InvalidOperationException("共享辅助ID有其它独立控件规则。");
                        if(id.Source.LinkConfigTableId.Length>0&&id.Source.LinkConfigTableId!=level.Target.Source.Id)throw new InvalidOperationException("共享辅助ID关联表不一致。");
                        if(level.DerivedName&&Metarill.Data.Queries.NonRedundantReads.MatchReadsTable(model.Catalog,level.Target.Source.Id,level.Name.Source.ColumnName,table.Source.TableName))throw new NotSupportedException("共享派生路径不能间接回到当前写入来源。");
                        levels.Add(new(level,id,name));var scope=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{target=level.Target.Source.Id,page=level.Page.Source.Id,name=level.Name.Source.Id,parent=level.Parent?.Source.Id,where=level.Page.Source.WhereSql}))));slots.Add(new(id.Source.ColumnName,name.Source.ColumnName,id.Type=="string?"?"string":"int64",scope));
                    }
                    var plan=new CrossTableWriteModel(definition,levels);if(CrossTableFrontendProof.Reason(plan) is{} mismatch)throw new InvalidOperationException(mismatch);plans.Add(plan);branches.Add(new(control.Source.Id,slots));
                }
                groups.Add(new(plans,new SharedCrossTableCoordinator(branches)));
            }
            catch(Exception error)when(error is InvalidOperationException or ArgumentException or NotSupportedException or JsonException){foreach(var c in component)gaps[c.Source.Id]="共享级联未通过完整证明："+error.Message;}
        }
        return new(groups,gaps,members);
    }
    /// <summary>仅识别固定结构以定位坏同伴，真正读取和存储证明仍由编译链完成。</summary>
    private static HashSet<string> Prefixes(GenerationModel model,PageModel page,ColumnModel column)
    {
        try{var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(f=>f.ConfigPageId==page.Source.Id&&f.ConfigColumnId==column.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();using var json=JsonDocument.Parse(string.IsNullOrWhiteSpace(overlay)?column.Source.ControlDetail:overlay);var root=json.RootElement;if(root.TryGetProperty("sourceMode",out var mode)&&mode.GetString()!="crossTable"||!root.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array)return new(StringComparer.Ordinal);return levels.EnumerateArray().Select(level=>level.TryGetProperty("columnPrefix",out var p)?p.GetString():level.TryGetProperty("table",out var t)?t.GetString():null).Where(p=>!string.IsNullOrWhiteSpace(p)).Select(p=>p!).ToHashSet(StringComparer.Ordinal);}
        catch(Exception error)when(error is InvalidOperationException or JsonException){return new(StringComparer.Ordinal);}
    }
}
