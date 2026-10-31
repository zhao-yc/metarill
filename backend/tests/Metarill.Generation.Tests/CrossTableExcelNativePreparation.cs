using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
namespace Metarill.Generation.Tests;

/// <summary>八种键/存储组合共享一个专属模型，基础表只安装一次；不连接数据库或创建账号。</summary>
internal static partial class CrossTableExcelNativePreparation
{
    internal const string RootNamespace="Metarill.CrossExcelNative",ProductCommit="bb8a410ba8cba0a00aea1d2a59cc40e6cdab5eb7";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    /// <summary>每个组的表/字段/页面ID独立，项目/后台共用；整个普通矩阵可在同一新库验收并清理。</summary>
    internal static MetadataCatalog Catalog()
    {
        var result=new MetadataCatalog{YzProjectId="9007199254740993",YzProjectName="Metarill级联Excel验收"};
        result.YzAdminTypes.Add(new(){Id="5",YzProjectId=result.YzProjectId,Name="验收后台"});
        var index=0;
        foreach(var redundant in new[]{0,1})foreach(var sourceType in new[]{"string","int64"})foreach(var parentType in new[]{"string","int64"})
        {
            var profile="r"+redundant+"_"+(sourceType=="string"?"s":"l")+(parentType=="string"?"s":"l");var prefix="cx_"+profile;
            var catalog=CrossTableExcelImportColumnsTests.Catalog(prefix,sourceType);
            catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=redundant;
            foreach(var column in catalog.ConfigColumns.Where(column=>column.Id is "45" or "55"))column.LogicalType=parentType;
            var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsNew=1;page.IsSave=1;page.IsCopy=1;page.Extra.Remove("type");
            catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").IsShow=1;
            catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="country_id",Name="祖先ID",LogicalType=sourceType,LinkConfigTableId="3",SortNo=30});
            catalog.ConfigPageColumns.Add(new(){Id="20128",ConfigPageId="201",ConfigColumnId="28",IsShow=0,IsEdit=1,SortNo=30});
            catalog.ConfigColumns.Add(new(){Id="9001",ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"accept\":\"*\",\"maxCount\":3}",SortNo=10000});
            catalog.ConfigPageColumns.Add(new(){Id="2019001",ConfigPageId="201",ConfigColumnId="9001",IsShow=1,IsEdit=1,SortNo=10000});
            foreach(var target in catalog.ConfigTables.Where(table=>table.Id!="2"))
            {
                var targetPage=catalog.ConfigPages.Single(p=>p.ConfigTableId==target.Id);
                var status=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="status");
                catalog.ConfigPageColumns.Add(new(){Id=targetPage.Id+"status",ConfigPageId=targetPage.Id,ConfigColumnId=status.Id,IsShow=1,IsEdit=1,SortNo=100});
            }
            // 本矩阵只有已知普通表/字段/页面，不静默丢弃以后新增的索引、子页或按钮声明。
            if(catalog.ConfigIndexes.Count!=0||catalog.ConfigIndexColumns.Count!=0||catalog.ConfigChildren.Count!=0||catalog.ConfigButtons.Count!=0||catalog.ConfigCustomPages.Count!=0||catalog.ConfigMenus.Count!=0||catalog.YzAdminTypes.Count!=0)
                throw new InvalidOperationException("八模型重编号发现未知目录，不能静默忽略。");
            var basis=81000000L+(++index)*10000;
            var tables=Map(catalog.ConfigTables,basis+1000);var columns=Map(catalog.ConfigColumns,basis+2000);var pages=Map(catalog.ConfigPages,basis+3000);
            foreach(var record in catalog.ConfigTables){record.Id=tables[record.Id];record.YzProjectId=result.YzProjectId;}
            foreach(var record in catalog.ConfigColumns){record.Id=columns[record.Id];record.ConfigTableId=tables[record.ConfigTableId];if(record.LinkConfigTableId.Length>0)record.LinkConfigTableId=tables[record.LinkConfigTableId];record.YzProjectId=result.YzProjectId;}
            foreach(var record in catalog.ConfigPages){record.Id=pages[record.Id];record.ConfigTableId=tables[record.ConfigTableId];record.YzAdminTypeId="5";record.YzProjectId=result.YzProjectId;record.Name=profile+" "+record.ConfigTableId;}
            for(var field=0;field<catalog.ConfigPageColumns.Count;field++)
            {var record=catalog.ConfigPageColumns[field];record.Id=(basis+4000+field).ToString(CultureInfo.InvariantCulture);record.ConfigPageId=pages[record.ConfigPageId];record.ConfigColumnId=columns[record.ConfigColumnId];record.YzProjectId=result.YzProjectId;}
            result.ConfigTables.AddRange(catalog.ConfigTables);result.ConfigColumns.AddRange(catalog.ConfigColumns);result.ConfigPages.AddRange(catalog.ConfigPages);result.ConfigPageColumns.AddRange(catalog.ConfigPageColumns);
            for(var item=0;item<catalog.ConfigPages.Count;item++)
                result.ConfigMenus.Add(new(){Id=(basis+5000+item).ToString(CultureInfo.InvariantCulture),YzProjectId=result.YzProjectId,YzAdminTypeId="5",Name=profile+" "+item,Kind="GENERATED_PAGE",ConfigPageId=catalog.ConfigPages[item].Id,SortNo=index*10+item});
        }
        return result;
    }
    /// <summary>使用记录次序的独立数值空间，不依赖旧夹具里可能带文字的配置ID。</summary>
    private static Dictionary<string,string> Map<T>(IReadOnlyList<T> records,long basis)where T:MetadataRecord
        =>records.Select((record,index)=>(record.Id,Value:(basis+index).ToString(CultureInfo.InvariantCulture))).ToDictionary(pair=>pair.Id,pair=>pair.Value,StringComparer.Ordinal);
    /// <summary>只在新的明确目录准备；关闭发布资格的标记不能被普通更新器使用。</summary>
    internal static PreparedCrossTableExcelApplication Create(string sourceRoot,string output)
    {
        var catalog=Catalog();var result=new CrossTableExcelApplicationPreparer().Prepare(catalog,output,RootNamespace,sourceRoot);
        var generated=CSharpGenerator.SafePath(output,"backend/generated");var hands=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach(var table in catalog.ConfigTables.Where(table=>table.TableName.EndsWith("_source",StringComparison.Ordinal)))
        {
            var path=CSharpGenerator.SafePath(generated,"Services/"+GenerationModel.Pascal(table.TableName)+"Service.cs");
            File.WriteAllText(path,Handwritten(table.TableName));hands[path]=File.ReadAllBytes(path);
        }
        for(var round=0;round<2;round++)
        {
            new CSharpGenerator().Generate(catalog,generated,RootNamespace);
            if(hands.Any(file=>!File.ReadAllBytes(file.Key).SequenceEqual(file.Value)))throw new InvalidOperationException("跨表Excel验收手写规则被再生成覆盖。");
        }
        return result;
    }
}
