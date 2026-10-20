namespace Metarill.Generation;

/// <summary>仅识别原账号稳定表、页与字段ID；同名业务字段不获得凭据协议特权。</summary>
internal static class ManagerCredentialCompilation
{
    internal const string TableId="6031906809708544", PageId="6031906822946816", PasswordId="1679698326770069505";
    private static readonly Dictionary<string,string> Credentials=new(){{PasswordId,"password"},{"1679698328187744257","salt"},{"1679698328317767681","old_password"}};
    /// <summary>固定表同时核对稳定ID和物理名称，同名业务表不会自动套用该协议。</summary>
    internal static bool IsTable(TableModel table)=>table.Source.Id==TableId && table.Source.TableName=="yz_manager";
    /// <summary>原页还须保持三组稳定物理字符串字段与原控件，页ID本身不能证明秘密可读。</summary>
    internal static bool IsOriginal(TableModel table,PageModel page)=>IsTable(table)&&page.Source.Id==PageId&&
        Credentials.All(pair=>table.Columns.Any(column=>column.Source.Id==pair.Key && column.Source.ColumnName==pair.Value && column.Type=="string?" &&
            column.Source.ControlKind=="" && column.Source.IsVirtualColumn==0 && !GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")));
    /// <summary>拒绝读取按固定表的物理凭据名识别，即使字段ID被改坏也不能外泄。</summary>
    internal static bool IsCredential(TableModel table,ColumnModel column)=>IsTable(table)&&Credentials.Values.Contains(column.Source.ColumnName,StringComparer.Ordinal);
    /// <summary>只认原两个废弃辅助字段，不全局剥离其他表的同名普通字段。</summary>
    internal static bool IsObsolete(ColumnModel column)=>column.Source.ConfigTableId==TableId&&Credentials.TryGetValue(column.Source.Id,out var name)&&name==column.Source.ColumnName&&column.Source.Id!=PasswordId;
    /// <summary>写入特权仍需原password稳定ID，物理秘密识别不会反向授予写权限。</summary>
    internal static bool IsPassword(TableModel table,ColumnModel column)=>IsCredential(table,column)&&column.Source.Id==PasswordId;
    /// <summary>启用翻转限原账号页的准确整数列，不改变普通checkbox或认证物理字段。</summary>
    internal static bool IsEnabled(TableModel table,PageModel page,ColumnModel column)=>IsOriginal(table,page)&&column.Source.Id=="1679698327931891714"&&column.Source.ColumnName=="is_disabled"&&column.Type=="int?";
    /// <summary>废弃摘要辅助字段保留原控件，但永不进入机器可写DTO或实体赋值。</summary>
    internal static IEnumerable<ColumnModel> Writable(TableModel table,PageModel page)=>page.Writable.Where(column=>!IsCredential(table,column)||IsOriginal(table,page)&&IsPassword(table,column));
    /// <summary>额外同表页若请求凭据，运行时明确拒绝而不是泄露或默默补成合法原页。</summary>
    internal static bool Unsupported(TableModel table,PageModel page)=>IsTable(table)&&
        ((!IsOriginal(table,page)&&page.Readable.Concat(page.Writable).Any(column=>IsCredential(table,column))) ||
         page.Searchable.Concat(page.Sortable).Concat(page.Scopes.Select(scope=>scope.Column)).Concat(GenerationModel.ValidateOrderBy(page.Source.OrderBy,table.Columns).Select(item=>item.Column)).Any(column=>IsCredential(table,column)));
    /// <summary>固定账号列表、详情及selection读取SQL都排除摘要及盐；原详情只输出与物理值无关的掩码和空别名。</summary>
    internal static string ReadProjection(TableModel table)=>"entity => new "+table.Name+" { "+string.Join(", ",table.Columns.Where(column=>!IsCredential(table,column)).Select(column=>column.Property+" = entity."+column.Property))+" }";
    /// <summary>用于固定原页，已保存的摘要从不参与VO构造。</summary>
    internal static string? Output(TableModel table,PageModel page,ColumnModel column)
        => IsCredential(table,column)?IsOriginal(table,page)?"includeCredentials ? "+(IsPassword(table,column)?"Metarill.Core.Contracts.ManagerCredentialValue.PasswordMask":"string.Empty")+" : null":"null":IsEnabled(table,page,column)?"Metarill.Core.Contracts.ManagerCredentialValue.ToEnabled(entity."+column.Property+")":null;
    /// <summary>不携带可选平台partial的BackendOnly也必须在Mapper之前明确失败。</summary>
    internal static string DigestCall(TableModel table,bool creating)=>IsTable(table)&&table.Columns.Any(column=>IsPassword(table,column)) ? "        "+(creating?"":"if (submitted.Contains(\"password\")) ")+"ManagerCredentialValue.RequireDigest(entity.Password);\n":"";
    /// <summary>新输入验证在手写钩子前执行；无partial时后置摘要证明仍会拒绝保存。</summary>
    internal static string Assignment(TableModel table,PageModel page,ColumnModel column,string access)
        => IsPassword(table,column)?"ManagerCredentialValue.Input("+access+")":IsEnabled(table,page,column)?"ManagerCredentialValue.ToDisabled("+access+")":access;
    /// <summary>排序按页面wire值表达，保留null排序语义而非简单反转方向。</summary>
    internal static string Order(TableModel table,PageModel page,ColumnModel column)=>IsEnabled(table,page,column)?"1 - entity."+column.Property:"entity."+column.Property;
    /// <summary>同表额外页不能借用途IR把物理凭据重新声明成可读依赖。</summary>
    internal static string? Gap(TableModel table,PageModel page)=>Unsupported(table,page)?"账号凭据查询声明或额外页面尚未证明，仅原固定账号页支持受限掩码协议。":null;
}
