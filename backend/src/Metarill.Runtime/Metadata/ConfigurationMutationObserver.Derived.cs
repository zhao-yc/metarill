using Metarill.Core.Persistence;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Metadata;

public sealed partial class ConfigurationMutationObserver
{
    /// <summary>页面字段只能引用同项目同表的活动页面和字段；省略外键不触碰历史缓存。</summary>
    private async Task ValidatePageColumnAsync(RecordMutationContext context,long project,CancellationToken token)
    {
        var row=await OwnedRowAsync("config_page_column",context.RecordId,project,token);
        var page=await OwnedRowAsync("config_page",SqlRowStore.Id(row,"config_page_id"),project,token);
        var layout=Metarill.Core.Metadata.DetailLayoutConfiguration.Parse(SqlRowStore.Text(page,"detail_layout"));
        layout.RequireGroup(SqlRowStore.Text(row,"detail_group_key"));
        // 沿原明确清空语义：只归一此次提交的null，不触碰其它字段或页面布局。
        if(context.SubmittedFields.ContainsKey("detail_group_key")&&row.GetValueOrDefault("detail_group_key") is null)
            if(await store.UpdateAsync("config_page_column",context.RecordId,new Dictionary<string,object?>{{"detail_group_key",""}},token)!=1)
                throw new InvalidOperationException("详情布局字段已失效。");
        var column=await OwnedRowAsync("config_column",SqlRowStore.Id(row,"config_column_id"),project,token);
        if(SqlRowStore.Id(page,"config_table_id")!=SqlRowStore.Id(column,"config_table_id"))throw new InvalidOperationException("页面字段必须引用所属表的字段。");
        await OwnedRowAsync("config_table",SqlRowStore.Id(page,"config_table_id"),project,token);
        if(context.IsCreate||context.SubmittedFields.ContainsKey("config_column_id"))
            ConfigurationMaterializedValues.RequirePageColumn(row,column);
    }

    /// <summary>改表名只同步当前目标的活动引用；先发现跨项目损坏，再按ID锁定自身引用并重验。</summary>
    private async Task SynchronizeTableNameAsync(IReadOnlyDictionary<string,object?> table,IReadOnlyList<Dictionary<string,object?>> pages,long project,CancellationToken token)
    {
        foreach(var page in pages)RequireProject(page,project);
        foreach(var discovered in pages.OrderBy(page=>SqlRowStore.Id(page)))
        {
            var page=await OwnedRowAsync("config_page",SqlRowStore.Id(discovered),project,token);
            if(SqlRowStore.Id(page,"config_table_id")!=SqlRowStore.Id(table))throw new InvalidOperationException("页面所属表已变化，请重试。");
            var patch=new Dictionary<string,object?>();ConfigurationMaterializedValues.Page(patch,table);
            if(!ConfigurationMaterializedValues.Same(page,patch)&&await store.UpdateAsync("config_page",SqlRowStore.Id(page),patch,token)!=1)throw new InvalidOperationException("同步页面表名失败。");
        }
    }

    /// <summary>只传播实际提交的名称/排序，不顺带修复未触及字段或整个历史库。</summary>
    private async Task SynchronizeColumnValuesAsync(IReadOnlyDictionary<string,object?> column,IReadOnlyList<Dictionary<string,object?>> fields,IReadOnlySet<string> changed,CancellationToken token)
    {
        if(!changed.Contains("column_name")&&!changed.Contains("sort_no"))return;
        foreach(var field in fields.OrderBy(field=>SqlRowStore.Id(field)))
        {
            var patch=new Dictionary<string,object?>();
            if(changed.Contains("column_name"))patch["config_column_column_name"]=column.GetValueOrDefault("column_name");
            if(changed.Contains("sort_no"))patch["config_column_sort_no"]=column.GetValueOrDefault("sort_no");
            if(!ConfigurationMaterializedValues.Same(field,patch)&&await store.UpdateAsync("config_page_column",SqlRowStore.Id(field),patch,token)!=1)throw new InvalidOperationException("同步页面字段派生值失败。");
        }
    }

    /// <summary>排序工作台的直接更新也复用同事务项目锁及引用校验，不借此调用完整字段修改联动。</summary>
    internal async Task SynchronizeColumnOrderDerivedAsync(long columnId,long project,CancellationToken token)
    {
        if(store.Session.CurrentTransaction is null)throw new InvalidOperationException("字段排序派生同步必须加入当前事务。");
        await LockProjectAsync(project,token);
        var column=await OwnedRowAsync("config_column",columnId,project,token);
        var fields=await ValidateColumnReferencesAsync(column,project,token);
        await SynchronizeColumnValuesAsync(column,fields,new HashSet<string>(StringComparer.Ordinal){"sort_no"},token);
    }
}

/// <summary>固定三个物理缓存的赋值；调用方必须已经证明并锁定真实同项目关联，不能使用客户端名称。</summary>
internal static class ConfigurationMaterializedValues
{
    /// <summary>新页面使用已核实目标表名，覆盖复制的历史缓存。</summary>
    internal static void Page(IDictionary<string,object?> values,IReadOnlyDictionary<string,object?> table)=>values["config_table_table_name"]=table.GetValueOrDefault("table_name");
    /// <summary>两个页面字段缓存共用已核实源字段，排序null必须保持null。</summary>
    internal static void PageColumn(IDictionary<string,object?> values,IReadOnlyDictionary<string,object?> column)
    {
        values["config_column_column_name"]=column.GetValueOrDefault("column_name");
        values["config_column_sort_no"]=column.GetValueOrDefault("sort_no");
    }
    /// <summary>创建或换外键后验证实际持久化缓存，不在这里修改写入回执。</summary>
    internal static void RequirePage(IReadOnlyDictionary<string,object?> row,IReadOnlyDictionary<string,object?> table)
    {var expected=new Dictionary<string,object?>();Page(expected,table);if(!Same(row,expected))throw new InvalidOperationException("页面表名缓存必须来自当前真实关联，不能写入客户端派生值。");}
    /// <summary>两个派生值都应与真实活动源字段一致。</summary>
    internal static void RequirePageColumn(IReadOnlyDictionary<string,object?> row,IReadOnlyDictionary<string,object?> column)
    {var expected=new Dictionary<string,object?>();PageColumn(expected,column);if(!Same(row,expected))throw new InvalidOperationException("页面字段缓存必须来自当前真实关联，不能写入客户端派生值。");}
    /// <summary>保留真正null并兼容数据库整数包装类型；空字符串不与null等价。</summary>
    internal static bool Same(IReadOnlyDictionary<string,object?> row,IReadOnlyDictionary<string,object?> expected)=>expected.All(pair=>
        row.TryGetValue(pair.Key,out var value)&&(value is null||pair.Value is null?value is null&&pair.Value is null:
        string.Equals(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture),Convert.ToString(pair.Value,System.Globalization.CultureInfo.InvariantCulture),StringComparison.Ordinal)));
}
