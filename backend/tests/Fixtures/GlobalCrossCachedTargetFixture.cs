using System.Text.Json;
using Metarill.Core.Metadata;
namespace Metarill.Testing;

/// <summary>Data和生成测试共用同一元数据来源；只定义表/页/关系，不写实体、CRUD或数据库。</summary>
internal static class GlobalCrossCachedTargetFixture
{
    /// <summary>根/中/末可分别作为无项目列全局缓存生产者，其它两级保留普通项目或显式全局范围。</summary>
    internal static MetadataCatalog Create(int mode,int cachedRole,string key,string parent,bool allGlobal=false)
    {
        if(mode is not(0 or 1)||cachedRole is <0 or >2||key is not("string" or "int64")||parent is not("string" or "int64"))throw new ArgumentException("测试模型不在固定范围。");
        var c=new MetadataCatalog{YzProjectId="100",YzProjectName="全局缓存名称级联",YzAdminTypes=[new(){Id="9",Name="测试后台",YzProjectId="100"}]};
        foreach(var(id,name,label,global)in new[]{("1","cached_global","全局缓存角色",true),("2","lookup","共享字典",true),("3","cross_consumer","消费记录",false),("4","cross_other_a","其它角色甲",allGlobal),("5","cross_other_b","其它角色乙",allGlobal)})c.ConfigTables.Add(new(){Id=id,TableName=name,Name=label,YzProjectId="100",IsRongyu=id=="3"?mode:1,Extra=global?new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}:[]});
        foreach(var table in c.ConfigTables)
        {
            Add(table.Id+"01",table.Id,"id","ID","int64",required:true);Add(table.Id+"02",table.Id,"delete_no","删除标记","datetime");
            if(!GlobalCrossTableTarget.Declared(table))Add(table.Id+"03",table.Id,"yz_project_id","项目","int64");
            if(table.Id!="3")Add(table.Id+"04",table.Id,"status","状态","int32");
            if(table.Id!="1")Add(table.Id+"05",table.Id,"name","名称","string");
        }
        Add("113","1","lookup_id","字典",parent,"List","2","{\"tableId\":\"2\",\"table\":\"lookup\",\"pageId\":\"20\",\"nameField\":\"name\"}");
        var cache=Add("114","1","lookup_name","缓存名称","string");cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("lookup.name");
        var roles=new List<string>{"4","5"};roles.Insert(cachedRole,"1");var prefixes=new[]{"country","area","place"};
        for(var i=1;i<3;i++)Add("p"+i,roles[i],prefixes[i-1]+"_id","上级"+i,parent,link:roles[i-1]);
        var levels=roles.Select((id,i)=>new{key=prefixes[i],columnPrefix=prefixes[i],table=c.ConfigTables.Single(t=>t.Id==id).TableName,nameField=id=="1"?"lookup_name":"name",parentField=i==0?"":prefixes[i-1]+"_id"}).ToArray();
        Add("334","3","place_id","区域",key,"CascadeTree",roles[2],JsonSerializer.Serialize(new{type="crossTable",joiner="/",levels}));
        var file=Add("339","3","attachments","附件","string","FileLibrary");file.ColumnLength="max";file.ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}";
        foreach(var table in c.ConfigTables)
        {
            var page=new PageMetadata{Id=table.Id+"0",ConfigTableId=table.Id,YzProjectId="100",YzAdminTypeId="9",Name=table.Name,IsSave=1,IsCopy=1,IsImport=1,IsExport=1,WhereSql=table.Id=="3"?"yz_project_id=@current_yz_project_id":"status=1"};c.ConfigPages.Add(page);
            foreach(var col in c.ConfigColumns.Where(x=>x.ConfigTableId==table.Id&&x.ColumnName is not("id" or "delete_no" or "yz_project_id")))c.ConfigPageColumns.Add(new(){Id=page.Id+"_"+col.Id,YzProjectId="100",ConfigPageId=page.Id,ConfigColumnId=col.Id,IsShow=1,IsEdit=col.Id=="114"?0:1,Extra=col.Id=="334"?new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}:[]});
            c.ConfigMenus.Add(new(){Id="700"+page.Id,YzProjectId="100",YzAdminTypeId="9",Name=page.Name,Kind="GENERATED_PAGE",ConfigPageId=page.Id});
        }
        c.ConfigButtons=[JsonSerializer.SerializeToElement(new{id="8001",name="维护表",configPageId="30",configTableId="3",yzProjectId="100",actionType=1,type=0,sql="/admin/sys/system_maintenance/tables",visibleScheme="Normal",place="List",saveOption="NotSave",afterAction="Refresh"}),JsonSerializer.SerializeToElement(new{id="8002",name="回填名称",configPageId="30",configTableId="3",yzProjectId="100",actionType=1,type=0,sql="/admin/sys/system_maintenance/backfill_join_columns",visibleScheme="Normal",place="List",saveOption="NotSave",afterAction="Refresh"})];return c;
        /// <summary>元数据是唯一来源，字段及页面只由原生成器生成。</summary>
        ColumnMetadata Add(string id,string table,string name,string label,string logical,string kind="",string link="",string detail="",bool required=false)
        {var col=new ColumnMetadata{Id=id,ConfigTableId=table,YzProjectId="100",ColumnName=name,Name=label,LogicalType=logical,ColumnLength=logical=="string"?"100":"",ControlKind=kind,LinkConfigTableId=link,ControlDetail=detail,IsDbNotNull=required?1:0};c.ConfigColumns.Add(col);return col;}
    }
    /// <summary>读取固定输入的三层角色，不从字段名重新推断目标。</summary>
    internal static JsonElement[] Levels(MetadataCatalog catalog)
    {using var json=JsonDocument.Parse(catalog.ConfigColumns.Single(x=>x.Id=="334").ControlDetail);return json.RootElement.GetProperty("levels").EnumerateArray().Select(x=>x.Clone()).ToArray();}
}
