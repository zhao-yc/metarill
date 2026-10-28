"""仅把固定V8离线样板及正式DDL转换为C#性能夹具元数据；不是旧项目迁移器，不执行SQL。"""
from __future__ import annotations
import argparse
import copy
import hashlib
import json
import pathlib
import re


class BaselineError(ValueError):
    """同模型证明不充分，不能猜测或补造业务配置。"""


def identity(value):
    """来源标识必须原为十进制字符串，避免先经浮点数丢失精度。"""
    if not isinstance(value, str) or not re.fullmatch(r"[0-9]{1,19}", value) or int(value) > 9223372036854775807:
        raise BaselineError("来源ID必须为int64范围的十进制字符串")
    return value


def parts(text):
    """仅分割括号之外的DDL逗号；原生成器不产生字符串常量定义。"""
    result, start, level = [], 0, 0
    for index, value in enumerate(text):
        if value == "(": level += 1
        if value == ")": level -= 1
        if level < 0: raise BaselineError("DDL括号不匹配")
        if value == "," and level == 0:
            result.append(text[start:index].strip()); start = index + 1
    if level: raise BaselineError("DDL括号不匹配")
    result.append(text[start:].strip())
    return result


def parse_schema(text):
    """只读本样板生成器的CREATE语法，未知语法拒绝，不把任意SQL当迁移脚本。"""
    text = re.sub(r"(?m)^--[^\n]*(?:\n|$)", "", text).strip()
    pattern = re.compile(r"CREATE TABLE `__YZP_SAMPLE_DATABASE__`\.`([a-z][a-z0-9_]*)` \((.*?)\);", re.S)
    tables, consumed = {}, []
    for match in pattern.finditer(text):
        name = match[1]
        if name in tables: raise BaselineError("重复DDL表")
        columns, indexes, primary = {}, [], False
        for fragment in parts(match[2]):
            column = re.fullmatch(r"`([a-z][a-z0-9_]*)` (BIGINT(?:\(20\))?|varchar\([0-9]+\)|DECIMAL\([0-9]+,[0-9]+\)|LONGTEXT|DATETIME) (NOT NULL|NULL)(?: DEFAULT (NULL|now\(\)))?", fragment, re.I)
            if column:
                if column[1] in columns: raise BaselineError("重复DDL列")
                columns[column[1]] = {"type":column[2].lower(),"nullable":column[3].upper() == "NULL","default":column[4]}
                continue
            index = re.fullmatch(r"(UNIQUE|index) ([a-z][a-z0-9_]*) \(([a-z][a-z0-9_]*)\)", fragment, re.I)
            if index:
                indexes.append({"name":index[2],"column":index[3],"unique":index[1].upper() == "UNIQUE"}); continue
            if fragment == 'PRIMARY KEY (`id`)' and not primary:
                primary = True; continue
            raise BaselineError("样板DDL存在未证明的列/索引语法")
        if not primary or columns.get("id",{}).get("type") not in ("bigint", "bigint(20)") or columns["id"]["nullable"]:
            raise BaselineError("样板必须有非空bigint主键")
        if any(index["column"] not in columns for index in indexes): raise BaselineError("索引引用缺失列")
        tables[name] = {"columns":columns,"indexes":indexes}; consumed.append(match.span())
    for first,last in reversed(consumed): text=text[:first]+text[last:]
    if text.strip() or not tables: raise BaselineError("DDL有非样板CREATE内容或为空")
    return tables


def physical(column):
    """保留Java真实类型和长度；datetime精度/默认值差异另记，不能静默宣称相同结构。"""
    kind=column["type"]
    if kind.startswith("bigint"): result={"logicalType":"int64","columnType":"bigint","columnLength":""}
    elif kind.startswith("varchar"): result={"logicalType":"string","columnType":"nvarchar","columnLength":re.search(r"[0-9]+",kind)[0]}
    elif kind=="longtext": result={"logicalType":"string","columnType":"nvarchar","columnLength":"max"}
    elif kind=="datetime": result={"logicalType":"datetime","columnType":"datetime","columnLength":""}
    elif kind.startswith("decimal"):
        precision,scale=map(int,re.findall(r"[0-9]+",kind)); result={"logicalType":"decimal","columnType":"decimal","columnLength":f"{precision},{scale}","precision":precision,"scale":scale}
    else: raise BaselineError("未知物理类型")
    return {**result,"isDbNotNull":0 if column["nullable"] else 1,"defaultValue":""}


