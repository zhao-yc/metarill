namespace Metarill.Generation;

/// <summary>多值名称物理缓存只允许同一来源ID与目标标量，不能借联接标记放行任意派生写入。</summary>
internal static class MultiNameStorageCompilation
{
    /// <summary>普通名称列沿既有合同；联接列必须声明同一来源别名的原两段路径。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,ColumnModel source,ColumnModel value,ColumnModel cache)
    {
        if(table.Source.IsRongyu!=1||source.Source.ConfigTableId!=table.Source.Id||cache.Source.ConfigTableId!=table.Source.Id||cache.Source.Id==source.Source.Id||cache.Type!="string?"||cache.IsSystem||cache.Source.IsVirtualColumn!=0||!NonRedundantReadCompilation.Physical(model,table,cache))return false;
        var stem=source.Source.ColumnName.EndsWith("_ids",StringComparison.Ordinal)?source.Source.ColumnName[..^4]:"";
        var field=value.Source.ColumnName;var expected=stem+"_"+(field.EndsWith('s')?field:field+"s");
        if(stem.Length==0||cache.Source.ColumnName!=expected||cache.Source.ControlKind is not("" or "TextArea")||cache.Source.LinkConfigTableId.Length>0||cache.Source.Extra.GetValueOrDefault("joinTable").ToString().Length>0||cache.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0)return false;
        if(!GenerationModel.Enabled(cache.Source.Extra,"isJoinColumn"))return true;
        var path=cache.Source.Extra.GetValueOrDefault("columnFullName");
        return path.ValueKind==System.Text.Json.JsonValueKind.String&&(path.GetString()==stem+"."+field||path.GetString()==source.Source.ColumnName+"."+field);
    }
}
