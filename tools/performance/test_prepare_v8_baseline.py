"""只验证固定V8离线样板到性能夹具的表示转换，不运行Java/.NET或连接数据库。"""
import copy
import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest import mock

spec=importlib.util.spec_from_file_location("prepare_v8_baseline",pathlib.Path(__file__).with_name("prepare_v8_baseline.py"))
subject=importlib.util.module_from_spec(spec);spec.loader.exec_module(subject)


def fixture():
    """最小元数据仍含原默认值、按钮和导航；DDL是显式测试输入，不模拟数据库通过。"""
    source={"schemaVersion":1,"project":{"id":"9007199254740993","name":"离线样板"},"tableDefaults":{"yzProjectId":"9007199254740993","isRongyu":1,"isView":0,"isFunctionTable":0},"columnDefaults":{"columnType":"nvarchar","columnLength":"100","isDbNotNull":0,"controlKind":"Text","controlDetail":""},"pageDefaults":{"pageSize":"20","whereSql":"name!='范围外'","isNew":1,"isSave":1},"pageColumnDefaults":{"isInGrid":1,"isInEdit":1,"isMust":1},"tables":[{"id":"2","tableName":"sample_item","name":"样板"}],"columns":[{"id":"3","configTableId":"2","columnName":"name","name":"名称"}],"pages":[{"id":"10","configTableId":"2","name":"样板"}],"pageColumns":[{"id":"11","configPageId":"10","configColumnId":"3"}],"children":[],"adminTypes":[{"id":"30","name":"后台","yzProjectId":"9007199254740993"}],"firstMenus":[{"id":"40","name":"导航","sortNo":"1"}],"secondMenus":[{"id":"41","configFirstMenuId":"40","configPageId":"10","sortNo":"2","name":"样板"}],"buttons":[{"id":"50","configPageId":"10","sql":"/admin/custom/fixture"}]}
    sql='-- 只读样板DDL\nCREATE TABLE `__YZP_SAMPLE_DATABASE__`.`sample_item` (`id` BIGINT(20) NOT NULL,`name` varchar(100) NULL,`delete_no` DATETIME NULL DEFAULT NULL,`insert_time` DATETIME NULL DEFAULT now(),`amount` DECIMAL(18,2) NULL,UNIQUE index_id (id),index index_delete_no (delete_no),PRIMARY KEY (`id`));\n'
    return source,sql


