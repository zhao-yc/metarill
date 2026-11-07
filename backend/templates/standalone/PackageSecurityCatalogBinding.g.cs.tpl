namespace {{NAMESPACE}}.Host;

/// <summary>由配置生成按钮与project.json一起生成，摘要在编译时成为运行时常量，不从manifest读取。</summary>
internal static class PackageSecurityCatalogBinding
{
    internal const string Sha256="{{PACKAGE_CATALOG_SHA256}}";
}
