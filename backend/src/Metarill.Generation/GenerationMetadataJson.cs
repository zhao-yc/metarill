using System.Text.Json;
namespace Metarill.Generation;

/// <summary>固定元数据模型拒绝大小写别名；嵌套业务字典按JSON的大小写敏感语义保留。</summary>
internal static class GenerationMetadataJson
{
    private static readonly HashSet<string> ModelCollections=new(StringComparer.OrdinalIgnoreCase)
    {"yzAdminTypes","configTables","configColumns","configPages","configPageColumns","configMenus","configChildren","configButtons","configCustomPages","configIndexes","configIndexColumns","files","requiredBackendAdapters","backendGaps"};

    /// <summary>只把顶层已知集合内的对象当元数据模型，不将业务字典的A/a误判为协议别名。</summary>
    internal static void RequireUnique(JsonElement value,bool model=true,int depth=0)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(model?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject())
            {
                if(!names.Add(property.Name))throw new InvalidOperationException("元数据JSON包含重复或大小写冲突字段。");
                RequireUnique(property.Value,depth==0&&ModelCollections.Contains(property.Name),depth+1);
            }
        }
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())RequireUnique(item,model,depth+1);
    }
}
