"""深度Excel专项的精确来源／预算／输入文件复用合同，不将外部边界桩记作实库。"""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import types
import unittest
from unittest import mock
import paired_deep_excel_import as deep
import paired_extended_business as extended
import paired_native_bootstrap as native
import test_extended_business as previous


def value():
    """仅测试用虚构范围，真实执行仍须Git固定来源与本轮所有权。"""
    result=previous.value();result.update(purpose=native.resources.DEEP_EXCEL_NATIVE,images=dict(native.resources.FILE_AUTH_BUSINESS_IMAGES),workbookOperations=12);return result


class DeepContractTests(unittest.TestCase):
    def test_deep_budget_and_images_are_exact_without_changing_old_scopes(self):
        """深度专项十二文件，原修正镜像用途仍十文件；预算互换在任何IO前拒绝。"""
        v=value();descriptor=native.validate(v)
        self.assertEqual(native.resources.DEEP_EXCEL_RESOURCES,descriptor['purpose']);self.assertEqual(native.resources.FILE_AUTH_BUSINESS_IMAGES,native.resources.images_for(descriptor))
        self.assertFalse(native.resources.is_measurement(descriptor));self.assertFalse(native.resources.is_observed(descriptor));self.assertEqual(12,extended.workbook_budget(v))
        old=previous.value();old.update(purpose=native.resources.FILE_AUTH_BUSINESS_NATIVE,images=dict(native.resources.FILE_AUTH_BUSINESS_IMAGES))
        self.assertEqual(10,extended.workbook_budget(old));native.validate(old)
        for item in [{**v,'workbookOperations':10},{**v,'workbookOperations':13},{**v,'workbookReplayRequests':12},{**v,'readReplayRequests':19336},{**old,'workbookOperations':12}]:
            with self.subTest(purpose=item['purpose']),mock.patch.object(native,'verify_tool_sources',side_effect=AssertionError('不允许IO')),self.assertRaises(ValueError):native.run(item)

    def test_deep_tool_must_be_in_the_actual_source_hash_set(self):
        """新专项不能只在版本号中声明模块或从未固定文件加载可执行代码。"""
        v=value();v['toolSourceCommit']='a'*40;root=Path(native.__file__).resolve().parents[1]
        names=['performance/paired_native_bootstrap.py','performance/paired_http.py','performance/paired_identity_seed.py','performance/server200_paired_resources.py','acceptance/server200-fresh-mysql.py','performance/paired_business_http.py','performance/paired_extended_business.py','performance/paired_workbook_container.py','performance/paired_workbook_oracle.cjs','performance/paired_deep_excel_import.py']
        v['toolSha256']={name:hashlib.sha256((root/name).read_bytes()).hexdigest() for name in names};native.verify_tool_sources(v)
        del v['toolSha256']['performance/paired_deep_excel_import.py']
        with self.assertRaises(ValueError):native.verify_tool_sources(v)

    def test_child_reimport_requires_the_actual_captured_workbook_digest_and_does_not_mutate_it(self):
        """只接受同轮已下载／解码的原子页，旧用途、缺文件或错摘要不能拿JSON凑父页导入。"""
        exchange=extended.Exchange.__new__(extended.Exchange);exchange.value={**value(),'purpose':native.resources.DEEP_EXCEL_BUSINESS}
        result=dict(sha256='a'*64,headers=['明细名称','明细金额','备注'],rows=[{'明细名称':'原导出','明细金额':1.25,'备注':'保留'}])
        exchange.parsed_workbooks={('csharp','read-export-line'):result};exchange.requests=[dict(target='csharp',step='download-line',status=200,responseSha256='a'*64)]
        reused=exchange.reused_child_export();reused['rows'][0]['备注']='仅修改测试副本';self.assertEqual('保留',result['rows'][0]['备注'])
        exchange.requests[0]['responseSha256']='b'*64
        with self.assertRaisesRegex(ValueError,'reuse-digest'):exchange.reused_child_export()
        exchange.value['purpose']=native.resources.FILE_AUTH_BUSINESS
        with self.assertRaisesRegex(ValueError,'reuse-scope'):exchange.reused_child_export()

    def test_wrong_deep_lease_fails_before_query_or_http(self):
        """真正的专项入口先拒绝旧用途／错预算，不能先查询数据库或发送一次业务。"""
        for change in [{'purpose':native.resources.FILE_AUTH_BUSINESS},{'workbookOperations':10}]:
            v={**value(),'purpose':native.resources.DEEP_EXCEL_BUSINESS,'leaseStatus':'running',**change}
            with self.subTest(change=change),self.assertRaises(ValueError):deep.run_deep_import(v,{},mock.Mock(),mock.Mock(side_effect=AssertionError('不允许查询')))


class DeepFlowTests(previous.NativeFlowTests):
    # 不继承原测试方法，避免把已通过的同一外部边界桩重复计数。
    test_original_initialization_and_ordinary_identity_precede_real_business_entry=None
    test_business_failure_preserves_first_stage_diagnostics_and_cleanup=None
    test_corrected_file_auth_scope_reaches_the_same_strict_business_after_identity=None

    def test_deep_scope_reaches_strict_business_with_twelve_workbooks(self):
        """协调器保持十二初始化／三身份／原维护要求，并传独立深度业务用途。"""
        self.value=value()
        with contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertTrue(result['passed']);self.assertEqual(12,self.jobs.call_count);self.assertFalse(result['performanceAccepted'])
        args=self.module.run_extended.call_args.args;self.assertEqual(native.resources.DEEP_EXCEL_BUSINESS,args[0]['purpose']);self.assertEqual(12,args[0]['workbookOperations']);self.assertEqual(extended.MAINTENANCE_PROFILE,args[0]['maintenanceProfile'])
        self.assertEqual([],result['cleanupErrors']);self.assertEqual('ended',result['leaseStatus'])


if __name__=='__main__':unittest.main()
