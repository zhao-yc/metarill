using Metarill.Core.Metadata;
using System.Text.Json.Nodes;
namespace Metarill.Generation.Tests;

/// <summary>八个正式模型分别使用独占空库；字符串键使用自动名称，整数键使用显式名称。</summary>
internal sealed record GlobalArrayTargetNativeModel(int TargetMode,int SourceMode,string InnerType)
{
    internal bool Automatic=>InnerType=="string";
    internal string Suffix=>$"array_t{TargetMode}_s{SourceMode}_{(Automatic?"text":"long")}";
    internal int Worker=>900+TargetMode*4+SourceMode*2+(Automatic?1:0);
    internal static IReadOnlyList<GlobalArrayTargetNativeModel> All{get;}=new[]{0,1}.SelectMany(target=>new[]{0,1}.SelectMany(source=>new[]{"int64","string"}.Select(inner=>new GlobalArrayTargetNativeModel(target,source,inner)))).ToArray();
    /// <summary>只接受完整矩阵中的固定模型，不能由外部字符串派生新库范围。</summary>
    internal void Validate(){if(TargetMode is not(0 or 1)||SourceMode is not(0 or 1)||InnerType is not("int64" or "string"))throw new InvalidOperationException("全局数组正式模型不在固定八组合内。");}
    /// <summary>沿原完整数组目录切换真实生产者模式和内层物理外键，不替换任何原控件。</summary>
    internal MetadataCatalog Catalog()
    {
        Validate();var c=GlobalMultipleCachedTargetGenerationTests.Catalog(SourceMode,Automatic);c.ConfigTables.Single(table=>table.Id=="1").IsRongyu=TargetMode;c.ConfigColumns.Single(field=>field.Id=="13").LogicalType=InnerType;
        // 复选保留自己的原候选条件；不能把type=2目标页的status条件偷偷当作复选条件。
        var checkbox=c.ConfigColumns.Single(field=>field.Id=="38");var detail=JsonNode.Parse(checkbox.ControlDetail)!.AsObject();detail["whereSql"]="status=1";checkbox.ControlDetail=detail.ToJsonString();return c;
    }
}
