"""固定文件测量的预算、真实来源、原数据边界和实际协调器编排；外部桩不计性能。"""
import contextlib
import hashlib
import io
from pathlib import Path
import unittest
from unittest import mock
import paired_file_measurement as files
import paired_measurement as measurement
import paired_measurement_fixture as fixture
import paired_native_bootstrap as native
import paired_write_measurement as write
import test_paired_native_bootstrap as previous
import test_indexed_write_measurement as indexed


def value():
    """本机边界测试范围，不授权真实数据库或容器。"""
    v=previous.settings();v.update(purpose=native.resources.FILE_MEASUREMENT_NATIVE,images=dict(native.resources.IMPORT_RULE_IMAGES),relationIndexProfile=native.resources.RELATION_INDEX_PROFILE,fileReplayRequests=4834,fileFixtureRequests=18);return v


def targets(v):
    """模拟由真实协调器构造的固定两个回环目标。"""
    return {side:dict(container='metarill-perf-'+v['runId']+'-'+side,expectedImageId=v['images'][side],containerPort=8080,baseUrl=f"http://127.0.0.1:{v['ports'][side]}/api") for side in ('java','csharp')}


def tokens():
    """非真实令牌只验证普通身份选择，不执行网络请求。"""
    return {(side,kind):('a'*64 if side=='csharp' else 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa') for side in ('java','csharp') for kind in native.seed.KINDS}


class FileMeasurementContractTests(unittest.TestCase):
    def test_fixed_budget_keeps_complete_binary_integrity_and_original_three_round_thresholds(self):
        """两次首检、32预热和4800正式下载相加4834，18样本请求独立不计时。"""
        v=value();d=native.validate(v);self.assertEqual(native.resources.FILE_MEASUREMENT_RESOURCES,d['purpose']);self.assertTrue(native.resources.is_measurement(d));self.assertFalse(native.resources.is_write_measurement(d));self.assertFalse(native.resources.is_extended_business(d));self.assertFalse(native.resources.is_observed(d))
        c=files.config_for(v,targets(v),tokens());self.assertEqual((3,400,8,[1,4],1.10,0.95),(c['rounds'],c['iterations'],c['warmupIterations'],c['concurrency'],c['maxP95Ratio'],c['minThroughputRatio']));step=c['scenarios'][0]['steps'][0]
        self.assertEqual((65536,files.FILE_SHA,'binary','GET'),(step['expectedBytes'],step['expectedSha256'],step['responseKind'],step['method']));self.assertNotIn('ignorePointers',step);self.assertEqual(4834,2*(2*(3*400+8)+1));self.assertEqual(65536,len(files.FILE_BYTES));self.assertEqual(hashlib.sha256(bytes(range(256))*256).hexdigest(),files.FILE_SHA)
        self.assertTrue(all(c['targets'][side]['headers']['loginStateId']==tokens()[side,'reader'] for side in ('java','csharp')))

    def test_other_purposes_images_or_budgets_fail_before_source_or_external_io(self):
        """文件用途不能借旧只读预算或功能租约，也不能随意更换文件大小／当前镜像。"""
        v=value()
        for changes in [{'fileReplayRequests':4833},{'fileReplayRequests':True},{'fileFixtureRequests':19},{'readReplayRequests':19336},{'writeReplayRequests':8624},{'workbookReplayRequests':12},{'workbookOperations':12},{'images':dict(native.resources.FILE_AUTH_BUSINESS_IMAGES)},{'authorizationStatus':'pending'}]:
            with self.subTest(changes=changes),mock.patch.object(native,'verify_tool_sources',side_effect=AssertionError('禁止IO')),self.assertRaises(ValueError):native.run({**v,**changes})
        old={**v,'purpose':native.resources.INDEXED_READ_MEASUREMENT_NATIVE,'images':native.resources.INDEXED_WRITE_IMAGES,'readReplayRequests':19336}
        with self.assertRaises(ValueError):native.validate(old)
        with self.assertRaises(ValueError):files.run_file_measurement(Path('.'),old,{}, {},mock.Mock(),mock.Mock(side_effect=AssertionError('禁止查询')),mock.Mock(side_effect=AssertionError('禁止归属检查')))

    def test_actual_file_module_is_required_in_fixed_git_source_set(self):
        """不能仅声明版本号，模块必须与其它八份实际运行源码一并逐摘要固定。"""
        v=value();v['toolSourceCommit']='a'*40;root=Path(native.__file__).resolve().parents[1];names=['acceptance/server200-fresh-mysql.py','performance/paired_native_bootstrap.py','performance/paired_http.py','performance/paired_identity_seed.py','performance/server200_paired_resources.py','performance/paired_business_http.py','performance/paired_measurement_fixture.py','performance/paired_measurement.py','performance/paired_cgroup_probe.py','performance/paired_file_measurement.py'];v['toolSha256']={n:hashlib.sha256((root/n).read_bytes()).hexdigest() for n in names};native.verify_tool_sources(v);del v['toolSha256']['performance/paired_file_measurement.py']
        with self.assertRaises(ValueError):native.verify_tool_sources(v)

    def test_exact_extra_contract_cannot_hide_original_rows_or_other_tables(self):
        """只有独立核验的新增合同ID可排除；原种子ID、越界ID或客户写入范围不能混入。"""
        run=value()['runId'];identifier='905900000000000001';sql=fixture.readback_sql(run,'java','sample_contract',exclude_contract_id=identifier);self.assertIn(' WHERE id<>'+identifier+' ORDER BY',sql)
        for table in ['sample_customer','sample_contract_line']:self.assertEqual(fixture.readback_sql(run,'csharp',table),fixture.readback_sql(run,'csharp',table,exclude_contract_id=identifier))
        for bad in [str(fixture.ID_BASE),str(fixture.ID_BASE+29999),'0','1 OR 1=1','9223372036854775808',123,True]:
            with self.subTest(bad=bad),self.assertRaises(ValueError):fixture.readback_sql(run,'java','sample_contract',exclude_contract_id=bad)
        with self.assertRaises(ValueError):fixture.readback_sql(run,'java','sample_contract',exclude_contract_id=identifier,exclude_write_samples=True)

    def test_actual_seed_gate_accepts_only_native_file_scope_with_ownership_and_same_indexes(self):
        """实际种子入口不得遗漏新用途；资源描述／未批准／结束租约仍在任何SQL前拒绝。"""
        v=value();database,model=mock.Mock(),mock.Mock();database.mysql.return_value='1\n1';model.verify_readback.return_value={'verified':True}
        with mock.patch.object(native,'require_database_ownership') as own:
            self.assertEqual({'verified':True},native.seed_measurement_database(database,v,'owner',model,'java'));own.assert_called_once();model.verify_readback.assert_called_once_with(v['runId'],'java',database.mysql,relation_indexes=True)
        for changes in [{'purpose':native.resources.FILE_MEASUREMENT_RESOURCES},{'authorizationStatus':'pending'},{'leaseStatus':'ended'}]:
            with self.subTest(changes=changes),mock.patch.object(native,'require_database_ownership',side_effect=AssertionError('不允许外调')),self.assertRaises(ValueError):native.seed_measurement_database(database,{**v,**changes},'owner',model,'java')

    def test_mismatched_target_fails_before_any_http(self):
        """即使目标是另一个有效回环或合法镜像，也必须与本轮端口／容器／固定内容一致。"""
        v=value()
        for key,wrong in [('baseUrl','http://127.0.0.1:39999/api'),('container','another-container'),('expectedImageId','sha256:'+'a'*64),('containerPort',8081)]:
            t=targets(v);t['csharp'][key]=wrong
            with self.subTest(key=key),self.assertRaises(ValueError):files.config_for(v,t,tokens())


class FileMeasurementFlowTests(previous.FlowFixture):
    def setUp(self):
        """直接运行协调器，只替换系统边界，原十二初始化和种子顺序仍由真实代码执行。"""
        super().setUp();self.value=value();self.jobs.side_effect=self.index_job;self.identity.return_value=([{'mockedOnly':True}],tokens());self.enterContext(mock.patch.object(native,'require_database_ownership'))
        self.enterContext(mock.patch.object(native,'seed_measurement_database',side_effect=lambda db,v,owner,model,side:self.seeded(side)))
        self.enterContext(mock.patch.object(fixture,'verify_readback',side_effect=self.preserved));self.replay=self.enterContext(mock.patch.object(files,'run_file_measurement',side_effect=self.file_run));self.enterContext(mock.patch.object(measurement,'run_read_measurement',side_effect=AssertionError('不能改测普通四读')));self.enterContext(mock.patch.object(write,'run_write_measurement',side_effect=AssertionError('不能改测客户写入')))

    def seeded(self,side):
        """独立标出原10752行装载必须早于新文件准备。"""
        self.events.append('seed-'+side);return {'target':side}

    def index_job(self,root,value,name,state,path,*,expected_index_plan=None):
        """返回受现有正式解析规则约束的原六和索引六短进程回执。"""
        state['jobs'].append(dict(kind='container',target='init-'+name,name='metarill-perf-'+value['runId']+'-init-'+name,id='e'*64,containerAbsent=True))
        if name in native.JOBS:return self.job(root,value,name,state,path)
        self.events.append(name);side,mode=name.split('-')[2:];return indexed.receipt(value,side,mode)

    def file_run(self,*args):
        """仅用桩结果验证接线，不冒充真实下载或样本核验。"""
        self.events.append('file-replay');return dict(measurementValid=True,stableRegression=False,fileFixture={'targets':{'java':{'contractId':'905900000000000001'},'csharp':{'contractId':'905900000000000002'}}})

    def preserved(self,run,side,query,**options):
        """后验必须同时保留同索引及精确一个附加合同排除ID。"""
        self.assertTrue(options['relation_indexes']);self.assertEqual('905900000000000001' if side=='java' else '905900000000000002',options['exclude_contract_id']);return {'target':side}

    def test_full_coordinator_seeds_then_measures_real_file_profile_and_keeps_cleanup(self):
        """原身份和数据核验全部执行后才进入文件负载，清理及首版边界保持。"""
        with contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertTrue(result['passed']);self.assertEqual(12,self.jobs.call_count);self.assertEqual('paired-protected-file-measurement-only',result['acceptedScope']);self.assertFalse(result['performanceAccepted']);self.assertTrue(result['measurement']['physicalFixtureUnchanged']);self.assertEqual([],result['cleanupErrors']);self.assertEqual('ended',result['leaseStatus']);self.assertLess(self.events.index('seed-csharp'),self.events.index('file-replay'));self.assertLess(self.events.index('file-replay'),self.events.index('resources-cleanup'))

    def test_file_failure_is_not_replaced_by_read_benchmark_or_cleanup_success(self):
        """下载完整性失败仍整轮失败并准确回收，不允许换成容易通过的普通只读负载。"""
        self.replay.side_effect=ValueError('measurement-file-preflight-mismatch')
        with contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertFalse(result['passed']);self.assertEqual('measurement-file-preflight-mismatch',result['failure']['code']);self.assertEqual([],result['cleanupErrors']);self.assertEqual('ended',result['leaseStatus']);self.assertIn('resources-cleanup',self.events);self.assertIn('database-cleanup',self.events)


if __name__=='__main__':unittest.main()
