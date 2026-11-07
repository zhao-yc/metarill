using System.Collections.ObjectModel;
using Metarill.Core.Metadata;
namespace Metarill.Data.Queries;

/// <summary>共享稀疏位先编译全部独立路径，再合并共同祖先；不把其它分支的空位当作未选择。</summary>
public static partial class MySqlNonRedundantReadCompiler
{
    private sealed partial class Builder
    {
        private readonly Dictionary<string,IReadOnlyDictionary<string,SharedCrossTableSparseSelection>> sharedSelections=new(StringComparer.Ordinal);
        private readonly Dictionary<string,IReadOnlyDictionary<string,(string Sql,string Type)>> sharedCrossValues=new(StringComparer.Ordinal);
        /// <summary>每个发布表只编译一次存储组，单控件和独立组继续使用旧计划。</summary>
        private SharedCrossTableSparseSelection? SharedCrossGroup(TableMetadata table,string source)
        {
            if(!sharedSelections.TryGetValue(table.Id,out var groups)){groups=new ReadOnlyDictionary<string,SharedCrossTableSparseSelection>(SharedCrossTableSparseSelection.CompileAll(table,columns[table.Id].Values).SelectMany(g=>g.Branches.Select(b=>(b.SourceColumnId,Group:g))).ToDictionary(x=>x.SourceColumnId,x=>x.Group,StringComparer.Ordinal));sharedSelections.Add(table.Id,groups);}return groups.GetValueOrDefault(source);
        }
        /// <summary>显式绑定所有同伴范围，冲突值使计数和每行读取均失败，不静默选COALESCE的第一个来源。</summary>
        private (string Sql,string Type) SharedCrossValue(TableMetadata table,string owner,SharedCrossTableSparseSelection group,string name)
        {
            origin=null;var groupKey=owner+":shared:"+group.Branches[0].SourceColumnId;if(!sharedCrossValues.TryGetValue(groupKey,out var merged))
            {
                var scopes=new Dictionary<string,NonRedundantCrossTableTargetScope>(StringComparer.Ordinal);
                foreach(var branch in group.Branches)
                {
                    if(!crossTables.TryGetValue(branch.SourceColumnId,out var binding)||binding.Levels.Count!=branch.Slots.Count)throw new NotSupportedException("共享逻辑读取要求全部同伴完整固定的目标页范围。");var control=columns[table.Id].Values.Single(c=>c.Id==branch.SourceColumnId);var levels=Definition(control).GetProperty("levels").EnumerateArray().ToArray();
                    for(var index=0;index<branch.Slots.Count;index++)
                    {
                        var target=tables.Values.Single(t=>t.TableName==levels[index].GetProperty("table").GetString());if(target.Id==root.Id)throw new NotSupportedException("共享稀疏读取的来源自参与尚需独立证明。");var slot=branch.Slots[index];var scope=binding.Levels[index];
                        // 全局目标沿用单链发布／固定条件证明，来源项目与共享同伴页面仍分别核验。
                        if(GlobalCrossTableTarget.Declared(target)){var page=catalog.ConfigPages.SingleOrDefault(p=>p.Id==scope.PageId&&p.ConfigTableId==target.Id)??throw new InvalidOperationException("共享全局目标页与实际目标表不符。");GlobalCrossTableTarget.Require(catalog,target,page);}if(scopes.TryGetValue(slot.ColumnName,out var old)&&(old.PageId!=scope.PageId||!SameSharedCondition(old.Condition,scope.Condition)))throw new InvalidOperationException("共享稀疏同一槽的候选页或固定条件不一致。");scopes[slot.ColumnName]=scope;
                    }
                    _=CrossValue(table,owner,control,group.BranchSelection(branch.SourceColumnId),binding,branch.Slots[^1].ColumnName);
                }
                var values=group.Branches.Select(b=>crossValues[owner+":"+b.SourceColumnId]).ToArray();var clauses=new List<string>();
                foreach(var slot in group.Slots)
                {
                    var peers=values.Where(v=>v.ContainsKey(slot.ColumnName)).Select(v=>v[slot.ColumnName].Sql).ToArray();for(var i=0;i<peers.Length;i++)for(var j=i+1;j<peers.Length;j++)clauses.Add("(("+peers[i]+") IS NULL OR ("+peers[j]+") IS NULL OR BINARY CAST(("+peers[i]+") AS CHAR)=BINARY CAST(("+peers[j]+") AS CHAR))");
                }
                var result=new Dictionary<string,(string Sql,string Type)>(StringComparer.OrdinalIgnoreCase);
                foreach(var field in group.Slots.SelectMany(s=>new[]{s.ColumnName,s.ColumnName[..^3]+"_name"}))
                {
                    var peers=values.Where(v=>v.ContainsKey(field)).Select(v=>v[field]).ToArray();if(peers.Select(p=>p.Type).Distinct(StringComparer.Ordinal).Count()!=1)throw new InvalidOperationException("共享逻辑字段类型不一致。");var sql=peers.Length==1?peers[0].Sql:"COALESCE("+string.Join(",",peers.Select(p=>"("+p.Sql+")"))+")";result.Add(field,(sql,peers[0].Type));
                }
                var keys=group.Branches.Select(b=>owner+":"+b.SourceColumnId).ToArray();var validity=string.Join(" AND ",keys.Select(k=>"("+crossProjections[k].Validity+")").Concat(clauses));for(var index=0;index<keys.Length;index++){var projection=crossProjections[keys[index]];crossProjections[keys[index]]=projection with{SharedGroupKey=groupKey,RequireRowValidity=index==0,Validity=index==0?validity:projection.Validity};}
                merged=new ReadOnlyDictionary<string,(string Sql,string Type)>(result);sharedCrossValues.Add(groupKey,merged);
            }
            origin=null;return merged[name];
        }
        /// <summary>同一共享槽不合并不同的读取范围；空计划和未指定计划也保持明确区别。</summary>
        private static bool SameSharedCondition(FixedConditionSqlPlan? left,FixedConditionSqlPlan? right)=>left is null?right is null:right is not null&&left.SameConstants(right);
    }
}
