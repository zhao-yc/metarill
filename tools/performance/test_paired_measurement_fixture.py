"""固定双端性能数据的离线合同，不创建数据库或发业务请求。"""
from collections import Counter
import hashlib
import unittest

import paired_measurement_fixture as subject


RUN="aa"*16


def response_gateway(target, mutate=None):
    """独立读回接口替身只允许精确 SELECT，故意改变结果而非变更预期。"""
    database=subject.database_name(RUN,target)
    def query(sql):
        assert sql.startswith("SELECT ") and sql.endswith(";") and sql.count(";")==1
        if sql=="SELECT @@hostname,@@server_uuid;":
            key="pin"
            reply=subject.PIN["hostname"]+"\t"+subject.PIN["serverUuid"]
        elif "FROM information_schema.TABLES" in sql:
            key="tables"
            reply="\n".join(table+"\tInnoDB\tutf8mb4_unicode_ci" for table in sorted(subject.COLUMNS))
        elif "FROM information_schema.COLUMNS" in sql:
            key="columns"
            reply="\n".join(subject.expected_column_layout())
        elif "FROM information_schema.STATISTICS" in sql:
            key="indexes"
            reply="\n".join("\t".join((table,name,unique,"1",column,"A","","BTREE","YES"))
                for table in sorted(subject.COLUMNS)
                for name,unique,column in (("PRIMARY","0","id"),("local_id","0","id"),("local_deleted","1","delete_no")))
        else:
            key=next(table for table in subject.COLUMNS if f"`{database}`.`{table}`" in sql)
            reply="\n".join(subject.expected_wire(key))
        return mutate(key,reply) if mutate else reply
    return query


class MeasurementFixtureTests(unittest.TestCase):
    def test_rows_hold_exact_ids_amounts_and_active_parent_distribution(self):
        """使用独立样本断言验证分布、删除状态和非 ASCII、空值、精确小数。"""
        customers=list(subject.rows("sample_customer"))
        contracts=list(subject.rows("sample_contract"))
        lines=list(subject.rows("sample_contract_line"))
        self.assertEqual([512,2048,8192],list(map(len,(customers,contracts,lines))))
        self.assertEqual((9007199254750000,"性能客户-0000",None),customers[0][:3])
        self.assertEqual("",customers[1][2])
        self.assertEqual("1.37",contracts[1][4])
        self.assertEqual("0.79",lines[1][4])
        self.assertEqual([13,17,16],[len(group[0]) for group in (customers,contracts,lines)])
        active_contracts={str(row[0]) for row in contracts if row[8] is None}
        active_children=Counter(row[1] for row in lines if row[7] is None)
        self.assertEqual(active_contracts,set(active_children))
        self.assertEqual({3},set(active_children.values()))
        self.assertEqual(10752,len({row[0] for group in (customers,contracts,lines) for row in group}))
        self.assertTrue(next(subject.expected_wire("sample_customer")).startswith("H39303037313939323534373530303030\tH"))
        self.assertEqual(46,len(list(subject.expected_column_layout())))

    def test_seed_is_bounded_to_one_empty_owned_database_and_84_batches(self):
        """纯 SQL 构造器保持两端相同写入和失败前提。"""
        left=subject.seed_transaction_sql(RUN,"java")
        right=subject.seed_transaction_sql(RUN,"csharp")
        java=subject.database_name(RUN,"java")
        csharp=subject.database_name(RUN,"csharp")
        # 信息模式库名以 UTF-8 十六进制绑定，实际 INSERT 目标使用固定标识符。
        self.assertEqual(left.replace(java,csharp).replace(subject.seed.literal(java),subject.seed.literal(csharp)),right)
        self.assertEqual(84,left.count("INSERT INTO "))
        self.assertEqual(84,left.count("ROW_COUNT()=128"))
        self.assertEqual(3,left.count("NOT EXISTS(SELECT 1 FROM"))
        self.assertTrue(left.startswith("START TRANSACTION;\n"))
        self.assertTrue(left.endswith("COMMIT;\n"))
        self.assertLess(left.rindex("PREPARE mt_fixture_gate FROM"),left.rindex("COMMIT;"))
        self.assertNotIn("DROP ",left)
        for run,target in (("a", "java"),(RUN,"other"),(RUN,"java`;")):
            with self.assertRaises(ValueError):
                subject.seed_transaction_sql(run,target)

    def test_independent_readback_detects_value_count_schema_and_index_drift(self):
        """同条件证明需要原数据和索引；仅两侧摘要碰巧相等仍不够。"""
        java=subject.verify_readback(RUN,"java",response_gateway("java"))
        csharp=subject.verify_readback(RUN,"csharp",response_gateway("csharp"))
        self.assertEqual(java["tableSha256"],csharp["tableSha256"])
        first=next(subject.expected_wire("sample_customer"))
        expected=hashlib.sha256(("\n".join(subject.expected_wire("sample_customer"))+"\n").encode("ascii")).hexdigest()
        self.assertEqual(expected,java["tableSha256"]["sample_customer"])
        changes=[
            ("pin",lambda text:"foreign"),
            ("tables",lambda text:text.replace("InnoDB","MyISAM",1)),
            ("columns",lambda text:text.replace("varchar","text",1)),
            ("sample_customer",lambda text:text.replace(first,first.replace("\tN\t","\tH\t",1),1)),
            ("sample_contract",lambda text:text+"\nextra-row"),
            ("indexes",lambda text:text.replace("BTREE\tYES","BTREE\tNO",1)),
            ("indexes",lambda text:text.replace("\tA\t\tBTREE","\tD\t\tBTREE",1)),
        ]
        for field,corrupt in changes:
            with self.subTest(field=field),self.assertRaises(ValueError):
                subject.verify_readback(RUN,"java",response_gateway("java",lambda key,reply:corrupt(reply) if key==field else reply))


    def test_write_replay_only_excludes_this_runs_customer_prefix(self):
        """保留 NULL 名称、其它客户和两个非客户表的完整原始核验。"""
        for table in subject.COLUMNS:
            normal=subject.readback_sql(RUN,"java",table)
            writing=subject.readback_sql(RUN,"java",table,exclude_write_samples=True)
            if table=="sample_customer":
                condition=" WHERE name IS NULL OR NOT ("+subject.write_sample_condition(RUN)+")"
                self.assertEqual(normal,writing.replace(condition,""))
            else:
                self.assertEqual(normal,writing)


if __name__=="__main__":
    unittest.main()