class V8BaselineTests(unittest.TestCase):
    """转换必须保留模型并揭露差异，不能为了生成通过而删能力或猜类型。"""
    def test_schema_parses_decimal_defaults_and_original_indexes(self):
        """括号里的逗号不能切开decimal，动态默认和重复主键二级索引均有证据。"""
        _,sql=fixture();schema=subject.parse_schema(sql)
        self.assertEqual(schema["sample_item"]["columns"]["amount"]["type"],"decimal(18,2)")
        self.assertEqual(schema["sample_item"]["columns"]["insert_time"]["default"],"now()")
        self.assertEqual(schema["sample_item"]["indexes"][0],{"name":"index_id","column":"id","unique":True})

    def test_unknown_or_mutating_sql_duplicate_tables_and_missing_primary_rejected(self):
        """非样板DDL、未知类型或不完整主键不能混入离线模型。"""
        _,sql=fixture()
        for changed in (sql+'DROP TABLE other;',sql.replace('__YZP_SAMPLE_DATABASE__','actual_database'),sql.replace('varchar(100)','blob'),sql+sql,sql.replace(',PRIMARY KEY (`id`)',''),sql.replace('UNIQUE index_id (id)','UNIQUE index_id (absent)')):
            with self.subTest(changed=changed[-40:]),self.assertRaises(subject.BaselineError):subject.parse_schema(changed)

    def test_conversion_preserves_model_large_ids_menu_and_reports_physical_differences(self):
        """全部配置仍在，DDL隐式列补为元数据；实际精度及默认差异不被掩盖。"""
        source,sql=fixture();catalog,receipt=subject.convert(source,subject.parse_schema(sql))
        self.assertEqual(catalog["yzProjectId"],"9007199254740993")
        self.assertEqual(catalog["configColumns"][1]["id"],"3")
        self.assertEqual(len(catalog["configColumns"]),5)
        self.assertEqual(catalog["configTables"][0]["scopeKind"],"global")
        self.assertEqual(catalog["configPages"][0]["whereSql"],source["pageDefaults"]["whereSql"])
        self.assertEqual(catalog["configButtons"],source["buttons"])
        self.assertEqual(catalog["configMenus"][1]["parentMenuId"],"40")
        self.assertEqual(catalog["configMenus"][1]["kind"],"GENERATED_PAGE")
        self.assertEqual(catalog["configPageColumns"][0]["isRequired"],1)
        self.assertEqual(receipt["indexesPreserved"],2)
        self.assertFalse(receipt["samePhysicalSchemaProven"]);self.assertFalse(receipt["performanceAccepted"])
        self.assertEqual(len(receipt["knownPhysicalDifferences"]),3)

    def test_input_is_unchanged_and_derived_id_stable(self):
        """默认展开不得污染Java原始元数据，重复转换稳定且保留原ID。"""
        source,sql=fixture();before=copy.deepcopy(source);schema=subject.parse_schema(sql)
        first=subject.convert(source,schema);second=subject.convert(source,schema)
        self.assertEqual(source,before);self.assertEqual(first,second)
        identifiers=[column["id"] for column in first[0]["configColumns"]]
        self.assertEqual(len(identifiers),len(set(identifiers)))
        with self.assertRaises(subject.BaselineError):subject.identity(9007199254740993)
        with self.assertRaises(subject.BaselineError):subject.identity("9223372036854775808")

    def test_source_types_table_sets_and_project_ownership_cannot_drift(self):
        """原配置冲突必须拒绝，不能用DDL覆盖错类型或将其它项目归属改写。"""
        source,sql=fixture();schema=subject.parse_schema(sql)
        variants=[]
        changed=copy.deepcopy(source);changed["columns"][0]["columnLength"]="50";variants.append(changed)
        changed=copy.deepcopy(source);changed["tables"][0]["tableName"]="other";variants.append(changed)
        changed=copy.deepcopy(source);changed["columns"][0]["configTableId"]="99";variants.append(changed)
        changed=copy.deepcopy(source);changed["tables"][0]["yzProjectId"]="99";variants.append(changed)
        changed=copy.deepcopy(source);changed["pages"][0]["configTableId"]="99";variants.append(changed)
        changed=copy.deepcopy(source);changed["schemaVersion"]=2;variants.append(changed)
        for changed in variants:
            with self.subTest(changed=changed),self.assertRaises(subject.BaselineError):subject.convert(changed,schema)

    def test_project_scope_only_when_actual_ddl_has_project_column(self):
        """只有实际物理项目列才能声明项目范围，不为通过C#门槛伪造该列。"""
        source,sql=fixture();sql=sql.replace('`name` varchar(100) NULL,','`name` varchar(100) NULL,`yz_project_id` BIGINT NULL,')
        catalog,receipt=subject.convert(source,subject.parse_schema(sql))
        self.assertEqual(catalog["configTables"][0]["scopeKind"],"project")
        self.assertTrue(any(column["columnName"]=="yz_project_id" for column in catalog["configColumns"]))
        self.assertEqual(receipt["scopeEvidence"][0]["scopeKind"],"project")

    def test_relation_keeps_exact_target_and_rejects_ambiguous_page(self):
        """保留原List及唯一目标页，不猜跨页权限或删除关系。"""
        source,sql=fixture()
        source["tables"].append({"id":"20","tableName":"sample_owner","name":"所有者"})
        source["columns"].extend([{"id":"21","configTableId":"20","columnName":"name","name":"名称"},{"id":"22","configTableId":"2","columnName":"sample_owner_id","name":"所有者","columnLength":"50","controlKind":"List","controlDetail":json.dumps({"table":"sample_owner","tableId":"20","nameField":"name"})}])
        source["pages"].append({"id":"23","configTableId":"20","name":"所有者"})
        sql=sql.replace('`name` varchar(100) NULL,','`name` varchar(100) NULL,`sample_owner_id` varchar(50) NULL,',1)
        sql+='CREATE TABLE `__YZP_SAMPLE_DATABASE__`.`sample_owner` (`id` BIGINT(20) NOT NULL,`name` varchar(100) NULL,PRIMARY KEY (`id`));'
        catalog,_=subject.convert(source,subject.parse_schema(sql));relation=next(row for row in catalog["configColumns"] if row["id"]=="22")
        self.assertEqual(relation["linkConfigTableId"],"20");self.assertEqual(relation["linkConfigPageId"],"23")
        self.assertEqual(relation["controlKind"],"List");self.assertEqual(relation["logicalType"],"string")
        source["pages"].append({"id":"24","configTableId":"20","name":"另一权限页"})
        with self.assertRaises(subject.BaselineError):subject.convert(source,subject.parse_schema(sql))

    def test_actual_compiled_comparison_keeps_temporal_defaults_and_indexes(self):
        """比较真实编译回执形状，显示宽度等价不掩盖datetime精度和动态默认差异。"""
        _,sql=fixture();schema=subject.parse_schema(sql);columns=[]
        for name,column in schema["sample_item"]["columns"].items():
            columns.append({"name":name,"storeType":"bigint" if column["type"]=="bigint(20)" else column["type"],"nullable":column["nullable"],"primaryKey":name=="id","defaultSql":None if column["default"] in (None,"NULL") else column["default"]})
        candidate={"offline":True,"ddlExecuted":False,"schema":{"tables":[{"name":"sample_item","columns":columns,"indexes":[{"name":index["name"],"unique":index["unique"],"columns":[index["column"]]} for index in schema["sample_item"]["indexes"]]}]}}
        self.assertTrue(subject.compare_physical(schema,candidate)["offlineCompiledSchemaEquivalent"])
        date=next(column for column in columns if column["name"]=="insert_time");date["storeType"]="datetime(6)";date["defaultSql"]=None
        comparison=subject.compare_physical(schema,candidate)
        self.assertFalse(comparison["offlineCompiledSchemaEquivalent"])
        self.assertEqual({row["kind"] for row in comparison["differences"]},{"storeType","defaultSql"})
        candidate["schema"]["tables"][0]["indexes"].pop()
        self.assertIn("indexes",{row["kind"] for row in subject.compare_physical(schema,candidate)["differences"]})
        candidate["ddlExecuted"]=True
        with self.assertRaises(subject.BaselineError):subject.compare_physical(schema,candidate)

    def test_cli_rejects_existing_output_without_overwriting(self):
        """旧基线证据不能被同名重跑覆盖，所有输入均为本机临时文件。"""
        source,sql=fixture()
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory);meta=root/'meta.json';ddl=root/'schema.sql';output=root/'existing'
            meta.write_text(json.dumps(source),encoding='utf-8');ddl.write_text(sql,encoding='utf-8');output.mkdir();(output/'keep.txt').write_text('preserve')
            with mock.patch('sys.argv',['prepare','--java-metadata',str(meta),'--java-schema',str(ddl),'--output',str(output)]),self.assertRaises(FileExistsError):subject.main()
            self.assertEqual((output/'keep.txt').read_text(),'preserve')
            self.assertFalse((output/'metadata.json').exists())


if __name__=='__main__':unittest.main()
