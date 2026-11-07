namespace Metarill.Generation;

/// <summary>生成代码只用完整实体类型，避免任意业务类名遮蔽框架、CLR或运行时类型。</summary>
internal static class RuntimeTypeBinding
{
    /// <summary>移除宽泛实体导入；保留声明、路由、文件名及已有手写文件的身份。</summary>
    internal static string BindEntities(GenerationModel model,string source)
    {
        var directive="using "+model.Namespace+".Generated.Entities;";
        // 只移除整行导入，元数据默认值或注释中的相同文字必须逐字保留。
        return string.Join("\n",source.Split('\n').Where(line=>!line.TrimEnd('\r').Equals(directive,StringComparison.Ordinal)));
    }
}
