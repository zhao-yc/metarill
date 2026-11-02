"""固定请求账本与采集快照复核；所有产物均为合成，零真实HTTP、数据库或容器。"""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import paired_observed_capture as subject
import test_audit_sql_artifacts as artifacts
import test_paired_native_bootstrap as fixtures


def value():
    """假范围只用于本机临时文件，pending/ended不会转为外部批准。"""
    return {**fixtures.settings(),'purpose':'paired-native-observed-v1','images':dict(subject.resources.OBSERVATION_IMAGES)}


class CaptureTests(unittest.TestCase):
    def setUp(self):
        """每项独占临时根；两个生产者的字段形状来自已有合同夹具。"""
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.root=Path(self.temp.name);self.scope=value();self.exchange=subject.Exchange(self.scope,mock.Mock(side_effect=AssertionError('不应发网络')))
        self.enterContext(mock.patch.object(subject.resources,'root_for',return_value=self.root));self.records={};self.indexes={};self.folders={}

    def materialize(self):
        """写46个固定样本，只有业务回滚包含一条成功UPDATE及rollback。"""
        run=self.scope['runId'];key=self.root/'observation.key';key.write_bytes(bytes([11])*32);raw=json.dumps({'observation.key':hashlib.sha256(key.read_bytes()).hexdigest()}).encode();(self.root/'materials.json').write_bytes(raw);self.scope['materialsSha256']=hashlib.sha256(raw).hexdigest()
        for name in ('resource-state.json','native-state.json'):
            (self.root/name).write_text(json.dumps(dict(runId=run,ownerToken='d'*64,status='stopped',hostsStarted=True)),encoding='utf-8')
        for side in ('java','csharp'):
            java=side=='java';source=self.root/('data-'+side)/'sql-observation';source.mkdir(parents=True);(source/'.metarill-paired-observation-owner').write_text(run,encoding='utf-8');folder=source/('00000000-0000-4000-8000-000000000001' if java else '1'*32);folder.mkdir();self.folders[side]=folder;index,base,_=artifacts.fixture(side);index['runId']=run;index['requests']=[];self.records[side]=[]
            if not java:index.update(dataAssemblySha256=subject.resources.OBSERVATION_DATA_SHA256,expectedDatabases=['metarill_fresh_'+run[:16]+'_paired_csharp'],retainedEntries=3)
            for number,plan in enumerate(self.exchange.plans[side],1):
                record=copy.deepcopy(base);ident=('00000000-0000-4000-8000-'+format(number,'012x')) if java else format(number,'032x');record.update(runId=run,id=ident,sample=plan['sample']);record['statusAtFilterExit' if java else 'statusAtMiddlewareExit']=plan['statusAtExit'];trace=record['trace']
                if plan['step']=='business:business-rollback':
                    trace['commands'][0]['sql']='UPDATE `sample_contract` SET remarks=?';trace['transactions'][1]['operation']='rollback'
                    if not java:trace['commands'][0]['database']='metarill_fresh_'+run[:16]+'_paired_csharp'
                else:trace.update(commands=[],transactions=[],started=0,completed=0,startedTransactions=0,completedTransactions=0)
                self.records[side].append(record)
            index.update(started=len(self.records[side]),completed=len(self.records[side]),observed=len(self.records[side]));self.indexes[side]=index;self.write(side);self.exchange.finished[side]=copy.deepcopy(self.exchange.plans[side]);self.exchange.sent[side]=copy.deepcopy(self.exchange.plans[side])

    def write(self,side):
        """篡改结果后重新算文件SHA，拒绝须来自结构/覆盖/事务而非只查散列。"""
        index=self.indexes[side];index['requests']=[];folder=self.folders[side]
        for record in self.records[side]:
            raw=json.dumps(record).encode();(folder/(record['id']+'.json')).write_bytes(raw);index['requests'].append(dict(id=record['id'],sha256=hashlib.sha256(raw).hexdigest()))
        (folder/'index.json').write_text(json.dumps(index),encoding='utf-8')

    def capture(self,graceful=True):
        """只运行文件保存及复核，不调用资源工具启动或删除对象。"""
        return subject.capture_for_review(self.root,self.scope,self.exchange,graceful)

    def test_fixed_request_plan_has_46_unique_samples_and_original_statuses(self):
        """计划先于结果固定；两端原错误状态差异明确保留。"""
        for side in ('java','csharp'):
            rows=self.exchange.plans[side];self.assertEqual(46,len(rows));self.assertEqual(46,len({row['sample'] for row in rows}));self.assertTrue(all(len(row['sample'])==32 for row in rows));self.assertFalse(self.exchange.receipt()['sqlCountExpectationsVerified'])
        java=next(row for row in self.exchange.plans['java'] if row['step']=='business:business-replay-conflict');csharp=next(row for row in self.exchange.plans['csharp'] if row['step']=='business:business-replay-conflict');self.assertEqual((200,500),(java['statusAtExit'],java['expectedCode']));self.assertEqual((409,409),(csharp['statusAtExit'],csharp['expectedCode']))

    def test_pending_and_scope_errors_reject_before_io_or_sender(self):
        """旧用途、布尔版本、旧镜像或ended都不能读文件或触发HTTP。"""
        for changes in ({'authorizationStatus':'pending'},{'leaseStatus':'ended'},{'version':True},{'purpose':'paired-native-v1'},{'images':dict(subject.resources.IMAGES)},{'ports':{'java':80,'csharp':81}}):
            with self.subTest(changes=changes),mock.patch.object(subject.reader,'ordinary',side_effect=AssertionError('不能读文件')),self.assertRaises(ValueError):subject.capture_for_review(self.root,{**self.scope,**changes},self.exchange,True)

    def test_request_never_retries_and_key_token_body_are_not_in_receipt(self):
        """真实sender参数保留原字段，账本只记固定sample及数量。"""
        sender=mock.Mock(return_value=(200,{'code':200,'data':{}}));exchange=subject.Exchange(self.scope,sender);exchange.call('java','identity:ready','GET','/fixed',{'password':'PRIVATE-BODY'},token='PRIVATE-TOKEN');self.assertEqual(1,sender.call_count);self.assertIn('sample',sender.call_args.kwargs);self.assertNotIn('PRIVATE',json.dumps(exchange.receipt()))
        for step in ('identity:ready','business:customer-add'):
            with self.assertRaises(ValueError):exchange.call('java',step,'GET','/fixed')
        self.assertEqual(1,sender.call_count)

    def test_sender_failure_does_not_advance_completed_or_retry(self):
        """未知HTTP结局只保留发出意图，不能当作已完成再发一次。"""
        sender=mock.Mock(side_effect=TimeoutError());exchange=subject.Exchange(self.scope,sender)
        with self.assertRaises(TimeoutError):exchange.call('java','identity:ready','GET','/fixed')
        self.assertEqual(1,len(exchange.sent['java']));self.assertEqual(0,len(exchange.finished['java']));self.assertEqual(1,sender.call_count)

    def test_complete_raw_snapshot_is_saved_but_sql_counts_remain_unapproved(self):
        """两端结构及写后rollback通过仍只供审阅，不推导SQL同值或性能。"""
        self.materialize();result=self.capture();self.assertFalse(result['sqlSemanticAccepted']);self.assertFalse(result['performanceAccepted']);self.assertTrue(result['privateKeyRetainedForReview'])
        for side,row in result['targets'].items():
            self.assertEqual(46,row['requests']);self.assertEqual(1,row['commands']);self.assertTrue(row['updateThenRollbackObserved']);self.assertFalse(row['sqlCountExpectationsVerified']);self.assertFalse(row['physicalTargetVerified']);self.assertEqual(47,len(row['files']))
        self.assertEqual(bytes([11])*32,(self.root/'observations/value.key').read_bytes())
        with self.assertRaises(FileExistsError):self.capture()

    def test_forged_index_true_does_not_hide_counter_drift(self):
        """自报一致且重算SHA仍必须重新核计数，失败原始记录已保留。"""
        self.materialize();self.indexes['java']['completed']=45;self.write('java')
        with self.assertRaisesRegex(ValueError,'capture-request-count'):self.capture()
        self.assertTrue((self.root/'observations/java/index.json').exists());self.assertTrue((self.root/'observations/csharp/index.json').exists())

    def test_unfinalized_exit_preserves_raw_but_is_not_accepted(self):
        """强制停止后可以保留诊断副本，但绝不算正常关闭采集。"""
        self.materialize();state=json.loads((self.root/'resource-state.json').read_text());state['status']='ended';(self.root/'resource-state.json').write_text(json.dumps(state),encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'capture-unfinalized-exit'):self.capture(False)
        self.assertTrue((self.root/'observations/value.key').exists());self.assertFalse((self.root/'observations/capture-manifest.json').exists())

    def test_client_graceful_flag_cannot_bypass_real_resource_state(self):
        """客户端传true但资源未停止时，在保存任何key/trace前拒绝。"""
        self.materialize();state=json.loads((self.root/'resource-state.json').read_text());state['status']='started';(self.root/'resource-state.json').write_text(json.dumps(state),encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'capture-resource-state'):self.capture()
        self.assertFalse((self.root/'observations').exists())

    def test_marker_mismatch_and_changed_key_do_not_pass(self):
        """输入key必须匹配冻结材料，而非只凭32字节长度。"""
        self.materialize();(self.root/'observation.key').write_bytes(bytes([12])*32)
        with self.assertRaisesRegex(ValueError,'capture-key-size'):self.capture()

    def test_authentication_update_cannot_fake_business_update_before_rollback(self):
        """必须是同事务内的sample_contract更新，不能拿认证表UPDATE冒充。"""
        self.materialize();record=next(row for row in self.records['java'] if row['sample']==subject.sample(self.scope['runId'],'java','business:business-rollback'));record['trace']['commands'][0]['sql']='UPDATE yz_manager SET name=?';self.write('java')
        with self.assertRaisesRegex(ValueError,'capture-update-before-rollback'):self.capture()

    def test_unplanned_sample_duplicate_and_failed_driver_are_rejected(self):
        """额外请求、重复标签或被R错误掩盖的驱动失败都必须失败。"""
        self.materialize();self.records['csharp'][0]['sample']=self.records['csharp'][2]['sample'];self.write('csharp')
        with self.assertRaisesRegex(ValueError,'capture-record-outcome|capture-unplanned-sample'):self.capture()

    def test_failed_driver_is_not_hidden_by_expected_business_error_response(self):
        """R500虽在回滚用例预期内，真实SQL失败仍必须独立拒绝。"""
        self.materialize();record=next(row for row in self.records['java'] if row['sample']==subject.sample(self.scope['runId'],'java','business:business-rollback'));record['trace']['commands'][0].update(succeeded=False,errorType='ExpectedSqlFailure',errorCode=1064,affectedRows=None);self.write('java')
        with self.assertRaisesRegex(ValueError,'capture-driver-failure'):self.capture()

    def test_incomplete_client_cannot_be_promoted_by_complete_server_files(self):
        """客户端少一笔确认时，即使服务端自报46完整也不能作为整链完成。"""
        self.materialize();self.exchange.finished['java'].pop()
        with self.assertRaisesRegex(ValueError,'capture-client-coverage'):self.capture()


if __name__=='__main__':unittest.main()