def merged(source, key, defaults):
    """显式展开原样板默认值，null扩展值不流入C#非空标量。"""
    values=source[key]
    if not isinstance(values,list): raise BaselineError("样板数组缺失")
    return [{k:copy.deepcopy(v) for k,v in {**source.get(defaults,{}),**row}.items() if v is not None} for row in values]


def synthetic(table_id, name):
    """物理内建列获得稳定夹具ID；不修改Java原字段ID或占用运行时保留前缀。"""
    return str(900000000000000000 + int(hashlib.sha256((table_id+":"+name).encode()).hexdigest()[:15],16)%90000000000000000)


def convert(source, schema):
    """保持样板全部业务字段/页面/按钮/关系；只做元数据表示转换，不移除难测能力。"""
    if source.get("schemaVersion") != 1: raise BaselineError("只支持固定V8样板格式版本1")
    project=source["project"]; project_id=identity(project["id"])
    tables=merged(source,"tables","tableDefaults"); declared=merged(source,"columns","columnDefaults")
    pages=merged(source,"pages","pageDefaults"); fields=merged(source,"pageColumns","pageColumnDefaults")
    if {table["tableName"] for table in tables} != set(schema): raise BaselineError("Java元数据与正式DDL表集合不一致")
    table_ids={identity(table["id"]):table for table in tables}
    if len({table["tableName"] for table in tables})!=len(tables): raise BaselineError("重复表名")
    if len(table_ids)!=len(tables): raise BaselineError("重复表ID")
    if any(identity(column["configTableId"]) not in table_ids for column in declared): raise BaselineError("配置字段引用样板之外的表")
    for table in tables:
        if table.get("yzProjectId",project_id)!=project_id: raise BaselineError("配置表跨项目")
        table["yzProjectId"]=project_id
        # 样板三表无项目物理列；只按正式DDL明确全局，不把未知来源默认为全局。
        table["scopeKind"]="project" if "yz_project_id" in schema[table["tableName"]]["columns"] else "global"
        for key in ("isRongyu","isFunctionTable","isView"): table[key]=int(table.get(key,0))
    columns, added, differences = [], [], []
    for table_id,table in table_ids.items():
        owners={column["columnName"]:column for column in declared if identity(column["configTableId"])==table_id}
        if len(owners)!=sum(column["configTableId"]==table_id for column in declared): raise BaselineError("重复配置列")
        physicals=schema[table["tableName"]]["columns"]
        if set(owners)-set(physicals): raise BaselineError("配置字段不在正式DDL中")
        for order,(name,column) in enumerate(physicals.items()):
            row=copy.deepcopy(owners.get(name,{}))
            if row:
                identity(row["id"])
                declared_type=row.get("columnType", "").lower(); length=str(row.get("columnLength", ""))
                expected=("longtext" if length.lower()=="max" else "varchar("+length+")") if declared_type in ("nvarchar", "varchar") else ("decimal("+length+")" if declared_type=="decimal" else declared_type)
                if expected!=column["type"]: raise BaselineError("原配置类型/长度与正式DDL冲突")
                if int(row.get("isDbNotNull",0)) != (0 if column["nullable"] else 1): raise BaselineError("字段非空声明与DDL冲突")
            else:
                row={"id":synthetic(table_id,name),"name":name,"configTableId":table_id,"controlKind":"","controlDetail":"","sortNo":order*10}
                added.append({"table":table["tableName"],"column":name,"id":row["id"],"source":"Java正式DDL内建列或关系辅助列"})
            row.update(physical(column)); row.update({"columnName":name,"yzProjectId":project_id})
            # 原List的精确目标来自原controlDetail，不猜别名或跨页权限。
            if row.get("controlKind")=="List":
                detail=json.loads(row["controlDetail"]); target=identity(detail["tableId"])
                if target not in table_ids or table_ids[target]["tableName"] != detail["table"]: raise BaselineError("关系目标身份不匹配")
                targets=[page for page in pages if page["configTableId"]==target]
                if len(targets)!=1: raise BaselineError("本样板关系目标页面不唯一")
                if row.get("linkConfigTableId") not in (None,"",target) or row.get("linkConfigPageId") not in (None,"",targets[0]["id"]): raise BaselineError("显式关系目标与样板唯一目标冲突")
                row["linkConfigTableId"]=target; row["linkConfigPageId"]=identity(targets[0]["id"])
            columns.append(row)
            if column["type"]=="datetime": differences.append({"table":table["tableName"],"column":name,"java":"datetime","csharp":"datetime(6)","status":"共同物理DDL待编译核对，未应用"})
            if column["default"] and column["default"].lower()=="now()": differences.append({"table":table["tableName"],"column":name,"javaDefault":"now()","csharpMetadataDefault":"","status":"动态默认不伪装为常量，待共同结构与写入核验"})
    # 原Java的物理List名称副本由正式DDL和精确目标共同确定；在C#中显式表达同一缓存语义。
    implicit_caches=[]
    configured_ids={row["id"] for row in declared}
    for foreign in columns:
        if foreign.get("controlKind")!="List":continue
        definition=json.loads(foreign["controlDetail"]);display=definition.get("nameField","name")
        if display=="id":continue
        name=foreign["columnName"][:-3]+"_"+display
        cache=next((row for row in columns if row["configTableId"]==foreign["configTableId"] and row["columnName"]==name),None)
        if cache is None:raise BaselineError("原List隐式名称未出现在正式DDL")
        if cache["id"] in configured_ids:continue
        cache["isJoinColumn"]=1;cache["columnFullName"]=foreign["columnName"][:-3]+"."+display
        implicit_caches.append({"foreign":foreign["id"],"cache":cache["id"],"path":cache["columnFullName"],"source":"原Java List定义及正式DDL隐式物理列"})
    by_column={identity(column["id"]):column for column in columns}
    if len(by_column)!=len(columns): raise BaselineError("原始/夹具字段ID碰撞")
    admins=copy.deepcopy(source["adminTypes"])
    if len(admins)!=1: raise BaselineError("本离线样板必须明确唯一后台，不猜多后台归属")
    admin_id=identity(admins[0]["id"])
    page_ids={identity(page["id"]):page for page in pages}
    if len(page_ids)!=len(pages): raise BaselineError("重复页面ID")
    for page in pages:
        identity(page["id"]); identity(page["configTableId"])
        if page["configTableId"] not in table_ids or page.get("yzProjectId",project_id)!=project_id: raise BaselineError("页面归属不匹配")
        page["yzAdminTypeId"]=admin_id;page["yzProjectId"]=project_id;page["pageSize"]=int(page.get("pageSize",20));page["sortNo"]=int(page.get("sortNo",0))
        page["isImport"]=int(page.get("isImport",bool(page.get("importMethod"))));page["isExport"]=int(page.get("isExport",bool(page.get("exportMethod"))))
    for field in fields:
        column=by_column[identity(field["configColumnId"])];identity(field["id"]);identity(field["configPageId"])
        if field["configPageId"] not in page_ids or column["configTableId"]!=page_ids[field["configPageId"]]["configTableId"]: raise BaselineError("页面字段跨表或页面缺失")
        field.update({"name":column["name"],"columnName":column["columnName"],"yzProjectId":project_id,"isShow":int(field.get("isInGrid",1)),"isEdit":int(field.get("isInEdit",1)),"isRequired":int(field.get("isMust",0)),"sortNo":int(field.get("sortNo",column.get("sortNo",0)))})
    menus=[]
    for menu in source["firstMenus"]:
        menus.append({**copy.deepcopy(menu),"yzProjectId":project_id,"yzAdminTypeId":admin_id,"kind":"DIRECTORY","parentMenuId":None,"sortNo":int(menu.get("sortNo",0))})
    for menu in source["secondMenus"]:
        menus.append({**copy.deepcopy(menu),"yzProjectId":project_id,"yzAdminTypeId":admin_id,"kind":"GENERATED_PAGE" if menu.get("configPageId") else "DIRECTORY","parentMenuId":menu.get("parentConfigSecondMenuId") or menu.get("configFirstMenuId"),"sortNo":int(menu.get("sortNo",0))})
    indexes,index_columns=[],[]
    for table in tables:
        for index in schema[table["tableName"]]["indexes"]:
            index_id=synthetic(table["id"],"index:"+index["name"])
            indexes.append({"id":index_id,"configTableId":table["id"],"indexName":index["name"],"isUnique":int(index["unique"]),"indexType":"UNIQUE" if index["unique"] else "INDEX","isEnabled":1,"yzProjectId":project_id})
            column=next(row for row in columns if row["configTableId"]==table["id"] and row["columnName"]==index["column"])
            index_columns.append({"id":synthetic(index_id,column["columnName"]),"configIndexId":index_id,"configColumnId":column["id"],"columnName":column["columnName"],"sortNo":0,"sortDirection":"ASC","yzProjectId":project_id})
    catalog={"schemaVersion":1,"backendTarget":"csharp","yzProjectId":project_id,"yzProjectName":project["name"],"primaryColor":project.get("primaryColor","#0ab3ad"),"databaseProvider":"mysql","configTables":tables,"configColumns":columns,"configPages":pages,"configPageColumns":fields,"yzAdminTypes":admins,"configMenus":menus,"configButtons":copy.deepcopy(source["buttons"]),"configChildren":copy.deepcopy(source["children"]),"configIndexes":indexes,"configIndexColumns":index_columns}
    receipt={"offline":True,"ddlExecuted":False,"implicitSingleCaches":implicit_caches,"originalConfiguredColumns":len(declared),"physicalColumns":len(columns),"addedPhysicalDeclarations":added,"tables":len(tables),"pages":len(pages),"buttonsPreserved":len(source["buttons"]),"childrenPreserved":len(source["children"]),"indexesPreserved":len(indexes),"knownPhysicalDifferences":differences,"scopeEvidence":[{"table":table["tableName"],"scopeKind":table["scopeKind"],"source":"Java正式DDL是否实际含yz_project_id；页面whereSql保持原样"} for table in tables],"samePhysicalSchemaProven":False,"performanceAccepted":False}
    return catalog,receipt


