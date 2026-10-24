using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

/// <summary>仅创建新映射时沿用原页面字段默认值，不修改既有页覆盖或复制配置。</summary>
internal static class ConfigurationPageFieldDefaults
{
    /// <summary>显示/编辑由创建入口确定；联接列只读、排序列默认99，其余覆盖不从源字段继承。</summary>
    internal static Dictionary<string,object?> BuildPageField(long id,IReadOnlyDictionary<string,object?> page,IReadOnlyDictionary<string,object?> column,long project,bool show)
    {
        var sort=SqlRowStore.Text(column,"column_name")=="sort_no";
        var values=new Dictionary<string,object?>
        {
            ["id"]=id,["config_page_id"]=SqlRowStore.Id(page),["config_column_id"]=SqlRowStore.Id(column),
            ["name"]=SqlRowStore.Text(column,"name"),["yz_project_id"]=project,
            ["is_in_grid"]=show?1:0,["is_in_edit"]=show?1:0,["is_read_only"]=SqlRowStore.Id(column,"is_join_column")==1?1:0,
            ["is_important"]=0,["is_in_filter"]=0,["default_value"]=sort?"99":"",["is_auto_focus"]=0,
            ["sum_kind"]=0,["is_must"]=sort?1:0,["is_print"]=0,["is_auto_save_after_change"]=0
        };
        // 原排序缓存属于真实源列，null不是0；物理列名也只由同表已验证来源写入。
        ConfigurationMaterializedValues.PageColumn(values,column);
        return values;
    }
}
