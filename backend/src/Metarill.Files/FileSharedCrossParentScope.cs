using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Metarill.Runtime.Persistence;
namespace Metarill.Runtime.Files;

/// <summary>共享文件父范围冻结全部同伴；只读取真实独立末端，一次合并当前实际祖先。</summary>
internal sealed class FileSharedCrossParentScope
{
    private readonly SharedCrossTableSparseSelection selection;
    private readonly IReadOnlyDictionary<string,FileCrossParentScope> branches;
    internal IReadOnlyList<string> StorageNames{get;}
    internal bool TextKeyAt(int index)=>selection.TextKeySlots.Contains(StorageNames[index]);
    /// <summary>共同位的每个分支均须绑定同一冻结页和名称，不能借一个好同伴补另一分支的范围。</summary>
    internal bool CoversTarget(string column,string table,string page,string name)
    {
        var matches=selection.Branches.SelectMany(branch=>branch.Slots.Select((slot,index)=>(branch.SourceColumnId,slot,index))).Where(item=>item.slot.ColumnName==column).ToArray();return matches.Length>0&&matches.All(item=>branches[item.SourceColumnId].CoversTargetAt(item.index,table,page,name));
    }
    /// <summary>组及分支已经编译，调用者不能在请求期间替换目录或路径。</summary>
    private FileSharedCrossParentScope(SharedCrossTableSparseSelection selection,IReadOnlyDictionary<string,FileCrossParentScope> branches){this.selection=selection;this.branches=branches;StorageNames=Array.AsReadOnly(selection.Slots.Select(s=>s.ColumnName).ToArray());}
    /// <summary>收齐每个同伴的目标页和固定条件后执行数据层完整证明；坏同伴使整个共享父范围拒绝。</summary>
    internal static FileCrossParentScope Compile(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata column,SharedCrossTableSparseSelection selection)
    {
        if(GlobalCrossTableTarget.Declared(table))throw new NotSupportedException("共享文件父范围要求普通项目来源。");var branches=new Dictionary<string,FileCrossParentScope>(StringComparer.Ordinal);var scopes=new List<NonRedundantCrossTableScope>();
        var allowExact=AllowsExactSource(catalog,table,page);
        var textKeys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var branch in selection.Branches)
        {
            var source=catalog.ConfigColumns.Single(c=>c.ConfigTableId==table.Id&&c.Id==branch.SourceColumnId);
            var compiled=FileCrossParentScope.CompileBranch(catalog,table,page,source,selection.BranchSelection(source.Id),0,
                allowExactSource:allowExact,allowTextKeys:true);
            branches.Add(source.Id,compiled.Scope);scopes.Add(compiled.Binding);
            for(var level=0;level<branch.Slots.Count;level++)
                if(compiled.Scope.TextKeyAt(level))textKeys.Add(branch.Slots[level].ColumnName);
        }
        _=NonRedundantReads.CompileWithCrossTables(catalog,table.Id,["id"],scopes,selection.Slots.Select(s=>s.ColumnName).ToArray());
        var plan=new FileSharedCrossParentScope(textKeys.Count==0?selection:selection.WithTextKeys(textKeys),branches);var index=plan.StorageNames.ToList().IndexOf(column.ColumnName);if(index<0)throw new InvalidOperationException("文件父字段不属于固定共享组。");return new FileCrossParentScope(selection.Branches[0].SourceColumnId,index,plan,scopes.AsReadOnly());
    }
    /// <summary>逻辑父范围可与同页已证明的物理、联接或级联标量叠加；无发布后台的声明仍拒绝。</summary>
    private static bool AllowsExactSource(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
    {
        if(!page.Extra.TryGetValue("exactDataManageMatchField",out var exact)||exact.ValueKind!=JsonValueKind.String||
            ExactDataManageValues.Field(exact.GetString()) is not {Length:>0} field||
            !catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId))return false;
        var column=FileMetadataContext.ResolveExactColumn(catalog,table,field);
        if(!FileReferenceReadAuthorization.IsScalarExactField(column))return false;
        // 与实际授权使用相同的逻辑类型转换，编译阶段不读取账号值。
        _=ExactDataManageValues.Unrestricted.ReadLogical(column.LogicalType);
        if(NonRedundantReads.SharedExactGroup(catalog,table,column.ColumnName) is not null)return true;
        if(NonRedundantReads.IndependentCrossExact(catalog,table,column.ColumnName) is {} independent)
        {
            var binding=FileMetadataContext.CompileExactBranchBinding(catalog,table,page,independent);
            var match=NonRedundantReads.CompileCrossExactMatch(catalog,table.Id,column.ColumnName,[binding]);
            return match.OutputTypes[column.ColumnName]==column.LogicalType;
        }
        var declared=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(item=>item.ConfigTableId==table.Id));
        if(CascadeTreeStorageColumns.IsLogicalNameField(table,declared,column.ColumnName))
        {
            var match=NonRedundantReads.CompileSelfTreeExactMatch(catalog,table.Id,page.Id);
            return match.OutputTypes[column.ColumnName]==column.LogicalType;
        }
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        if(storage.Kind(column)==MetadataFieldStorage.Join)
            return NonRedundantReads.CompileMatch(catalog,table.Id,column.ColumnName).OutputTypes[column.ColumnName]==column.LogicalType;
        return storage.IsPhysical(column);
    }

    /// <summary>在同一读快照／写事务内检查全部已存末端，拒绝实际祖先冲突；共同物理位只读取一次。</summary>
    internal async Task<string[]> PathKeysAsync(SqlRowStore store,IReadOnlyDictionary<string,object?> record,RuntimeActor actor,bool locking,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();IReadOnlyDictionary<string,CrossTableSelectedKey?> selected;
        try{selected=selection.ReadBranchKeys(selection.Slots.ToDictionary(s=>s.ColumnName,s=>record.TryGetValue(s.ColumnName,out var v)?v:throw new InvalidOperationException("缺少实际共享稀疏位。")));}
        catch(Exception invalid)when(invalid is InvalidOperationException or ArgumentException){throw new UnauthorizedAccessException("文件来源共享稀疏选择无效。",invalid);}
        var values=Enumerable.Repeat("",selection.Slots.Count).ToArray();var visited=new HashSet<string>(StringComparer.Ordinal);
        foreach(var branch in selection.Branches)
        {
            if(selected[branch.SourceColumnId] is not{} node||!visited.Add(branch.Slots[node.Level].ColumnName))continue;var actual=await branches[branch.SourceColumnId].PathKeysAsync(store,record,actor,locking,token);
            for(var level=0;level<actual.Length;level++){var index=StorageNames.ToList().IndexOf(branch.Slots[level].ColumnName);if(values[index].Length>0&&values[index]!=actual[level])throw new UnauthorizedAccessException("文件来源共享末端的实际祖先冲突。");values[index]=actual[level];}
        }
        if(visited.Count==0)throw new UnauthorizedAccessException("文件来源没有真实共享父选择。");return values;
    }
}
