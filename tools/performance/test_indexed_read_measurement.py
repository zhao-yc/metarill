"""当前完整候选同索引只读测量的独立预算和编排，不连接外部资源。"""
import contextlib
import io
import unittest
from unittest import mock
import paired_native_bootstrap as native
import paired_measurement as measurement
import paired_measurement_fixture as fixture
import paired_write_measurement as write
import test_paired_native_bootstrap as previous
from test_indexed_write_measurement import receipt
import test_paired_measurement as read_tests


def indexed_read():
    """虚构的专用读取范围只用于离线测试，不生成真实口令或外部对象。"""
    value=previous.settings();value.update(purpose=native.resources.INDEXED_READ_MEASUREMENT_NATIVE,images=dict(native.resources.INDEXED_WRITE_IMAGES),relationIndexProfile=native.resources.RELATION_INDEX_PROFILE,readReplayRequests=19336);return value


class IndexedReadContractTests(unittest.TestCase):
    def test_read_scope_cannot_be_upgraded_to_write_or_use_older_image(self):
        """用途/预算/索引配置/镜像必须成套匹配，错误在IO之前拒绝。"""
        value=indexed_read();descriptor=native.validate(value);self.assertEqual(native.resources.INDEXED_READ_MEASUREMENT_RESOURCES,descriptor['purpose']);self.assertTrue(native.resources.is_measurement(value));self.assertFalse(native.resources.is_write_measurement(value));self.assertFalse(native.resources.is_observed(value))
        for change in ({'readReplayRequests':None},{'readReplayRequests':True},{'readReplayRequests':19337},{'writeReplayRequests':8624},{'authorizationStatus':'pending'},{'relationIndexProfile':'other'},{'images':native.resources.CURRENT_MEASUREMENT_IMAGES}):
            with self.subTest(change=change),mock.patch.object(native,'verify_tool_sources',side_effect=AssertionError('禁止IO')),self.assertRaises(ValueError):native.run({**value,**change})
        with self.assertRaises(ValueError):native.validate({**value,'purpose':native.resources.INDEXED_WRITE_MEASUREMENT_NATIVE,'writeReplayRequests':8624})
        self.assertEqual(['--Metarill:Paired:SqlObservation:Enabled=false'],native.resources.observation_arguments(descriptor,'csharp'))

    def test_actual_scenario_budget_counts_preflight_warmup_and_every_step(self):
        """真实场景增删步骤必须拒绝，不能只核入口中声明的数字。"""
        value=measurement.config_for(read_tests.RUN,read_tests.TARGETS,read_tests.TOKENS)
        self.assertEqual(4,len(value['scenarios'][0]['steps']))
        for count in (3,5):
            changed=measurement.read_scenario();changed['steps']=(changed['steps']+[changed['steps'][0]])[:count]
            with mock.patch.object(measurement,'read_scenario',return_value=changed),self.assertRaisesRegex(ValueError,'measurement-read-budget'):measurement.config_for(read_tests.RUN,read_tests.TARGETS,read_tests.TOKENS)

    def test_real_seed_entry_requires_ownership_and_verifies_shared_relation_indexes(self):
        """共享种子逐值回读必须选中新索引合同，资源描述本身不能借用播种。"""
        value=indexed_read();mysql,model=mock.Mock(),mock.Mock();mysql.mysql.return_value='1\n1';model.verify_readback.return_value={'verified':True}
        with mock.patch.object(native,'require_database_ownership') as own:
            self.assertEqual({'verified':True},native.seed_measurement_database(mysql,value,'owner',model,'java'));own.assert_called_once();model.verify_readback.assert_called_once_with(value['runId'],'java',mysql.mysql,relation_indexes=True)
            with self.assertRaises(ValueError):native.seed_measurement_database(mysql,{**value,'purpose':native.resources.INDEXED_READ_MEASUREMENT_RESOURCES},'owner',model,'java')


class IndexedReadFlowTests(previous.FlowFixture):
    def setUp(self):
        """实际执行协调器，所有外部边界替换，不能当实库或性能验收。"""
        super().setUp();self.value=indexed_read();self.jobs.side_effect=self.indexed_job
    def indexed_job(self,root,value,name,state,path,*,expected_index_plan=None):
        """完整记录原六短进程和六索引短进程，回执仍由真实校验器逐项检查。"""
        state['jobs'].append({'kind':'container','target':'init-'+name,'name':'metarill-perf-'+value['runId']+'-init-'+name,'id':'e'*64,'containerAbsent':True})
        if name in native.JOBS:return super().job(root,value,name,state,path)
        self.events.append(name);side,mode=name.split('-')[2:]
        if mode=='apply':self.assertEqual('e'*64,expected_index_plan)
        return receipt(value,side,mode)
    def test_complete_coordinator_upgrades_then_seeds_reads_and_verifies_without_write_branch(self):
        """最新完整候选执行同索引后原四读，原数据后验及清理保持，写入分支不得调用。"""
        def seeded(mysql,value,owner,model,side):self.events.append('seed-'+side);return {'target':side}
        def measured(*args):self.events.append('read');return {'measurementValid':True,'stableRegression':False}
        def preserved(run,side,query,*,relation_indexes=False,exclude_write_samples=False):self.assertTrue(relation_indexes);self.assertFalse(exclude_write_samples);return {'target':side}
        with mock.patch.object(native,'seed_measurement_database',side_effect=seeded),mock.patch.object(native,'require_database_ownership'),mock.patch.object(measurement,'run_read_measurement',side_effect=measured),mock.patch.object(fixture,'verify_readback',side_effect=preserved),mock.patch.object(write,'run_write_measurement',side_effect=AssertionError('只读不得写入')),contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertTrue(result['passed']);self.assertEqual(12,self.jobs.call_count);self.assertEqual('paired-read-measurement-only',result['acceptedScope']);self.assertFalse(result['performanceAccepted']);self.assertTrue(result['measurement']['physicalFixtureUnchanged']);self.assertLess(self.events.index(native.INDEX_JOBS[-1]),self.events.index('seed-java'));self.assertLess(self.events.index('read'),self.events.index('database-cleanup'))
    def test_failed_index_stops_before_read_replay_and_recovers_owned_resources(self):
        """任一端索引缺失不能发读负载，也不能略过既定清理。"""
        old=self.jobs.side_effect
        def broken(*args,**kwargs):
            value=old(*args,**kwargs)
            if args[2]=='csharp-index-csharp-verify':value['changes']=native.INDEX_CHANGES
            return value
        self.jobs.side_effect=broken
        with mock.patch.object(native,'require_database_ownership'),mock.patch.object(native,'seed_measurement_database') as seeded,mock.patch.object(measurement,'run_read_measurement') as replay,contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        seeded.assert_not_called();replay.assert_not_called();self.assertFalse(result['passed']);self.assertEqual([],result['cleanupErrors'])


if __name__=='__main__':unittest.main()