def compare_physical(schema, candidate):
    """比较真实C#方言编译结果，字段、主键、默认及索引任一不同均明确保留。"""
    if candidate.get("offline") is not True or candidate.get("ddlExecuted") is not False:
        raise BaselineError("只接受明确未执行DDL的离线编译回执")
    tables=candidate["schema"]["tables"];actual={table["name"]:table for table in tables}
    if len(actual)!=len(tables): raise BaselineError("C#物理计划重复表名")
    differences=[]
    for name in sorted(set(schema)|set(actual)):
        if name not in schema or name not in actual:
            differences.append({"table":name,"kind":"tableSet"});continue
        columns={column["name"]:column for column in actual[name]["columns"]}
        if len(columns)!=len(actual[name]["columns"]):raise BaselineError("C#物理计划重复列名")
        for column_name in sorted(set(schema[name]["columns"])|set(columns)):
            if column_name not in schema[name]["columns"] or column_name not in columns:
                differences.append({"table":name,"column":column_name,"kind":"columnSet"});continue
            source=schema[name]["columns"][column_name];target=columns[column_name]
            # MySQL bigint(20)仅显示宽度；不将datetime和datetime(6)当成相同精度。
            old_type="bigint" if source["type"]=="bigint(20)" else source["type"]
            old_default=None if source["default"] is None or source["default"].upper()=="NULL" else source["default"].lower()
            values={"storeType":(old_type,target["storeType"].lower()),"nullable":(source["nullable"],target["nullable"]),"primaryKey":(column_name=="id",target["primaryKey"]),"defaultSql":(old_default,target["defaultSql"])}
            for kind,(before,after) in values.items():
                if before!=after:differences.append({"table":name,"column":column_name,"kind":kind,"java":before,"csharp":after})
        before=sorted((index["name"],index["unique"],(index["column"],)) for index in schema[name]["indexes"])
        after=sorted((index["name"],index["unique"],tuple(index["columns"])) for index in actual[name].get("indexes",[]))
        if before!=after:differences.append({"table":name,"kind":"indexes","java":before,"csharp":after})
    return {"offlineCompiledSchemaEquivalent":not differences,"differences":differences,"databaseInspected":False,"performanceAccepted":False}


