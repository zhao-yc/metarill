"""普通身份双端映射与种子执行合同；所有query为内存桩，不访问MySQL。"""
import copy
import hashlib
import json
import unittest
from unittest import mock
import paired_identity_seed as subject


def fixture():
    """假密码/盐只用于可重复的离线测试。"""
    return subject.prepare("a"*32,{k:{"password":str(i+1)*64,"javaSalt":str(i+4)*32,"csharpSalt":bytes([i+1])*24} for i,k in enumerate(subject.KINDS)})


def lease(value):
    """构造只供桩使用的批准描述，不作为任何外部创建/写入授权。"""
    return {"version":1,"purpose":"paired-identity-seed-v1","runId":value["runId"],"authorizationStatus":"approved","leaseStatus":"running","fixtureSha256":subject.fingerprint(value),"databases":["metarill_fresh_"+value["runId"][:16]+"_paired_"+t for t in ("java","csharp")],"instancePin":{"hostname":"3107e214a8a3","serverUuid":"6d3d024a-9d73-11f1-9939-ce69308a21dc"}}


class SeedTests(unittest.TestCase):
    """从同一逻辑权限验证两套原物理表示，不拿静态映射冒充真实授权结果。"""
    @classmethod
    def setUpClass(cls):
        """只计算一次PBKDF2夹具，避免重复测试无意义消耗。"""
        cls.fixed=fixture()

    def setUp(self):
        """每项独立副本，漂移测试不污染其它回归。"""
        self.value=copy.deepcopy(self.fixed)

    def test_normal_principals_same_ids_enabled_and_not_super(self):
        """16位字符串ID、同项目后台和三种角色对应一致，启用物理值分别1/0。"""
        self.assertEqual([c["kind"] for c in self.value["common"]],list(subject.KINDS))
        for target in ("java","csharp"):
            rows=self.value["rows"][target]
            for account,role,common in zip(rows["yz_manager"],rows["yz_role"],self.value["common"]):
                self.assertGreater(account["id"],2**53);self.assertEqual(str(account["id"]),common["accountId"])
                self.assertEqual(json.loads(account["yz_role_ids"]),[str(role["id"])])
                self.assertEqual(account["is_disabled"],1 if target=="java" else 0)
                self.assertEqual(role["is_super"],0);self.assertEqual(role["is_scope"],0)
                if target=="csharp":self.assertEqual(account["yz_project_id"],subject.PROJECT)

    def test_permission_mapping_matches_original_menu_and_compiled_page_buttons(self):
        """Java菜单值包含原按钮ID，C#页面值与独立按钮行表达同一逻辑授权。"""
        java=self.value["rows"]["java"]["yz_role_permission"]; csharp=self.value["rows"]["csharp"]["yz_role_permission"]
        self.assertEqual(len(java),6);self.assertEqual(len(csharp),9)
        for common in self.value["common"]:
            role=int(common["roleId"])
            jrows=[r for r in java if r["yz_role_id"]==role];crows=[r for r in csharp if r["yz_role_id"]==role]
            for page,menu in subject.PAGES.items():
                j=next((r for r in jrows if r.get("config_second_menu_id")==menu),None)
                c=next((r for r in crows if r.get("config_page_id")==page),None)
                expected=common["pageActions"][str(page)]
                self.assertEqual(json.loads(c["permission_values"]) if c else [],expected)
                self.assertEqual([v for v in json.loads(j["permission_values"]) if v.startswith("is_")] if j else [],expected)
            self.assertEqual([str(r["config_button_id"]) for r in crows if r.get("config_button_id")],common["buttons"])
            if common["kind"]=="denied":self.assertFalse(jrows);self.assertFalse(crows)

    def test_hash_formats_match_existing_java_and_csharp_contracts(self):
        """摘要差异来自原算法；相同明文没有被存到播种字段。"""
        import base64
        for index,kind in enumerate(subject.KINDS):
            password=str(index+1)*64;salt=str(index+4)*32
            java=self.value["rows"]["java"]["yz_manager"][index]
            csharp=self.value["rows"]["csharp"]["yz_manager"][index]
            self.assertEqual(java["password"],hashlib.md5((password+salt).encode()).hexdigest())
            algorithm,iterations,encoded_salt,encoded_hash=csharp["password"].split("$")
            self.assertEqual(algorithm,"pbkdf2-sha256");self.assertEqual(iterations,"350000")
            self.assertEqual(base64.b64decode(encoded_hash),hashlib.pbkdf2_hmac("sha256",password.encode(),base64.b64decode(encoded_salt),350000,32))
            self.assertNotEqual(java["password"],password);self.assertNotEqual(csharp["password"],password)

    def test_fixture_digest_survives_json_property_order(self):
        """私有清单重排字典属性不改变种子身份，行值和集合顺序仍受摘要保护。"""
        reordered=json.loads(json.dumps(self.value,sort_keys=True))
        self.assertEqual(subject.fingerprint(self.value),subject.fingerprint(reordered))
        self.assertEqual(subject.authorize(lease(self.value),"java",reordered),"metarill_fresh_"+"a"*16+"_paired_java")

    def test_sql_only_inserts_exact_database_with_utf8_hex_text(self):
        """中文/引号/反斜线不会被拼接解释，语句仅INSERT到固定三个表。"""
        for target in ("java","csharp"):
            db="metarill_fresh_"+"a"*16+"_paired_"+target
            sql=subject.statements(db,self.value,target)
            self.assertEqual(len(sql),12 if target=="java" else 15)
            self.assertTrue(all(s.startswith("INSERT INTO `"+db+"`.") for s in sql))
            self.assertNotIn("性能","".join(sql));self.assertIn("CONVERT(0x","".join(sql))
        self.assertEqual(subject.literal("'\\;DROP DATABASE x"),"CONVERT(0x"+"'\\;DROP DATABASE x".encode().hex()+" USING utf8mb4)")
        for value in (True,None,1.5):
            with self.assertRaises(ValueError):subject.literal(value)

    def test_foreign_database_invalid_column_and_super_seed_refuse(self):
        """种子不能转到平台库、插入任意列名或建立超级角色。"""
        with self.assertRaises(ValueError):subject.statements("metarill_dev",self.value,"java")
        db="metarill_fresh_"+"a"*16+"_paired_java"
        self.value["rows"]["java"]["yz_role"][0]["is_super"]=1
        with self.assertRaises(ValueError):subject.statements(db,self.value,"java")
        self.value=copy.deepcopy(self.fixed);self.value["rows"]["java"]["yz_manager"][0]["x`) VALUES(1);--"]=1
        with self.assertRaises(ValueError):subject.statements(db,self.value,"java")

    def test_pending_ended_drift_wrong_scope_reject_before_query(self):
        """pending或结束租约不读取实例/数据，修改已冻结播种同样拒绝。"""
        current=lease(self.value)
        for change in ({"authorizationStatus":"pending"},{"leaseStatus":"ended"},{"purpose":"agent"},{"databases":["old"]},{"fixtureSha256":"0"*64},{"instancePin":{"hostname":"other"}}):
            query=mock.Mock(side_effect=AssertionError("禁止数据库"))
            with self.subTest(change=change),self.assertRaises(ValueError):subject.apply({**current,**change},"java",self.value,query)
            query.assert_not_called()
        self.value["rows"]["java"]["yz_manager"][0]["name"]="changed"
        with self.assertRaises(ValueError):subject.apply(current,"java",self.value,mock.Mock(side_effect=AssertionError("禁止数据库")))

    def test_actual_instance_realm_collision_or_readback_failure_never_report_success(self):
        """写前真实归属/碰撞失败没有INSERT，写后不一致保留失败，不重试或覆盖。"""
        pin="3107e214a8a3\t6d3d024a-9d73-11f1-9939-ce69308a21dc"
        for outputs in (["wrong"],[pin,"0"],[pin,"1","1"]):
            query=mock.Mock(side_effect=outputs)
            with self.subTest(outputs=outputs),self.assertRaises(RuntimeError):subject.apply(lease(self.value),"java",self.value,query)
            self.assertFalse(any("INSERT" in c.args[0] for c in query.call_args_list))
        query=mock.Mock(side_effect=[pin,"1","0","0","0","","0"])
        with self.assertRaises(RuntimeError):subject.apply(lease(self.value),"java",self.value,query)
        self.assertEqual(sum("START TRANSACTION" in c.args[0] for c in query.call_args_list),1)

    def test_apply_inserts_once_and_reads_each_seed_value(self):
        """完整固定桩验证12/15行回读；回执明确未验证密码登录/实际权限。"""
        pin="3107e214a8a3\t6d3d024a-9d73-11f1-9939-ce69308a21dc"
        for target,count in (("java",12),("csharp",15)):
            query=mock.Mock(side_effect=[pin,"1",*(["1"] if target=="csharp" else []),"0","0","0","",*(["1"]*count)])
            result=subject.apply(lease(self.value),target,self.value,query)
            self.assertEqual(result["verifiedRows"],count);self.assertEqual(result["superAccounts"],0)
            self.assertFalse(result["actualPasswordLoginVerified"]);self.assertFalse(result["permissionBehaviorVerified"])
            statements=[c.args[0] for c in query.call_args_list]
            self.assertEqual(sum("START TRANSACTION" in q for q in statements),1)
            self.assertEqual(sum("<=>" in q for q in statements),count)

    def test_invalid_password_or_salt_and_duplicate_password_refuse(self):
        """不接受短密码、重复密码或错误盐长度。"""
        for args in (("x","a"*32,b"x"*24),("a"*64,"x",b"x"*24),("a"*64,"a"*32,b"x")):
            with self.assertRaises(ValueError):subject.password_hashes(*args)
        credentials={k:{"password":"1"*64,"javaSalt":"a"*32,"csharpSalt":b"x"*24} for k in subject.KINDS}
        with self.assertRaises(ValueError):subject.prepare("a"*32,credentials)


if __name__=="__main__":
    unittest.main()