def main():
    """新建独立输出目录并保存来源摘要，不覆盖已有基线或连接任何服务器。"""
    parser=argparse.ArgumentParser(description="V8离线样板到C#同模型性能基线")
    parser.add_argument("--java-metadata",required=True);parser.add_argument("--java-schema",required=True);parser.add_argument("--output",required=True)
    parser.add_argument("--csharp-plan")
    args=parser.parse_args()
    source=pathlib.Path(args.java_metadata).read_bytes();ddl=pathlib.Path(args.java_schema).read_bytes()
    catalog,receipt=convert(json.loads(source),parse_schema(ddl.decode("utf-8-sig")))
    receipt["sourceMetadataSha256"]=hashlib.sha256(source).hexdigest();receipt["sourceDdlSha256"]=hashlib.sha256(ddl).hexdigest()
    if args.csharp_plan:
        plan=pathlib.Path(args.csharp_plan).read_bytes();receipt["compiledComparison"]=compare_physical(parse_schema(ddl.decode("utf-8-sig")),json.loads(plan));receipt["csharpPlanSha256"]=hashlib.sha256(plan).hexdigest()
    output=pathlib.Path(args.output);output.mkdir(parents=True,exist_ok=False)
    for name,value in (("metadata.json",catalog),("conversion.json",receipt)):
        (output/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(f"离线夹具转换完成：{receipt['tables']}表/{receipt['pages']}页/{receipt['physicalColumns']}物理列；结构等价及性能尚未证明。")


if __name__=="__main__":
    main()
